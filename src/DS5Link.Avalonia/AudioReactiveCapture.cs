using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace DS5Link;
internal sealed record AudioOutputDevice(string Id,string Name){public override string ToString()=>Name;}
internal sealed record HapticFrame(sbyte[] Samples,long Tick);
// Continuous stereo waveform, DC removal and anti-alias filtering before
// resampling to the controller's 3000 Hz / signed 8-bit haptic stream.
internal sealed class AudioWaveform {
 readonly sbyte[] frame=new sbyte[64];int used,rate;double phase;
 double dcL,dcR,lowL,lowR,antiL,antiR;
 internal double RawPeak {get;private set;}internal double FilteredRms {get;private set;}
 internal int Level {get;private set;}
 internal void Reset(){used=rate=0;phase=dcL=dcR=lowL=lowR=antiL=antiR=0;RawPeak=FilteredRms=0;Level=0;Array.Clear(frame);}
 static double Read(ReadOnlySpan<byte> data,int bits,bool floating){
  double value=bits switch{16=>System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(data)/32768.0,24=>(((data[0]|data[1]<<8|data[2]<<16)<<8)>>8)/8388608.0,_=>floating?BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data)):System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data)/2147483648.0};
  return double.IsFinite(value)?Math.Clamp(value,-1,1):0;
 }
 internal HapticFrame[] Process(ReadOnlySpan<byte> data,int sampleRate,int channels,int bits,bool floating,EffectOptions options,long tick){
  if(sampleRate<8000 || sampleRate>384000 || channels is <1 or >32 || bits is not(16 or 24 or 32) || floating && bits!=32)throw new IOException(L.T("Неподдерживаемый формат звука."));
  if(rate!=sampleRate){Reset();rate=sampleRate;}
  int bytes=bits/8,frames=data.Length/(channels*bytes);if(frames==0){RawPeak=0;Level=0;return Array.Empty<HapticFrame>();}
  var output=new List<HapticFrame>();double peak=0,sum=0,outputPeak=0;
  double dcAlpha=1-Math.Exp(-2*Math.PI*20/sampleRate),lowAlpha=1-Math.Exp(-2*Math.PI*(options.BassOnly?180:900)/sampleRate);
  double gate=options.Gate==0?0:Math.Pow(10,(-80+options.Gate*2)/20.0),gain=options.Sensitivity/100.0*12,limit=options.Limit/100.0;
  for(int i=0;i<frames;i++){
   int offset=i*channels*bytes;double left=Read(data.Slice(offset,bytes),bits,floating),right=Read(data.Slice(offset+(channels>1?bytes:0),bytes),bits,floating);
   peak=Math.Max(peak,Math.Max(Math.Abs(left),Math.Abs(right)));
   dcL+=dcAlpha*(left-dcL);dcR+=dcAlpha*(right-dcR);left-=dcL;right-=dcR;
   lowL+=lowAlpha*(left-lowL);lowR+=lowAlpha*(right-lowR);antiL+=lowAlpha*(lowL-antiL);antiR+=lowAlpha*(lowR-antiR);
   sum+=(antiL*antiL+antiR*antiR)/2;
   phase+=3000;
   if(phase>=sampleRate){
    phase-=sampleRate;
    double l=Math.Abs(antiL)<gate?0:Math.Clamp(antiL*gain,-limit,limit),r=Math.Abs(antiR)<gate?0:Math.Clamp(antiR*gain,-limit,limit);
    outputPeak=Math.Max(outputPeak,Math.Max(Math.Abs(l),Math.Abs(r)));
    frame[used++]=(sbyte)Math.Round(l*127);frame[used++]=(sbyte)Math.Round(r*127);
    if(used==64){output.Add(new((sbyte[])frame.Clone(),tick));used=0;}
   }
  }
  RawPeak=peak;FilteredRms=Math.Sqrt(sum/frames);Level=(int)Math.Round(outputPeak*100);return output.ToArray();
 }
}
internal sealed class FastLoopbackCapture:WasapiCapture {
 internal FastLoopbackCapture(MMDevice device):base(device,false,20){}
 protected override AudioClientStreamFlags GetAudioClientStreamFlags()=>AudioClientStreamFlags.Loopback;
}
internal sealed class AudioReactiveCapture:IDisposable {
 internal const string GameSource="ds5link:game-process";
 ProcessLoopback? processCapture;
 internal uint ProcessId {get;private set;}
 string endpointId="";long nextEndpointCheck;
 internal bool DefaultEndpointChanged(){
  if(Environment.TickCount64<nextEndpointCheck)return false;nextEndpointCheck=Environment.TickCount64+1000;
  try{using var e=new MMDeviceEnumerator();using var d=e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);return d.ID!=endpointId;}catch{return true;}
 }
 readonly object sync=new();readonly AudioWaveform waveform=new();readonly Queue<HapticFrame> frames=new();
 FastLoopbackCapture? capture;MMDevice? device;EffectOptions options=new();
 bool running;
 internal bool IsRunning {get{lock(sync)return running;}}
 long lastSample,packets;string deviceName="",formatName="",error="";
 internal object Diagnostics {get{lock(sync)return new{device=deviceName,format=formatName,packets,peak=waveform.RawPeak,bassRms=waveform.FilteredRms,level=waveform.Level,queued=frames.Count,error};}}
 internal double SignalPeak {get{lock(sync)return Fresh?waveform.RawPeak:0;}}
 internal string SourceName=>options.AudioDeviceId==GameSource && ProcessId==0
  ?L.T("Ожидание игры из профиля"):deviceName;
 internal string Error {get{lock(sync)return error;}}
 bool Fresh=>lastSample!=0 && Stopwatch.GetElapsedTime(lastSample).TotalMilliseconds<100;
 internal int Level {get{lock(sync)return Fresh?waveform.Level:0;}}
 internal long SampleTick=>Environment.TickCount64;
 internal HapticFrame[] TakeFrames(){lock(sync){var now=Environment.TickCount64;while(frames.Count>0 && now-frames.Peek().Tick>=80)frames.Dequeue();var result=frames.Take(4).ToArray();for(int i=0;i<result.Length;i++)frames.Dequeue();return result;}}
 internal static AudioOutputDevice[] Devices(){using var e=new MMDeviceEnumerator();var list=new List<AudioOutputDevice>{new("",L.T("Устройство вывода Windows")),new(GameSource,L.T("Только звук игры из профиля"))};foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){using(d)list.Add(new(d.ID,d.FriendlyName));}return list.ToArray();}
 internal void UpdateOptions(EffectOptions value){lock(sync)options=value;}
 internal void Start(EffectOptions value,uint gamePid=0){
  Stop();lock(sync){options=value;error="";}if(!value.AudioEnabled)return;
  if(value.AudioDeviceId==GameSource){
   if(gamePid==0)return;
   ProcessLoopback? source=null;
   source=new ProcessLoopback(gamePid,(buffer,length)=>{
    lock(sync){if(ReferenceEquals(processCapture,source))Push(buffer,length,ProcessLoopback.Format);}
   },failure=>{
    lock(sync){if(ReferenceEquals(processCapture,source)){running=false;frames.Clear();lastSample=0;error=failure.Message;}}
   });
   lock(sync){
    processCapture=source;ProcessId=gamePid;running=true;
    deviceName=L.T("Только звук игры из профиля");formatName=ProcessLoopback.Format.ToString();packets=0;
   }
   source.Start();
   return;
  }
  try{using var e=new MMDeviceEnumerator();device=string.IsNullOrEmpty(value.AudioDeviceId)?e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):e.GetDevice(value.AudioDeviceId);
   endpointId=device.ID;deviceName=device.FriendlyName;capture=new FastLoopbackCapture(device);formatName=capture.WaveFormat.ToString();packets=0;capture.DataAvailable+=Data;capture.RecordingStopped+=Stopped;lock(sync)running=true;capture.StartRecording();
  }catch(Exception ex){Stop();lock(sync)error=ex.Message;}
 }
 void Data(object? sender,WaveInEventArgs args){lock(sync){if(!ReferenceEquals(sender,capture) || capture==null || args.BytesRecorded<=0)return;
  Push(args.Buffer,args.BytesRecorded,capture.WaveFormat);
 }}
 void Push(byte[] buffer,int length,WaveFormat f){
  try{packets++;bool floating=f.Encoding==WaveFormatEncoding.IeeeFloat || f is WaveFormatExtensible ext && ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
   foreach(var frame in waveform.Process(buffer.AsSpan(0,length),f.SampleRate,f.Channels,f.BitsPerSample,floating,options,Environment.TickCount64)){while(frames.Count>=4)frames.Dequeue();frames.Enqueue(frame);}lastSample=Stopwatch.GetTimestamp();
  }catch(Exception ex){frames.Clear();waveform.Reset();error=ex.Message;}
 }
 void Stopped(object? sender,StoppedEventArgs args){lock(sync){if(!ReferenceEquals(sender,capture))return;running=false;frames.Clear();waveform.Reset();lastSample=0;if(args.Exception!=null)error=args.Exception.Message;}}
 internal void Stop(){FastLoopbackCapture? old;MMDevice? oldDevice;ProcessLoopback? oldProcess;lock(sync){old=capture;oldDevice=device;oldProcess=processCapture;processCapture=null;ProcessId=0;capture=null;device=null;running=false;frames.Clear();waveform.Reset();lastSample=0;}
  oldProcess?.Dispose();
  if(old!=null){old.DataAvailable-=Data;old.RecordingStopped-=Stopped;try{old.StopRecording();}catch(Exception ex){lock(sync)error=ex.Message;}finally{try{old.Dispose();}catch(Exception ex){lock(sync)error=ex.Message;}}}try{oldDevice?.Dispose();}catch(Exception ex){lock(sync)error=ex.Message;}
 }
 public void Dispose()=>Stop();
}
