using System;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DS5Link;
internal sealed record BridgeState {
 public string State {get;init;}="Waiting";
 public string Mode {get;init;}="ps5";
 public bool Enabled {get;init;}
 public bool UsbConnected {get;init;}
 public bool BluetoothDisconnected {get;init;}
 public bool? AutoDisconnectEnabled {get;init;}
 public string Transport {get;init;}="Bluetooth";
 public int? BatteryPercent {get;init;}
 public bool Charging {get;init;}
 public bool Muted {get;init;}
 public bool Headphones {get;init;}
 public bool AudioActive {get;init;}
 public bool MicrophoneActive {get;init;}
 public double PacketsPerSecond {get;init;}
 public long GapsOver10ms {get;init;}
 public double MaxGapMs {get;init;}
 public double ObservedSeconds {get;init;}
 public int? Rssi {get;init;}
 public string Error {get;init;}="";
 public EffectState Effects {get;init;}=new();
 public InputOptions InputOptions {get;init;}=new();
 public string[] LogLines {get;init;}=Array.Empty<string>();
}
internal static class BridgeClient {
 internal static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
 public static Task<BridgeState> SendAsync(string command,string pipeName="DS5Link.Control.v1",bool? gameInForeground=null) =>
  SendPayloadAsync<BridgeState>(command,pipeName,gameInForeground:gameInForeground);
 public static Task<BridgeState> SetAutoDisconnectAsync(bool enabled) =>
  SendPayloadAsync<BridgeState>("setAutoDisconnect","DS5Link.Control.v1",new {enabled});
 public static Task<BridgeState> SetInputOptionsAsync(InputOptions options,string pipeName="DS5Link.Control.v1") => SendPayloadAsync<BridgeState>("setInputOptions",pipeName,new {leftDeadzone=options.LeftDeadzone,rightDeadzone=options.RightDeadzone,touchEnabled=options.TouchEnabled,consumeTouchpad=options.ConsumeTouchpad,touchClickMask=options.TouchClickMask});
 public static Task<EffectState> SetEffectOptionsAsync(EffectOptions options,string pipeName="DS5Link.Control.v1")=>SendPayloadAsync<EffectState>("setEffectOptions",pipeName,options.Wire());
 public static Task<EffectState> ReadEffectsAsync(int audioLevel,string pipeName="DS5Link.Control.v1",long audioTick=0,HapticFrame[]? audioFrames=null)=>SendPayloadAsync<EffectState>("getEffects",pipeName,audioLevel:audioLevel,audioTick:audioTick,audioFrames:audioFrames);
 public static Task<InputPreview> ReadInputPreviewAsync(string pipeName="DS5Link.Control.v1") => SendPayloadAsync<InputPreview>("getInputPreview",pipeName);
 public static Task<TouchInputBatch> ReadInputEventsAsync(long after) => SendPayloadAsync<TouchInputBatch>("getInputEvents","DS5Link.Control.v1",afterSequence:after);
 static async Task<T> SendPayloadAsync<T>(string command,string pipeName,object? options=null,long afterSequence=0,int audioLevel=0,long audioTick=0,HapticFrame[]? audioFrames=null,bool? gameInForeground=null) where T:class {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
   using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
   await pipe.ConnectAsync(timeout.Token);
   var id=Guid.NewGuid().ToString("N");
     var request=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {protocolVersion=1,id,command,options,afterSequence,audioLevel,audioTick,audioFrames=audioFrames?.Select(f=>new {samples=f.Samples,tick=f.Tick}),gameInForeground})+"\n");
   await pipe.WriteAsync(request,timeout.Token);await pipe.FlushAsync(timeout.Token);
   using var data=new MemoryStream();var buffer=new byte[2048];bool finished=false;
   while(!finished && data.Length<=65536) {
     var count=await pipe.ReadAsync(buffer,timeout.Token);
     if(count==0)throw new IOException(L.T("Служба закрыла соединение без ответа."));
     for(int i=0;i<count;++i) {if(buffer[i]==10){finished=true;break;}data.WriteByte(buffer[i]);}
   }
   if(data.Length>65536)throw new IOException(L.T("Ответ службы превышает допустимый размер."));
   using var json=JsonDocument.Parse(data.ToArray());var root=json.RootElement;
   if(root.GetProperty("protocolVersion").GetInt32()!=1 || root.GetProperty("id").GetString()!=id)throw new IOException(L.T("Несовместимый ответ службы."));
   if(!root.GetProperty("ok").GetBoolean())throw new IOException(root.GetProperty("error").GetString());
   return root.GetProperty("payload").Deserialize<T>(JsonOptions) ?? throw new IOException(L.T("Пустое состояние службы."));
 }
}
