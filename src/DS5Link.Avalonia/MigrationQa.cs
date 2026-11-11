using System.IO;
using System.Text.Json;
using Avalonia.Media.Imaging;
namespace DS5Link;
internal static class MigrationQa
{
 internal static async Task Run(MainWindow main){
  var report=new System.Collections.Generic.List<string>();
  var folder=Environment.GetEnvironmentVariable("DS5LINK_QA_OUTPUT")??Path.Combine(AppContext.BaseDirectory,"ui-qa");Directory.CreateDirectory(folder);
  try{
   await Task.Delay(100);
   main.WindowState=WindowState.Minimized;
   await Task.Delay(50);
   if(main.IsVisible)throw new Exception("Minimized window did not hide");
   main.Open();
   if(!main.IsVisible || main.WindowState!=WindowState.Normal)throw new Exception("Minimized window did not reopen");
   main.Hide();
   main.Open();
   if(!main.IsVisible)throw new Exception("Hidden window did not reopen");
   report.Add("Minimized and hidden window restore");
   foreach(var weight in new[]{FontWeight.Normal,FontWeight.Medium,FontWeight.SemiBold,FontWeight.Bold}){
    var glyph=new Typeface(main.FontFamily,FontStyle.Normal,weight).GlyphTypeface;
    if(!glyph.FamilyName.Contains("Geologica"))throw new Exception("Unexpected font: "+glyph.FamilyName);
    report.Add("Geologica "+weight);
   }
   foreach(var level in new int?[]{null,0,10,20,50,85,100})foreach(bool charging in new[]{false,true}){
    main.Display(new BridgeState{State="Active",Enabled=true,BatteryPercent=level,Charging=charging});
    if(main.FindControl<BatteryGauge>("BatteryIndicator")!.Level!=level)throw new Exception("Battery level mismatch");
   }
   report.Add("Battery: unknown, 0, 10, 20, 50, 85, 100; charging on/off");
   foreach(var state in new[]{"Active","Paused","Error","Wired","Conflict","Stopping","Connecting"})foreach(var mode in new[]{"ps5","xbox"}){
    main.Display(new BridgeState{State=state,Mode=mode,Enabled=state!="Paused"});
    if(state!="Active" && main.FindControl<TextBlock>("EmuTitle")!.Text==L.T("Эмуляция включена"))throw new Exception("False active state");
   }
   report.Add("14 controller state/mode combinations");
   main.Display(new BridgeState{State="Active",Enabled=true,BatteryPercent=85,Charging=true});
   await Task.Delay(100);
   Capture(main,Path.Combine(folder,"main.png"));
   var window=main.CreateInputSettingsWindow();window.Show(main);await Task.Delay(100);
   var tabs=window.FindControl<TabControl>("SettingsTabs")!;
   if(tabs.ItemCount!=6)throw new Exception("Missing settings tabs");
   for(int i=0;i<tabs.ItemCount;i++){
    tabs.SelectedIndex=i;await Task.Delay(80);
    var content=(Control)((TabItem)tabs.Items[i]!).Content!;
    if(content.Bounds.Width<=0 || content.Bounds.Height<=0)throw new Exception("Empty tab");
    Capture(window,Path.Combine(folder,"settings-"+i+".png"));
   }
   report.Add("All six real settings pages initialized and rendered");
   var sample=new InputPreview{Enabled=true,AgeMs=5,LeftX=180,LeftY=130,RightX=128,RightY=200};window.SetPreview(sample);
   if(!window.FindControl<StickView>("LeftVisual")!.HasSample)throw new Exception("Input preview missing");
   window.SetPreview(new InputPreview{AgeMs=1000});
   if(window.FindControl<StickView>("LeftVisual")!.HasSample)throw new Exception("Stale input retained");
   report.Add("Fresh and stale input samples");
   for(int key=0;key<256;key++){
    var binding=new KeyBinding(key,0);if(binding.Valid && KeyInterop.VirtualKeyFromKey(KeyInterop.KeyFromVirtualKey(key))!=key)throw new Exception("Keyboard mapping failed "+key);
   }
   report.Add("Virtual-key round trips for every supported binding");
   window.Close();
   L.SetLanguage("en");var english=new MainWindow(true);english.Show();await Task.Delay(80);var settings=english.CreateInputSettingsWindow();settings.Show(english);await Task.Delay(80);
   if(!settings.FindControl<TabItem>("AudioTab")!.GetValue(AutomationProperties.NameProperty)!.Contains("Audio"))throw new Exception("English localization missing");
   settings.Close();english.Hide();L.SetLanguage("ru");report.Add("RU and EN windows");
   File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{ok=true,checks=report}));
   DesktopLifetime.Current.Shutdown(0);
  }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{ok=false,error=ex.ToString(),checks=report}));DesktopLifetime.Current.Shutdown(1);}
 }
 static void Capture(Window window,string path){using var image=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96));image.Render(window);image.Save(path,PngBitmapEncoderOptions.Default);}
}
