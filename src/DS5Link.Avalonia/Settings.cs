using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DS5Link;
internal sealed class Settings {
 public int SchemaVersion {get;set;}=1;
 public string Language {get;set;}="ru";
 public bool FirstRun {get;set;}=true;
 public string? LastNotifiedUpdate {get;set;}
 public static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DS5Link");
 public static Settings Load() {
  try {var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DirectoryPath,"settings.json")));return settings?.SchemaVersion==1?settings:new();}
  catch(IOException){return new();}catch(JsonException){return new();}catch(UnauthorizedAccessException){return new();}
 }
 public void Save() {Directory.CreateDirectory(DirectoryPath);var path=Path.Combine(DirectoryPath,"settings.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,BridgeClient.JsonOptions));File.Move(path+".tmp",path,true);}
 public static bool AutoStart {
  get {using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");return key?.GetValue("DS5Link") is string;}
  set {using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(value)key.SetValue("DS5Link","\""+Environment.ProcessPath+"\" --background");else key.DeleteValue("DS5Link",false);}
 }
}
