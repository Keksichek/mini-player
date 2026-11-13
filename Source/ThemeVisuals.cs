using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace MiniPlayer;
public sealed partial class Player
{
 static Brush BrushOf(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
 static string ThemeBackground(string theme)=>theme switch{"Винил"=>"#26212B","Закат"=>"#FFF4E9","Карманный LCD"=>"#CFD8C5",_=>StudioFace.For(theme).Paper};
 static string ThemeAccent(string theme)=>theme switch{"Винил"=>"#FA8095","Закат"=>"#FF8655","Карманный LCD"=>"#FF795C","Minecraft"=>"#91DE47","Ретро"=>"#B44940","Терминал"=>"#52ED89","Стекло"=>"#6FADE8","Ночной океан"=>"#4CD8ED","Лес"=>"#B2D975","Кофе"=>"#ECB570","Лаванда"=>"#B38DE3","Персик"=>"#F59B75",_=>"#7FA6D9"};
 Brush Pattern(string theme,Brush background){
  var drawing=new DrawingGroup();using(var dc=drawing.Open()){
   dc.DrawRectangle(background,null,new Rect(0,0,64,64));
   if(theme=="Minecraft"){
    for(int y=0;y<64;y+=8)for(int x=0;x<64;x+=8){var shade=(x*3+y*7)%5;dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(12+shade*6),shade%2==0?(byte)0:(byte)255,shade%2==0?(byte)0:(byte)255,shade%2==0?(byte)0:(byte)255)),null,new Rect(x,y,8,8));}
   }
  }
  var brush=new DrawingBrush(drawing){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,64,64)};brush.Freeze();return brush;
 }
 void ApplyThemeVisuals(Border body){
  string theme=appearance.Theme;bool pixel=theme is "Minecraft" or "Терминал",retro=theme is "Ретро" or "Карманный LCD",glass=theme=="Стекло";
  var baseColor=BrushOf(appearance.BackgroundColor??ThemeBackground(theme));
  if(glass){var tint=(Color)ColorConverter.ConvertFromString(appearance.BackgroundColor??ThemeBackground(theme));body.Background=new LinearGradientBrush(Color.FromArgb(195,tint.R,tint.G,tint.B),Color.FromArgb(110,tint.R,tint.G,tint.B),90);}
  else if(theme=="Minecraft")body.Background=Pattern(theme,baseColor);
  else if(theme=="Закат" && appearance.BackgroundColor==null)
   body.Background=new LinearGradientBrush(Color.FromRgb(255,250,241),Color.FromRgb(252,210,194),32);
  else body.Background=baseColor;
  Resources["ThemeTrack"]=BrushOf(pixel?"#182514":retro?"#8F9982":glass?"#88FFFFFF":"#66708090");
  Resources["ThemeProgress"]=pixel||retro?BrushOf(appearance.Accent):new LinearGradientBrush((Color)ColorConverter.ConvertFromString(appearance.Accent),Colors.White,0);
  string thumb=pixel?"<Rectangle Fill='{DynamicResource AccentBrush}' Stroke='#172012' StrokeThickness='2'/>":retro?"<Border Background='#5C6557' BorderBrush='#E5E3CD' BorderThickness='2'><Rectangle Width='2' Fill='White' Margin='0,2'/></Border>":theme=="Ночной океан"?"<Path Data='M 7,0 L 14,7 L 7,14 L 0,7 Z' Fill='{DynamicResource AccentBrush}' Stroke='White'/>":"<Ellipse Fill='White' Stroke='{DynamicResource AccentBrush}' StrokeThickness='2'/>";
  string thickness=pixel?"8":retro?"6":glass?"3":"5";
  var template=(ControlTemplate)XamlReader.Parse($"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Slider'><Grid Margin='3,0' Background='Transparent'><Border Height='{thickness}' Background='{{DynamicResource ThemeTrack}}' CornerRadius='{(pixel?0:4)}' VerticalAlignment='Center'/><Track Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='{thickness}' Background='{{DynamicResource ThemeProgress}}' CornerRadius='{(pixel?0:4)}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='{(retro?9:14)}' Height='14'><Thumb.Template><ControlTemplate TargetType='Thumb'>{thumb}</ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>");
  seek.Template=template;
  var buttonTemplate=(ControlTemplate)XamlReader.Parse($"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Name='B' Background='{{TemplateBinding Background}}' CornerRadius='{(pixel?0:retro?3:glass||theme is "Закат" or "Винил"?13:8)}' BorderBrush='{(pixel?"#9BAA91":retro?"#82725C":"#55FFFFFF")}' BorderThickness='{(pixel||retro?2:1)}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='{{DynamicResource AccentBrush}}'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Opacity' Value='0.6'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  void Walk(DependencyObject node){if(node is Button b)b.Template=buttonTemplate;if(node is ComboBox)return;foreach(var child in LogicalTreeHelper.GetChildren(node))if(child is DependencyObject d)Walk(d);}Walk(body);
  if(cover.Parent is Border frame){
   bool vinyl=theme=="Винил", sunset=theme=="Закат", lcd=theme=="Карманный LCD";
   DockPanel.SetDock(frame,sunset?Dock.Right:Dock.Left);
   frame.Margin=sunset?new Thickness(10,0,0,0):new Thickness(0,0,10,0);
   frame.BorderBrush=BrushOf(appearance.BorderColor??appearance.Accent);
   double edge=pixel?3:retro?2:1;
   frame.BorderThickness=new Thickness(edge);
   double size=appearance.CoverSize-2*edge;
   double radius=vinyl?size/2:pixel||lcd?0:glass||sunset?12:5;
   frame.CornerRadius=new CornerRadius(radius);
   frame.Background=BrushOf(lcd?"#829278":"#27394C");
   if(vinyl){
    var disc=new RadialGradientBrush();
    for(int ring=0;ring<9;ring++){
     disc.GradientStops.Add(new GradientStop(Color.FromRgb(62,58,66),.44+ring*.06));
     disc.GradientStops.Add(new GradientStop(Color.FromRgb(28,26,32),.47+ring*.06));
    }
    frame.Background=disc;
   }
   cover.Width=cover.Height=vinyl?size*.46:size;
   cover.Clip=new RectangleGeometry(new Rect(0,0,cover.Width,cover.Height),radius,radius);
   play.Width=lcd?52:vinyl?26:36;
   if(lcd){title.Background=BrushOf("#A5B498");play.Background=BrushOf("#ED805E");}
   else title.ClearValue(TextBlock.BackgroundProperty);
  }
 }
}
