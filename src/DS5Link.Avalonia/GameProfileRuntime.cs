using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
namespace DS5Link;

internal static class ForegroundExecutable {
 [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public uint Size; public Rect Monitor,WorkArea; public uint Flags; }
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
 [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowStyle(IntPtr window,int index);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out Rect bounds,int size);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 internal static (bool Own,string Path) Read()=>Read(GetForegroundWindow());
 static (bool Own,string Path) Read(IntPtr window){
  GetWindowThreadProcessId(window,out uint id);
  if(id==Environment.ProcessId)return (true,"");
  var handle=OpenProcess(0x1000,false,id);
  if(handle==IntPtr.Zero)return (false,"");
  try{var path=new StringBuilder(32768);int size=path.Capacity;return (false,QueryFullProcessImageName(handle,0,path,ref size)?path.ToString():"");}
  finally{CloseHandle(handle);}
 }
 internal static bool GameInForeground(GameProfiles profiles){
  var window=GetForegroundWindow();
  if(window==IntPtr.Zero || window==GetShellWindow() || window==GetDesktopWindow())return false;
  GetWindowThreadProcessId(window,out uint id);
  if(id==0 || id==Environment.ProcessId)return false;
  // A normal maximized window can cover the monitor when the taskbar auto-hides.
  const int caption=0x00c00000;
  if(!(IsZoomed(window) && (GetWindowStyle(window,-16)&caption)==caption)){
   var monitor=MonitorFromWindow(window,2);
   var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
   if(monitor!=IntPtr.Zero && GetMonitorInfo(monitor,ref info) &&
      DwmGetWindowAttribute(window,9,out var bounds,Marshal.SizeOf<Rect>())==0 &&
      bounds.Left<=info.Monitor.Left && bounds.Top<=info.Monitor.Top &&
      bounds.Right>=info.Monitor.Right && bounds.Bottom>=info.Monitor.Bottom)return true;
  }
  return profiles.Games.Count>0 && profiles.Find(Read(window).Path).Executable.Length>0;
 }
}

internal sealed partial class MainWindow {
 readonly DispatcherTimer profileTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
 GameProfiles profiles=new();
 string activeProfile="",profileLoadError="";
 bool profileSwitching,profileApplied;
 long nextProfileRetry;
 int profileRevision,appliedProfileRevision=-1;
 void InitializeGameProfiles(){
  if(preview)return;
  try{profiles=GameProfiles.Load(GameProfiles.FilePath,inputProfile,effectOptions);}
  catch(Exception error){profileLoadError=error.Message;return;}
  profileTimer.Tick+=async(_,_)=>await RefreshGameProfile();
  profileTimer.Start();
 }
 async Task SaveProfile(GameProfile profile){
  var next=profiles.Put(profile);next.Save(GameProfiles.FilePath);profiles=next;++profileRevision;
  await RefreshGameProfile();
 }
 async Task DeleteProfile(string executable){
  if(executable=="")return;
  var next=profiles.Remove(executable);next.Save(GameProfiles.FilePath);profiles=next;++profileRevision;
  await RefreshGameProfile();
 }
 async Task RefreshGameProfile(){
  if(preview || profileLoadError!="" || profileSwitching || commanding || exiting || exitRequested || Environment.TickCount64<nextProfileRetry)return;
  var foreground=ForegroundExecutable.Read();
  var target=profiles.Find(foreground.Own?activeProfile:foreground.Path);
  if(profileApplied && activeProfile==target.Executable && appliedProfileRevision==profileRevision)return;
  profileSwitching=true;
  // Invalidate pending touch events before changing either half of the profile.
  KeyboardOutput.ReleaseAll();inputCursor.Reset();
  var oldInput=inputProfile;var oldEffects=effectOptions;int revision=profileRevision;
  try{
   await ProfileApplication.ApplyAsync(target,new(){Input=oldInput,Effects=oldEffects},ApplyInputSettings,ApplyEffectSettings);
   activeProfile=target.Executable;appliedProfileRevision=revision;profileApplied=true;
   inputSettingsWindow?.SetActiveProfile(target.Label);
  }catch(Exception error){
   profileApplied=false;nextProfileRetry=Environment.TickCount64+3000;
   inputSettingsWindow?.SetActiveProfile(L.T("Ошибка применения: ")+error.Message);
  }finally{profileSwitching=false;}
 }
 bool ProfileAllowsKeyboard(){
  if(profileSwitching || !profileApplied)return false;
  var foreground=ForegroundExecutable.Read();
  return !foreground.Own && string.Equals(profiles.Find(foreground.Path).Executable,activeProfile,StringComparison.OrdinalIgnoreCase);
 }
}
