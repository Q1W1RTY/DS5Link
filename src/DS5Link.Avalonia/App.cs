using System.IO;
using System.Security.Principal;
using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace DS5Link;
internal static class DesktopLifetime
{
 internal static IClassicDesktopStyleApplicationLifetime Current=>(IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
}
internal sealed class App:Application
{
 static string[] arguments=Array.Empty<string>();
 static MainWindow? hiddenStartupWindow;
 static bool IsBackgroundLaunch=>arguments.Contains("--background") && !arguments.Contains("--show");
 [STAThread] public static int Main(string[] args)
 {
  arguments=args;
  try{
   if(args.Contains("--state")){Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(BridgeClient.SendAsync("getState").GetAwaiter().GetResult()));return 0;}
   bool preview=args.Contains("--preview")||args.Contains("--ui-qa");
   string sid=WindowsIdentity.GetCurrent().User!.Value;
   using var mutex=new Mutex(true,(preview?"Local\\DS5Link.QA."+Environment.ProcessId+".":"Local\\DS5Link.UI.")+sid,out bool first);
   using var activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DS5Link.Activate."+sid);
   if(!preview && !first){if(!IsBackgroundLaunch)activate.Set();return 0;}
   var registration=preview?null:ThreadPool.RegisterWaitForSingleObject(activate,(_,_)=>Dispatcher.UIThread.Post(()=>((MainWindow?)DesktopLifetime.Current.MainWindow??hiddenStartupWindow)?.Open()),null,Timeout.Infinite,false);
   try{return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);}
   finally{registration?.Unregister(null);}
  }catch(Exception ex){Directory.CreateDirectory(Settings.DirectoryPath);File.WriteAllText(Path.Combine(Settings.DirectoryPath,"avalonia-startup-error.txt"),ex.ToString());return 1;}
 }
 public override void Initialize(){RequestedThemeVariant=ThemeVariant.Dark;Styles.Add(new FluentTheme());}
 public override void OnFrameworkInitializationCompleted()
 {
  if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop){
   bool preview=arguments.Contains("--preview")||arguments.Contains("--ui-qa");
   var settings=preview?new Settings():Settings.Load();L.SetLanguage(settings.Language);
   desktop.ShutdownMode=preview?ShutdownMode.OnMainWindowClose:ShutdownMode.OnExplicitShutdown;
   var main=new MainWindow(preview);
   if(arguments.Contains("--ui-qa"))main.Opened+=RunQa;
   async void RunQa(object? sender,EventArgs e){
    main.Opened-=RunQa;
    await MigrationQa.Run(main);
   }
   if(preview||!IsBackgroundLaunch){
    desktop.MainWindow=main;
    if(!preview && settings.FirstRun){settings.FirstRun=false;settings.Save();}
   }else{
    // Avalonia shows desktop.MainWindow automatically when the lifetime starts.
    hiddenStartupWindow=main;
    main.Opened+=(_,_)=>{desktop.MainWindow=main;hiddenStartupWindow=null;};
   }
  }
  base.OnFrameworkInitializationCompleted();
 }
}
