using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WidgetLauncher;

// One renderer for the workshop and the actual player. All dimensions are independent of font size.
public sealed class PresetSurface : Canvas,IDisposable
{
 readonly string root;
 readonly ImageBrush mediaBrush=new();
 readonly DispatcherTimer animation=new(DispatcherPriority.Background);
 readonly List<(BitmapSource Image,int Delay)> frames=new();
 string mediaFile="";
 int frameIndex;
 bool disposed;
 WidgetPreset preset=new();
 public Dictionary<string,FrameworkElement> Elements {get;}=new();
 public Geometry ShapeGeometry {get;private set;}=Geometry.Empty;
 public string MediaError {get;private set;}="";
 public int AnimationFrameCount=>frames.Count;
 public int AnimationFrameIndex=>frameIndex;
 public bool AnimationRunning=>animation.IsEnabled;
 public PresetSurface(string hubRoot){
  root=Path.GetFullPath(hubRoot);ClipToBounds=true;
  animation.Tick+=(_,_)=>AdvanceAnimation();Loaded+=(_,_)=>StartAnimation();Unloaded+=(_,_)=>animation.Stop();
  IsVisibleChanged+=(_,_)=>{if(IsVisible)StartAnimation();else animation.Stop();};
 }
 static Brush Brush(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
 public static Geometry Outline(WidgetPreset p){
  var bounds=new Rect(0,0,p.Width,p.Height);
  return p.Shape switch{
   "ellipse" or "circle"=>new EllipseGeometry(bounds),
   "rectangle" or "square"=>new RectangleGeometry(bounds),
   "pill"=>new RectangleGeometry(bounds,Math.Min(p.Width,p.Height)/2,Math.Min(p.Width,p.Height)/2),
   "cut"=>Cut(bounds,Math.Min(Math.Max(8,p.CornerRadius),Math.Min(p.Width,p.Height)/3)),
   "star"=>Polygon(p,10,true),"diamond"=>Polygon(p,4,false),"hexagon"=>Polygon(p,6,false),
   "heart"=>Heart(p),
   _=>new RectangleGeometry(bounds,p.CornerRadius,p.CornerRadius)
  };
 }
 static Geometry Heart(WidgetPreset p){var geometry=Geometry.Parse("M 50,96 C 42,86 0,56 0,27 C 0,0 35,-6 50,21 C 65,-6 100,0 100,27 C 100,56 58,86 50,96 Z").Clone();geometry.Transform=new ScaleTransform(p.Width/100,p.Height/96);return geometry;}
 static Geometry Polygon(WidgetPreset p,int count,bool star){
  var points=new List<Point>();for(int i=0;i<count;i++){double angle=-Math.PI/2+Math.PI*2*i/count,radius=star&&i%2!=0?.48:1;points.Add(new Point(p.Width/2+Math.Cos(angle)*p.Width/2*radius,p.Height/2+Math.Sin(angle)*p.Height/2*radius));}
  var g=new StreamGeometry();using(var dc=g.Open()){dc.BeginFigure(points[0],true,true);dc.PolyLineTo(points.Skip(1).ToArray(),true,false);}g.Freeze();return g;
 }
 static Geometry Cut(Rect r,double c){var g=new StreamGeometry();using(var dc=g.Open()){dc.BeginFigure(new Point(c,0),true,true);dc.PolyLineTo(new[]{new Point(r.Width-c,0),new Point(r.Width,c),new Point(r.Width,r.Height-c),new Point(r.Width-c,r.Height),new Point(c,r.Height),new Point(0,r.Height-c),new Point(0,c)},true,false);}g.Freeze();return g;}
 public void MoveElement(string id,double x,double y){
  if(!Elements.TryGetValue(id,out var control))return;
  var element=preset.Elements[id];
  element.X=x;element.Y=y;
  SetLeft(control,x);SetTop(control,y);
 }
 public void SizeElement(string id,double width,double height){
  if(!Elements.TryGetValue(id,out var control)||control.Width==width&&control.Height==height)return;
  var element=preset.Elements[id];
  element.Width=control.Width=width;element.Height=control.Height=height;
  if(control is Button { Content:FrameworkElement icon }){
   icon.Width=icon.Height=IconSize(width,height);
   if(icon is Grid group && group.Children[0] is FrameworkElement glyph)glyph.Width=glyph.Height=icon.Width;
  }
 }
 static double IconSize(double width,double height)=>Math.Max(5,Math.Min(width,height)*.52);
 public void Configure(WidgetPreset value){
  if(disposed)return;preset=value.Copy();preset.Normalize();var p=preset;
  LoadMedia(p.MediaPath);Children.Clear();Elements.Clear();Width=p.Width;Height=p.Height;Opacity=1;Background=Brushes.Transparent;Clip=ShapeGeometry=Outline(p);
  // Apply transparency to each layer so a locked control keeps its own saved opacity.
  Children.Add(new System.Windows.Shapes.Rectangle{Width=p.Width,Height=p.Height,Fill=Brush(p.BackgroundColor),Opacity=p.Opacity,IsHitTestVisible=false});
  var art=Decoration(p);art.Width=p.Width;art.Height=p.Height;art.Opacity=p.Opacity;Children.Add(art);
  if(frames.Count>0){
   mediaBrush.Stretch=Stretch.Fill;
   double sw=frames[0].Image.PixelWidth,sh=frames[0].Image.PixelHeight;
   double scale=p.MediaFit=="contain"?Math.Min(p.Width/sw,p.Height/sh):Math.Max(p.Width/sw,p.Height/sh);
   double width=p.MediaFit=="stretch"?p.Width:sw*scale,height=p.MediaFit=="stretch"?p.Height:sh*scale;
   var photo=new System.Windows.Shapes.Rectangle{Width=width,Height=height,Fill=mediaBrush,Opacity=p.MediaOpacity*p.Opacity,IsHitTestVisible=false};SetLeft(photo,(p.Width-width)*p.MediaX);SetTop(photo,(p.Height-height)*p.MediaY);Children.Add(photo);
  }
  TextBlock Text(string id,string value,double fraction){
   var style=p.EffectiveStyle(id);FontFamily font;
   try{font=style.FontFamily=="Onest"?new FontFamily(new Uri(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar),"./Fonts/#Onest"):new FontFamily(style.FontFamily);}catch{font=new FontFamily("Segoe UI");}
   return new(){Text=value,FontFamily=font,FontWeight=FontWeight.FromOpenTypeWeight(style.FontWeight),FontSize=Math.Max(1,style.FontSize*fraction),Foreground=Brush(style.TextColor),TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,VerticalAlignment=VerticalAlignment.Center};
  }
  var cover=new Image{Stretch=Stretch.UniformToFill,Source=new DrawingImage(((DrawingBrush)CoverArt(p.EffectiveStyle("cover"))).Drawing)};Put("cover",cover);
  Put("title",Text("title","Название трека",1));Put("artist",Text("artist","Исполнитель",.72));Put("time",Text("time","1:35 / 4:00",.56));
  foreach(string id in new[]{"previous","play","next","repeat","pin","lock","clickthrough","hide","menu"}){
   var style=p.EffectiveStyle(id);var b=new Button{Background=id=="play"?Brush(style.AccentColor):new SolidColorBrush(Color.FromArgb(34,128,128,128)),Foreground=Brush(style.TextColor),Padding=new Thickness(0),BorderThickness=new Thickness(0),MinHeight=0,MinWidth=0,Cursor=System.Windows.Input.Cursors.Hand,ToolTip=Label(id)};
   b.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Name='Frame' Background='{TemplateBinding Background}' CornerRadius='7'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
   Put(id,b);SetButtonIcon(id,id=="lock"?"unlocked":id=="repeat"?"none":"off");
  }
  Put("progress",MakeSlider(p.EffectiveStyle("progress"),240,95));Put("volume",MakeSlider(p.EffectiveStyle("volume"),100,50));
  Children.Add(new System.Windows.Shapes.Path{Data=ShapeGeometry,Stroke=Brush(p.BorderColor),StrokeThickness=p.BorderWidth*2,Opacity=p.Opacity,IsHitTestVisible=false});
 }
 void Put(string id,FrameworkElement element){var item=preset.Elements[id];element.Width=item.Width;element.Height=item.Height;element.Opacity=preset.EffectiveStyle(id).Opacity;SetLeft(element,item.X);SetTop(element,item.Y);element.Visibility=preset.IsElementVisible(id)?Visibility.Visible:Visibility.Collapsed;element.ClipToBounds=true;System.Windows.Automation.AutomationProperties.SetName(element,Label(id));Elements[id]=element;Children.Add(element);}
 public static string Label(string id)=>id switch{"cover"=>"Обложка","title"=>"Название трека","artist"=>"Исполнитель","previous"=>"Предыдущий трек","play"=>"Пауза / воспроизведение","next"=>"Следующий трек","repeat"=>"Повтор","pin"=>"Поверх окон","lock"=>"Блокировка перемещения","clickthrough"=>"Клики насквозь","hide"=>"Скрыть","menu"=>"Меню плеера","progress"=>"Шкала трека","time"=>"Время трека","volume"=>"Громкость",_=>id};
 static Slider MakeSlider(PresetElementStyle p,double max,double value){
  var slider=new Slider{Minimum=0,Maximum=max,Value=value,IsMoveToPointEnabled=true,SmallChange=1,LargeChange=10,MinHeight=0,MinWidth=0};slider.Resources["PresetAccent"]=Brush(p.AccentColor);slider.Resources["PresetText"]=Brush(p.TextColor);
  slider.Template=(ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Slider">
 <Grid Background="Transparent" Margin="5,0"><Border Height="3" Background="{DynamicResource PresetText}" Opacity=".22" CornerRadius="1.5" VerticalAlignment="Center"/>
 <Track Name="PART_Track"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="{DynamicResource PresetAccent}" CornerRadius="1.5" VerticalAlignment="Center"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
 <Track.Thumb><Thumb Width="10" Height="10"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="{DynamicResource PresetText}" Stroke="{DynamicResource PresetAccent}" StrokeThickness="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
 <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track>
 </Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>
""");return slider;
 }
 public void SetButtonIcon(string id,string state){
  if(!Elements.TryGetValue(id,out var item)||item is not Button b)return;var style=preset.EffectiveStyle(id);
  string data=id switch{
   "previous"=>"M 2,2 V 14 M 13,2 L 4,8 L 13,14 Z", "next"=>"M 14,2 V 14 M 3,2 L 12,8 L 3,14 Z",
   "play"=>state=="pause"?"M 4,2 H 6 V 14 H 4 Z M 10,2 H 12 V 14 H 10 Z":"M 4,2 L 4,14 L 14,8 Z",
   "pin"=>state=="on"?"M 5,1 H 11 L 10,6 L 13,10 H 9 L 8,16 L 7,10 H 3 L 6,6 Z":"M 3,4 L 8,1 L 10,5 L 14,7 L 11,10 L 15,15 L 9,11 L 6,14 L 4,10 L 1,8 Z",
   "lock"=>state=="locked"?"M 4,7 V 5 A 4,4 0 0 1 12,5 V 7 M 3,7 H 13 V 15 H 3 Z M 8,10 V 12":"M 6,7 V 4 A 4,4 0 0 1 14,4 M 3,7 H 13 V 15 H 3 Z M 8,10 V 12",
   "clickthrough"=>state=="on"?"M 3,2 L 15,9 L 9,10 L 6,16 Z M 12,3 H 16 M 14,1 L 16,3 L 14,5":"M 3,2 L 15,9 L 9,10 L 6,16 Z",
   "hide"=>"M 1,8 Q 8,-2 15,8 Q 8,18 1,8 Z M 10,8 A 2,2 0 1 1 6,8 A 2,2 0 1 1 10,8",
   "menu"=>"M 3,7 V 5 A 5,5 0 0 1 13,5 V 11 A 5,5 0 0 1 3,11 Z M 8,1 V 7 H 13",
   _=>"M 3,6 V 4 H 13 L 10,1 M 13,4 V 7 M 13,10 V 12 H 3 L 6,15 M 3,12 V 9"
  };
  double size=IconSize(b.Width,b.Height);
  var icon=new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=Brush(style.TextColor),Fill=id is "play" or "previous" or "next" or "pin"?Brush(style.TextColor):Brushes.Transparent,StrokeThickness=1.4,Width=size,Height=size,Stretch=Stretch.Uniform};
  if(id=="repeat"&&state=="none")icon.Opacity=.5;
  if(id=="repeat"&&state=="one"){var grid=new Grid{Width=size,Height=size};grid.Children.Add(icon);grid.Children.Add(new TextBlock{Text="1",FontSize=7,FontFamily=new FontFamily("Segoe UI"),Foreground=Brush(style.TextColor),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});b.Content=grid;}else b.Content=icon;
 }
 static Brush CoverArt(PresetElementStyle p){
  var g=new DrawingGroup();using(var dc=g.Open()){
   dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(p.AccentColor),(Color)ColorConverter.ConvertFromString(p.BackgroundColor),65),null,new Rect(0,0,100,100));
   dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(170,255,255,255)),null,new Point(72,27),13,13);
   var mountain=Geometry.Parse("M 0,87 L 28,42 L 49,69 L 68,46 L 100,82 L 100,100 L 0,100 Z");dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(120,12,24,39)),null,mountain);
  }return new DrawingBrush(g){Stretch=Stretch.UniformToFill};
 }
 static FrameworkElement Decoration(WidgetPreset p){
  var g=new DrawingGroup();var accent=(Color)ColorConverter.ConvertFromString(p.AccentColor);var soft=new SolidColorBrush(Color.FromArgb(32,accent.R,accent.G,accent.B));
  using(var dc=g.Open()){
   var pen=new Pen(soft,2);
   if(p.Theme=="Minecraft"){for(int y=0;y<p.Height;y+=16)for(int x=0;x<p.Width;x+=16)if((x*3+y*7)%5<2)dc.DrawRectangle(soft,null,new Rect(x,y,16,16));}
   else if(p.Theme=="Терминал"||p.Theme=="Карманный LCD")for(int y=0;y<p.Height;y+=5)dc.DrawLine(new Pen(soft,.6),new Point(0,y),new Point(p.Width,y));
   else if(p.Theme=="Винил")for(int i=0;i<12;i++)dc.DrawEllipse(null,pen,new Point(p.Width*.83,p.Height*.5),15+i*7,15+i*7);
   else if(p.Theme=="Ночной океан")for(int y=0;y<7;y++){var geo=new StreamGeometry();using(var c=geo.Open()){c.BeginFigure(new Point(0,p.Height*.4+y*18),false,false);c.BezierTo(new Point(p.Width*.3,p.Height*.15+y*18),new Point(p.Width*.7,p.Height*.8+y*18),new Point(p.Width,p.Height*.4+y*18),true,false);}dc.DrawGeometry(null,pen,geo);}
   else if(p.Theme=="Лес"||p.Theme=="Лаванда")for(int i=0;i<20;i++){double x=(i*73)%p.Width,y=p.Height*.58+(i*31)%(p.Height*.4);dc.DrawLine(pen,new Point(x,p.Height),new Point(x,y));dc.DrawEllipse(soft,null,new Point(x-4,y+9),5,12);dc.DrawEllipse(soft,null,new Point(x+4,y+21),5,12);}
   else if(p.Theme=="Кофе"||p.Theme=="Ретро")for(int i=0;i<4;i++)dc.DrawEllipse(null,new Pen(soft,5),new Point(p.Width*.85,p.Height*.6),30+i*12,30+i*12);
   else{dc.DrawEllipse(soft,null,new Point(p.Width*.85,p.Height*.25),p.Height*.6,p.Height*.6);dc.DrawEllipse(soft,null,new Point(p.Width*.1,p.Height),p.Height*.7,p.Height*.7);}
  }
  return new System.Windows.Shapes.Rectangle{Fill=new DrawingBrush(g){Viewbox=new Rect(0,0,p.Width,p.Height),ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill},IsHitTestVisible=false};
 }
 void LoadMedia(string relative){
  string file="";
  try{file=new PresetStore(root).ResolveMedia(relative);}catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){SetMediaFailure("Изображение недоступно. Выберите файл заново.");return;}
  if(file==mediaFile&&MediaError.Length==0)return;
  animation.Stop();frames.Clear();mediaBrush.ImageSource=null;frameIndex=0;mediaFile=file;MediaError="";
  if(file.Length==0)return;
  try{
   if(new FileInfo(file).Length>PresetStore.MaximumMediaBytes)throw new IOException("Файл больше 20 МБ.");
   using var stream=File.OpenRead(file);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
   var size=PresetStore.ValidateMediaFrames(decoder,Path.GetExtension(file).ToLowerInvariant());
   if(decoder is GifBitmapDecoder gif)DecodeGif(gif,size.Width,size.Height);
   else{var first=decoder.Frames[0];first.Freeze();frames.Add((first,100));}
   mediaBrush.ImageSource=frames[0].Image;StartAnimation();
  }catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or InvalidOperationException){SetMediaFailure("Не удалось открыть изображение или GIF: "+ex.Message);}
 }
 void SetMediaFailure(string error){animation.Stop();frames.Clear();mediaBrush.ImageSource=null;mediaFile="";MediaError=error;}
 static int Metadata(BitmapMetadata? metadata,string query,int fallback){try{return metadata?.ContainsQuery(query)==true?Convert.ToInt32(metadata.GetQuery(query)):fallback;}catch{return fallback;}}
 void DecodeGif(GifBitmapDecoder decoder,int width,int height){
  var metadata=decoder.Metadata as BitmapMetadata;
  byte[] canvas=new byte[checked(width*height*4)];
  var firstMetadata=decoder.Frames[0].Metadata as BitmapMetadata;bool transparent=Metadata(firstMetadata,"/grctlext/TransparencyFlag",1)!=0;
  int backgroundIndex=Metadata(metadata,"/logscrdesc/BackgroundColorIndex",0);Color transparentPixel=Color.FromArgb(0,0,0,0),background=transparentPixel;
  if(!transparent&&decoder.Palette is { } palette&&backgroundIndex<palette.Colors.Count)background=palette.Colors[backgroundIndex];
  void Clear(int x,int y,int w,int h,Color color){for(int row=y;row<Math.Min(height,y+h);row++)for(int col=x;col<Math.Min(width,x+w);col++){int at=(row*width+col)*4;canvas[at]=color.B;canvas[at+1]=color.G;canvas[at+2]=color.R;canvas[at+3]=color.A;}}
  Clear(0,0,width,height,background);
  foreach(var frame in decoder.Frames){
   var fm=frame.Metadata as BitmapMetadata;int left=Metadata(fm,"/imgdesc/Left",0),top=Metadata(fm,"/imgdesc/Top",0);
   if(left<0||top<0||left+frame.PixelWidth>width||top+frame.PixelHeight>height)throw new IOException("Некорректная область кадра GIF.");
   int disposal=Metadata(fm,"/grctlext/Disposal",0);byte[]? previous=disposal==3?(byte[])canvas.Clone():null;
   var converted=new FormatConvertedBitmap(frame,PixelFormats.Bgra32,null,0);byte[] pixels=new byte[checked(frame.PixelWidth*frame.PixelHeight*4)];converted.CopyPixels(pixels,frame.PixelWidth*4,0);
   for(int y=0;y<frame.PixelHeight;y++)for(int x=0;x<frame.PixelWidth;x++){int from=(y*frame.PixelWidth+x)*4,to=((y+top)*width+x+left)*4;byte alpha=pixels[from+3];if(alpha==0)continue;for(int c=0;c<3;c++)canvas[to+c]=(byte)((pixels[from+c]*alpha+canvas[to+c]*(255-alpha))/255);canvas[to+3]=(byte)(alpha+canvas[to+3]*(255-alpha)/255);}
   var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,canvas,width*4);image.Freeze();frames.Add((image,Math.Clamp(Metadata(fm,"/grctlext/Delay",10)*10,20,10000)));
   if(disposal==2)Clear(left,top,frame.PixelWidth,frame.PixelHeight,Metadata(fm,"/grctlext/TransparencyFlag",1)!=0?transparentPixel:background);
   else if(disposal==3&&previous!=null)canvas=previous;
  }
 }
 void AdvanceAnimation(){if(frames.Count<2)return;frameIndex=(frameIndex+1)%frames.Count;mediaBrush.ImageSource=frames[frameIndex].Image;animation.Interval=TimeSpan.FromMilliseconds(frames[frameIndex].Delay);}
 void StartAnimation(){if(disposed||!IsLoaded||!IsVisible||frames.Count<2)return;animation.Interval=TimeSpan.FromMilliseconds(frames[frameIndex].Delay);animation.Start();}

 public void Dispose(){if(disposed)return;disposed=true;animation.Stop();frames.Clear();mediaBrush.ImageSource=null;Children.Clear();Elements.Clear();}
}
