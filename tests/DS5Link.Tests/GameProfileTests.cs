using DS5Link;
using System.IO;
using System.Text.Json;

internal static class GameProfileTests {
 internal static async Task Run(){
  void Check(bool value,string message){if(!value)throw new Exception(message);}
  string directory=Path.Combine(Path.GetTempPath(),"DS5Link-profiles-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  try{
   string path=Path.Combine(directory,"profiles.json");
   var input=new InputProfile {Options=new(){LeftDeadzone=12},Bindings=new(){["tap"]=new(0x41,2)}};
   var effects=new EffectOptions {Left=new(){Mode="resistance",Strength=7},AudioDeviceId="endpoint-test"};
   var catalog=GameProfiles.Load(path,input,effects);
   Check(catalog.Default.Input.Options.LeftDeadzone==12 && catalog.Default.Effects.Left.Strength==7 && !File.Exists(path),"Migration lost legacy settings or wrote before a user change");
   var mapping=new InputMapping{Buttons="-E------------",Suppress=2,LeftX=new(12,137,245),RightStage=new(3,180)};
   var game=new GameProfile {Executable=@"C:\Games\One\game.exe",Input=new(){Options=new(){RightDeadzone=20,Mapping=mapping}},Effects=new(){LedMode="rainbow"}};
   catalog=catalog.Put(game).Put(game with {Executable=@"D:\Games\Two\game.exe",Effects=effects});catalog.Save(path);
   catalog=GameProfiles.Load(path,new(),new());
   Check(catalog.Find(game.Executable).Input.RuntimeOptions.Mapping==mapping &&
         catalog.Default.Input.RuntimeOptions.Mapping==new InputMapping(),"Per-game input mapping was lost or leaked into defaults");
   Check(catalog.Games.Count==2 && catalog.Default.Input.Bindings["tap"].Modifiers==2 && catalog.Default.Effects.AudioDeviceId=="endpoint-test","Profile round trip lost bindings/effects");
   Check(catalog.Find(@"c:\games\one\GAME.EXE").Effects.LedMode=="rainbow" && catalog.Find(@"D:\Games\Two\game.exe").Effects.Left.Strength==7,"Exact full-path matching or case handling broken");
   Check(ReferenceEquals(catalog.Find(@"E:\Other\game.exe"),catalog.Default) && ReferenceEquals(catalog.Find(null),catalog.Default),"Same filename or inaccessible foreground incorrectly activates a game");
   var replacement=game with {Effects=new(){LedMode="fixed"}};
   var updated=catalog.Put(replacement);
   Check(updated.Games.Count==2 && catalog.Find(game.Executable).Effects.LedMode=="rainbow" && updated.Default.Input.Options.LeftDeadzone==12,"Editing a game modified defaults or original catalog");
   Check(updated.Remove(game.Executable).Find(game.Executable).Executable=="","Deletion did not restore defaults");
   string original=File.ReadAllText(path);
   try{(catalog with {Games=new(){game,game with {Executable=game.Executable.ToUpperInvariant()}}}).Save(path);throw new Exception("Duplicate accepted");}catch(IOException){}
   Check(File.ReadAllText(path)==original,"Invalid save damaged existing profiles");
   foreach(string malformed in new[]{"{", "null", "{\"SchemaVersion\":99}","{\"Games\":null}","{\"Default\":{\"Input\":null}}"}){
    File.WriteAllText(path,malformed);
    try{GameProfiles.Load(path,new(),new());throw new Exception("Corrupt profiles accepted");}catch(Exception error)when(error is IOException or JsonException){}
    Check(File.ReadAllText(path)==malformed,"Corrupt file was silently overwritten");
   }
   Check(!GameProfiles.ValidExecutable("game.exe") && !GameProfiles.ValidExecutable(@"C:\Games\game.cmd"),"Invalid executable binding accepted");
   var previous=new GameProfile {Input=input,Effects=effects};var calls=new List<string>();
   await ProfileApplication.ApplyAsync(game,previous,p=>{calls.Add("input");return Task.CompletedTask;},p=>{calls.Add("effects");return Task.CompletedTask;});
   Check(calls.SequenceEqual(new[]{"input","effects"}),"Profile did not apply both channels");
   calls.Clear();
   try{
    await ProfileApplication.ApplyAsync(game,previous,p=>{calls.Add(ReferenceEquals(p,input)?"restore-input":"new-input");return Task.CompletedTask;},p=>{if(ReferenceEquals(p,game.Effects)){calls.Add("fail-effects");throw new IOException("simulated service failure");}calls.Add("restore-effects");return Task.CompletedTask;});
    throw new Exception("Profile failure swallowed");
   }catch(IOException){}
   Check(calls.SequenceEqual(new[]{"new-input","fail-effects","restore-input","restore-effects"}),"Partial switch did not restore both channels");
   bool effectRestored=false;
   try{await ProfileApplication.ApplyAsync(game,previous,p=>throw new IOException("input offline"),p=>{effectRestored=true;return Task.CompletedTask;});throw new Exception("Rollback error swallowed");}catch(AggregateException){}
   Check(effectRestored,"Input rollback failure prevented effects rollback");
  }finally{Directory.Delete(directory,true);}
  Console.WriteLine("13 game-profile persistence/matching/migration/rollback scenarios passed");
 }
}
