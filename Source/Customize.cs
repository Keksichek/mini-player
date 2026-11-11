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
  UpdateFacePin();
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
  bool dark=appearance.Theme is "Тёмная" or "Minecraft" or "Ночной океан" or "Лес" or "Терминал" or "Винил";bool pixel=appearance.Theme=="Minecraft";
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
  var original=appearance;
  bool saved=false, syncing=false;
  var ink=BrushOf("#292535");var muted=BrushOf("#645E71");var purple=BrushOf("#6544A0");
  var w=new Window{Title="mini payer · Оформление",Width=Math.Min(552,SystemParameters.WorkArea.Width),Height=Math.Min(692,SystemParameters.WorkArea.Height),WindowStartupLocation=WindowStartupLocation.CenterScreen,Topmost=true,Background=BrushOf("#F8F6FA"),Foreground=ink,FontFamily=new FontFamily("Segoe UI")};
  customizeWindow=w;
  var shell=new DockPanel();w.Content=new Border{Background=w.Background,Padding=new Thickness(20,14,20,12),Child=shell};
  var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
  TextBlock Text(string value,double size,Brush color,bool bold=false)=>new(){Text=value,FontSize=size,Foreground=color,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
  header.Children.Add(Text("Оформление плеера",25,ink,true));
  header.Children.Add(Text("Изменения сразу видны в плеере.",13,muted));
  var skin=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#7653AD'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='.78'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  Button Btn(object label)=>new(){Content=label,Template=skin,Background=Brushes.White,Foreground=ink,BorderBrush=BrushOf("#DDD8E4"),Padding=new Thickness(12,8,12,8),Cursor=System.Windows.Input.Cursors.Hand};
  void Mark(Button b,bool selected){b.Background=selected?BrushOf("#EEE7FA"):Brushes.White;b.BorderBrush=selected?purple:BrushOf("#DDD8E4");}
  var nav=new System.Windows.Controls.Primitives.UniformGrid{Rows=1,Margin=new Thickness(0,14,0,10)};header.Children.Add(nav);
  var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);
  var save=Btn("Сохранить");save.Background=purple;save.Foreground=Brushes.White;
  var cancel=Btn("Отмена");cancel.Margin=new Thickness(0,0,8,0);
  foreach(var b in new[]{save,cancel}){DockPanel.SetDock(b,Dock.Right);footer.Children.Add(b);}
  var reset=Btn("Сбросить");reset.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(reset);
  var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};shell.Children.Add(scroll);
  var gallery=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
  var colors=new StackPanel();var geometry=new StackPanel();
  var pages=new FrameworkElement[]{gallery,colors,geometry};var tabs=new Button[3];
  void Page(int index){scroll.Content=pages[index];scroll.ScrollToTop();for(int i=0;i<tabs.Length;i++)Mark(tabs[i],i==index);}
  string[] names={"Темы","Цвета","Размер и детали"};
  for(int i=0;i<3;i++){int index=i;var b=tabs[i]=Btn(names[i]);b.Margin=new Thickness(2);nav.Children.Add(b);b.Click+=(_,_)=>Page(index);}
  var cards=new System.Collections.Generic.Dictionary<string,Button>();
  StackPanel Card(StackPanel parent,string heading){
   var box=new StackPanel();box.Children.Add(Text(heading,17,ink,true));
   parent.Children.Add(new Border{Background=Brushes.White,CornerRadius=new CornerRadius(12),Padding=new Thickness(16),Margin=new Thickness(2,3,2,8),Child=box});return box;
  }
  var palette=Card(colors,"Палитра");var swatches=new System.Windows.Controls.Primitives.UniformGrid{Rows=1,Margin=new Thickness(0,12,0,10)};palette.Children.Add(swatches);
  string[] roles={"Акцент","Рамка","Фон"};int role=0;
  string ColorAt(int i)=>i==0?appearance.Accent:i==1?appearance.BorderColor??appearance.Accent:appearance.BackgroundColor??ThemeBackground(appearance.Theme);
  var chips=new Button[3];var samples=new Border[3];
  for(int i=0;i<3;i++){
   int index=i;var group=new StackPanel();samples[i]=new Border{Height=17,CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1),BorderBrush=BrushOf("#33808080")};group.Children.Add(samples[i]);group.Children.Add(Text(roles[i],12,ink));
   var b=chips[i]=Btn(group);b.Margin=new Thickness(2);swatches.Children.Add(b);b.Tag=index;
  }
  var editor=new DockPanel();palette.Children.Add(editor);var wheel=new ColorWheel{Margin=new Thickness(0,4,16,2)};DockPanel.SetDock(wheel,Dock.Left);editor.Children.Add(wheel);
  var values=new StackPanel{VerticalAlignment=VerticalAlignment.Center};editor.Children.Add(values);var code=Text("",15,ink,true);values.Children.Add(code);values.Children.Add(Text("Яркость",12,muted));var brightness=new Slider{Minimum=0,Maximum=1};values.Children.Add(brightness);
  brightness.ValueChanged+=(_,_)=>{if(!syncing){wheel.Value=brightness.Value;wheel.Publish();}};
  wheel.Changed+=hex=>{appearance=role switch{0=>appearance with{Accent=hex},1=>appearance with{BorderColor=hex},_=>appearance with{BackgroundColor=hex}};code.Text=hex;for(int i=0;i<3;i++)samples[i].Background=BrushOf(ColorAt(i));ApplyAppearance();};
  var form=Card(geometry,"Размер и форма");var updates=new System.Collections.Generic.List<Action>();
  void Setting(string name,double min,double max,double step,Func<double> read,Action<double> write,bool percent=false){
   var line=new DockPanel{Margin=new Thickness(0,8,0,0)};form.Children.Add(line);var val=Text("",12,purple,true);DockPanel.SetDock(val,Dock.Right);line.Children.Add(val);line.Children.Add(Text(name,13,ink));
   var slider=new Slider{Minimum=min,Maximum=max,TickFrequency=step,IsSnapToTickEnabled=true};form.Children.Add(slider);
   System.Windows.Automation.AutomationProperties.SetName(slider,name);
   void Update(){val.Text=percent?$"{read()*100:0}%":$"{read():0} px";}
   updates.Add(()=>{slider.Value=read();Update();});slider.ValueChanged+=(_,_)=>{if(syncing)return;write(slider.Value);Update();ApplyAppearance();};
  }
  Setting("Масштаб",.85,1.4,.05,()=>appearance.Scale,v=>appearance=appearance with{Scale=v},true);
  Setting("Непрозрачность",.65,1,.01,()=>appearance.Alpha,v=>appearance=appearance with{Alpha=v},true);
  Setting("Скругление углов",0,26,1,()=>appearance.Round,v=>appearance=appearance with{Round=v});
  Setting("Толщина рамки",0,4,1,()=>appearance.BorderWidth,v=>appearance=appearance with{BorderWidth=v});
  Setting("Размер обложки",40,64,1,()=>appearance.CoverSize,v=>appearance=appearance with{CoverSize=v});
  var info=Card(geometry,"Информация о треке");
  var artistCheck=new CheckBox{Content="Исполнитель",Margin=new Thickness(0,8,0,0)};var timeCheck=new CheckBox{Content="Время трека",Margin=new Thickness(0,6,0,0)};
  info.Children.Add(artistCheck);info.Children.Add(timeCheck);
  artistCheck.Click+=(_,_)=>{appearance=appearance with{ShowArtist=artistCheck.IsChecked==true};ApplyAppearance();};
  timeCheck.Click+=(_,_)=>{appearance=appearance with{ShowTime=timeCheck.IsChecked==true};ApplyAppearance();};
  void Sync(){syncing=true;foreach(var pair in cards)Mark(pair.Value,pair.Key==appearance.Theme);for(int i=0;i<3;i++){samples[i].Background=BrushOf(ColorAt(i));Mark(chips[i],role==i);}code.Text=ColorAt(role);wheel.SetColor(code.Text);brightness.Value=wheel.Value;foreach(var action in updates)action();artistCheck.IsChecked=appearance.ShowArtist;timeCheck.IsChecked=appearance.ShowTime;syncing=false;}
  foreach(var name in new[]{"Винил","Закат","Карманный LCD"}.Concat(Themes.Where(t=>t is not ("Винил" or "Закат" or "Карманный LCD")))){
   var content=new StackPanel();var img=new Image{Height=65,Stretch=Stretch.Uniform};
   using(var stream=typeof(Player).Assembly.GetManifestResourceStream("preview."+name+".png")){
    if(stream!=null){var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();img.Source=bitmap;}
   }
   content.Children.Add(img);var label=Text(name,13,ink,true);label.HorizontalAlignment=HorizontalAlignment.Center;label.Margin=new Thickness(0,5,0,0);content.Children.Add(label);
   var b=Btn(content);System.Windows.Automation.AutomationProperties.SetName(b,name);b.Margin=new Thickness(3);cards.Add(name,b);gallery.Children.Add(b);
   b.Click+=(_,_)=>{if(appearance.Theme==name)return;appearance=appearance with{Theme=name,Accent=ThemeAccent(name),BorderColor=ThemeAccent(name),BackgroundColor=null,Round=name=="Minecraft"?0:20,BorderWidth=name=="Minecraft"?3:1};Sync();ApplyAppearance();};
  }
  foreach(var chip in chips)chip.Click+=(_,_)=>{role=(int)chip.Tag;Sync();};
  reset.Click+=(_,_)=>{appearance=new();Sync();ApplyAppearance();};
  cancel.Click+=(_,_)=>w.Close();cancel.IsCancel=true;
  save.Click+=(_,_)=>{try{Directory.CreateDirectory(Path.GetDirectoryName(AppearancePath)!);File.WriteAllText(AppearancePath,JsonSerializer.Serialize(appearance));saved=true;w.Close();}catch(Exception ex){MessageBox.Show(w,ex.Message,"Не удалось сохранить");}};
  w.Closed+=(_,_)=>{if(!saved){appearance=original;ApplyAppearance();}customizeWindow=null;};
  Sync();Page(0);if(!preview){shownByUser=true;Show();w.Show();}
 }
}