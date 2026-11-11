using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace DS5Link;
internal sealed record AxisCalibration(int Minimum=0, int Center=128, int Maximum=255)
{
 internal bool Valid => Center is >=32 and <=223 && Minimum>=0 && Minimum<=Center-32 &&
  Maximum<=255 && Maximum>=Center+32;
 internal int Apply(int raw)
 {
  var value=Math.Clamp(raw,Minimum,Maximum);
  return value<Center ? (value-Minimum)*128/(Center-Minimum) : 128+(value-Center)*127/(Maximum-Center);
 }
}
internal sealed record TriggerStage(int Target=-1, int Threshold=192)
{
 internal bool Valid => Target is >=-1 and <14 && Threshold is >=32 and <=255;
}
internal sealed record InputMapping
{
 internal static readonly string[] ButtonNames={"□","×","○","△","L1","R1","Create","Options","L3","R3","↑","→","↓","←"};
 public string Buttons {get;init;}="--------------";
 public int Suppress {get;init;}
 public AxisCalibration LeftX {get;init;}=new();
 public AxisCalibration LeftY {get;init;}=new();
 public AxisCalibration RightX {get;init;}=new();
 public AxisCalibration RightY {get;init;}=new();
 public TriggerStage LeftStage {get;init;}=new();
 public TriggerStage RightStage {get;init;}=new();
 internal bool Valid => Buttons?.Length==ButtonNames.Length && Buttons.All(c=>c=='-' || c is >= 'A' and <= 'N') &&
  Suppress is >=0 and <16384 && LeftX?.Valid==true && LeftY?.Valid==true &&
  RightX?.Valid==true && RightY?.Valid==true && LeftStage?.Valid==true && RightStage?.Valid==true;
}
internal sealed record InputOptions {
 public int LeftDeadzone {get;init;}
 public int RightDeadzone {get;init;}
 public bool TouchEnabled {get;init;}
 public bool ConsumeTouchpad {get;init;}=true;
 public int TouchClickMask {get;init;}=3;
 public InputMapping Mapping {get;init;}=new();
}
internal sealed record KeyBinding(int VirtualKey,int Modifiers) {
 public bool Valid => (VirtualKey is >=0x30 and <=0x39 or >=0x41 and <=0x5A or >=0x70 and <=0x7B or 0x20 or 0x0D or 0x1B or 0x09 or >=0x21 and <=0x28 or 0x2D or 0x2E) && (Modifiers&~7)==0;
 public string Label => string.Join("+",new[]{(Modifiers&2)!=0?"Ctrl":null,(Modifiers&1)!=0?"Alt":null,(Modifiers&4)!=0?"Shift":null,KeyInterop.KeyFromVirtualKey(VirtualKey).ToString()}.Where(s=>s!=null));
}
internal sealed class InputProfile {
 public int SchemaVersion {get;set;}=1;
 public InputOptions Options {get;set;}=new();
 public Dictionary<string,KeyBinding> Bindings {get;set;}=new();
 internal static readonly string[] Actions={"leftClick","rightClick","tap","swipeLeft","swipeRight","swipeUp","swipeDown"};
 internal InputOptions RuntimeOptions=>Options with {TouchClickMask=(Bindings.ContainsKey("leftClick")?1:0)|(Bindings.ContainsKey("rightClick")?2:0)};
 internal bool Valid => SchemaVersion==1 && Options?.Mapping?.Valid==true && Options.LeftDeadzone is >=0 and <=30 && Options.RightDeadzone is >=0 and <=30 && Bindings!=null && Bindings.Count<=7 && Bindings.All(p=>Actions.Contains(p.Key) && p.Value!=null && p.Value.Valid) && (!Options.TouchEnabled || Bindings.Count>0);
 internal static InputProfile Load() {
  try {var value=JsonSerializer.Deserialize<InputProfile>(File.ReadAllText(Path.Combine(Settings.DirectoryPath,"input-profile.json")));return value?.Valid==true?value:new();}
  catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){return new();}
 }
 internal void Save() {
  if(!Valid)throw new IOException(L.T("Проверьте назначения и мёртвые зоны. Для тачпада нужно хотя бы одно назначение."));
  Directory.CreateDirectory(Settings.DirectoryPath);var path=Path.Combine(Settings.DirectoryPath,"input-profile.json");
  File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,BridgeClient.JsonOptions));File.Move(path+".tmp",path,true);
 }
}
internal sealed record TouchInputEvent {
 public long Sequence {get;init;}
 public string Action {get;init;}="";
 public long AgeMs {get;init;}
}
internal sealed record TouchInputBatch {
 public string Epoch {get;init;}="";
 public long Sequence {get;init;}
 public bool Enabled {get;init;}
 public TouchInputEvent[] Events {get;init;}=Array.Empty<TouchInputEvent>();
}
internal sealed class InputEventCursor {
 string? epoch;public long Sequence {get;private set;}
 internal void Reset(){epoch=null;Sequence=0;}
 internal TouchInputEvent[] Read(TouchInputBatch batch) {
  if(string.IsNullOrEmpty(batch.Epoch)){Reset();return Array.Empty<TouchInputEvent>();}
  if(epoch!=batch.Epoch || batch.Sequence<Sequence){epoch=batch.Epoch;Sequence=batch.Sequence;return Array.Empty<TouchInputEvent>();}
  var fresh=batch.Enabled?batch.Events.Where(e=>e.Sequence>Sequence && e.Sequence<=batch.Sequence && e.AgeMs is >=0 and <=150).OrderBy(e=>e.Sequence).DistinctBy(e=>e.Sequence).ToArray():Array.Empty<TouchInputEvent>();
  Sequence=batch.Sequence;return fresh;
 }
}

internal sealed record TouchPreview { public bool Active {get;init;} public int X {get;init;} public int Y {get;init;} }
internal sealed record InputPreview {
 public bool Enabled {get;init;} public long AgeMs {get;init;}=-1;
 public int LeftX {get;init;}=128; public int LeftY {get;init;}=128;
 public int RightX {get;init;}=128; public int RightY {get;init;}=128;
 public bool Click {get;init;} public TouchPreview[] Touches {get;init;}=Array.Empty<TouchPreview>();
 public bool Fresh=>Enabled && AgeMs is >=0 and <500;
}
