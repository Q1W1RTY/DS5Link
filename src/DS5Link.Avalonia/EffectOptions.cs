using System;
using System.IO;
using System.Text.Json;
namespace DS5Link;
internal sealed record TriggerOptions {
 public string Mode {get;init;}="game";
 public int Strength {get;init;}=3;
 public int Start {get;init;}=2;
 public int End {get;init;}=7;
 public int Frequency {get;init;}=15;
 public bool Valid=>Mode is "game" or "resistance" or "weapon" or "pulse" && Strength is >=1 and <=8 && Start is >=0 and <=9 && End is >=1 and <=9 && Frequency is >=1 and <=40 && (Mode!="weapon" || Start is >=2 and <=7 && End>Start && End<=8);
}
internal sealed record EffectOptions {
 public TriggerOptions Left {get;init;}=new();public TriggerOptions Right {get;init;}=new();
 public string LedMode {get;init;}="game";
 public int Red {get;init;}=10;public int Green {get;init;}=132;public int Blue {get;init;}=255;
 public int Brightness {get;init;}=60;public int Speed {get;init;}=50;
 public bool AudioEnabled {get;init;}public bool BassOnly {get;init;}=true;
 public int Sensitivity {get;init;}=100;public int Limit {get;init;}=35;public int Gate {get;init;}=3;
 public string AudioDeviceId {get;init;}="";
 public bool Enabled=>Left.Mode!="game" || Right.Mode!="game" || LedMode!="game" || AudioEnabled;
 public bool Valid=>Left!=null && Right!=null && Left.Valid && Right.Valid && LedMode is "game" or "fixed" or "pulse" or "rainbow" or "battery" && Red is >=0 and <=255 && Green is >=0 and <=255 && Blue is >=0 and <=255 && Brightness is >=1 and <=100 && Speed is >=1 and <=100 && Sensitivity is >=10 and <=300 && Limit is >=1 and <=80 && Gate is >=0 and <=20 && AudioDeviceId!=null && AudioDeviceId.Length<=2048;
 internal static EffectOptions Load(){try{var value=JsonSerializer.Deserialize<EffectOptions>(File.ReadAllText(Path.Combine(Settings.DirectoryPath,"effects.json")));return value?.Valid==true?value:new();}catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){return new();}}
 internal void Save(){if(!Valid)throw new IOException(L.T("Проверьте настройки эффектов."));Directory.CreateDirectory(Settings.DirectoryPath);var path=Path.Combine(Settings.DirectoryPath,"effects.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,BridgeClient.JsonOptions));File.Move(path+".tmp",path,true);}
 internal object Wire()=>new {left=new {mode=Left.Mode,strength=Left.Strength,start=Left.Start,end=Left.End,frequency=Left.Frequency},right=new {mode=Right.Mode,strength=Right.Strength,start=Right.Start,end=Right.End,frequency=Right.Frequency},ledMode=LedMode,red=Red,green=Green,blue=Blue,brightness=Brightness,speed=Speed,audioEnabled=AudioEnabled,bassOnly=BassOnly,sensitivity=Sensitivity,limit=Limit,gate=Gate};
}
internal sealed record EffectState {
 public bool Active {get;init;}
 public int? BatteryPercent {get;init;}
 public int[] LedRgb {get;init;}=[];
 public EffectOptions Options {get;init;}=new();
 public bool LeftOwned {get;init;}public bool RightOwned {get;init;}public bool LedOwned {get;init;}public bool RumbleOwned {get;init;}public bool LeaseActive {get;init;}
}
