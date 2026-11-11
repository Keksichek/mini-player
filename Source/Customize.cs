using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;
namespace YandexMiniPlayer;
public sealed partial class Player
{
 record Appearance(string Theme="Светлая",string Accent="#7FA6D9",double Scale=1,double Alpha=1,string? BorderColor=null,double Round=20,double BorderWidth=1,double CoverSize=52,bool ShowArtist=true,bool ShowTime=true,string? BackgroundColor=null);
 Appearance appearance=new();Window? customizeWindow;
 void UpdatePin(){
  if(pinButton==null)return;
  var shape=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 8,2 L 20,2 L 18,10 L 23,15 L 15,15 L 15,24 L 12,27 L 12,15 L 5,15 L 10,10 Z"),Fill=Topmost?Brushes.Black:Brushes.SlateGray,Width=20,Height=20,Stretch=Stretch.Uniform,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(Topmost?0:-45)};
  pinButton.Content=shape;pinButton.Background=Topmost?(Brush)new BrushConverter().ConvertFromString(appearance.Accent)!:Brushes.Transparent;
  pinButton.ToolTip=Topmost?"Закреплён поверх окон — нажмите, чтобы открепить":"Не закреплён — нажмите, чтобы закрепить";
  System.Windows.Automation.AutomationProperties.SetName(pinButton,Topmost?"Открепить плеер":"Закрепить плеер");
 }
 string AppearancePath=>Path.Combine(Path.GetDirectoryName(settingsPath)!,"appearance.json");
 static readonly string[] Themes={"Светлая","Тёмная","Minecraft","Ретро","Лаванда","Ночной океан","Лес","Кофе","Персик","Терминал","Стекло","Винил","Закат","Карманный LCD"};
 static readonly string[] Accents={"#7FA6D9","#8BE878","#55DCCB","#F0AA60","#D99BEF","#EE829A"};
 static bool ValidColor(string value){try{return value.Length==7&&value[0]=='#'&&ColorConverter.ConvertFromString(value) is Color;}catch{return false;}}
 void LoadAppearance(){try{var a=JsonSerializer.Deserialize<Appearance>(File.ReadAllText(AppearancePath));if(a!=null&&Themes.Contains(a.Theme)&&ValidColor(a.Accent)&&(a.BorderColor==null||ValidColor(a.BorderColor))&&(a.BackgroundColor==null||ValidColor(a.BackgroundColor)))appearance=a with{Scale=Math.Clamp(a.Scale,0.85,1.4),Alpha=Math.Clamp(a.Alpha,0.65,1),Round=Math.Clamp(a.Round,0,26),BorderWidth=Math.Clamp(a.BorderWidth,0,4),CoverSize=Math.Clamp(a.CoverSize,40,64)};}catch{}ApplyAppearance();}
 void ApplyAppearance(){
  if(Content is not Border body)return;
  bool dark=appearance.Theme is "Тёмная" or "Minecraft" or "Ночной океан" or "Лес" or "Кофе" or "Терминал" or "Винил";bool pixel=appearance.Theme=="Minecraft";
  string bg=appearance.Theme switch{"Тёмная"=>"#19212D","Minecraft"=>"#383D34","Ретро"=>"#E0DAC5","Лаванда"=>"#EBE5F5","Ночной океан"=>"#102C46","Лес"=>"#213E32","Кофе"=>"#3E2C26","Персик"=>"#FFE4D7","Терминал"=>"#0B1911",_=>"#F2F3F6"};
  var foreground=(Brush)new BrushConverter().ConvertFromString(dark?"#F1F3ED":"#354052")!;
  var accent=(Brush)new BrushConverter().ConvertFromString(appearance.Accent)!;
  Resources["AccentBrush"]=accent;body.Background=(Brush)new BrushConverter().ConvertFromString(bg)!;body.BorderBrush=(Brush)new BrushConverter().ConvertFromString(appearance.BorderColor??appearance.Accent)!;body.BorderThickness=new Thickness(appearance.BorderWidth);body.CornerRadius=new CornerRadius(appearance.Round);
  body.LayoutTransform=new ScaleTransform(appearance.Scale,appearance.Scale);body.Opacity=appearance.Alpha;
  TextElement.SetFontFamily(body,new FontFamily(pixel||appearance.Theme is "Терминал" or "Карманный LCD"?"Consolas":"Segoe UI"));
  void Walk(DependencyObject node){
   if(node is TextBlock t)t.Foreground=foreground;
   if(node is CheckBox c)c.Foreground=foreground;
   if(node is Button b){b.Foreground=foreground;b.Background=(Brush)new BrushConverter().ConvertFromString(dark?"#4B5860":"#F8F7F4")!;}
   if(node is ComboBox)return;
   foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())Walk(child);
  }
  cover.Width=cover.Height=appearance.CoverSize;if(cover.Parent is Border art){art.Width=art.Height=appearance.CoverSize;}artist.Visibility=appearance.ShowArtist?Visibility.Visible:Visibility.Collapsed;timeLabel.Visibility=appearance.ShowTime?Visibility.Visible:Visibility.Collapsed;
  Walk(body);play.Background=accent;play.Foreground=Brushes.Black;
  ApplyThemeVisuals(body);ApplyDeviceFace(body);UpdatePin();SetExpanded(expanded);
 }
 void Customize(bool preview=false){
  if(customizeWindow!=null){customizeWindow.Activate();return;}
  var original=appearance;bool saved=false;var w=new Window{Title="Кастомизация мини-плеера",Width=410,Height=640,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Topmost=true,Background=Brushes.WhiteSmoke};customizeWindow=w;
  var panel=new StackPanel{Margin=new Thickness(22),Background=Brushes.WhiteSmoke};var shell=new DockPanel{Background=Brushes.WhiteSmoke};var scroll=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};w.Content=shell;
  void Label(string text)=>panel.Children.Add(new TextBlock{Text=text,Margin=new Thickness(0,8,0,6)});
  Label("Тема");var theme=new ComboBox{ItemsSource=Themes,SelectedItem=appearance.Theme};panel.Children.Add(theme);
  var colorRow=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(colorRow);
  ColorWheel AddPicker(string name,string initial,Action<string> update){
   var group=new StackPanel{Width=110,Margin=new Thickness(0,12,8,0)};colorRow.Children.Add(group);group.Children.Add(new TextBlock{Text=name,Margin=new Thickness(0,0,0,6)});
   var wheel=new ColorWheel();wheel.SetColor(initial);group.Children.Add(wheel);
   group.Children.Add(new TextBlock{Text="Яркость",FontSize=10});var brightness=new Slider{Minimum=0,Maximum=1,Value=wheel.Value};group.Children.Add(brightness);
   var rgb=new TextBlock{FontSize=10,TextWrapping=TextWrapping.Wrap,Text=initial};group.Children.Add(rgb);
   wheel.Changed+=hex=>{var c=(Color)ColorConverter.ConvertFromString(hex);rgb.Text=$"{hex}\nRGB {c.R}, {c.G}, {c.B}";update(hex);ApplyAppearance();};
   bool sync=false;wheel.LoadedColor+=hex=>{sync=true;brightness.Value=wheel.Value;rgb.Text=hex;sync=false;};brightness.ValueChanged+=(_,_)=>{if(sync)return;wheel.Value=brightness.Value;wheel.Publish();};return wheel;
  }
  var accentWheel=AddPicker("Акцент",appearance.Accent,hex=>appearance=appearance with{Accent=hex});
  var borderWheel=AddPicker("Рамка",appearance.BorderColor??appearance.Accent,hex=>appearance=appearance with{BorderColor=hex});
  var backgroundWheel=AddPicker("Фон",appearance.BackgroundColor??ThemeBackground(appearance.Theme),hex=>appearance=appearance with{BackgroundColor=hex});
  panel.Children.Add(new TextBlock{Text="Тема меняет текстуры, кнопки, шкалы и бегунки. Стекло — полупрозрачное, без размытия рабочего стола.",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  Label("Размер: 85–140%");var scale=new Slider{Minimum=.85,Maximum=1.4,Value=appearance.Scale,TickFrequency=.05,IsSnapToTickEnabled=true};panel.Children.Add(scale);
  Label("Непрозрачность: 65–100%");var opacity=new Slider{Minimum=.65,Maximum=1,Value=appearance.Alpha};panel.Children.Add(opacity);
  Label("Форма и обложка");
  Slider Setting(string name,double min,double max,double value,Action<double> changed){
   var caption=new TextBlock{Text=$"{name}: {value:0} px",FontSize=11,Margin=new Thickness(0,4,0,0)};panel.Children.Add(caption);
   var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true};panel.Children.Add(slider);slider.ValueChanged+=(_,_)=>{caption.Text=$"{name}: {slider.Value:0} px";changed(slider.Value);ApplyAppearance();};return slider;
  }
  var rounding=Setting("Скругление",0,26,appearance.Round,v=>appearance=appearance with{Round=v});
  var borderSize=Setting("Толщина рамки (0 — скрыть)",0,4,appearance.BorderWidth,v=>appearance=appearance with{BorderWidth=v});
  var coverSize=Setting("Размер обложки",40,64,appearance.CoverSize,v=>appearance=appearance with{CoverSize=v});
  var showArtist=new CheckBox{Content="Показывать исполнителя",IsChecked=appearance.ShowArtist,Margin=new Thickness(0,7,0,0)};panel.Children.Add(showArtist);
  var showTime=new CheckBox{Content="Показывать время трека",IsChecked=appearance.ShowTime,Margin=new Thickness(0,7,0,0)};panel.Children.Add(showTime);
  showArtist.Click+=(_,_)=>{appearance=appearance with{ShowArtist=showArtist.IsChecked==true};ApplyAppearance();};showTime.Click+=(_,_)=>{appearance=appearance with{ShowTime=showTime.IsChecked==true};ApplyAppearance();};
  panel.Children.Add(new TextBlock{Text="Предпросмотр сразу в плеере.\nЗакрытие окна отменяет несохранённые изменения.",FontSize=11,Margin=new Thickness(0,14,0,10)});
  var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(22,8,22,14)};DockPanel.SetDock(row,Dock.Bottom);shell.Children.Add(row);shell.Children.Add(scroll);
  var reset=new Button{Content="Сброс",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,10,0)};row.Children.Add(reset);
  var save=new Button{Content="Сохранить",Padding=new Thickness(12,6,12,6)};row.Children.Add(save);
  theme.SelectionChanged+=(_,_)=>{appearance=appearance with{Theme=(string)theme.SelectedItem,Accent=ThemeAccent((string)theme.SelectedItem),BorderColor=ThemeAccent((string)theme.SelectedItem),BackgroundColor=null,Round=(string)theme.SelectedItem=="Minecraft"?0:20,BorderWidth=(string)theme.SelectedItem=="Minecraft"?3:1};rounding.Value=appearance.Round;borderSize.Value=appearance.BorderWidth;accentWheel.SetColor(appearance.Accent);borderWheel.SetColor(appearance.BorderColor??appearance.Accent);backgroundWheel.SetColor(ThemeBackground(appearance.Theme));ApplyAppearance();};
  scale.ValueChanged+=(_,_)=>{appearance=appearance with{Scale=scale.Value};ApplyAppearance();};opacity.ValueChanged+=(_,_)=>{appearance=appearance with{Alpha=opacity.Value};ApplyAppearance();};
  reset.Click+=(_,_)=>{appearance=new();theme.SelectedItem=appearance.Theme;scale.Value=1;opacity.Value=1;rounding.Value=20;borderSize.Value=1;coverSize.Value=52;showArtist.IsChecked=true;showTime.IsChecked=true;accentWheel.SetColor(appearance.Accent);borderWheel.SetColor(appearance.BorderColor??appearance.Accent);backgroundWheel.SetColor(ThemeBackground(appearance.Theme));ApplyAppearance();};
  save.Click+=(_,_)=>{try{Directory.CreateDirectory(Path.GetDirectoryName(AppearancePath)!);File.WriteAllText(AppearancePath,JsonSerializer.Serialize(appearance));saved=true;w.Close();}catch(Exception ex){MessageBox.Show(w,ex.Message,"Не удалось сохранить");}};
  w.Closed+=(_,_)=>{if(!saved){appearance=original;ApplyAppearance();}customizeWindow=null;};if(!preview){shownByUser=true;Show();w.Show();}
 }
}
