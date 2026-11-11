using System;
using System.Globalization;
namespace DS5Link;
internal static class InputDrawing {
 internal static readonly Typeface Face=new(new FontFamily("avares://DS5Link/Assets/Fonts#Geologica"),FontStyle.Normal,FontWeight.Normal,FontStretch.Normal);
 internal static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(143,145,151));
 internal static readonly Brush Blue=new SolidColorBrush(Color.FromRgb(64,156,255));
 internal static readonly Pen GridPen=new(new SolidColorBrush(Color.FromRgb(66,67,72)),1);
 internal static void Text(Visual owner,DrawingContext dc,string text,double x,double y,double size,Brush? color=null)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Face,size,color??Muted),new Point(x,y));
}
public sealed class StickView:Control {
 internal int Deadzone;
 internal bool HasSample;
 internal double RawX,RawY;
 double calibratedX,calibratedY;
 internal static double Axis(int value)=>(Math.Clamp(value,0,255)-128)/(value>=128?127.0:128.0);
 internal void SetSample(bool fresh,int x,int y,AxisCalibration? calibrationX=null,AxisCalibration? calibrationY=null){
  HasSample=fresh;RawX=Axis(x);RawY=Axis(y);
  calibratedX=Axis(calibrationX?.Apply(x)??x);calibratedY=Axis(calibrationY?.Apply(y)??y);InvalidateVisual();
 }
 internal (double X,double Y) Output {
  get{double radius=Math.Sqrt(calibratedX*calibratedX+calibratedY*calibratedY),zone=Deadzone/100.0;if(radius<=zone)return (0,0);double scale=(radius-zone)/(radius*(1-zone));return (Math.Clamp(calibratedX*scale,-1,1),Math.Clamp(calibratedY*scale,-1,1));}
 }
 public override void Render(DrawingContext dc){
  base.Render(dc);double x=Bounds.Width/2,y=Bounds.Height/2-9,r=Math.Max(1,Math.Min(Bounds.Width-24,Bounds.Height-42)/2);
  dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(29,30,33)),InputDrawing.GridPen,new Point(x,y),r,r);
  dc.DrawLine(InputDrawing.GridPen,new Point(x-r,y),new Point(x+r,y));dc.DrawLine(InputDrawing.GridPen,new Point(x,y-r),new Point(x,y+r));
  dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(55,64,156,255)),new Pen(InputDrawing.Blue,1),new Point(x,y),r*Deadzone/100.0,r*Deadzone/100.0);
  if(HasSample){var output=Output;dc.DrawEllipse(null,new Pen(Brushes.White,1.5),new Point(x+RawX*r,y+RawY*r),5,5);dc.DrawEllipse(InputDrawing.Blue,null,new Point(x+output.X*r,y+output.Y*r),3,3);InputDrawing.Text(this,dc,$"X {output.X*100:0}%  Y {output.Y*100:0}%",Math.Max(2,x-57),Bounds.Height-21,14);}
  else InputDrawing.Text(this,dc,L.T("Нет данных"),Math.Max(2,x-33),Bounds.Height-21,14);
 }
}
public sealed class TouchpadView:Control {
 internal InputPreview Sample=new();
 internal string? SelectedAction;
 internal event Action<string>? ZoneSelected;
 public TouchpadView(){Cursor=new Cursor(StandardCursorType.Hand);PointerPressed+=(_,e)=>{ZoneSelected?.Invoke(e.GetPosition(this).X<Bounds.Width/2?"leftClick":"rightClick");e.Handled=true;};}
 public override void Render(DrawingContext dc){
  base.Render(dc);var rect=new Rect(1,1,Math.Max(1,Bounds.Width-2),Math.Max(1,Bounds.Height-2));
  dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30,31,35)),new Pen(new SolidColorBrush(Color.FromRgb(77,80,88)),1),rect,26,26);
  using var clip=dc.PushClip(new RoundedRect(rect,26));
  if(SelectedAction is "leftClick" or "rightClick"){bool left=SelectedAction=="leftClick";dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28,64,156,255)),null,new Rect(left?0:Bounds.Width/2,0,Bounds.Width/2,Bounds.Height));}
  if(Sample.Fresh && Sample.Click)dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(45,64,156,255)),null,rect);
  var dash=new Pen(new SolidColorBrush(Color.FromRgb(69,72,80)),1){DashStyle=new DashStyle(new double[]{2,2},0)};dc.DrawLine(dash,new Point(Bounds.Width/2,14),new Point(Bounds.Width/2,Bounds.Height-14));
  InputDrawing.Text(this,dc,L.T("Левое нажатие"),18,20,14);InputDrawing.Text(this,dc,L.T("Правое нажатие"),Bounds.Width/2+15,20,14);
  InputDrawing.Text(this,dc,"1920 × 1080",Bounds.Width/2-37,Bounds.Height-26,12);
  if(Sample.Fresh)for(int i=0;i<Sample.Touches.Length && i<2;i++){var touch=Sample.Touches[i];if(!touch.Active)continue;var point=new Point(8+Math.Clamp(touch.X/1920.0,0,1)*(Bounds.Width-16),8+Math.Clamp(touch.Y/1080.0,0,1)*(Bounds.Height-16));dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(45,64,156,255)),new Pen(InputDrawing.Blue,1),point,16,16);dc.DrawEllipse(InputDrawing.Blue,null,point,5,5);}

 }
}
