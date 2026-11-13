using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DS5Link;

internal sealed partial class InputSettingsWindow {
 Func<GameProfiles>? readProfiles;
 Func<GameProfile,Task>? saveProfile;
 Func<string,Task>? deleteProfile;
 GameProfile editingProfile=new();
 bool selectingProfile;
 Func<GameProfiles,Task>? replaceProfiles;

 internal void InitializeProfiles(Func<GameProfiles> read,Func<GameProfile,Task> save,Func<string,Task> delete,string active,string error="",Func<GameProfiles,Task>? replace=null){
  readProfiles=read;saveProfile=save;deleteProfile=delete;
  replaceProfiles=replace;
  ExportProfilesButton.Content=L.T("Экспорт");
  ImportProfilesButton.Content=L.T("Импорт");
  ProfileBar.IsEnabled=!preview && error=="";AddProfileButton.IsEnabled=ProfileBar.IsEnabled;
  if(error!=""){InputSettingsNotice.Text=error;InputSettingsNotice.IsVisible=true;SettingsTabs.IsEnabled=false;return;}
  editingProfile=read().Find(active);
  RefreshProfileList();LoadProfileDraft();SetActiveProfile(read().Find(active).Label);
 }
 async void ExportProfiles(object? sender,RoutedEventArgs e)=>await TransferProfiles(false);
 async void ImportProfiles(object? sender,RoutedEventArgs e)=>await TransferProfiles(true);
 async Task TransferProfiles(bool import){
  if(preview || !ProfileBar.IsEnabled || readProfiles==null || replaceProfiles==null)return;
  ProfileBar.IsEnabled=SettingsTabs.IsEnabled=false;
  try{
   if(!await ApplyPendingSettings())return;
   var json=new FilePickerFileType("JSON"){Patterns=new[]{"*.json"}};
   if(import){
    var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{
     Title=L.T("Импорт профилей"),AllowMultiple=false,FileTypeFilter=new[]{json}});
    if(files.Count==0 || closed)return;
    var path=files[0].TryGetLocalPath()??throw new System.IO.IOException(L.T("Выберите локальный файл JSON."));
    if(new System.IO.FileInfo(path).Length>2*1024*1024)throw new System.IO.IOException(L.T("Файл профилей слишком большой."));
    var next=GameProfiles.Load(path,new(),new());
    if(MessageBox.Show(this,L.T("Заменить общие настройки и все игровые профили содержимым файла?"),
        L.T("Импорт профилей"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
    await replaceProfiles(next);
    if(closed)return;
    editingProfile=readProfiles().Find(editingProfile.Executable);
    RefreshProfileList();LoadProfileDraft();
   }else{
    var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{
     Title=L.T("Экспорт профилей"),SuggestedFileName="DS5Link-profiles",DefaultExtension="json",FileTypeChoices=new[]{json}});
    if(file==null || closed)return;
    var path=file.TryGetLocalPath()??throw new System.IO.IOException(L.T("Выберите локальный файл JSON."));
    readProfiles().Save(path);
   }
  }catch(Exception error){
   if(!closed){InputSettingsNotice.Text=error.Message;InputSettingsNotice.IsVisible=true;}
  }finally{
   if(!closed)ProfileBar.IsEnabled=SettingsTabs.IsEnabled=true;
  }
 }
 internal void SetActiveProfile(string label)=>ActiveProfileText.Text=L.T("Сейчас: ")+label;
 void RefreshProfileList(){
  selectingProfile=true;
  try{ProfileSelector.ItemsSource=new[]{readProfiles!().Default}.Concat(readProfiles().Games.OrderBy(p=>p.Label)).ToArray();ProfileSelector.SelectedItem=readProfiles().Find(editingProfile.Executable);}
  finally{selectingProfile=false;}
  RemoveProfileButton.IsEnabled=!preview && editingProfile.Executable!="";
  ProfilePath.Text=editingProfile.Executable==""?L.T("Для приложений без отдельного профиля"):editingProfile.Executable;
 }
 void LoadProfileDraft(){
  bool ready=autoReady;autoReady=false;CancelCapture();
  try{
   var input=editingProfile.Input;bindings.Clear();foreach(var item in input.Bindings)bindings.Add(item.Key,item.Value);
   RefreshTouchSelection();
   TouchBindingsEnabled.IsChecked=input.Options.TouchEnabled;ConsumeTouchpad.IsChecked=input.Options.ConsumeTouchpad;
   LeftDeadzone.Value=input.Options.LeftDeadzone;RightDeadzone.Value=input.Options.RightDeadzone;
   LoadInputMapping(input.Options.Mapping);
   LoadEffectDraft(editingProfile.Effects);
   if(!preview)LoadAudioDevices(editingProfile.Effects.AudioDeviceId);
  }finally{autoReady=ready;}
 }
 async void SelectProfile(object sender,SelectionChangedEventArgs e){
  if(selectingProfile || readProfiles==null || ProfileSelector.SelectedItem is not GameProfile selected || selected.Executable==editingProfile.Executable)return;
  ProfileBar.IsEnabled=false;SettingsTabs.IsEnabled=false;
  try{if(await ApplyPendingSettings()){editingProfile=readProfiles().Find(selected.Executable);LoadProfileDraft();}RefreshProfileList();}
  finally{ProfileBar.IsEnabled=true;SettingsTabs.IsEnabled=true;}
 }
 async void AddGameProfile(object sender,RoutedEventArgs e){
  if(preview || readProfiles==null || saveProfile==null)return;
  ProfileBar.IsEnabled=false;SettingsTabs.IsEnabled=false;
  try{
   if(!await ApplyPendingSettings())return;
   var selectedFiles=await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions {Title=L.T("Выберите EXE самой игры (не лаунчер)"),AllowMultiple=false,FileTypeFilter=new[]{new Avalonia.Platform.Storage.FilePickerFileType("EXE"){Patterns=new[]{"*.exe"}}}});
   if(selectedFiles.Count==0)return;
   var fileName=selectedFiles[0].TryGetLocalPath()??throw new InvalidOperationException("Local file required");
   var existing=readProfiles().Games.FirstOrDefault(p=>string.Equals(p.Executable,fileName,StringComparison.OrdinalIgnoreCase));
   var next=existing??editingProfile with {Executable=System.IO.Path.GetFullPath(fileName)};
   if(existing==null)await saveProfile(next);
   editingProfile=next;RefreshProfileList();LoadProfileDraft();
  }catch(Exception error){InputSettingsNotice.Text=error.Message;InputSettingsNotice.IsVisible=true;}
  finally{ProfileBar.IsEnabled=true;SettingsTabs.IsEnabled=true;}
 }
 async void RemoveGameProfile(object sender,RoutedEventArgs e){
  if(preview || deleteProfile==null || readProfiles==null || editingProfile.Executable=="")return;
  if(MessageBox.Show(this,L.T("Удалить профиль «")+editingProfile.Label+L.T("»? Игра будет использовать общие настройки."),L.T("Профили игр"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  ProfileBar.IsEnabled=false;SettingsTabs.IsEnabled=false;
  try{if(!await ApplyPendingSettings())return;await deleteProfile(editingProfile.Executable);editingProfile=readProfiles().Default;RefreshProfileList();LoadProfileDraft();}
  catch(Exception error){InputSettingsNotice.Text=error.Message;InputSettingsNotice.IsVisible=true;}
  finally{ProfileBar.IsEnabled=true;SettingsTabs.IsEnabled=true;}
 }
 async Task SaveProfileInput(InputProfile input){
  var next=editingProfile with {Input=input};await saveProfile!(next);editingProfile=next;
 }
 async Task SaveProfileEffects(EffectOptions effects){
  var next=editingProfile with {Effects=effects};await saveProfile!(next);editingProfile=next;
 }
}
