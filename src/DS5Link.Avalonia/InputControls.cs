using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
namespace DS5Link;
internal sealed partial class MainWindow {
 readonly DispatcherTimer inputTimer=new(){Interval=TimeSpan.FromMilliseconds(30)};
 readonly InputEventCursor inputCursor=new();
 InputProfile inputProfile=new();
 InputSettingsWindow? inputSettingsWindow;
 bool inputPolling,keyboardErrorShown;
 void InitializeInputControls() {
  inputProfile=preview?new():InputProfile.Load();
  inputTimer.Tick+=async(_,_)=>await PollInput();
  if(!preview && inputProfile.Options.TouchEnabled)inputTimer.Start();
 }
 internal InputSettingsWindow CreateInputSettingsWindow(){
  var window=new InputSettingsWindow(inputProfile,ApplyInputSettings,preview,effectOptions,ApplyEffectSettings);
  window.InitializeProfiles(()=>profiles,SaveProfile,DeleteProfile,activeProfile,profileLoadError,ReplaceProfiles);
  window.SetEffectState(current.Effects,audioEffects.Error,audioEffects.Level,audioEffects.SignalPeak,audioEffects.SourceName,current.Mode=="xbox");return window;
 }
 void ToggleInputSettings(object sender,RoutedEventArgs e)=>OpenInputSettings();
 void OpenInputSettings(){
  if(inputSettingsWindow!=null){if(inputSettingsWindow.WindowState==WindowState.Minimized)inputSettingsWindow.WindowState=WindowState.Normal;inputSettingsWindow.Show(this);inputSettingsWindow.Activate();return;}
  inputSettingsWindow=CreateInputSettingsWindow();
  inputSettingsWindow.Closed+=(_,_)=>inputSettingsWindow=null;
  inputSettingsWindow.Show(this);
 }
 async Task ApplyInputSettings(InputProfile next) {
  if(commanding || exiting || exitRequested)throw new InvalidOperationException(L.T("Дождитесь завершения текущей операции."));
  commanding=true;++stateGeneration;KeyboardOutput.ReleaseAll();inputCursor.Reset();Display(current);
  try {
   var state=await BridgeClient.SetInputOptionsAsync(next.RuntimeOptions);
   inputProfile=next;keyboardErrorShown=false;Display(state);
   if(next.Options.TouchEnabled)inputTimer.Start();else inputTimer.Stop();
  }finally{commanding=false;Display(current);}
 }
 async Task PollInput() {
  if(inputPolling || profileSwitching || commanding || exiting || exitRequested || !inputProfile.Options.TouchEnabled)return;
  inputPolling=true;
  try {
   int generation=stateGeneration;
   var batch=await BridgeClient.ReadInputEventsAsync(inputCursor.Sequence);
   if(generation!=stateGeneration)return;
   var received=Stopwatch.StartNew();
   foreach(var action in inputCursor.Read(batch).TakeLast(4)){
    if(generation!=stateGeneration || !ProfileAllowsKeyboard() || commanding || exiting || exitRequested || IsActive || inputSettingsWindow?.IsActive==true || action.AgeMs+received.ElapsedMilliseconds>150)continue;
    if(inputProfile.Bindings.TryGetValue(action.Action,out var binding) && !await KeyboardOutput.SendAsync(binding) && !keyboardErrorShown){keyboardErrorShown=true;Notice(L.T("Windows не приняла назначение тачпада. Проверьте права приложения и игры."));}
   }
  }catch(Exception){inputCursor.Reset();KeyboardOutput.ReleaseAll();}
  finally{inputPolling=false;}
 }
}
