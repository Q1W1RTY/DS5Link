using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
namespace DS5Link;
internal sealed record EffectChoice(string Key,string Label){public override string ToString()=>Label;}
internal sealed class EffectSlider:StackPanel {
 internal readonly Slider Slider;
 internal int Value {get=>(int)Slider.Value;set=>Slider.Value=value;}
 internal EffectSlider(Window window,string caption,int min,int max,string unit="",Func<int,string>? format=null){
  Margin=new Thickness(0,0,0,5);var row=new Grid();var label=new TextBlock {Text=caption,FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(196,196,196))};var value=new TextBlock {HorizontalAlignment=HorizontalAlignment.Right,FontSize=12};row.Children.Add(label);row.Children.Add(value);Children.Add(row);
  Slider=new Slider {Minimum=min,Maximum=max,TickFrequency=1,IsSnapToTickEnabled=true,Theme=window.RequireTheme("InputSlider"),Margin=new Thickness(0,3,0,0)};Slider.ValueChanged+=(_,_)=>value.Text=format?.Invoke((int)Slider.Value)??$"{Slider.Value:F0}{unit}";Children.Add(Slider);Value=min;
 }
}
internal sealed class TriggerEditor:StackPanel {
 readonly ComboBox mode;readonly StackPanel details;readonly EffectSlider strength,start,end,frequency;bool changing;
 internal event Action? Changed;
 void Notify(){if(!changing)Changed?.Invoke();}
 internal TriggerEditor(Window window,string title){
  Children.Add(new TextBlock {Text=title,FontSize=14,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,7)});
  mode=new ComboBox {Theme=window.RequireTheme("DarkCombo"),Margin=new Thickness(0,0,0,8),ItemsSource=new[]{new EffectChoice("game",L.T("Управляет игра")),new("resistance",L.T("Сопротивление")),new("weapon",L.T("Щелчок / спуск")),new("pulse",L.T("Пульсация"))}};Children.Add(mode);
  details=new StackPanel();Children.Add(details);strength=new(window,L.T("Сила"),1,8);start=new(window,L.T("Начало сопротивления"),0,9);end=new(window,L.T("Конец сопротивления"),1,9);frequency=new(window,L.T("Частота"),1,40,L.T(" Гц"));
  foreach(var item in new[]{strength,start,end,frequency})details.Children.Add(item);
  mode.SelectionChanged+=(_,_)=>{Update();Notify();};start.Slider.ValueChanged+=(_,_)=>{Update();Notify();};
  foreach(var field in new[]{strength,end,frequency})field.Slider.ValueChanged+=(_,_)=>Notify();Margin=new Thickness(0,0,0,12);Set(new());
 }
 string Mode=>(mode.SelectedItem as EffectChoice)?.Key??"game";
 void Update(){if(changing)return;changing=true;try{bool weapon=Mode=="weapon";details.IsVisible=Mode=="game"?false:true;start.Slider.Minimum=weapon?2:0;start.Slider.Maximum=weapon?7:9;end.Slider.Maximum=weapon?8:9;end.Slider.Minimum=weapon?start.Value+1:1;end.IsVisible=weapon?true:false;frequency.IsVisible=Mode=="pulse"?true:false;}finally{changing=false;}}
 internal TriggerOptions Value=>new(){Mode=Mode,Strength=strength.Value,Start=start.Value,End=end.Value,Frequency=frequency.Value};
 internal void Set(TriggerOptions value){changing=true;mode.SelectedItem=mode.Items.Cast<EffectChoice>().First(c=>c.Key==value.Mode);start.Slider.Minimum=0;start.Slider.Maximum=9;end.Slider.Minimum=1;end.Slider.Maximum=9;strength.Value=value.Strength;start.Value=value.Start;end.Value=value.End;frequency.Value=value.Frequency;changing=false;Update();Notify();}
}
internal sealed partial class InputSettingsWindow {
 TriggerEditor leftTrigger=null!,rightTrigger=null!;
 EffectSlider redSlider=null!,greenSlider=null!,blueSlider=null!,sensitivitySlider=null!,limitSlider=null!,gateSlider=null!;
 Func<EffectOptions,Task>? applyEffects;
 EffectState effectState=new();readonly Stopwatch ledClock=Stopwatch.StartNew();
 bool testingTriggers;
 void InitializeEffectControls(EffectOptions options,Func<EffectOptions,Task>? save){
  TriggerTestButton.Content=L.T("Проверить курки · 3 с");
  applyEffects=save;leftTrigger=new(this,L.T("Левый · L2"));rightTrigger=new(this,L.T("Правый · R2"));TriggerEditors.Children.Add(new Border {Child=leftTrigger,Theme=this.RequireTheme("SharedCard"),Padding=new Thickness(22),Margin=new Thickness(0,0,7,0)});TriggerEditors.Children.Add(new Border {Child=rightTrigger,Theme=this.RequireTheme("SharedCard"),Padding=new Thickness(22),Margin=new Thickness(7,0,0,0)});
  TriggerPreset.ItemsSource=new[]{L.T("Свой / без пресета"),L.T("Без ручных эффектов"),L.T("Лёгкое сопротивление"),L.T("Упругое сопротивление"),L.T("Шутер"),L.T("Гонка"),L.T("Пульсация")};TriggerPreset.SelectedIndex=0;
  TriggerPreset.SelectionChanged+=(_,_)=>{
   TriggerOptions left=new(),right=new();
   switch(TriggerPreset.SelectedIndex){case 0:return;case 2:left=right=new(){Mode="resistance",Strength=2,Start=2};break;case 3:left=right=new(){Mode="resistance",Strength=5,Start=1};break;case 4:left=new(){Mode="resistance",Strength=3,Start=2};right=new(){Mode="weapon",Strength=6,Start=3,End=6};break;case 5:left=new(){Mode="resistance",Strength=5,Start=0};right=new(){Mode="resistance",Strength=3,Start=1};break;case 6:left=right=new(){Mode="pulse",Strength=2,Start=3,Frequency=15};break;}
   leftTrigger.Set(left);rightTrigger.Set(right);
  };
  LedMode.ItemsSource=new[]{new EffectChoice("game",L.T("Управляет игра")),new("fixed",L.T("Постоянный цвет")),new("pulse",L.T("Пульсация")),new("rainbow",L.T("Радуга")),new("battery",L.T("Уровень заряда"))};
  redSlider=new(this,L.T("Красный"),0,255);greenSlider=new(this,L.T("Зелёный"),0,255);blueSlider=new(this,L.T("Синий"),0,255);foreach(var field in new[]{redSlider,greenSlider,blueSlider}){RgbSliders.Children.Add(field);field.Slider.ValueChanged+=(_,_)=>UpdateLedPreview();}
  foreach(var color in new[]{Color.FromRgb(10,132,255),Color.FromRgb(90,200,250),Color.FromRgb(191,90,242),Color.FromRgb(48,209,88),Color.FromRgb(255,159,10),Color.FromRgb(255,69,58),Avalonia.Media.Colors.White}){
   var button=new Button {Width=32,Height=32,MinHeight=0,Margin=new Thickness(1),Theme=this.RequireTheme("CompactButton"),Content=new Border {Width=17,Height=17,CornerRadius=new CornerRadius(8.5),Background=new SolidColorBrush(color)},Padding=new Thickness(3)};button.Click+=(_,_)=>{redSlider.Value=color.R;greenSlider.Value=color.G;blueSlider.Value=color.B;};LedSwatches.Children.Add(button);
  }
  sensitivitySlider=new(this,L.T("Чувствительность"),10,300,"%");limitSlider=new(this,L.T("Максимальная сила"),1,80,"%");gateSlider=new(this,L.T("Порог тишины"),0,20,format:v=>v==0?L.T("выкл"):L.F("{0} дБ",-80+v*2));foreach(var field in new[]{sensitivitySlider,limitSlider,gateSlider})AudioSliders.Children.Add(field);
  LedMode.SelectionChanged+=(_,_)=>UpdateLedPreview();LedBrightness.ValueChanged+=(_,_)=>UpdateLedPreview();LedSpeed.ValueChanged+=(_,_)=>UpdateLedPreview();
  var devices=new[]{new AudioOutputDevice("",L.T("Устройство вывода Windows"))};AudioDevice.ItemsSource=devices;AudioDevice.SelectedIndex=0;
  LoadEffectDraft(options);
  Loaded+=(_,_)=>{if(!preview)LoadAudioDevices(readProfiles==null?options.AudioDeviceId:editingProfile.Effects.AudioDeviceId);};
 }
 async void TestTriggers(object? sender,RoutedEventArgs e){
  if(preview || !TriggerTestButton.IsEnabled)return;
  testingTriggers=true;
  TriggerTestButton.IsEnabled=false;
  TriggerTestFeedback.IsVisible=true;
  try{
   await BridgeClient.PreviewTriggersAsync(leftTrigger.Value,rightTrigger.Value);
   TriggerTestFeedback.Text=L.T("Нажмите курки. Тест завершится сам.");
   await Task.Delay(3000);
   if(!closed)TriggerTestFeedback.IsVisible=false;
  }catch(Exception){
   if(!closed)TriggerTestFeedback.Text=L.T("Тест недоступен. Подключите контроллер и выберите свободный курок с эффектом.");
  }finally{
   testingTriggers=false;
   if(!closed)TriggerTestButton.IsEnabled=effectState.Active;
  }
 }
 void LoadEffectDraft(EffectOptions options){
  leftTrigger.Set(options.Left);rightTrigger.Set(options.Right);TriggerPreset.SelectedIndex=0;
  LedMode.SelectedItem=LedMode.Items.Cast<EffectChoice>().First(c=>c.Key==options.LedMode);
  redSlider.Value=options.Red;greenSlider.Value=options.Green;blueSlider.Value=options.Blue;LedBrightness.Value=options.Brightness;LedSpeed.Value=options.Speed;
  AudioEffectsEnabled.IsChecked=options.AudioEnabled;AudioBassOnly.IsChecked=options.BassOnly;sensitivitySlider.Value=options.Sensitivity;limitSlider.Value=options.Limit;gateSlider.Value=options.Gate;
  UpdateLedPreview();
 }
 void LoadAudioDevices(string selected){
  bool wasReady=autoReady;autoReady=false;
  try{var devices=AudioReactiveCapture.Devices().ToList();if(!devices.Any(d=>d.Id==selected))devices.Add(new(selected,L.T("Выбранное устройство недоступно")));AudioDevice.ItemsSource=devices;AudioDevice.SelectedItem=devices.First(d=>d.Id==selected);}
  catch(Exception error){AudioStatus.Text=L.T("Не удалось получить устройства: ")+error.Message;}finally{autoReady=wasReady;}
 }
 void RefreshAudioDevices(object sender,RoutedEventArgs e){if(!preview)LoadAudioDevices((AudioDevice.SelectedItem as AudioOutputDevice)?.Id??"");}
 void ResetEffects(object sender,RoutedEventArgs e){LoadEffectDraft(new());QueueAutoApply(true);}
 void UpdateLedPreview(){
  if(redSlider==null || LedMode.SelectedItem is not EffectChoice choice)return;
  LedManualControls.IsEnabled=choice.Key!="game";RgbSliders.IsEnabled=choice.Key is "fixed" or "pulse";LedSwatches.IsEnabled=RgbSliders.IsEnabled;LedSpeed.IsEnabled=choice.Key is "pulse" or "rainbow";
  BrightnessLabel.Text=$"{LedBrightness.Value:F0}%";SpeedLabel.Text=$"{LedSpeed.Value:F0}%";
  double r=redSlider.Value,g=greenSlider.Value,b=blueSlider.Value,scale=LedBrightness.Value/100,cycle=ledClock.Elapsed.TotalSeconds*(0.05+LedSpeed.Value/100*0.4);
  if(choice.Key=="pulse")scale*=0.12+0.88*(0.5-0.5*Math.Cos(cycle*Math.PI*2));
  if(choice.Key=="rainbow"){double hue=(cycle%1)*6;int sector=(int)hue;double up=(hue-sector)*255,down=255-up;(r,g,b)=sector switch{0=>(255d,up,0d),1=>(down,255d,0d),2=>(0d,255d,up),3=>(0d,down,255d),4=>(up,0d,255d),_=>(255d,0d,down)};}
  if(choice.Key=="battery")(r,g,b)=effectState.BatteryPercent is not int battery?(80,80,80):battery<=20?(255,45,35):battery<=50?(255,160,10):(48,209,88);
  if(choice.Key=="game"){(r,g,b)=(90,90,95);scale=1;}
  LedPreview.Background=new SolidColorBrush(Color.FromRgb((byte)Math.Clamp(r*scale,0,255),(byte)Math.Clamp(g*scale,0,255),(byte)Math.Clamp(b*scale,0,255)));
 }
 internal void SetEffectState(EffectState state,string audioError,int level,double inputPeak=0,string source="",bool xboxMode=false){
  TriggerTestButton.IsEnabled=!preview && !testingTriggers && state.Active;
  effectState=state;TriggerOwnership.Text=$"L2: {(state.LeftOwned?L.T("управляет игра"):L.T("ручные эффекты доступны"))} · R2: {(state.RightOwned?L.T("управляет игра"):L.T("ручные эффекты доступны"))}";
  LedOwnership.Text=state.LedOwned?L.T("Подсветкой управляет игра до переподключения эмуляции."):L.T("Цвет игры всегда имеет приоритет. Для режима заряда: зелёный / оранжевый / красный.");
  AudioEffectsCard.IsEnabled=!xboxMode;
  AudioMeter.Value=xboxMode?0:Math.Clamp(inputPeak*100,0,100);
  AudioStatus.Text=xboxMode?L.T("Недоступно в Xbox-режиме. Переключитесь на PS5."):state.RumbleOwned?L.T("Игра использует вибрацию / HD-haptics. Звук не подмешивается."):!string.IsNullOrEmpty(audioError)?L.T("Захват звука недоступен: ")+audioError:!state.Options.AudioEnabled?L.T("Захват звука выключен"):!state.Active?L.T("Захват приостановлен: включите эмуляцию и подключите контроллер."):inputPeak>0?L.F("Вход {0:F1}% · haptics {1}%",inputPeak*100,level):L.F("Ожидание звука · {0}",source);
 }
 async Task ApplyEffectDraftAsync(){
  var next=new EffectOptions {Left=leftTrigger.Value,Right=rightTrigger.Value,LedMode=(LedMode.SelectedItem as EffectChoice)?.Key??"game",Red=redSlider.Value,Green=greenSlider.Value,Blue=blueSlider.Value,Brightness=(int)LedBrightness.Value,Speed=(int)LedSpeed.Value,AudioEnabled=AudioEffectsEnabled.IsChecked==true,BassOnly=AudioBassOnly.IsChecked==true,Sensitivity=sensitivitySlider.Value,Limit=limitSlider.Value,Gate=gateSlider.Value,AudioDeviceId=(AudioDevice.SelectedItem as AudioOutputDevice)?.Id??""};
  if(!next.Valid)throw new InvalidOperationException(L.T("Проверьте диапазоны курков и эффектов."));
  if(applyEffects==null)throw new InvalidOperationException(L.T("Предпросмотр не изменяет настройки контроллера."));
  if(saveProfile!=null)await SaveProfileEffects(next);else await applyEffects(next);
 }
}
