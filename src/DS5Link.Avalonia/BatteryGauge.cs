namespace DS5Link;
public sealed class BatteryGauge:Control
{
 public static readonly StyledProperty<int?> LevelProperty=AvaloniaProperty.Register<BatteryGauge,int?>(nameof(Level));
 public static readonly StyledProperty<bool> ChargingProperty=AvaloniaProperty.Register<BatteryGauge,bool>(nameof(Charging));
 public int? Level{get=>GetValue(LevelProperty);set=>SetValue(LevelProperty,value);}
 public bool Charging{get=>GetValue(ChargingProperty);set=>SetValue(ChargingProperty,value);}
 static BatteryGauge(){AffectsRender<BatteryGauge>(LevelProperty,ChargingProperty);}
 public override void Render(DrawingContext dc){
  base.Render(dc);
  var outline=AppearancePalette.BatteryOutline;
  dc.DrawRectangle(null,new Pen(outline,1),new Rect(.5,.5,25,12),3,3);
  dc.DrawRectangle(outline,null,new Rect(27,4.5,1.5,4),.75,.75);
  if(Level is int level){var width=22*Math.Clamp(level,0,100)/100d;if(width>0)dc.DrawRectangle(Brush.Parse(level<=20?"#FF9F0A":"#30D158"),null,new Rect(2,2,width,9),1.5,1.5);}
  if(Charging){var bolt=Geometry.Parse("M14 1.5 L9 7 H12 L11 11.5 L17 5.5 H13.5 Z");dc.DrawGeometry(Brushes.White,new Pen(Brush.Parse("#6618181B"),.6),bolt);}
 }
}
