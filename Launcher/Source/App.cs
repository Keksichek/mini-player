using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WidgetLauncher;

public sealed class LauncherApp : Application
{
 [STAThread]
 public static int Main(string[] args)
 {
  try
  {
   string root=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
   string identity="Local\\WidgetLauncher."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())))[..16];
   bool preview=args.Length>=2&&args[0]=="--preview";
   bool remove=args.Length==2&&args[0]=="--remove-widget"&&args[1]=="mini-player";
   using var mutex=new Mutex(true,identity,out bool first);
   if(!first&&!preview)
   {
    string eventName=identity+(remove?".RemoveMiniPlayer":".Show");
    for(int attempt=0;attempt<30;attempt++)
    {
     if(EventWaitHandle.TryOpenExisting(eventName,out var existing)){using(existing)existing.Set();return 0;}
     Thread.Sleep(100);
    }
    throw new IOException("Лаунчер ещё запускается. Повторите попытку через несколько секунд.");
   }
   var app=new LauncherApp{ShutdownMode=ShutdownMode.OnMainWindowClose};
   var window=new LauncherWindow(root,preview,preview&&args.Length>2?args[2]:"catalog");
   if(preview)
   {
    var surface=(FrameworkElement)window.Content;surface.Measure(new Size(window.Width,window.Height));surface.Arrange(new Rect(0,0,window.Width,window.Height));surface.UpdateLayout();
    var bitmap=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);
    bitmap.Render((Visual)window.Content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var output=File.Create(args[1]);encoder.Save(output);window.Close();return 0;
   }
   using var show=new EventWaitHandle(false,EventResetMode.AutoReset,identity+".Show");
   using var stop=new EventWaitHandle(false,EventResetMode.AutoReset,identity+".Stop");
   using var removeEvent=new EventWaitHandle(false,EventResetMode.AutoReset,identity+".RemoveMiniPlayer");
   var waits=new[]{
    ThreadPool.RegisterWaitForSingleObject(show,(_,_)=>app.Dispatcher.BeginInvoke(window.BringForward),null,Timeout.Infinite,false),
    ThreadPool.RegisterWaitForSingleObject(stop,(_,_)=>app.Dispatcher.BeginInvoke(window.RequestShutdown),null,Timeout.Infinite,false),
    ThreadPool.RegisterWaitForSingleObject(removeEvent,(_,_)=>app.Dispatcher.BeginInvoke(window.RequestMiniPlayerRemoval),null,Timeout.Infinite,false)
   };
   if(remove)window.Loaded+=(_,_)=>window.RequestMiniPlayerRemoval();
   try{return app.Run(window);}
   finally{foreach(var wait in waits)wait.Unregister(null!);}
  }
  catch(Exception ex){MessageBox.Show(ex.Message,"Widget Launcher",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
 }
}

public sealed class LauncherWindow : Window
{
 static readonly Brush BackgroundBrush=ColorBrush("#101117"),PanelBrush=ColorBrush("#191A23"),MutedBrush=ColorBrush("#999BAF"),AccentBrush=ColorBrush("#B9A1FF"),TextBrush=ColorBrush("#F1F0F7");
 readonly WidgetManager manager;
 readonly List<Widget> catalog;
 readonly StackPanel content=new(){Margin=new Thickness(34,28,34,28)};
 readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=AccentBrush,Margin=new Thickness(0,0,0,16)};
 readonly Dictionary<string,Button> nav=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
 string page="catalog",message="",statusSnapshot="";
 bool busy,pendingStop,removalPrompt;
 readonly bool preview;

 public LauncherWindow(string root,bool previewMode=false,string initialPage="catalog")
 {
  preview=previewMode;page=initialPage is "installed" or "settings"?initialPage:"catalog";manager=new WidgetManager(root);catalog=manager.ReadCatalog().ToList();
  Title="Widget Launcher";Width=1020;Height=720;MinWidth=900;MinHeight=650;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  Background=BackgroundBrush;Foreground=TextBrush;
  FontFamily=new FontFamily(new Uri(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar),"./Fonts/#Onest");FontWeight=FontWeights.Bold;FontSize=13;
  Resources[typeof(Button)]=(Style)XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Cursor' Value='Hand'/><Setter Property='Foreground' Value='#F1F0F7'/><Setter Property='Background' Value='#292B39'/><Setter Property='Padding' Value='18,11'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='10' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
  var layout=new Grid{Background=BackgroundBrush};layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(208)});layout.ColumnDefinitions.Add(new ColumnDefinition());Content=layout;
  var sidebar=new Border{Background=ColorBrush("#15161E"),BorderBrush=ColorBrush("#272835"),BorderThickness=new Thickness(0,0,1,0),Padding=new Thickness(20,28,20,22)};
  layout.Children.Add(sidebar);var side=new DockPanel();sidebar.Child=side;
  var footer=new StackPanel{Margin=new Thickness(10,0,0,0)};footer.Children.Add(Label("Всё на своём месте",11,MutedBrush));footer.Children.Add(Label("Версия 1.0",10,ColorBrush("#64677B"),new Thickness(0,6,0,0)));DockPanel.SetDock(footer,Dock.Bottom);side.Children.Add(footer);
  var navigation=new StackPanel();side.Children.Add(navigation);
  var logo=new Border{Width=40,Height=40,CornerRadius=new CornerRadius(12),Background=AccentBrush,HorizontalAlignment=HorizontalAlignment.Left,Child=Label("W",23,BackgroundBrush)};((TextBlock)logo.Child).HorizontalAlignment=HorizontalAlignment.Center;((TextBlock)logo.Child).VerticalAlignment=VerticalAlignment.Center;navigation.Children.Add(logo);
  navigation.Children.Add(Label("Widget\nLauncher",22,TextBrush,new Thickness(0,15,0,34)));
  AddNav(navigation,"catalog","Каталог");AddNav(navigation,"installed","Мои виджеты");AddNav(navigation,"settings","Настройки");
  var scroller=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(scroller,1);layout.Children.Add(scroller);
  timer.Tick+=(_,_)=>{if(busy)return;string snapshot=Snapshot();if(snapshot!=statusSnapshot)Render();};
  Loaded+=(_,_)=>{if(!preview)timer.Start();};Closed+=(_,_)=>timer.Stop();
  Closing+=(_,e)=>{if(busy){e.Cancel=true;message="Подождите, пока завершится текущая операция.";notice.Text=message;}};
  Render();
 }
 static Brush ColorBrush(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
 static TextBlock Label(string text,double size,Brush? color=null,Thickness? margin=null)=>new(){Text=text,FontSize=size,Foreground=color??TextBrush,TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0)};
 Button ActionButton(string title,Action action,bool accent=false)
 {
  var button=new Button{Content=title,HorizontalContentAlignment=HorizontalAlignment.Center,IsEnabled=!busy,Background=accent?AccentBrush:ColorBrush("#292B39"),Foreground=accent?BackgroundBrush:TextBrush};
  button.Click+=(_,_)=>action();return button;
 }
 void AddNav(Panel parent,string id,string title)
 {
  var button=ActionButton(title,()=>{page=id;message="";Render();});button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,0,0,7);button.Padding=new Thickness(14,13,14,13);nav[id]=button;parent.Children.Add(button);
 }
 string Snapshot()=>string.Join("|",catalog.Where(x=>x.Available).Select(x=>$"{x.Id}:{manager.HasInstallation(x)}:{manager.IsInstalled(x)}:{manager.IsRunning(x)}"));
 void Render()
 {
  statusSnapshot=Snapshot();content.Children.Clear();
  foreach(var item in nav){item.Value.Background=item.Key==page?ColorBrush("#30283F"):Brushes.Transparent;item.Value.Foreground=item.Key==page?AccentBrush:MutedBrush;item.Value.IsEnabled=!busy;}
  var titles=page switch{"installed"=>("Мои виджеты","Ваш набор для рабочего стола."),"settings"=>("Настройки","Папки и управление лаунчером."),_=>("Ваш рабочий стол.\nВаши виджеты.","Выбирайте, устанавливайте и запускайте в одном месте.")};
  content.Children.Add(Label(titles.Item1,30));content.Children.Add(Label(titles.Item2,12,MutedBrush,new Thickness(0,10,0,22)));
  notice.Text=message;notice.Visibility=string.IsNullOrWhiteSpace(message)?Visibility.Collapsed:Visibility.Visible;content.Children.Add(notice);
  if(page=="settings"){RenderSettings();return;}
  var widgets=catalog.Where(x=>x.Available&&(page!="installed"||manager.HasInstallation(x))).ToList();
  foreach(var widget in widgets)content.Children.Add(WidgetCard(widget));
  if(page=="installed"&&widgets.Count==0)
  {
   var empty=new StackPanel();empty.Children.Add(Label("Здесь появятся ваши виджеты",20));empty.Children.Add(Label("Начните с мини-плеера — установите его из каталога.",13,MutedBrush,new Thickness(0,10,0,22)));var browse=ActionButton("Открыть каталог",()=>{page="catalog";Render();},true);browse.HorizontalAlignment=HorizontalAlignment.Left;empty.Children.Add(browse);content.Children.Add(Card(empty,new Thickness(26)));
  }
  if(page=="catalog")RenderUpcoming();
 }
 static Border Card(UIElement child,Thickness padding)=>new(){Background=PanelBrush,BorderBrush=ColorBrush("#2B2C3A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=padding,Child=child,Margin=new Thickness(0,0,0,20)};
 UIElement WidgetCard(Widget widget)
 {
  bool managed=manager.HasInstallation(widget),installed=manager.IsInstalled(widget),running=manager.IsRunning(widget);
  var panel=new StackPanel();
  var art=new Border{Height=194,Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#302A45"),(Color)ColorConverter.ConvertFromString("#1C2130"),25),CornerRadius=new CornerRadius(17,17,0,0),ClipToBounds=true};
  var imagePath=Path.Combine(manager.Root,"Assets",widget.Id+".png");
  if(File.Exists(imagePath))
  {
   var image=new Image{Height=162,MaxWidth=390,Stretch=Stretch.Uniform,Margin=new Thickness(20,16,20,16),Source=new BitmapImage(new Uri(imagePath))};art.Child=image;
  }
  else
  {
   var sample=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};sample.Children.Add(Label("♫",48,AccentBrush));sample.Children.Add(Label("Музыка всегда рядом",15));art.Child=sample;
  }
  panel.Children.Add(art);
  var details=new StackPanel{Margin=new Thickness(24,20,24,22)};panel.Children.Add(details);
  var titleRow=new DockPanel();var badge=new Border{Background=ColorBrush(running?"#203A31":installed?"#30283F":"#292B39"),CornerRadius=new CornerRadius(7),Padding=new Thickness(10,5,10,5),VerticalAlignment=VerticalAlignment.Center,Child=Label(running?"Запущен":installed?"Установлен":managed?"Нужна переустановка":"Доступен",10,running?ColorBrush("#A0E2BD"):AccentBrush)};DockPanel.SetDock(badge,Dock.Right);titleRow.Children.Add(badge);titleRow.Children.Add(Label(widget.Name,24));details.Children.Add(titleRow);
  details.Children.Add(Label(widget.Description,12,MutedBrush,new Thickness(0,10,0,18)));
  var controls=new WrapPanel{Orientation=Orientation.Horizontal};details.Children.Add(controls);
  var primary=ActionButton(busy?"Подождите…":installed?(running?"Показать":"Запустить"):managed?"Переустановить":"Установить",()=>{if(installed)RunAction(()=>manager.LaunchAsync(widget),"Виджет запущен. Его можно оставить работать после закрытия лаунчера.");else RunAction(async()=>{if(managed)await manager.RemoveAsync(widget);await manager.InstallAsync(widget);},"Готово. Виджет установлен и готов к запуску.");},true);primary.MinWidth=132;primary.Margin=new Thickness(0,0,9,0);controls.Children.Add(primary);
  if(managed)
  {
   if(installed){var settings=ActionButton("Настройки",()=>RunAction(()=>manager.LaunchAsync(widget,"--settings"),"Настройки открыты в окне виджета."));settings.Margin=new Thickness(0,0,9,0);controls.Children.Add(settings);}
   var remove=ActionButton("Удалить",()=>ConfirmRemove(widget));remove.Foreground=ColorBrush("#F0A2AC");remove.Background=Brushes.Transparent;controls.Children.Add(remove);
  }
  var version=Label("v"+widget.Version+"  ·  Рабочий стол Windows",10,ColorBrush("#777B91"),new Thickness(0,14,0,0));details.Children.Add(version);
  return Card(panel,new Thickness(0));
 }
 void RenderUpcoming()
 {
  var panel=new DockPanel();
  var badge=new Border{Background=ColorBrush("#292B39"),CornerRadius=new CornerRadius(7),Padding=new Thickness(10,5,10,5),VerticalAlignment=VerticalAlignment.Top,Child=Label("В будущем",10,MutedBrush)};DockPanel.SetDock(badge,Dock.Right);panel.Children.Add(badge);
  var text=new StackPanel{Margin=new Thickness(0,0,18,0)};text.Children.Add(Label("Новые виджеты",17));text.Children.Add(Label("Каталог будет пополняться. Будущие виджеты появятся здесь, когда будут готовы.",12,MutedBrush,new Thickness(0,8,0,0)));panel.Children.Add(text);content.Children.Add(Card(panel,new Thickness(23,20,23,20)));
  content.Children.Add(Label("Виджеты устанавливаются в папку лаунчера.",11,ColorBrush("#777B91")));
 }
 void RenderSettings()
 {
  var paths=new StackPanel();paths.Children.Add(Label("Папка лаунчера",17));paths.Children.Add(PathText(manager.Root));paths.Children.Add(Label("Папка виджетов",17,TextBrush,new Thickness(0,22,0,0)));paths.Children.Add(PathText(manager.WidgetsDirectory));paths.Children.Add(Label("Настройки виджетов сохраняются в подпапке Data. При удалении отдельного виджета они остаются для повторной установки.",12,MutedBrush,new Thickness(0,16,0,18)));
  var open=ActionButton("Открыть папку",()=>RunAction(()=>{Process.Start(new ProcessStartInfo(manager.Root){UseShellExecute=true});return Task.CompletedTask;},""));open.HorizontalAlignment=HorizontalAlignment.Left;paths.Children.Add(open);content.Children.Add(Card(paths,new Thickness(25)));
  var removePanel=new StackPanel();removePanel.Children.Add(Label("Удаление лаунчера",17));removePanel.Children.Add(Label("Установщик покажет, какие файлы и виджеты будут удалены, и попросит подтверждение.",12,MutedBrush,new Thickness(0,10,0,18)));var remove=ActionButton("Удалить лаунчер",()=>RunAction(()=>{var executable=Path.Combine(manager.Root,"Uninstall.exe");if(!File.Exists(executable))throw new FileNotFoundException("Программа удаления отсутствует. Переустановите лаунчер в эту же папку.");Process.Start(new ProcessStartInfo(executable){UseShellExecute=true,WorkingDirectory=manager.Root});return Task.CompletedTask;},""));remove.Foreground=ColorBrush("#F0A2AC");remove.HorizontalAlignment=HorizontalAlignment.Left;removePanel.Children.Add(remove);content.Children.Add(Card(removePanel,new Thickness(25)));
 }
 static TextBox PathText(string path)=>new(){Text=path,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=MutedBrush,FontSize=12,Padding=new Thickness(0),Margin=new Thickness(0,8,0,0)};
 async void RunAction(Func<Task> action,string success)
 {
  if(busy)return;busy=true;message="Выполняем…";Render();
  try{await action();message=success;}
  catch(Exception ex){message=ex is UnauthorizedAccessException?"Нет доступа к папке лаунчера. Выберите доступную папку при установке.":ex.Message;}
  finally{busy=false;Render();if(pendingStop)Close();}
 }
 void ConfirmRemove(Widget widget)
 {
  if(busy||removalPrompt)return;removalPrompt=true;
  try
  {
   if(MessageBox.Show(this,$"Удалить «{widget.Name}»?\n\nРаботающий виджет будет закрыт. Его настройки сохранятся для повторной установки.","Удаление виджета",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
    RunAction(()=>manager.RemoveAsync(widget),"Виджет удалён. Настройки сохранены для повторной установки.");
  }
  finally{removalPrompt=false;}
 }
 public void BringForward(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
 public void RequestShutdown(){if(busy){pendingStop=true;return;}Close();}
 public void RequestMiniPlayerRemoval()
 {
  BringForward();var widget=catalog.FirstOrDefault(x=>x.Id=="mini-player");
  if(widget==null||!manager.HasInstallation(widget)){message="Мини-плеер уже удалён.";Render();return;}
  if(busy){message="Дождитесь завершения текущей операции и повторите удаление.";notice.Text=message;return;}
  page="installed";Render();ConfirmRemove(widget);
 }
}
