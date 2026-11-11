using Color=System.Drawing.Color;
using Pen=System.Drawing.Pen;
using Point=System.Drawing.Point;
using System.Drawing;
using System.Drawing.Drawing2D;
using Forms=System.Windows.Forms;
namespace DS5Link;
internal sealed partial class MainWindow {
 internal Forms.ContextMenuStrip CreateTrayMenu(){
  var menu=new Forms.ContextMenuStrip {Renderer=new TrayMenuRenderer(),BackColor=Color.FromArgb(24,24,27),ForeColor=Color.FromArgb(240,241,243),Font=new Font("Segoe UI",9),ShowImageMargin=false,ShowCheckMargin=true,Padding=new Forms.Padding(5),DropShadowEnabled=false};
  menu.Items.Add(L.T("Открыть DS5Link"),null,(_,_)=>Open());
  menu.Items.Add(L.T("Настройки контроллера"),null,(_,_)=>{Open();OpenInputSettings();});
  menu.Items.Add(new Forms.ToolStripSeparator());
  var toggle=menu.Items.Add(L.T("Включить / выключить эмуляцию"),null,async(_,_)=>await Command(current.Enabled?"pause":"enable"));
  var reconnect=menu.Items.Add(L.T("Переподключить"),null,async(_,_)=>await Command("reconnect"));
  var disconnect=menu.Items.Add(L.T("Отключить геймпад"),null,async(_,_)=>await Command("disconnect"));
  menu.Opening+=(_,_)=>{
   var ready=!commanding && !exitRequested && !exiting;
   toggle.Enabled=ready && CanRunConnectionCommand(current.Enabled?"pause":"enable")
       && (current.Enabled || !current.UsbConnected || current.Mode=="xbox");
   reconnect.Enabled=ready && current.Enabled && current.State is not ("Wired" or "Conflict")
       && CanRunConnectionCommand("reconnect");
   disconnect.Enabled=ready && !current.BluetoothDisconnected && !current.UsbConnected
       && current.State is not ("Wired" or "Conflict" or "Error") && CanRunConnectionCommand("disconnect");
  };
  startupMenu=new Forms.ToolStripMenuItem(L.T("Автозапуск")){Checked=StartupSwitch.IsChecked==true,CheckOnClick=true};
  startupMenu.CheckedChanged+=(_,_)=>ChangeStartup(startupMenu.Checked);menu.Items.Add(startupMenu);
  menu.Items.Add(L.T("Диагностика"),null,(_,_)=>{Open();SetDiagnosticsOpen(true);});
  menu.Items.Add(L.T("Сохранить диагностику"),null,async(_,_)=>await ExportDiagnostics());
  menu.Items.Add("RU / EN",null,async(_,_)=>await ChangeLanguage(L.Language=="ru"?"en":"ru"));
  menu.Items.Add(new Forms.ToolStripSeparator());
  menu.Items.Add(L.T("Выход"),null,async(_,_)=>await Exit());
  foreach(Forms.ToolStripItem item in menu.Items){item.ForeColor=menu.ForeColor;if(item is not Forms.ToolStripSeparator)item.Padding=new Forms.Padding(6,5,12,5);}
  return menu;
 }
}
internal sealed class TrayMenuRenderer:Forms.ToolStripRenderer {
 protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e){using var brush=new SolidBrush(Color.FromArgb(24,24,27));e.Graphics.FillRectangle(brush,e.AffectedBounds);}
 protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e){using var pen=new Pen(Color.FromArgb(59,59,63));e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
 protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e){
  if(!e.Item.Selected && !e.Item.Pressed)return;
  e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var r=new Rectangle(2,1,e.Item.Width-4,e.Item.Height-2);using var path=new GraphicsPath();int diameter=12;
  path.AddArc(r.Left,r.Top,diameter,diameter,180,90);path.AddArc(r.Right-diameter,r.Top,diameter,diameter,270,90);path.AddArc(r.Right-diameter,r.Bottom-diameter,diameter,diameter,0,90);path.AddArc(r.Left,r.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();
  using var brush=new SolidBrush(Color.FromArgb(48,48,52));e.Graphics.FillPath(brush,path);
 }
 protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Color.FromArgb(240,241,243):Color.FromArgb(128,128,134);base.OnRenderItemText(e);}
 protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e){using var pen=new Pen(Color.FromArgb(48,48,52));e.Graphics.DrawLine(pen,9,e.Item.Height/2,e.Item.Width-9,e.Item.Height/2);}
 protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e){
  var r=e.ImageRectangle;using var pen=new Pen(Color.FromArgb(64,156,255),2){StartCap=LineCap.Round,EndCap=LineCap.Round};e.Graphics.DrawLines(pen,new[]{new Point(r.Left+2,r.Top+r.Height/2),new Point(r.Left+r.Width/2-1,r.Bottom-3),new Point(r.Right-1,r.Top+3)});
 }
}
