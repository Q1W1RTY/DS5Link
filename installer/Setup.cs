using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Security.AccessControl;
using System.Security.Principal;

[assembly: AssemblyTitle("Установка DS5Link")]

internal static class Setup {
 internal static string Work;
 internal static string Log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DS5Link", "Setup", "last-setup.log");
 [STAThread] static int Main(string[] args) {
  Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
  try {
   Directory.CreateDirectory(Path.GetDirectoryName(Log));
   if(args.Length==1 && args[0]=="--update-installed") return UpdateInstalled();
   if(args.Length==3 && args[0]=="--update-elevated") {
    if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new IOException("Administrator token required");
    try { Extract();Verify();return RunElevated(args[1],args[2],false,true); } finally {Cleanup();}
   }
   if(args.Length==2 && args[0]=="--dependency-elevated") {
    if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new IOException("Administrator token required");
    if(args[1]!="usbip-win2" && args[1]!="HidHide" && args[1]!="ViGEmBus")throw new IOException("Unknown dependency");
    try {Extract();Verify();return DependencyScript("-InstallName "+args[1]);}finally{Cleanup();}
   }
   if((args.Length==3 || (args.Length==4 && args[3]=="--reboot-required")) && args[0]=="--install-elevated") {
    if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new IOException("Administrator token required");
    try { Extract();Verify();return RunElevated(args[1],args[2],args.Length==4); } finally {Cleanup();}
   }
   Directory.CreateDirectory(Path.GetDirectoryName(Log));
   File.WriteAllText(Log, DateTime.Now.ToString("o") + " DS5Link Setup\r\n", Encoding.UTF8);
   if (args.Length == 2 && args[0] == "--verify-only") {
    try { Extract(); Verify(); File.WriteAllText(args[1], "{\"ok\":true,\"installed\":false,\"payloadFiles\":" + Directory.GetFiles(Work,"*",SearchOption.AllDirectories).Length + "}"); return 0; }
    catch (Exception e) { Write(e.ToString()); File.WriteAllText(args[1], "{\"ok\":false}"); return 1; }
    finally { Cleanup(); }
   }
   using (var wizard = new Wizard()) { Application.Run(wizard); return wizard.ExitCode; }
  } catch (Exception e) { MessageBox.Show(e.Message, "DS5Link", MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
 }
 static int UpdateInstalled() {
  string destination;
  using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link"))
   destination=key==null?null:key.GetValue("InstallLocation") as string;
  if(String.IsNullOrWhiteSpace(destination) || destination.IndexOf('"')>=0 ||
     !File.Exists(Path.Combine(destination,"DS5Link.exe")))
   throw new IOException("Installed DS5Link was not found.");
  var info=new ProcessStartInfo(Application.ExecutablePath,
   "--update-elevated \""+destination+"\" "+WindowsIdentity.GetCurrent().User.Value) {
    UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden
   };
  int result;
  try {
   using(var child=Process.Start(info)) {child.WaitForExit();result=child.ExitCode;}
  } catch(System.ComponentModel.Win32Exception error) {
   if(error.NativeErrorCode==1223)return 1223;
   throw;
  }
  if(result==3010)MessageBox.Show("Перезапустите Windows для завершения обновления.","DS5Link");
  else if(result!=0)
   MessageBox.Show("DS5Link update failed. See the setup log.","DS5Link",MessageBoxButtons.OK,MessageBoxIcon.Error);
  if(File.Exists(Path.Combine(destination,"DS5Link.exe")))
   using(var ui=Process.Start(new ProcessStartInfo(Path.Combine(destination,"DS5Link.exe"),"--show"){UseShellExecute=true})) {}
  return result;
 }
 internal static int DependencyScript(string arguments) {
  var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(Work,"Dependencies.ps1")+"\" "+arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  using(var child=Process.Start(info)) {var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();child.WaitForExit();Task.WaitAll(output,error);Write(output.Result);Write(error.Result);if(child.ExitCode!=0 && child.ExitCode!=3010)throw new IOException("Не удалось проверить или установить драйвер. Журнал: "+Log);return child.ExitCode;}
 }
 internal static int RunElevated(string destination,string owner,bool rebootRequired,bool updateOnly=false) {
  new SecurityIdentifier(owner);
  var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(Work,"Install.ps1")+"\" -InstallerMode -InstallDir \""+destination+"\" -OwnerSid "+owner+(rebootRequired?" -RebootRequired":"")+(updateOnly?" -UpdateOnly":"")){UseShellExecute=false,CreateNoWindow=true};
  using(var process=Process.Start(info)){process.WaitForExit();return process.ExitCode;}
 }
 internal static void Write(string value) { File.AppendAllText(Log, DateTime.Now.ToString("o") + " " + value + "\r\n", Encoding.UTF8); }
 internal static void Extract() {
  Cleanup();
  bool elevated=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
  Work = Path.Combine(elevated?Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles):Path.GetTempPath(), "DS5Link-Setup-" + Guid.NewGuid().ToString("N"));
  if(elevated){var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(new SecurityIdentifier("S-1-5-32-544"));foreach(var sid in new[]{"S-1-5-18","S-1-5-32-544"})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.CreateDirectory(Work,acl);}else Directory.CreateDirectory(Work);
  if(elevated)Log=Work+".log"; // Protected sibling survives temporary payload cleanup.
  using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DS5Link.Payload"))
  using (var zip = new ZipArchive(stream,ZipArchiveMode.Read)) {
   foreach (var entry in zip.Entries) {
    var path = Path.GetFullPath(Path.Combine(Work,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));
    if (!path.StartsWith(Work + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Недопустимый путь в комплекте.");
    if (entry.Name.Length == 0) { Directory.CreateDirectory(path); continue; }
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    using (var input = entry.Open()) using (var output = new FileStream(path,FileMode.CreateNew)) input.CopyTo(output);
   }
  }
  Write("Payload extracted.");
 }
 internal static void Verify() {
  foreach (var line in File.ReadAllLines(Path.Combine(Work,"files.sha256"))) {
   if (String.IsNullOrWhiteSpace(line)) continue;
   if (line.Length < 67 || line.Substring(64,2) != "  ") throw new IOException("Повреждён список файлов.");
   var path = Path.GetFullPath(Path.Combine(Work,line.Substring(66)));
   if (!path.StartsWith(Work + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Недопустимый путь в списке.");
   using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) {
    var hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
    if (!hash.Equals(line.Substring(0,64),StringComparison.OrdinalIgnoreCase)) throw new IOException("Повреждён файл комплекта: " + Path.GetFileName(path));
   }
  }
  Write("All payload SHA256 hashes verified.");
 }
 internal static void Cleanup() {
  if (Work == null) return;
  var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
  if ((Path.GetDirectoryName(Path.GetFullPath(Work)).Equals(parent,StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(Path.GetFullPath(Work)).Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),StringComparison.OrdinalIgnoreCase)) && Path.GetFileName(Work).StartsWith("DS5Link-Setup-",StringComparison.Ordinal)) {
   try { Directory.Delete(Work,true); } catch (Exception e) { Write("Temporary cleanup: " + e.Message); }
  }
 }
}
internal sealed class Wizard : Form {
 internal int ExitCode = 1602;
 readonly Label title = new Label(); readonly Label text = new Label(); readonly Label status = new Label();
 readonly Button next = new SetupButton(true); readonly Button close = new SetupButton(false); readonly ProgressBar progress = new ProgressBar();
 readonly CheckBox launch = new CheckBox(); bool busy, finished, reboot;
 readonly Label pathLabel = new Label(); readonly TextBox installPath = new TextBox();
 readonly Button browse = new SetupButton(false); readonly Label pathHint = new Label();
 readonly bool updating; string installationDirectory;
 readonly Label dependencyHeading=new Label();
 readonly Label[] dependencyLabels={new Label(),new Label(),new Label()};
 readonly Button[] dependencyButtons={new SetupButton(false),new SetupButton(false),new SetupButton(false)};
 readonly string[] dependencyNames={"usbip-win2","HidHide","ViGEmBus"};
 bool dependenciesReady;
 readonly SetupCard heroCard=new SetupCard(),dependencyCard=new SetupCard(),pathCard=new SetupCard();
 readonly Label brand=new Label(),subtitle=new Label();
 readonly Button windowClose=new SetupButton(false),windowMinimize=new SetupButton(false);
 [DllImport("user32.dll")] static extern bool ReleaseCapture();
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam);
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var border=new Pen(Color.FromArgb(65,65,69)))using(var shape=SetupShape.Round(new Rectangle(0,0,ClientSize.Width-1,ClientSize.Height-1),24))e.Graphics.DrawPath(border,shape);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(ClientSize.Width>0 && ClientSize.Height>0){using(var shape=SetupShape.Round(new Rectangle(0,0,ClientSize.Width,ClientSize.Height),24)){var old=Region;Region=new Region(shape);if(old!=null)old.Dispose();}}}


 internal static string RegisteredDirectory() {
  using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64)) {
   foreach(var keyName in new[]{@"SOFTWARE\DS5Link",@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link"})
    using(var key=machine.OpenSubKey(keyName)) { var value=key==null?null:key.GetValue("InstallLocation") as string; if(!String.IsNullOrWhiteSpace(value))return Path.GetFullPath(value).TrimEnd('\\'); }
  }
  return null;
 }
 internal Wizard() {
  installationDirectory=RegisteredDirectory(); updating=installationDirectory!=null;
  if(!updating)installationDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DS5Link");
  Text="Установка DS5Link";ClientSize=new Size(620,690);FormBorderStyle=FormBorderStyle.None;
  Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
  BackColor=Color.FromArgb(22,22,24);ForeColor=Color.FromArgb(240,241,243);Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
  SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
  brand.SetBounds(24,17,450,26);brand.Text="DS5Link";brand.Font=new Font("Segoe UI Semibold",10);
  windowMinimize.SetBounds(524,12,30,30);windowMinimize.Text="−";windowMinimize.AccessibleName="Свернуть";
  windowClose.SetBounds(564,12,30,30);windowClose.Text="×";windowClose.AccessibleName="Закрыть";
  windowMinimize.Click+=(sender,args)=>WindowState=FormWindowState.Minimized;
  windowClose.Click+=(sender,args)=>{if(!busy)Close();};
  MouseEventHandler drag=(sender,args)=>{if(args.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
  MouseDown+=drag;brand.MouseDown+=drag;
  heroCard.SetBounds(20,58,580,96);dependencyCard.SetBounds(20,166,580,232);pathCard.SetBounds(20,410,580,136);
  var logo=new PictureBox();logo.SetBounds(18,22,52,52);logo.SizeMode=PictureBoxSizeMode.Zoom;logo.Image=Icon.ToBitmap();heroCard.Controls.Add(logo);
  title.SetBounds(88,18,470,32);title.Font=new Font("Segoe UI Semibold",19);title.Text=updating?"Обновление DS5Link":"Установка DS5Link";
  subtitle.SetBounds(90,56,460,23);subtitle.Font=new Font("Segoe UI",9);subtitle.ForeColor=Color.FromArgb(160,160,168);subtitle.Text="DualSense по Bluetooth · PS5 и Xbox 360";
  heroCard.Controls.AddRange(new Control[]{title,subtitle});
  dependencyHeading.SetBounds(18,15,544,26);dependencyHeading.Text="Проверяем компоненты…";dependencyHeading.Font=new Font("Segoe UI Semibold",11);
  text.SetBounds(18,43,544,25);text.Font=new Font("Segoe UI",9);text.ForeColor=Color.FromArgb(160,160,168);text.Text=".NET и Visual C++ включены в приложение";
  dependencyCard.Controls.AddRange(new Control[]{dependencyHeading,text});
  for(int i=0;i<dependencyNames.Length;i++){
   int index=i;var label=dependencyLabels[i];label.SetBounds(18,80+i*48,384,43);label.Font=new Font("Segoe UI",9);label.Text=dependencyNames[i];dependencyCard.Controls.Add(label);
   var button=dependencyButtons[i];button.SetBounds(420,82+i*48,142,34);button.Text="Установить";button.Enabled=false;dependencyCard.Controls.Add(button);
   button.Click+=async (sender,args)=>await InstallDependency(index);
  }
  pathLabel.SetBounds(18,15,544,25);pathLabel.Text="Папка установки";pathLabel.Font=new Font("Segoe UI Semibold",11);
  var pathWell=new SetupCard();pathWell.BackColor=Color.FromArgb(29,29,32);pathWell.SetBounds(18,47,424,32);
  installPath.SetBounds(10,6,404,22);installPath.Text=installationDirectory;installPath.BackColor=pathWell.BackColor;installPath.ForeColor=ForeColor;installPath.BorderStyle=BorderStyle.None;installPath.Font=new Font("Segoe UI",9);installPath.ReadOnly=updating;pathWell.Controls.Add(installPath);
  browse.SetBounds(452,47,110,32);browse.Text="Обзор…";browse.Enabled=!updating;
  pathHint.SetBounds(18,90,544,34);pathHint.ForeColor=Color.FromArgb(155,155,163);pathHint.Font=new Font("Segoe UI",8.5f);
  pathHint.Text=updating?"Обновление сохранит папку установки и ваши настройки.":"Выберите пустую папку на постоянном локальном диске.";
  browse.Click+=(sender,args)=>{using(var dialog=new FolderBrowserDialog()){dialog.Description="Папка установки DS5Link";if(Directory.Exists(installPath.Text))dialog.SelectedPath=installPath.Text;if(dialog.ShowDialog(this)==DialogResult.OK)installPath.Text=dialog.SelectedPath;}};
  pathCard.Controls.AddRange(new Control[]{pathLabel,pathWell,browse,pathHint});
  progress.SetBounds(28,560,564,4);progress.Visible=false;
  status.SetBounds(28,575,564,45);status.ForeColor=Color.FromArgb(160,160,168);status.Font=new Font("Segoe UI",9);
  next.SetBounds(340,632,252,38);next.Text=updating?"Обновить DS5Link":"Установить DS5Link";
  close.SetBounds(28,632,140,38);close.Text="Отмена";
  launch.SetBounds(28,575,280,30);launch.Text="Открыть DS5Link";launch.Checked=true;launch.Visible=false;
  Controls.AddRange(new Control[]{brand,windowMinimize,windowClose,heroCard,dependencyCard,pathCard,status,progress,next,close,launch});
  next.Enabled=false;
  Shown+=async (sender,args)=>await CheckDependencies();
  next.Click+=async (s,e)=> { if(finished){if(launch.Checked && !reboot)OpenApp(); Close();}else await Install(); };
  close.Click+=(s,e)=>Close(); FormClosing+=(s,e)=>{if(busy)e.Cancel=true;else Setup.Cleanup();};
 }
 void SetDependencyBusy(bool value) {
  busy=value;next.Enabled=!value && dependenciesReady;close.Enabled=!value;
  foreach(var button in dependencyButtons)button.Enabled=false;
 }
 async Task CheckDependencies() {
  SetDependencyBusy(true);
  try {
   if(Setup.Work==null)await Task.Run(()=>{Setup.Extract();Setup.Verify();});
   await RefreshDependencies();
  }catch(Exception error){dependenciesReady=false;status.Text=error.Message;dependencyHeading.Text="Проверка не завершена";}
  finally{busy=false;close.Enabled=true;next.Enabled=dependenciesReady;}
 }
 async Task RefreshDependencies() {
  string report=Path.Combine(Setup.Work,"dependency-status.tsv");
  await Task.Run(()=>Setup.DependencyScript("-ReportPath \""+report+"\""));
  var rows=File.ReadAllLines(report,Encoding.UTF8);dependenciesReady=rows.Length==dependencyNames.Length;
  for(int i=0;i<dependencyNames.Length;i++) {
   string[] row=null;foreach(var line in rows){var parts=line.Split('\t');if(parts.Length==3 && parts[0]==dependencyNames[i])row=parts;}
   if(row==null)throw new IOException("Неполный результат проверки драйверов.");
   bool installed=row[1]=="Ready" || row[1]=="Reboot";
   dependenciesReady &= installed;if(row[1]=="Reboot")reboot=true;
   dependencyLabels[i].Text=(i==0?"USBip · режим PS5":i==1?"HidHide · защита от двойного ввода":"ViGEmBus · режим Xbox")+"\r\n"+(row[1]=="Ready"?"Установлен, готов к работе":row[1]=="Reboot"?"Установлен, нужна перезагрузка":row[1]=="Repair"?"Нужно восстановление":"Не установлен");
   ((SetupButton)dependencyButtons[i]).Ready=installed;
   dependencyButtons[i].Text=installed?"Готов":row[1]=="Repair"?"Восстановить":"Установить";
   dependencyButtons[i].Enabled=!installed;
  }
  dependencyHeading.Text="Компоненты и драйверы";
  status.Text=dependenciesReady?(reboot?"После установки потребуется перезагрузка Windows.":"Все компоненты готовы. Можно установить приложение."):"Установите недостающие компоненты кнопками справа.";
 }
 async Task InstallDependency(int index) {
  SetDependencyBusy(true);status.Text="Установка "+dependencyNames[index]+". Подтвердите запрос Windows…";
  try {
   int code=await Task.Run(()=>{
    var info=new ProcessStartInfo(Application.ExecutablePath,"--dependency-elevated "+dependencyNames[index]){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
    using(var child=Process.Start(info)){child.WaitForExit();return child.ExitCode;}
   });
   if(code!=0 && code!=3010)throw new IOException("Компонент не установлен (код "+code+").");
   if(code==3010)reboot=true;
  }catch(Exception error){MessageBox.Show(this,error.Message,"DS5Link",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  try{await RefreshDependencies();}catch(Exception error){dependenciesReady=false;status.Text=error.Message;}
  busy=false;close.Enabled=true;next.Enabled=dependenciesReady;
 }
 async Task Install() {
  if(!dependenciesReady)return;
  busy=true; foreach(var button in dependencyButtons)button.Enabled=false;next.Enabled=false; close.Enabled=false;installPath.Enabled=false;browse.Enabled=false; progress.Visible=true; progress.Style=ProgressBarStyle.Marquee;
  try {
   installationDirectory=Path.GetFullPath(installPath.Text.Trim()).TrimEnd('\\');
   if(installationDirectory.IndexOf('"')>=0)throw new IOException("Недопустимый путь установки.");
   status.Text="Распаковка и проверка комплекта…";
   int code=await Task.Run(()=>{
    Setup.Verify();
    var report=Path.Combine(Setup.Work,"preinstall-status.tsv");Setup.DependencyScript("-ReportPath \""+report+"\"");
    foreach(var row in File.ReadAllLines(report)){var parts=row.Split('\t');if(parts.Length!=3 || (parts[1]!="Ready" && parts[1]!="Reboot"))throw new IOException("Компоненты изменились. Перезапустите установщик.");}
    int validation=RunInstallerScript(true);
    if(validation!=0)throw new IOException("Выбранная папка не подходит. Подробности: " + Setup.Log);
    BeginInvoke((Action)(()=>status.Text="Установка компонентов. Подтвердите запрос Windows…"));
    return RunInstallerScript(false);
   });
   if(code!=0 && code!=3010)throw new IOException("Установка не завершена (код " + code + ").\r\nЖурнал: " + Setup.Log + "\r\nПодробности: %ProgramData%\\DS5Link\\setup.log");
   reboot=reboot || code==3010; finished=true; ExitCode=reboot?3010:code; title.Text="Готово";
   dependencyCard.Visible=false;pathCard.Visible=false;subtitle.Text="Приложение установлено";
   heroCard.Height=96;
   Controls.Add(text);text.SetBounds(38,180,544,250);text.ForeColor=ForeColor;text.Font=new Font("Segoe UI",10);
   pathLabel.Visible=false;installPath.Visible=false;browse.Visible=false;pathHint.Visible=false;
   text.Text=reboot ? "DS5Link установлен.\r\n\r\nДрайверам нужна перезагрузка Windows. Сохраните работу и перезагрузите компьютер, затем откройте DS5Link из меню «Пуск»." : "DS5Link установлен.\r\n\r\n1. Сопрягите DualSense с Windows по Bluetooth.\r\n    Удерживайте Create + PS до мигания индикатора,\r\n    затем добавьте Bluetooth-устройство в Windows.\r\n2. Откройте DS5Link и выберите режим PS5 или Xbox.\r\n3. Для нативных эффектов отключите Steam Input\r\n    в настройках нужной игры, если он мешает.";
   status.Text=reboot ? "Автоматическая перезагрузка не выполняется." : "Установлено: " + installationDirectory;
   if(!reboot)status.Top=535;launch.Visible=!reboot; next.Text="Завершить"; close.Visible=false;
  }catch(Exception e){ExitCode=1;Setup.Write(e.ToString()); status.Text="Установка не завершена. Можно повторить."; MessageBox.Show(this,e.Message,"DS5Link",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  finally{busy=false; next.Enabled=true; close.Enabled=true; progress.Visible=false;if(!finished){installPath.Enabled=true;browse.Enabled=!updating;}}
  if(!finished)await CheckDependencies();
 }
 int RunInstallerScript(bool validateOnly) {
  if(!validateOnly){var elevated=new ProcessStartInfo(Application.ExecutablePath,"--install-elevated \""+installationDirectory+"\" "+WindowsIdentity.GetCurrent().User.Value+(reboot?" --reboot-required":"")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};using(var child=Process.Start(elevated)){child.WaitForExit();return child.ExitCode;}}
  var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),
   "-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(Setup.Work,"Install.ps1")+"\" -InstallDir \""+installationDirectory+"\" "+(validateOnly?"-ValidateOnly":"-InstallerMode"));
  info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;
  using(var process=Process.Start(info)){
   var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();process.WaitForExit();Task.WaitAll(stdout,stderr);Setup.Write(stdout.Result);Setup.Write(stderr.Result);
   if(validateOnly && process.ExitCode!=0){var lines=stderr.Result.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);throw new IOException(lines.Length>0?lines[0]:"Не удалось проверить папку установки.");}
   return process.ExitCode;
  }
 }
 void OpenApp() {
  try{Process.Start(new ProcessStartInfo(Path.Combine(installationDirectory,"DS5Link.exe"),"--show"){UseShellExecute=true,WorkingDirectory=installationDirectory});}
  catch(Exception e){MessageBox.Show(this,e.Message,"DS5Link");}
 }
}
