using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
namespace DS5Link;
// A keyed, serialized queue: changes made during an outstanding request
// replace the pending value rather than racing or replaying stale drafts.
internal sealed class LatestSettingsApplier {
 readonly Dictionary<string,Func<Task>> pending=new();Task? running;
 internal bool HasPending=>pending.Count>0;internal bool IsRunning=>running is {IsCompleted:false};
 internal void Queue(string key,Func<Task> apply)=>pending[key]=apply;
 internal Task FlushAsync(){if(IsRunning)return running!;return running=Run();}
 async Task Run(){while(pending.Count>0){var item=pending.First();pending.Remove(item.Key);try{await item.Value();}catch{if(!pending.ContainsKey(item.Key))pending[item.Key]=item.Value;throw;}}}
}
internal sealed partial class InputSettingsWindow {
 readonly LatestSettingsApplier autoApplier=new();
 readonly DispatcherTimer autoApplyTimer=new(){Interval=TimeSpan.FromMilliseconds(120)};
 bool autoReady,allowClose,closingFlush;
 void InitializeAutoApply(){
  autoApplyTimer.Tick+=async(_,_)=>{autoApplyTimer.Stop();await ApplyPendingSettings();};
  TouchBindingsEnabled.IsCheckedChanged+=(_,_)=>QueueAutoApply(false);
  ConsumeTouchpad.IsCheckedChanged+=(_,_)=>QueueAutoApply(false);
  LeftDeadzone.ValueChanged+=(_,_)=>QueueAutoApply(false);RightDeadzone.ValueChanged+=(_,_)=>QueueAutoApply(false);
  leftTrigger.Changed+=()=>QueueAutoApply(true);rightTrigger.Changed+=()=>QueueAutoApply(true);
  LedMode.SelectionChanged+=(_,_)=>QueueAutoApply(true);AudioDevice.SelectionChanged+=(_,_)=>QueueAutoApply(true);
  foreach(var field in new[]{redSlider,greenSlider,blueSlider,sensitivitySlider,limitSlider,gateSlider})field.Slider.ValueChanged+=(_,_)=>QueueAutoApply(true);
  LedBrightness.ValueChanged+=(_,_)=>QueueAutoApply(true);LedSpeed.ValueChanged+=(_,_)=>QueueAutoApply(true);
  AudioEffectsEnabled.IsCheckedChanged+=(_,_)=>QueueAutoApply(true);
  AudioBassOnly.IsCheckedChanged+=(_,_)=>QueueAutoApply(true);
  autoReady=true;
 }
 void QueueAutoApply(bool effects){
  if(!autoReady || preview || closed)return;
  autoApplier.Queue(effects?"effects":"input",effects?ApplyEffectDraftAsync:ApplyTouchDraftAsync);
  SaveStatusText.Text=L.T("Сохранение…");SaveStatusDot.Fill=new SolidColorBrush(Color.FromRgb(64,156,255));
  InputSettingsNotice.Text="";InputSettingsNotice.IsVisible=false;autoApplyTimer.Stop();autoApplyTimer.Start();
 }
 async Task<bool> ApplyPendingSettings(){
  if(closed)return false;
  try{await autoApplier.FlushAsync();SaveStatusText.Text=L.T(preview?"Предпросмотр":"Изменения сохранены");SaveStatusDot.Fill=new SolidColorBrush(Color.FromRgb(48,209,88));InputSettingsNotice.Text="";InputSettingsNotice.IsVisible=false;return true;}
  catch(Exception error){SaveStatusText.Text=L.T("Не удалось сохранить");SaveStatusDot.Fill=new SolidColorBrush(Color.FromRgb(255,159,10));InputSettingsNotice.Text=L.T("Не удалось применить: ")+error.Message;InputSettingsNotice.IsVisible=true;return false;}
 }
 async void OnSettingsClosing(object? sender,WindowClosingEventArgs e){
  if(!allowClose && (autoApplier.HasPending || autoApplier.IsRunning)){
   e.Cancel=true;if(closingFlush)return;closingFlush=true;autoApplyTimer.Stop();
   bool ok=await ApplyPendingSettings();closingFlush=false;if(ok || MessageBox.Show(this,L.T("Не удалось сохранить изменения. Закрыть окно и отказаться от неприменённых изменений?"),L.T("Настройки"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes){allowClose=true;Close();}return;
  }
  closed=true;autoApplyTimer.Stop();previewTimer.Stop();CancelCapture();
 }
 async Task ApplyTouchDraftAsync(){
  var next=new InputProfile {Options=new(){LeftDeadzone=(int)LeftDeadzone.Value,RightDeadzone=(int)RightDeadzone.Value,TouchEnabled=TouchBindingsEnabled.IsChecked==true,ConsumeTouchpad=ConsumeTouchpad.IsChecked==true,Mapping=ReadInputMapping(),GyroMouse=GyroMouseSwitch.IsChecked==true,TouchMouse=TouchMouseSwitch.IsChecked==true,MouseSensitivity=(int)MouseSensitivitySlider.Value},Bindings=new(bindings)};
  if(!next.Valid)throw new InvalidOperationException(L.T("Назначьте хотя бы одну клавишу для тачпада."));
  if(saveProfile!=null)await SaveProfileInput(next);else await apply(next);
 }
}
