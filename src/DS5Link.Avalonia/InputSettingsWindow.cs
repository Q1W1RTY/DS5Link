using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
namespace DS5Link;
internal sealed partial class InputSettingsWindow:Window {
 readonly Dictionary<string,Button> actionButtons=new();
 string selectedAction="leftClick";
 readonly Dictionary<string,KeyBinding> bindings;
 readonly Func<InputProfile,Task> apply;
 readonly DispatcherTimer previewTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
 readonly bool preview;
 string? capturingAction;
 bool polling,closed;
 readonly Stopwatch sampleAge=new();
 long sampleReportAge=500;
 internal static string[] Labels=>new[]{L.T("Нажатие слева"),L.T("Нажатие справа"),L.T("Касание"),L.T("Свайп влево"),L.T("Свайп вправо"),L.T("Свайп вверх"),L.T("Свайп вниз")};
 internal InputSettingsWindow(InputProfile profile,Func<InputProfile,Task> apply,bool preview=false,EffectOptions? effects=null,Func<EffectOptions,Task>? applyEffects=null){
  this.apply=apply;this.preview=preview;bindings=new(profile.Bindings);InitializeComponent();

  var screen=Screens.Primary;var work=new Size((screen?.WorkingArea.Width??1920)/RenderScaling,(screen?.WorkingArea.Height??1080)/RenderScaling);
  Width=Math.Min(1120,work.Width);Height=Math.Min(840,work.Height);
  MaxWidth=Width;MaxHeight=Height;
  TouchBindingsEnabled.IsChecked=profile.Options.TouchEnabled;ConsumeTouchpad.IsChecked=profile.Options.ConsumeTouchpad;
  LeftDeadzone.Value=profile.Options.LeftDeadzone;RightDeadzone.Value=profile.Options.RightDeadzone;
  InitializeTouchActions();
  TouchVisual.ZoneSelected+=SelectTouchAction;
  KeyDown+=CaptureKey;Deactivated+=(_,_)=>CancelCapture();
  Opened+=(_,_)=>NativeWindowChrome.Attach(this,DragHeader);
  previewTimer.Tick+=async(_,_)=>{if(sampleAge.IsRunning && sampleReportAge<500 && sampleAge.ElapsedMilliseconds+sampleReportAge>=500)SetPreview(new());UpdateLedPreview();await RefreshPreview();};
  Loaded+=(_,_)=>{if(!preview)previewTimer.Start();};
  Closing+=OnSettingsClosing;
  SettingsTabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,SettingsTabs)){CancelCapture();ResetEffectsButton.IsVisible=SettingsTabs.SelectedIndex>=3?true:false;SettingsTabs.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s=>s.Name=="SettingsPageScroll")?.ScrollToHome();}};
  InitializeEffectControls(effects??new(),applyEffects);
  InitializeInputMapping(profile.Options.Mapping);
  DeadzoneChanged(this,null!);
  GyroMouseSwitch.Content=L.T("Гироскоп как мышь при удержании L2");
  TouchMouseSwitch.Content=L.T("Тачпад как мышь");
  LoadMouseOptions(profile.Options);
  InitializeAutoApply();
  GyroMouseSwitch.IsCheckedChanged+=(_,_)=>QueueAutoApply(false);
  TouchMouseSwitch.IsCheckedChanged+=(_,_)=>QueueAutoApply(false);
  MouseSensitivitySlider.ValueChanged+=(_,_)=>{UpdateMouseSensitivity();QueueAutoApply(false);};
  if(preview)SaveStatusText.Text=L.T("Предпросмотр");
 }
 void UpdateMouseSensitivity()=>MouseSensitivityLabel.Text=L.F("Чувствительность мыши: {0}%",(int)MouseSensitivitySlider.Value);
 void LoadMouseOptions(InputOptions options){
  GyroMouseSwitch.IsChecked=options.GyroMouse;
  TouchMouseSwitch.IsChecked=options.TouchMouse;
  MouseSensitivitySlider.Value=options.MouseSensitivity;
  UpdateMouseSensitivity();
 }
 string BindingLabel(string action)=>bindings.TryGetValue(action,out var binding)?binding.Label:L.T("Не назначено");
 void InitializeTouchActions(){
  string[] icons={"","","·","←","→","↑","↓"};
  for(int i=0;i<InputProfile.Actions.Length;i++){
   var action=InputProfile.Actions[i];var content=new StackPanel();
   if(i>=2)content.Children.Add(new TextBlock {Text=icons[i],FontSize=20,HorizontalAlignment=HorizontalAlignment.Center});
   content.Children.Add(new TextBlock {Text=Labels[i],FontSize=14,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,MaxWidth=i<2?double.PositiveInfinity:72});
   var button=new Button {Content=content,Tag=action,Theme=this.RequireTheme("CompactButton"),Margin=new Thickness(4),Padding=new Thickness(8),MinHeight=i<2?44:66};
   AutomationProperties.SetName(button,Labels[i]);
   button.Click+=(_,_)=>SelectTouchAction(action);actionButtons[action]=button;
   if(i<2)ClickButtons.Children.Add(button);else GestureButtons.Children.Add(button);
  }
  RefreshTouchSelection();
 }
 void RefreshTouchSelection(){
  SelectedActionName.Text=Labels[Array.IndexOf(InputProfile.Actions,selectedAction)];
  if(capturingAction==null)SelectedBindingButton.Content=BindingLabel(selectedAction);
  AutomationProperties.SetName(SelectedBindingButton,L.T("Назначение")+": "+BindingLabel(selectedAction));
  foreach(var pair in actionButtons){
   bool selected=pair.Key==selectedAction;
   pair.Value.Background=new SolidColorBrush(selected?Color.FromArgb(45,10,132,255):Color.FromArgb(20,255,255,255));
   pair.Value.BorderBrush=new SolidColorBrush(selected?Color.FromArgb(90,10,132,255):Color.FromArgb(14,255,255,255));
   pair.Value.Foreground=selected?Brushes.White:new SolidColorBrush(Color.FromRgb(224,224,224));
  }
  TouchVisual.SelectedAction=selectedAction;TouchVisual.InvalidateVisual();
 }
 void SelectTouchAction(string action){CancelCapture();selectedAction=action;RefreshTouchSelection();}
 void CaptureSelectedAction(object sender,RoutedEventArgs e)=>BeginCapture(selectedAction);
 void ClearSelectedBinding(object sender,RoutedEventArgs e){
  CancelCapture();bindings.Remove(selectedAction);if(bindings.Count==0)TouchBindingsEnabled.IsChecked=false;
  RefreshTouchSelection();QueueAutoApply(false);
 }
 void BeginCapture(string action){
  CancelCapture();selectedAction=action;RefreshTouchSelection();capturingAction=action;
  SelectedBindingButton.Content=L.T("Нажмите клавишу…");SelectedBindingButton.Focus();
  CaptureHint.Text=Labels[Array.IndexOf(InputProfile.Actions,action)]+L.T(": нажмите клавишу или сочетание. Backspace — убрать.");
 }
 void CancelCapture(){if(capturingAction!=null){capturingAction=null;RefreshTouchSelection();}}
 void CaptureKey(object? sender,KeyEventArgs e){
  if(capturingAction==null)return;e.Handled=true;var key=e.Key;
  if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
  if(key==Key.Back){bindings.Remove(capturingAction);if(bindings.Count==0)TouchBindingsEnabled.IsChecked=false;}
  else{var binding=new KeyBinding(KeyInterop.VirtualKeyFromKey(key),(int)e.KeyModifiers);if(!binding.Valid){CaptureHint.Text=L.T("Поддерживаются буквы, цифры, F1–F12, пробел, Enter, Esc, Tab и навигация. Win не поддерживается.");return;}bindings[capturingAction]=binding;}
  CancelCapture();RefreshTouchSelection();CaptureHint.Text=L.T("Назначение применяется автоматически.");QueueAutoApply(false);
 }
 void DeadzoneChanged(object sender,RangeBaseValueChangedEventArgs e){
  if(LeftDeadzoneValue==null || RightDeadzoneValue==null || LeftVisual==null || RightVisual==null)return;
  LeftDeadzoneValue.Text=$"{LeftDeadzone.Value:F0}%";RightDeadzoneValue.Text=$"{RightDeadzone.Value:F0}%";
  LeftVisual.Deadzone=(int)LeftDeadzone.Value;RightVisual.Deadzone=(int)RightDeadzone.Value;LeftVisual.InvalidateVisual();RightVisual.InvalidateVisual();
 }
 internal void SetPreview(InputPreview value){
  latestInputPreview=value;
  bool fresh=value.Fresh;sampleReportAge=fresh?value.AgeMs:500;sampleAge.Restart();
  TouchVisual.Sample=fresh?value:new();TouchVisual.InvalidateVisual();
  LeftVisual.SetSample(fresh,value.LeftX,value.LeftY,mappingDraft.LeftX,mappingDraft.LeftY);
  RightVisual.SetSample(fresh,value.RightX,value.RightY,mappingDraft.RightX,mappingDraft.RightY);
 }
 void MinimizeWindow(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
 void CloseWindow(object sender,RoutedEventArgs e)=>Close();
 async Task RefreshPreview(){
  if(polling || closed)return;polling=true;
  try{var value=await BridgeClient.ReadInputPreviewAsync();if(!closed){SetPreview(value);}}catch(Exception){if(!closed)SetPreview(new());}finally{polling=false;}
 }
}
