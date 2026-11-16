using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DS5Link;

internal sealed record GameProfile {
 public string Executable {get;init;}="";
 public InputProfile Input {get;init;}=new();
 public EffectOptions Effects {get;init;}=new();
 public string Label=>Executable.Length==0?L.T("Общие настройки"):Path.GetFileNameWithoutExtension(Executable);
 public override string ToString()=>Label;
 internal bool Valid=>Input?.Valid==true && Effects?.Valid==true && (Executable=="" || GameProfiles.ValidExecutable(Executable));
}

internal sealed record GameProfiles {
 public int SchemaVersion {get;init;}=1;
 public GameProfile Default {get;init;}=new();
 public List<GameProfile> Games {get;init;}=new();
 internal static string FilePath=>Path.Combine(Settings.DirectoryPath,"game-profiles.json");
 internal static bool ValidExecutable(string path)=>!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) && path.Length<=32767;
 internal bool Valid=>SchemaVersion==1 && Default?.Valid==true && Default.Executable=="" && Games!=null && Games.Count<=128 && Games.All(p=>p?.Valid==true && p.Executable!="") && Games.Select(p=>p.Executable).Distinct(StringComparer.OrdinalIgnoreCase).Count()==Games.Count;
 internal GameProfile Find(string? executable)=>Games.FirstOrDefault(p=>string.Equals(p.Executable,executable,StringComparison.OrdinalIgnoreCase))??Default;
 internal GameProfiles Put(GameProfile profile)=>profile.Executable==""?this with {Default=profile}:this with {Games=Games.Where(p=>!string.Equals(p.Executable,profile.Executable,StringComparison.OrdinalIgnoreCase)).Append(profile).ToList()};
 internal GameProfiles Remove(string executable)=>this with {Games=Games.Where(p=>!string.Equals(p.Executable,executable,StringComparison.OrdinalIgnoreCase)).ToList()};
 internal static GameProfiles Load(string path,InputProfile input,EffectOptions effects){
  try{return Read(path);}
  catch(FileNotFoundException){return new(){Default=new(){Input=input,Effects=effects}};}
  catch(DirectoryNotFoundException){return new(){Default=new(){Input=input,Effects=effects}};}
 }
 internal static GameProfiles Read(string path){
  var result=JsonSerializer.Deserialize<GameProfiles>(File.ReadAllText(path),BridgeClient.JsonOptions);
  if(result?.Valid!=true)throw new IOException(L.T("Файл профилей повреждён или имеет неподдерживаемый формат. Исходный файл сохранён."));
  return result;
 }
 internal void Save(string path,bool preservePrevious=false){
  if(!Valid)throw new IOException(L.T("Проверьте профили: нужны уникальные полные пути к EXE, не более 128 игр."));
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{
   File.WriteAllText(temp,JsonSerializer.Serialize(this,BridgeClient.JsonOptions));
   if(preservePrevious && File.Exists(path))
    File.Replace(temp,path,path+".corrupt-"+Guid.NewGuid().ToString("N"));
   else
    File.Move(temp,path,true);
  }
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
