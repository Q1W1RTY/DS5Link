using System;
using System.Threading.Tasks;
namespace DS5Link;
internal sealed partial class MainWindow {
 readonly DispatcherTimer effectTimer=new(){Interval=TimeSpan.FromMilliseconds(10)};
 readonly AudioReactiveCapture audioEffects=new();
 EffectOptions effectOptions=new();bool effectPolling,audioRunning;long nextAudioRetry;
 void InitializeEffects(){
  effectOptions=preview?new():EffectOptions.Load();effectTimer.Tick+=async(_,_)=>await PollEffects();
  if(!preview && effectOptions.Enabled)effectTimer.Start();
 }
 void SyncAudioEffects(BridgeState state){
  if(preview)return;bool wanted=state.State=="Active" && state.Effects.Active && state.Mode!="xbox" && state.Transport!="USB" && effectOptions.AudioEnabled && !state.Effects.RumbleOwned && !exiting && !exitRequested;
  bool gameSource=effectOptions.AudioDeviceId==AudioReactiveCapture.GameSource;
  uint gamePid=gameSource && wanted?ForegroundExecutable.GameProcess(activeProfile):0;
  if(gameSource){
   wanted &= gamePid!=0;
   if(audioEffects.ProcessId!=0 && audioEffects.ProcessId!=gamePid)audioEffects.Stop();
  }
  if(wanted && audioEffects.IsRunning && string.IsNullOrEmpty(effectOptions.AudioDeviceId) && audioEffects.DefaultEndpointChanged())audioEffects.Stop();
  audioRunning=audioEffects.IsRunning;
  effectTimer.Interval=TimeSpan.FromMilliseconds(wanted?10:100);
  if(wanted && !audioRunning && Environment.TickCount64>=nextAudioRetry){nextAudioRetry=Environment.TickCount64+1000;audioEffects.Start(effectOptions,gamePid);audioRunning=audioEffects.IsRunning;}
  else if(!wanted && audioRunning){audioEffects.Stop();audioRunning=false;}
 }
 async Task ApplyEffectSettings(EffectOptions next){
  if(commanding || exiting || exitRequested)throw new InvalidOperationException(L.T("Дождитесь завершения текущей операции."));
  if(!next.Valid)throw new InvalidOperationException(L.T("Проверьте параметры эффектов."));
  commanding=true;++stateGeneration;Display(current);
  try{
   var state=await BridgeClient.SetEffectOptionsAsync(next);
   if(next.AudioDeviceId!=effectOptions.AudioDeviceId || next.AudioEnabled!=effectOptions.AudioEnabled){audioEffects.Stop();audioRunning=false;}
   audioEffects.UpdateOptions(next);
   effectOptions=next;
   if(next.Enabled)effectTimer.Start();else effectTimer.Stop();
   Display(current with {Effects=state});
  }finally{commanding=false;Display(current);}
 }
 async Task PollEffects(){
  if(effectPolling || profileSwitching || commanding || exiting || exitRequested || !effectOptions.Enabled)return;
  effectPolling=true;
  try{if(effectOptions.AudioDeviceId==AudioReactiveCapture.GameSource)SyncAudioEffects(current);var state=await BridgeClient.ReadEffectsAsync(audioEffects.Level,audioTick:audioEffects.SampleTick,audioFrames:audioEffects.TakeFrames());if(!exiting && !exitRequested){current=current with {Effects=state};SyncAudioEffects(current);inputSettingsWindow?.SetEffectState(state,current.Transport=="USB" && effectOptions.AudioEnabled?L.T("Звук → haptics пока доступен через Bluetooth"):audioEffects.Error,audioEffects.Level,audioEffects.SignalPeak,audioEffects.SourceName,current.Mode=="xbox");}}
  catch(Exception){audioEffects.Stop();audioRunning=false;inputSettingsWindow?.SetEffectState(new(),L.T("Нет связи со службой"),0,xboxMode:current.Mode=="xbox");}
  finally{effectPolling=false;}
 }
}
