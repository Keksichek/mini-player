using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace YandexMiniPlayer;
public sealed partial class Player
{
 Canvas? deviceFace;
 RotateTransform? platterRotation;
 FaceMeter? activeMeter;
 bool mediaPlaying;
 readonly System.Diagnostics.Stopwatch motionTime=new();
 readonly System.Windows.Threading.DispatcherTimer motionTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
 void SyncMotion(){
  bool run=mediaPlaying&&IsVisible&&!closing&&deviceFace!=null;
  if(run){motionTime.Start();motionTimer.Start();}
  else{motionTime.Stop();motionTimer.Stop();}
 }
 void RenderMotion(){
  double seconds=motionTime.Elapsed.TotalSeconds;
  if(platterRotation!=null)platterRotation.Angle=seconds*45%360;
  else if(activeMeter!=null){activeMeter.Phase=seconds*2;activeMeter.InvalidateVisual();}
 }
 void SetMediaPlaying(bool playing){mediaPlaying=playing;SyncMotion();}
 void ApplyDeviceFace(Border body){
  var stack=(StackPanel)body.Child;
  if(deviceFace!=null){stack.Children.Remove(deviceFace);deviceFace=null;}
  bool vinyl=appearance.Theme=="Винил",sun=appearance.Theme=="Закат",lcd=appearance.Theme=="Карманный LCD";
  var classic=!(vinyl||sun||lcd);
  stack.Children[0].Visibility=stack.Children[1].Visibility=classic?Visibility.Visible:Visibility.Collapsed;
  platterRotation=null;activeMeter=null;
  if(classic){SyncMotion();return;}
  Brush B(string c)=>BrushOf(c);
  LinearGradientBrush Gradient(string first,string last,double angle=90)=>new((Color)ColorConverter.ConvertFromString(first),(Color)ColorConverter.ConvertFromString(last),angle);
  var face=new Canvas{Width=vinyl?360:sun?320:230,Height=vinyl?110:sun?168:204,Background=Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Center};
  deviceFace=face;stack.Children.Insert(2,face);
  body.Background=appearance.BackgroundColor!=null?B(appearance.BackgroundColor):vinyl?Gradient("#F2E7E7","#AD939A",70):sun?Gradient("#FFFAF4","#F6E1DA",55):Gradient("#F1EFDE","#BEBDAE");
  body.CornerRadius=new CornerRadius(vinyl?appearance.Round*2.8:sun?appearance.Round:appearance.Round*.65);
  body.BorderBrush=B(appearance.BorderColor??(vinyl?"#D1BDC2":sun?"#FFFDF9":"#AAA99C"));
  var ink=B(vinyl?"#312732":sun?"#382A26":"#DBE5D7");
  var accent=B(appearance.Accent);
  Canvas drawingLayer=face;
  void Put(FrameworkElement e,double x,double y,double w,double h){
   e.Width=w;e.Height=h;Canvas.SetLeft(e,x);Canvas.SetTop(e,y);drawingLayer.Children.Add(e);
  }
  void Box(double x,double y,double w,double h,Brush fill,double radius){
   Put(new Border{Background=fill,CornerRadius=new CornerRadius(radius),IsHitTestVisible=false},x,y,w,h);
  }
  void Oval(double x,double y,double d,Brush fill,Brush? edge=null){
   Put(new Ellipse{Fill=fill,Stroke=edge,StrokeThickness=edge==null?0:1,IsHitTestVisible=false},x,y,d,d);
  }
  void Shape(string data,Brush? fill,Brush? stroke=null,double thickness=1){
   face.Children.Add(new Path{Data=Geometry.Parse(data),Fill=fill,Stroke=stroke,StrokeThickness=thickness,IsHitTestVisible=false});
  }
  void Caption(TextBlock src,double x,double y,double w,double size,Brush color,bool bold=false){
   var t=new TextBlock{Foreground=color,FontSize=size,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,FontFamily=new FontFamily(lcd?"Consolas":"Segoe UI"),TextTrimming=TextTrimming.CharacterEllipsis};
   t.SetBinding(TextBlock.TextProperty,new Binding("Text"){Source=src});
   Put(t,x,y,w,size*1.6);
  }
  void Art(double x,double y,double size,double radius){
   var img=new Image{Stretch=Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,size,size),radius,radius)};
   img.SetBinding(Image.SourceProperty,new Binding("Source"){Source=cover});Put(img,x,y,size,size);
  }
  var buttonSkin=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='"+(lcd?"4":"24")+"' BorderBrush='#70FFFFFF' BorderThickness='1'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='.78'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter Property='Opacity' Value='.60'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.46'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  Button Key(double x,double y,double w,double h,string label,string hint,Brush fill,Action action){
   var b=new Button{Content=label,Foreground=B("#302B2E"),Background=fill,Template=buttonSkin,ToolTip=hint,Cursor=Cursors.Hand};
   System.Windows.Automation.AutomationProperties.SetName(b,hint);
   b.Click+=(_,_)=>action();Put(b,x,y,w,h);return b;
  }
  void Transport(Button src,double x,double y,double w,double h,Brush fill,string hint){
   var b=Key(x,y,w,h,"",hint,fill,()=>src.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
      var glyph=new Path{Width=src==play?14:12,Height=src==play?15:12,Stretch=Stretch.Fill,
    Fill=sun&&src==play?Brushes.White:B("#322E30")};
   var geometry=new Style(typeof(Path));
   string path=src==prev?"M 1,1 L 3,1 L 3,13 L 1,13 Z M 13,1 L 4,7 L 13,13 Z":
    src==next?"M 11,1 L 13,1 L 13,13 L 11,13 Z M 1,1 L 10,7 L 1,13 Z":"M 2,0 L 14,7 L 2,14 Z";
   geometry.Setters.Add(new Setter(Path.DataProperty,Geometry.Parse(path)));
   if(src==play){
    var pausedIcon=new DataTrigger{Binding=new Binding("Content"){Source=src},Value="Ⅱ"};
    pausedIcon.Setters.Add(new Setter(Path.DataProperty,Geometry.Parse("M 2,0 H 6 V 14 H 2 Z M 9,0 H 13 V 14 H 9 Z")));
    geometry.Triggers.Add(pausedIcon);
   }
   glyph.Style=geometry;b.Content=glyph;
   if(!sun||src==play)b.Effect=new DropShadowEffect{BlurRadius=sun?8:3,ShadowDepth=2,Opacity=.28};
   b.SetBinding(IsEnabledProperty,new Binding("IsEnabled"){Source=src});
  }
  Brush metal=Gradient("#F0E9E6","#A79DA1");
  double scale=appearance.CoverSize/52;
  if(vinyl){
   double d=Math.Min(105,96*scale),x=54-d/2,y=55-d/2;
   Oval(x-2,y-2,d+4,metal);
   platterRotation=new RotateTransform(0,54,55);
   var platter=new Canvas{Width=108,Height=110,RenderTransform=platterRotation,IsHitTestVisible=false};
   face.Children.Add(platter);drawingLayer=platter;
   Oval(x,y,d,Gradient("#39333F","#121217",45));
   for(int i=1;i<=11;i++)Oval(x+i*2,y+i*2,d-i*4,Brushes.Transparent,Gradient(i%2==0?"#514A51":"#322E36","#222023",40));
   double label=d*.33;Oval(54-label/2,55-label/2,label,accent);Art(54-label/2,55-label/2,label,label/2);Oval(52,53,4,B("#43343B"));
   drawingLayer=face;
   Shape("M 111,19 L 96,35 Q 94,58 75,75",null,B("#564B53"),4);
   Shape("M 110,18 L 95,34 Q 93,57 74,74",null,metal,2);
   Oval(101,9,15,metal,B("#796A73"));Shape("M 71,73 L 77,69 L 81,73 L 74,79 Z",B("#57515C"));
   Caption(title,125,16,178,15,ink,true);
   if(appearance.ShowArtist)Caption(artist,126,36,174,10,ink);
   Transport(prev,126,53,32,32,metal,"Предыдущий трек");Transport(play,164,50,38,38,accent,"Воспроизведение / пауза");Transport(next,208,53,32,32,metal,"Следующий трек");
   if(appearance.ShowTime)Caption(timeLabel,127,89,128,10,ink);
  }else if(sun){
   double d=Math.Min(107,92*scale);
   // The landscape stays visible until the media session supplies album art.
   var landscape=new Canvas{Background=Gradient("#FFDFBD","#F79A68"),Clip=new RectangleGeometry(new Rect(0,0,92,92),12,12)};
   var solar=new Ellipse{Width=45,Height=45,Fill=Gradient("#FFB154","#F66936")};Canvas.SetLeft(solar,28);Canvas.SetTop(solar,14);landscape.Children.Add(solar);
   foreach(var (g,c) in new[]{("M 0,51 Q 19,45 48,68 Q 70,44 92,53 L 92,92 L 0,92 Z","#E9854F"),("M 0,59 Q 35,82 60,66 Q 80,57 92,62 L 92,92 L 0,92 Z","#C35C40"),("M 0,83 Q 24,84 54,71 Q 75,63 92,74 L 92,92 L 0,92 Z","#9B4540")})landscape.Children.Add(new Path{Data=Geometry.Parse(g),Fill=B(c)});
   landscape.Width=landscape.Height=92;var view=new Viewbox{Child=landscape};Put(view,7,12,d,d);Art(7,12,d,d*.13);
   var meta=new TextBlock{Text="♪   СЕЙЧАС В ЭФИРЕ",FontSize=9,FontWeight=FontWeights.Bold,Foreground=B("#9B4127")};Put(meta,122,19,140,18);
   Caption(title,122,42,185,16,ink,true);if(appearance.ShowArtist)Caption(artist,122,68,184,11,ink);
   if(appearance.ShowTime)Caption(timeLabel,122,108,170,10,ink);
   Transport(prev,85,133,32,31,Brushes.Transparent,"Предыдущий трек");Transport(play,138,122,44,44,accent,"Воспроизведение / пауза");Transport(next,200,133,32,31,Brushes.Transparent,"Следующий трек");
  }else{
   Box(2,2,226,115,B("#858877"),10);Box(4,4,222,111,Gradient("#303735","#202724"),8);
   Box(12,12,30*scale,30*scale,B("#414C45"),3);Shape("M 18,30 V 34 H 21 V 24 L 31,22 V 29 H 28 V 33 H 33 V 19 L 19,22 V 30 Z",ink);Art(12,12,30*scale,3);Caption(title,54,13,142,14,ink,true);
   if(appearance.ShowArtist)Caption(artist,54,35,160,10,ink);
   if(appearance.ShowTime)Caption(timeLabel,13,94,150,10,ink);
   for(int row=0;row<3;row++)for(int i=0;i<44;i++)Oval(7+i*5+(row%2)*2,124+row*4,2,B("#555950"));
   Transport(prev,4,146,53,52,metal,"Предыдущий трек");Transport(play,60,146,109,52,accent,"Воспроизведение / пауза");Transport(next,172,146,54,52,metal,"Следующий трек");
  }
  var meter=new FaceMeter(vinyl,lcd,accent);
  activeMeter=meter;RenderMotion();SyncMotion();
  meter.SetBinding(FaceMeter.PositionProperty,new Binding("Value"){Source=seek});
  meter.SetBinding(FaceMeter.DurationProperty,new Binding("Maximum"){Source=seek});
  Put(meter,vinyl?301:sun?122:12,vinyl?12:sun?85:58,vinyl?52:sun?181:204,vinyl?88:sun?22:34);
  meter.ToolTip="Перемотка";meter.Cursor=Cursors.Hand;meter.Focusable=true;
  void Locate(MouseEventArgs e){var p=e.GetPosition(meter);double f=vinyl?(Math.Atan2(p.Y-meter.Height/2,Math.Max(0,p.X))*180/Math.PI+78)/156:p.X/meter.Width;seek.Value=Math.Clamp(f,0,1)*seek.Maximum;}
  meter.MouseLeftButtonDown+=(_,e)=>{if(!seek.IsEnabled)return;meter.Focus();seekTrack=track;seeking=true;meter.CaptureMouse();Locate(e);e.Handled=true;};
  meter.MouseMove+=(_,e)=>{if(meter.IsMouseCaptured)Locate(e);};
  meter.MouseLeftButtonUp+=async(_,e)=>{if(!meter.IsMouseCaptured)return;Locate(e);meter.ReleaseMouseCapture();e.Handled=true;await Seek();};
  meter.LostMouseCapture+=(_,_)=>seeking=false;
  meter.KeyDown+=async(_,e)=>{if(!seek.IsEnabled||e.Key is not (System.Windows.Input.Key.Left or System.Windows.Input.Key.Right))return;seekTrack=track;seek.Value+=e.Key==System.Windows.Input.Key.Left?-5:5;e.Handled=true;await Seek();};
  var menu=new ContextMenu();
  void Item(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
  Item("Развернуть / свернуть",()=>SetExpanded(!expanded));Item("Кастомизация",()=>Customize());Item("Закрепить / открепить",()=>{Topmost=!Topmost;UpdatePin();Save();});Item("Скрыть",()=>Hide());
  face.ContextMenu=menu;
  Key(vinyl?252:sun?286:198,vinyl?56:sun?4:10,26,22,"⋯","Меню плеера",vinyl?metal:B("#EAE3DB"),()=>menu.IsOpen=true);

 }
 sealed class FaceMeter:FrameworkElement{
  public static readonly DependencyProperty PositionProperty=DependencyProperty.Register("Position",typeof(double),typeof(FaceMeter),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
  public static readonly DependencyProperty DurationProperty=DependencyProperty.Register("Duration",typeof(double),typeof(FaceMeter),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
  public double Phase {get;set;}
  readonly bool arc,lcd;readonly Brush accent,idle;
  public FaceMeter(bool arc,bool lcd,Brush accent){this.arc=arc;this.lcd=lcd;this.accent=accent;idle=BrushOf(lcd?"#79827A":"#D6BEB4");}
  protected override void OnRender(DrawingContext dc){
   double w=ActualWidth,h=ActualHeight,f=Math.Clamp((double)GetValue(PositionProperty)/Math.Max(1,(double)GetValue(DurationProperty)),0,1);
   dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,w,h));
   if(arc){
    Point At(double t){double a=(-78+t*156)*Math.PI/180;return new Point((w-6)*Math.Cos(a),h/2+(h/2-3)*Math.Sin(a));}
    for(int i=1;i<=80;i++)dc.DrawLine(new Pen(i<=f*80?accent:BrushOf("#645861"),2),At((i-1)/80d),At(i/80d));
    dc.DrawEllipse(BrushOf("#E5DBDC"),new Pen(BrushOf("#776970"),1),At(f),4,4);
   }else{

    for(int x=1;x<w;x+=3){
     double envelope=.2+.8*Math.Pow(Math.Sin(x*.031+.4-Phase*.6),2);
     double bar=2+(h-3)*envelope*(.35+.65*Math.Pow(Math.Sin(x*.10-Phase),2));
     dc.DrawRectangle(x/w<=f?accent:idle,null,new Rect(x,(h-bar)/2,lcd?1:2,bar));
    }
   }
  }
 }
}
