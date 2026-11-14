using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using DS5Link;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
if(args is ["--process-audio-smoke",var processId]){
 var result=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
 using var capture=new ProcessLoopback(uint.Parse(processId),(buffer,count)=>{
  if(Array.Exists(buffer.AsSpan(0,count).ToArray(),value=>value!=0))result.TrySetResult(true);
 },error=>result.TrySetException(error));
 capture.Start();
 await result.Task.WaitAsync(TimeSpan.FromSeconds(12));
 Console.WriteLine("Process loopback delivered non-silent audio");
 return;
}
{
 var motion=new MouseMotion();
 var now=System.Diagnostics.Stopwatch.GetTimestamp();
 var tick=System.Diagnostics.Stopwatch.Frequency/100;
 var options=new InputOptions{TouchMouse=true,GyroMouse=true};
 var contact=new TouchPreview{Active=true,Id=1,X=100,Y=100};
 InputPreview sample=new(){Enabled=true,AgeMs=0,Touches=new[]{contact,new TouchPreview()}};
 Check(motion.Read(sample,options,now)==default,"Touch down moved the cursor");
 sample=sample with {Touches=new[]{contact with {X=120},new TouchPreview()}};
 Check(motion.Read(sample,options,now+tick).X==15,"Touch movement was not relative");
 sample=sample with {Touches=new[]{contact with {Id=2,X=900},new TouchPreview()}};
 Check(motion.Read(sample,options,now+tick*2)==default,"New contact caused a jump");
 sample=sample with {GyroY=1000};
 Check(motion.Read(sample,options,now+tick*3)==default,"Gyro moved without L2");
 Check(motion.Read(sample with {AimHeld=true},options,now+tick*4).X<0,"Gyro did not move while aiming");
 Check(motion.Read(sample with {AimHeld=true,AgeMs=90},options,now+tick*5)==default,"Stale motion was injected");
 Console.WriteLine("Mouse contact, gyro activation and stale input checks passed");
}
static async Task TestClient(string scenario) {
 var name="DS5Link.ClientTest."+Guid.NewGuid().ToString("N");
 using var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
 var client=scenario=="options"?BridgeClient.SetInputOptionsAsync(new InputOptions {LeftDeadzone=10,TouchClickMask=1},name):BridgeClient.SendAsync("getState",name);
 await server.WaitForConnectionAsync();
 using var reader=new StreamReader(server,Encoding.UTF8,false,1024,true);
 var line=await reader.ReadLineAsync();using var request=JsonDocument.Parse(line!);
 if(scenario=="options")Check(request.RootElement.GetProperty("options").GetProperty("leftDeadzone").GetInt32()==10 && request.RootElement.GetProperty("options").GetProperty("touchClickMask").GetInt32()==1,"Input options wire casing is incompatible with native service");
 var id=request.RootElement.GetProperty("id").GetString();
 string response=scenario switch {
  "id"=>JsonSerializer.Serialize(new{protocolVersion=1,id="wrong",ok=true,payload=new{state="Active"}}),
  "version"=>JsonSerializer.Serialize(new{protocolVersion=2,id,ok=true,payload=new{state="Active"}}),
  "error"=>JsonSerializer.Serialize(new{protocolVersion=1,id,ok=false,error="BRIDGE_PAUSED"}),
  "large"=>new string('x',66000),
  _=>JsonSerializer.Serialize(new{protocolVersion=1,id,ok=true,payload=new{state="Active",batteryPercent=75}})
 };
 if(scenario=="eof")server.Disconnect();
 else {try{await server.WriteAsync(Encoding.UTF8.GetBytes(response+"\n"));await server.FlushAsync();}catch(IOException)when(scenario=="large") {}}
 try {var state=await client;Check((scenario=="valid" || scenario=="options") && state.State=="Active" && state.BatteryPercent==75,"Invalid reply was accepted");}
 catch(IOException)when(scenario!="valid"){}
}
var installedUpdateVersion=new Version(0,1,57);
foreach(var (tag,draft,prerelease,expected) in new[]{
 ("v0.1.58",false,false,true),("v0.1.57",false,false,false),("v0.1.56",false,false,false),
 ("v0.1.58",true,false,false),("v0.1.58",false,true,false),("v0.1.58-beta",false,false,false)
}){
 var updateRelease=JsonSerializer.SerializeToElement(new {tag_name=tag,draft,prerelease});
 Check((ReleaseUpdate.ReadVersion(updateRelease,installedUpdateVersion)!=null)==expected,"Update version selection failed: "+tag);
}
var checksum=new string('A',64);
Check(ReleaseUpdate.ReadChecksum(checksum+"  DS5Link-Setup.exe").Length==32,"Valid update checksum was rejected");
try{ReleaseUpdate.ReadChecksum(checksum+"  other.exe");throw new Exception("Wrong update asset was accepted");}
catch(IOException){}
Console.WriteLine("Update version and checksum selection passed");
foreach(var scenario in new[]{"valid","options","id","version","error","large","eof"})await TestClient(scenario);
var masked=Diagnostics.Mask("address=12:34:56:78:9a:bc HID\\VID_054C&PID_0CE6\\123456789ABC");
Check(!masked.Contains("12:34") && !masked.Contains("123456789ABC") && !masked.Contains("VID_054C"),"Private device identity leaked");
var eventInput=new[]{
 "2026-11-02T12:00:00.100+03:00|daemon|INFO|Service started",
 "2026-11-02T12:00:01.100+03:00|bluetooth|WARN|address=12:34:56:78:9a:bc disconnected",
 "2026-11-02T12:00:02.100+03:00|bluetooth|WARN|address=12:34:56:78:9a:bc disconnected",
 "2026-11-02T12:00:03.100+03:00|input-control|DEBUG|packet details",
 "2026-11-02T12:00:04.100+03:00|port|ERROR|"+new string('x',500),
 "Unstructured legacy message"
};
var events=Diagnostics.RecentEvents(eventInput);
Check(events.Length==3 && events[0].Message=="Unstructured legacy message","Recent event order/bound failed");
Check(events[1].Level=="ERROR" && events[1].Time=="12:00:04" && events[1].Message.Length==220 && events[1].Detail.Length>500,"Severity, timestamp or bounded text lost");
Check(events[2].Count==2 && events[2].RepeatLabel=="×2" && !events[2].Detail.Contains("12:34:56") && events[2].Time=="12:00:02","Repeated events or masking failed");
Check(Diagnostics.RecentEvents(new[]{"", " ", "2026-11-02T12:00:00+03:00|daemon|DEBUG|hidden detail"}).Length==0,"Empty/debug-only log must remain empty");
Check(eventInput[1].Contains("12:34:56:78:9a:bc"),"Event presentation mutated original export data");
Check(Diagnostics.RecentEvents(new[]{"legacy|same", "same"}).Length==2,"Different legacy messages were incorrectly merged");
var reportDirectory=Path.Combine(Path.GetTempPath(),"DS5Link-diagnostic-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(reportDirectory);
try {
 var archive=Path.Combine(reportDirectory,"offline.zip");
 var result=await DiagnosticArchive.SaveAsync(archive,new BridgeState {State="Error",Error="cached error"},()=>Task.FromException<BridgeState>(new TimeoutException("pipe unavailable")),new[]{
  new DiagnosticSource("service.json",()=>DiagnosticArchive.Json(new ServiceSnapshot("DS5LinkBridge",true,"Stopped",Win32ExitCode:1067))),
  new DiagnosticSource("logs/setup.log",()=>"Компьютер: FRIEND-PC\nИмя пользователя: FRIEND-PC\\Alice\nOwnerSid S-1-5-21-111-222-333-1001\nC:\\Users\\Alice\\AppData\\Local\\DS5Link\n12:34:56:78:9a:bc"),
  new DiagnosticSource("logs/bridge.log",()=>throw new FileNotFoundException("No bridge log")),
  new DiagnosticSource("denied.json",()=>throw new UnauthorizedAccessException("No access")),
  new DiagnosticSource("timeout.json",()=>throw new TimeoutException("Reader timeout")),
  new DiagnosticSource("failure.json",()=>throw new IOException("Reader failed"))
 });
 Check(!result.BridgeReachable && result.Collected==2 && result.Unavailable==4 && !File.Exists(archive+".partial"),"Offline collection did not preserve successful sources");
 using(var zip=ZipFile.OpenRead(archive)) {
  string Read(string name){using var reader=new StreamReader(zip.GetEntry(name)!.Open());return reader.ReadToEnd();}
  using var manifest=JsonDocument.Parse(Read("manifest.json"));var statuses=manifest.RootElement.GetProperty("sources").EnumerateArray().Select(v=>v.GetProperty("Status").GetString()).ToArray();
  Check(statuses.SequenceEqual(new[]{"collected","collected","missing","accessDenied","timeout","error"}),"Source failures were not diagnosed independently");
  using var connection=JsonDocument.Parse(Read("connection.json"));Check(!connection.RootElement.GetProperty("reachable").GetBoolean() && connection.RootElement.GetProperty("stateIsCached").GetBoolean() && connection.RootElement.GetProperty("errorType").GetString()!.Contains("TimeoutException"),"IPC failure detail was lost");
  var log=Read("logs/setup.log");Check(!log.Contains("FRIEND-PC") && !log.Contains("Alice") && !log.Contains("S-1-5-21") && !log.Contains("12:34:56"),"Identity leaked in collected log");
  using var service=JsonDocument.Parse(Read("service.json"));Check(service.RootElement.GetProperty("Win32ExitCode").GetInt32()==1067,"Service startup error missing from offline archive");
 }
 var connected=Path.Combine(reportDirectory,"connected.zip");
 var connectedResult=await DiagnosticArchive.SaveAsync(connected,new(),()=>Task.FromResult(new BridgeState {State="Paused",LogLines=new[]{"HID\\VID_054C&PID_0CE6\\123456789ABC"}}),Array.Empty<DiagnosticSource>());
 Check(connectedResult.BridgeReachable,"Reachable bridge marked offline");
 using(var zip=ZipFile.OpenRead(connected)){using var reader=new StreamReader(zip.GetEntry("status.json")!.Open());Check(!reader.ReadToEnd().Contains("VID_054C"),"Identity leaked in status JSON");}
 var collision=Path.Combine(reportDirectory,"collision.zip");File.WriteAllText(collision+".partial","preserve existing file");
 try{await DiagnosticArchive.SaveAsync(collision,new(),()=>Task.FromResult(new BridgeState()),Array.Empty<DiagnosticSource>());throw new Exception("Archive collision accepted");}catch(IOException){}
 Check(File.ReadAllText(collision+".partial")=="preserve existing file" && !File.Exists(collision),"Archive failure deleted an existing temporary file");
 var tail=Path.Combine(reportDirectory,"tail.log");File.WriteAllText(tail,string.Concat(Enumerable.Repeat("old line\n",1000))+"последняя строка\n",Encoding.Unicode);
 var tailText=DiagnosticArchive.ReadTail(tail,128);Check(tailText.Contains("последняя строка") && tailText.Length<160 && tailText.StartsWith("[tail:"),"Bounded UTF-16 log tail failed");
 var xml="<Events><Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Application Error'/><EventID>1000</EventID><Computer>PRIVATE-PC</Computer></System><EventData><Data>unrelated.exe</Data></EventData></Event><Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Application Error'/><EventID>1000</EventID><Computer>PRIVATE-PC</Computer></System><EventData><Data>vdsd.exe</Data><Data>C:\\Users\\Alice\\app.exe</Data></EventData></Event></Events>";
 using var filtered=JsonDocument.Parse(WindowsDiagnostics.FilterEvents(xml));Check(filtered.RootElement.GetProperty("events").GetArrayLength()==1 && !filtered.RootElement.ToString().Contains("PRIVATE-PC") && !filtered.RootElement.ToString().Contains("Alice"),"Windows event filtering or privacy failed");
 Check(ServiceState.Read("DS5Link.NonexistentDiagnosticTest."+Guid.NewGuid().ToString("N")).Exists==false,"Missing service was not identified");
}finally{foreach(var file in Directory.GetFiles(reportDirectory))File.Delete(file);Directory.Delete(reportDirectory);}
var exitCommands=new List<string>();
var release=await BridgeShutdown.AttemptAsync(command=>{exitCommands.Add(command);return Task.FromResult(new BridgeState {State=command=="pause"?"Stopping":"Paused"});},()=>false);
Check(release.Released && exitCommands.SequenceEqual(new[]{"pause","getState"}),"Exit did not wait for controller release");
var offlineExit=await BridgeShutdown.AttemptAsync(_=>Task.FromException<BridgeState>(new IOException("pipe down")),()=>false);
Check(!offlineExit.Released && offlineExit.Error=="pipe down","Unavailable service must offer UI-only exit instead of claiming release");
var stoppedExit=await BridgeShutdown.AttemptAsync(_=>Task.FromException<BridgeState>(new IOException("pipe down")),()=>true);
Check(stoppedExit.Released,"Stopped service unnecessarily blocked UI exit");
var pendingExit=await BridgeShutdown.AttemptAsync(_=>Task.FromResult(new BridgeState {State="Error",Error="HidHide recovery pending"}),()=>false);
Check(!pendingExit.Released && pendingExit.Error=="HidHide recovery pending","Recovery failure was treated as controller release");
var slowExit=await BridgeShutdown.AttemptAsync(_=>Task.FromResult(new BridgeState {State="Stopping"}),()=>false,TimeSpan.Zero);
Check(!slowExit.Released,"Exit ignored release timeout");
if(args.Length==2 && args[0]=="--diagnostic-smoke") {
 Directory.CreateDirectory(args[1]);var smoke=Path.Combine(args[1],"diagnostic-offline-smoke.zip");
 var result=await DiagnosticArchive.SaveAsync(smoke,new BridgeState {State="Error"},()=>BridgeClient.SendAsync("getDiagnostics","DS5Link.NonexistentProbe."+Guid.NewGuid().ToString("N")));
 Console.WriteLine("Live read-only diagnostic collection: "+result.Collected+" collected, "+result.Unavailable+" unavailable; IPC reachable="+result.BridgeReachable);
}
Check(new InputProfile().Valid && !new InputProfile().Options.TouchEnabled && new InputProfile().Options.LeftDeadzone==0,"Input customization must default to native passthrough");
Check(!new KeyBinding(0x5B,0).Valid && !new KeyBinding(0x41,8).Valid && !new InputProfile {Options=new(){LeftDeadzone=31}}.Valid,"Unsupported keys/modifiers or deadzones accepted");
Check(!new InputProfile {Options=new(){TouchEnabled=true}}.Valid && new InputProfile {Options=new(){TouchEnabled=true},Bindings=new(){{"tap",new(0x70,2)}}}.Valid,"Empty touch mappings accepted");
var cursor=new InputEventCursor();
TouchInputBatch Batch(string epoch,long seq,bool enabled,params TouchInputEvent[] events)=>new(){Epoch=epoch,Sequence=seq,Enabled=enabled,Events=events};
TouchInputEvent Event(long seq,int age=0)=>new(){Sequence=seq,AgeMs=age,Action="tap"};
Check(cursor.Read(Batch("a",10,true,Event(10))).Length==0,"Initial input event replayed");
Check(cursor.Read(Batch("a",13,true,Event(11),Event(11),Event(12,400),Event(13,-1))).Length==1 && cursor.Sequence==13,"Old, duplicate or future events injected");
Check(cursor.Read(Batch("a",14,false,Event(14))).Length==0 && cursor.Read(Batch("b",1,true,Event(1))).Length==0,"Paused or restarted service replayed input");
cursor.Reset();Check(cursor.Read(Batch("b",2,true,Event(2))).Length==0 && cursor.Read(Batch("",3,true,Event(3))).Length==0,"Reconnection baseline was not reset");
Check(!new InputPreview().Fresh && !new InputPreview {Enabled=true,AgeMs=500}.Fresh && !new InputPreview {Enabled=false,AgeMs=0}.Fresh && new InputPreview {Enabled=true,AgeMs=499}.Fresh,"Preview freshness boundaries are incorrect");
var previewPipe="DS5Link.InputPreview.Test."+Guid.NewGuid().ToString("N");
using(var server=new NamedPipeServerStream(previewPipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
 var client=BridgeClient.ReadInputPreviewAsync(previewPipe);await server.WaitForConnectionAsync();
 using var reader=new StreamReader(server,Encoding.UTF8,leaveOpen:true);var request=JsonDocument.Parse((await reader.ReadLineAsync())!);
 Check(request.RootElement.GetProperty("command").GetString()=="getInputPreview","Preview requested a mutating command");
 var response=JsonSerializer.Serialize(new {protocolVersion=1,id=request.RootElement.GetProperty("id").GetString(),ok=true,payload=new {enabled=true,ageMs=10,leftX=140,leftY=128,rightX=255,rightY=0,click=true,touches=new[]{new {active=true,x=1000,y=500}}}});
 await server.WriteAsync(Encoding.UTF8.GetBytes(response+"\n"));await server.FlushAsync();var result=await client;
 Check(result.Fresh && result.LeftX==140 && result.RightY==0 && result.Click && result.Touches.Single().X==1000,"Preview coordinates were not decoded");
}
Check(new EffectOptions().Valid && !new EffectOptions().Enabled,"Effects must default to native game control");
Check(!new TriggerOptions {Mode="weapon",Start=8}.Valid && !new TriggerOptions {Strength=9}.Valid && !new EffectOptions {Limit=81}.Valid,"Invalid effect ranges accepted");
Check(!new EffectOptions {LedMode="unknown"}.Valid && new EffectOptions {Left=new(){Mode="weapon",Start=2,End=8}}.Valid,"Effect mode validation incorrect");
var effectJson=JsonSerializer.Serialize(new EffectOptions {Left=new(){Mode="pulse"}}.Wire());using(var effectDoc=JsonDocument.Parse(effectJson))Check(effectDoc.RootElement.GetProperty("left").GetProperty("mode").GetString()=="pulse" && effectDoc.RootElement.GetProperty("limit").GetInt32()==35,"Effect protocol casing incorrect");
byte[] Signal(int frames,double hz,double amplitude,int rate=48000){var data=new byte[frames*2*4];for(int frame=0;frame<frames;frame++){var value=(float)(Math.Sin(2*Math.PI*hz*frame/rate)*amplitude);var bytes=BitConverter.GetBytes(value);Buffer.BlockCopy(bytes,0,data,frame*8,4);Buffer.BlockCopy(bytes,0,data,frame*8+4,4);}return data;}
var waveform=new AudioWaveform();var waveOptions=new EffectOptions {AudioEnabled=true,BassOnly=false,Gate=0,Limit=35};
var silentFrames=waveform.Process(new byte[4800*8],48000,2,32,true,waveOptions,1000);
Check(silentFrames.Length==9 && silentFrames.All(f=>f.Samples.All(v=>v==0)),"Silence generated a haptic waveform");
waveform.Reset();var signed=waveform.Process(Signal(4800,100,0.1),48000,2,32,true,waveOptions,1000).SelectMany(f=>f.Samples).ToArray();
Check(signed.Any(v=>v>0) && signed.Any(v=>v<0) && signed.Max(v=>Math.Abs((int)v))<=45,"Audio sign or output limit was lost");
Check(Math.Abs(signed.Average(v=>(int)v))<2,"Waveform has a DC motor bias");
waveform.Reset();var leftOnly=Signal(4800,80,0.03);for(int frame=0;frame<4800;frame++)Array.Clear(leftOnly,frame*8+4,4);
var stereo=waveform.Process(leftOnly,48000,2,32,true,waveOptions,1000).SelectMany(f=>f.Samples).ToArray();Check(stereo.Where((v,i)=>i%2==0).Any(v=>v!=0) && stereo.Where((v,i)=>i%2==1).All(v=>v==0),"Stereo haptic channels were mixed");
waveform.Reset();var constant=new byte[48000*8];var half=BitConverter.GetBytes(0.5f);for(int i=0;i<constant.Length;i+=4)Buffer.BlockCopy(half,0,constant,i,4);
var dc=waveform.Process(constant,48000,2,32,true,waveOptions,1000);Check(dc.TakeLast(10).All(f=>f.Samples.All(v=>v==0)),"Constant audio produced sustained haptic drive");
waveform.Reset();waveform.Process(Signal(4800,100,0.1),48000,2,32,true,waveOptions,1000);var stopped=waveform.Process(new byte[4800*8],48000,2,32,true,waveOptions,1100);Check(stopped.TakeLast(3).All(f=>f.Samples.All(v=>v==0)),"Last sound was held after silence");
waveform.Reset();int count=waveform.Process(Signal(48000,100,0.02),48000,2,32,true,waveOptions,1000).Length;Check(count==93,"Waveform stream has the wrong sample rate");
waveform.Reset();var noisy=new byte[8];Buffer.BlockCopy(BitConverter.GetBytes(float.NaN),0,noisy,0,4);Check(waveform.Process(noisy,48000,2,32,true,waveOptions,1000).Length==0,"Invalid audio made an output frame");
var autoApply=new LatestSettingsApplier();var applied=new List<int>();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releasePending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
autoApply.Queue("effects",async()=>{entered.SetResult();await releasePending.Task;applied.Add(1);});var applying=autoApply.FlushAsync();await entered.Task;
autoApply.Queue("effects",()=>{applied.Add(2);return Task.CompletedTask;});autoApply.Queue("effects",()=>{applied.Add(3);return Task.CompletedTask;});
Check(ReferenceEquals(applying,autoApply.FlushAsync()),"Auto-apply started concurrent workers");releasePending.SetResult();await applying;
Check(applied.SequenceEqual(new[]{1,3}) && !autoApply.HasPending,"Auto-apply replayed an old draft or lost the latest draft");
var retrySettings=new LatestSettingsApplier();int attempts=0;
retrySettings.Queue("input",()=>{if(++attempts==1)throw new IOException("temporary");return Task.CompletedTask;});
try{await retrySettings.FlushAsync();}catch(IOException){}
Check(retrySettings.HasPending,"Failed settings disappeared");await retrySettings.FlushAsync();
Check(attempts==2 && !retrySettings.HasPending,"Failed settings were not retried");
using(var failedCapture=new AudioReactiveCapture()){
 failedCapture.Start(new EffectOptions{AudioEnabled=true,AudioDeviceId="DS5Link-nonexistent-test-endpoint"});
 Check(!failedCapture.IsRunning && !string.IsNullOrEmpty(failedCapture.Error),"Failed capture reported running");
}
foreach(bool save in new[]{true,false}){
 var pipeName="DS5Link.Effect.Test."+Guid.NewGuid().ToString("N");using var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
 var client=save?BridgeClient.SetEffectOptionsAsync(new EffectOptions {LedMode="pulse"},pipeName):BridgeClient.ReadEffectsAsync(17,pipeName,audioFrames:new[]{new HapticFrame(new sbyte[]{-127,0,127},1234)});await server.WaitForConnectionAsync();
 using var reader=new StreamReader(server,Encoding.UTF8,leaveOpen:true);using var request=JsonDocument.Parse((await reader.ReadLineAsync())!);var root=request.RootElement;
 Check(root.GetProperty("command").GetString()==(save?"setEffectOptions":"getEffects"),"Wrong effects command");
 Check(save?root.GetProperty("options").GetProperty("ledMode").GetString()=="pulse":root.GetProperty("audioLevel").GetInt32()==17,"Effects request payload incorrect");
 if(!save){var frame=root.GetProperty("audioFrames")[0];Check(frame.GetProperty("samples")[0].GetInt32()==-127 && frame.GetProperty("samples")[2].GetInt32()==127 && frame.GetProperty("tick").GetInt64()==1234,"Signed PCM wire format corrupted");}
 var response=JsonSerializer.Serialize(new {protocolVersion=1,id=root.GetProperty("id").GetString(),ok=true,payload=new {options=new EffectOptions().Wire(),ledOwned=true,leftOwned=false,rightOwned=true,rumbleOwned=false,leaseActive=true,batteryPercent=45}});
 await server.WriteAsync(Encoding.UTF8.GetBytes(response+"\n"));await server.FlushAsync();var result=await client;Check(result.LedOwned && result.RightOwned && result.BatteryPercent==45 && result.Options.Limit==35,"Effects ownership reply not decoded");
}
if(args.Contains("--audio-smoke")){
 var devices=AudioReactiveCapture.Devices();using var capture=new AudioReactiveCapture();capture.Start(new EffectOptions {AudioEnabled=true});await Task.Delay(1200);
 Check(string.IsNullOrEmpty(capture.Error),"Real output loopback initialization failed: "+capture.Error);
 Console.WriteLine(JsonSerializer.Serialize(capture.Diagnostics));
 Console.WriteLine("Read-only WASAPI smoke: "+devices.Length+" output choices; capture initialized; no controller output or audio recording.");
}
Console.WriteLine("57 managed protocol/diagnostic/input/waveform/auto-apply scenarios passed");

await GameProfileTests.Run();

var partialTouch=new InputProfile {Bindings=new(){["tap"]=new(0x41,0)}};
Check(partialTouch.RuntimeOptions.TouchClickMask==0,"Gesture binding blocks native map click");
partialTouch.Bindings["leftClick"]=new(0x42,0);Check(partialTouch.RuntimeOptions.TouchClickMask==1,"Unbound side blocked");
partialTouch.Bindings["rightClick"]=new(0x43,0);Check(partialTouch.RuntimeOptions.TouchClickMask==3,"Both click bindings missing");
Console.WriteLine("3 partial touch binding scenarios passed");

L.SetLanguage("en");
Check(L.T("Настройки контроллера")=="Controller settings","English catalog missing");
Check(L.F("Вход {0:F1}% · haptics {1}%",12.5,25)=="Input 12.5% · haptics 25%","Localized interpolation corrupted");
Check(L.ByKey(L.Key("Общие настройки"))=="General settings","XAML language key differs");
Check(InputSettingsWindow.Labels[0]=="Left click","Language switch retained cached Russian labels");
Check(L.HasPrefix("Нет связи со службой","Нет связи") && L.HasPrefix("Disconnected","Нет связи") && L.HasPrefix("Cannot reach the service. Check the DS5Link installation.","Нет связи"),"Localized error classification failed");
var languageSettings=JsonSerializer.Deserialize<Settings>("{\"SchemaVersion\":1,\"FirstRun\":false,\"Language\":\"en\"}")!;
Check(languageSettings.Language=="en" && !languageSettings.FirstRun,"Language preference broke existing settings");
using(var languageStream=typeof(L).Assembly.GetManifestResourceStream("DS5Link.Strings.en.json")!){
 var catalog=JsonSerializer.Deserialize<Dictionary<string,string>>(languageStream)!;
 foreach(var entry in catalog){
  Check(!string.IsNullOrWhiteSpace(entry.Value),"Empty English translation");
  var original=System.Text.RegularExpressions.Regex.Matches(entry.Key,@"\{\d+[^}]*\}").Select(m=>m.Value).Order().ToArray();
  var translated=System.Text.RegularExpressions.Regex.Matches(entry.Value,@"\{\d+[^}]*\}").Select(m=>m.Value).Order().ToArray();
  Check(original.SequenceEqual(translated),"Translation changed placeholders: "+entry.Key);
 }
}
L.SetLanguage("invalid");Check(L.Language=="ru" && L.T("Настройки контроллера")=="Настройки контроллера" && InputSettingsWindow.Labels[0]=="Нажатие слева","Russian fallback or reverse switch failed");
Console.WriteLine("Language catalog, formatting, settings and reverse-switch checks passed");
