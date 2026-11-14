using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WButton = System.Windows.Controls.Button;

namespace MiniPlayer;

sealed class PlayerApplication : Application
{
 internal static readonly FontFamily InterfaceFont = new(new Uri(AppContext.BaseDirectory), "./Fonts/#Onest");

 public PlayerApplication()
 {
  Resources[SystemFonts.MessageFontFamilyKey] = InterfaceFont;
  Resources[SystemFonts.MessageFontWeightKey] = FontWeights.Bold;
  Resources[SystemFonts.MenuFontFamilyKey] = InterfaceFont;
  Resources[SystemFonts.MenuFontWeightKey] = FontWeights.Bold;
  foreach (var type in new[] { typeof(ToolTip), typeof(ContextMenu), typeof(MenuItem) })
  {
   var style = new Style(type);
   style.Setters.Add(new Setter(Control.FontFamilyProperty, InterfaceFont));
   style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
   Resources.Add(type, style);
  }
 }
}

public sealed partial class Player : Window
{
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 Button? pinButton;
 readonly TextBlock title=new(){Text="Включите музыку или видео",FontSize=13,TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock artist=new(){Text="Ожидание подключения…",FontSize=12,Foreground=Brushes.LightSlateGray,TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock status=new(){Text="",FontSize=10,Foreground=Brushes.LightSlateGray,TextWrapping=TextWrapping.Wrap};
 readonly System.Windows.Controls.Image cover=new(){Width=52,Height=52,Stretch=Stretch.UniformToFill};
 readonly ComboBox sources=new(){IsEnabled=false};
 readonly CheckBox auto=new(){Content="Показывать при сворачивании",IsChecked=true};
 readonly WButton play=new(){Content="▶",Width=58,Height=36};
 readonly WButton prev=new(){Content="⏮",Width=44,Height=36};
 readonly WButton next=new(){Content="⏭",Width=44,Height=36};
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
 readonly System.Windows.Forms.NotifyIcon tray;
 GlobalSystemMediaTransportControlsSessionManager? manager;
 GlobalSystemMediaTransportControlsSession? session;
 List<GlobalSystemMediaTransportControlsSession> sessions=new();
 bool refreshing,busy,closing,shownByUser=false; string track="";
 readonly string settingsPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MiniPlayer","settings.json");
 record Settings(double X,double Y,bool Pin,bool Auto,string Source,string AudioTarget="",string AudioSource="");
 string preferred="";
 readonly Slider volumeSlider=new(){Minimum=0,Maximum=100,Height=28,IsMoveToPointEnabled=true,IsEnabled=false};
 readonly TextBlock volumeLabel=new(){Text="—",Width=46,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
 bool volumeSync=false;
 readonly TextBlock audioHint=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
 readonly ComboBox audioTargets=new(){IsEnabled=false};
 string audioTarget="",audioSource="";
 readonly Border seekHit=new(){Background=Brushes.Transparent,Cursor=System.Windows.Input.Cursors.Hand};
 readonly Slider seek=new(){Minimum=0,Maximum=1,IsEnabled=false,Height=20,IsMoveToPointEnabled=true};
 readonly TextBlock timeLabel=new(){Text="0:00 / 0:00",FontSize=10,Foreground=Brushes.Gray};
 bool seeking=false,updatingSeek=false,commandPending=false;
 string seekTrack="";
 void SetSeekFromPointer(double x){seek.Value=seek.Minimum+Math.Clamp((x-9)/Math.Max(1,seek.ActualWidth-18),0,1)*(seek.Maximum-seek.Minimum);}
 void SizePlayer(){
  var padding=((Border)Content).Padding;
  Width=(deviceFace!=null?deviceFace.Width+padding.Left+padding.Right+2*appearance.BorderWidth:300)*appearance.Scale;
  Height=(deviceFace!=null?deviceFace.Height+padding.Top+padding.Bottom+2*appearance.BorderWidth:144)*appearance.Scale;
 }
 async Task Seek(){
  if(session==null||!seek.IsEnabled||seekTrack!=track)return;
  var selected=session;var selectedTrack=track;double target=seek.Value;int request=++seekRequest;seekPending=true;
  try{
   bool accepted=await selected.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(target).Ticks);
   if(request!=seekRequest||selected!=session||selectedTrack!=track)return;
   if(accepted)playbackClock.Seek(target);else status.Text="Источник отклонил перемотку.";
  }catch{if(request==seekRequest)status.Text="Перемотка недоступна.";}
  finally{if(request==seekRequest){seekPending=false;RenderTimeline();}}
 }
 public Player()
 {
  FontFamily=PlayerApplication.InterfaceFont;FontWeight=FontWeights.Bold;
  Title="Mini Player";Width=380;Height=260;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=true;
  Left=SystemParameters.WorkArea.Right-Width-24;Top=SystemParameters.WorkArea.Bottom-Height-24;
  var body=new Border{Background=new SolidColorBrush(Color.FromRgb(242,243,246)),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(22),Padding=new Thickness(8)};Content=body;
  var style=(Style)System.Windows.Markup.XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Foreground' Value='#46505F'/><Setter Property='Background' Value='#F8F9FB'/><Setter Property='FontSize' Value='16'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Name='B' Background='{TemplateBinding Background}' CornerRadius='10' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='#DFE8F5'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='#C7D7EE'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
  Resources.Add(typeof(WButton),style);title.Foreground=new SolidColorBrush(Color.FromRgb(45,53,66));artist.Foreground=Brushes.SlateGray;auto.Foreground=Brushes.DimGray;
  seek.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Slider'><Grid Margin='3,0' Background='Transparent'><Border Height='4' Background='#CDD4DF' CornerRadius='2' VerticalAlignment='Center'/><Track Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' Background='{DynamicResource AccentBrush}' CornerRadius='2'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='12' Height='12'><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='#FFFFFF' Stroke='#A3B2C7' StrokeThickness='1'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>");
  Point? dragOrigin=null;
  PreviewMouseLeftButtonDown+=(_,e)=>{
   dragOrigin=null;
   for(var node=e.OriginalSource as DependencyObject;node is Visual;node=VisualTreeHelper.GetParent(node)){
    if(node is Slider or ComboBox or CheckBox ||node==seekHit||node is FaceMeter)return;
   }
   dragOrigin=e.GetPosition(this);
  };
  PreviewMouseMove+=(_,e)=>{
   if(e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed){dragOrigin=null;return;}
   if(dragOrigin is not Point start)return;
   var delta=e.GetPosition(this)-start;
   if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
   dragOrigin=null;System.Windows.Input.Mouse.Capture(null);e.Handled=true;DragMove();Save();
  };
  var stack=new StackPanel();body.Child=stack;
  var header=new DockPanel{Margin=new Thickness(0,0,0,4)};
  var close=Button("×",()=>Hide());DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
  var expand=Button("⋯");AttachPlayerMenu(expand);DockPanel.SetDock(expand,Dock.Right);header.Children.Add(expand);
  var pin=Button("",TogglePin);pinButton=pin;UpdatePin();DockPanel.SetDock(pin,Dock.Right);header.Children.Add(pin);
  var name=new TextBlock{Text="Mini Player",Foreground=Brushes.SlateGray,FontSize=10,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(name);stack.Children.Add(header);
  var row=new DockPanel();var art=new Border{Width=52,Height=52,Background=new SolidColorBrush(Color.FromRgb(39,57,76)),Margin=new Thickness(0,0,10,0),Child=cover};DockPanel.SetDock(art,Dock.Left);row.Children.Add(art);
  EnableFullTitle(title);
  var info=new StackPanel();info.Children.Add(title);info.Children.Add(artist);var controls=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,Margin=new Thickness(0,6,0,0)};
  foreach(var b in new[]{prev,play,next}){b.Margin=new Thickness(0,0,6,0);b.Height=26;b.Width=36;controls.Children.Add(b);}
  info.Children.Add(controls);var seekContainer=new Grid{Height=20};seekContainer.Children.Add(seek);seekContainer.Children.Add(seekHit);info.Children.Add(seekContainer);info.Children.Add(timeLabel);row.Children.Add(info);stack.Children.Add(row);
  seekHit.PreviewMouseLeftButtonDown+=(_,e)=>{if(!seek.IsEnabled)return;seeking=true;seekTrack=track;seekHit.CaptureMouse();SetSeekFromPointer(e.GetPosition(seek).X);e.Handled=true;};
  seekHit.PreviewMouseMove+=(_,e)=>{if(seeking&&e.LeftButton==System.Windows.Input.MouseButtonState.Pressed){SetSeekFromPointer(e.GetPosition(seek).X);e.Handled=true;}};
  seekHit.PreviewMouseLeftButtonUp+=async(_,e)=>{if(seeking){SetSeekFromPointer(e.GetPosition(seek).X);seeking=false;seekHit.ReleaseMouseCapture();e.Handled=true;await Seek();}};
  seekHit.LostMouseCapture+=async(_,_)=>{if(seeking&&System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Released){seeking=false;await Seek();}};
  seek.PreviewKeyDown+=(_,e)=>{if(e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Home or System.Windows.Input.Key.End){seekTrack=track;seeking=true;}};
  seek.PreviewKeyUp+=async(_,e)=>{if(e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Home or System.Windows.Input.Key.End){seeking=false;await Seek();}};
  seek.LostKeyboardFocus+=(_,_)=>{if(!seekHit.IsMouseCaptured&&!seek.IsMouseCaptureWithin)seeking=false;};
  seek.ValueChanged+=(_,_)=>{if(!updatingSeek)timeLabel.Text=$"{TimeSpan.FromSeconds(seek.Value):m\\:ss} / {TimeSpan.FromSeconds(seek.Maximum):m\\:ss}";};
  prev.Click+=async(_,_)=>await Command(0);play.Click+=async(_,_)=>await Command(1);next.Click+=async(_,_)=>await Command(2);
  volumeSlider.Template=seek.Template;
  audioTargets.ToolTip="Выберите аудиоприложение. Для браузера меняется громкость всех его вкладок.";audioTargets.SelectionChanged+=(_,_)=>{if(volumeSync)return;audioTarget=audioTargets.SelectedItem as string??"";audioSource=session?.SourceAppUserModelId??"";Save();RefreshAudioTargets();};
  volumeSlider.ValueChanged+=(_,_)=>{volumeLabel.Text=$"{volumeSlider.Value:0}%";if(!volumeSync){try{Audio.Set(audioTarget,volumeSlider.Value);}catch{audioHint.Text="Не удалось изменить громкость приложения.";}}};
  sources.SelectionChanged+=async(_,_)=>{if(refreshing)return;session=sources.SelectedIndex>=0?sessions[sources.SelectedIndex]:null;ResetTimeline();SetMediaPlaying(false);preferred=session?.SourceAppUserModelId??"";track="";Save();await Refresh();};
  Width=410;Foreground=Brushes.DimGray;SizePlayer();
  auto.Checked+=(_,_)=>{shownByUser=false;Save();};auto.Unchecked+=(_,_)=>{shownByUser=true;Save();};
  tray=new(){Icon=new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory,"Player.ico")),Text="Mini Player",Visible=true};
  var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add("Показать плеер",null,(_,_)=>Dispatcher.Invoke(()=>ReturnToScreen()));menu.Items.Add("Настройки",null,(_,_)=>Dispatcher.Invoke(()=>OpenSettings()));menu.Items.Add("Кастомизация",null,(_,_)=>Dispatcher.Invoke(()=>Customize()));menu.Items.Add("Выход",null,(_,_)=>Dispatcher.Invoke(()=>{closing=true;Close();}));tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ReturnToScreen();
  motionTimer.Tick+=(_,_)=>RenderMotion();
  IsVisibleChanged+=(_,_)=>{SyncMotion();UpdateRecoveryArrow();};
  LocationChanged+=(_,_)=>UpdateRecoveryArrow();SizeChanged+=(_,_)=>UpdateRecoveryArrow();
  SourceInitialized+=(_,_)=>InitializeHotkeys();
  Closing+=(_,_)=>{closing=true;DisposeHotkeys();recoveryWindow?.Close();settingsWindow?.Close();customizeWindow?.Close();peakReader.Dispose();motionTimer.Stop();Save();timer.Stop();tray.Dispose();};
  Loaded+=async(_,_)=>{ImportLegacySettings();LoadSettings();LoadAppearance();UpdateRecoveryArrow();timer.Start();try{manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();await Refresh();}catch(Exception ex){status.Text="Нет доступа к медиасессиям Windows: "+ex.Message;}};
  timer.Tick+=async(_,_)=>{UpdateRecoveryArrow();await Refresh();};
 }
 static WButton Button(string label,Action? action=null){var b=new WButton{Content=label,MinWidth=30,Height=26,Padding=new Thickness(5,0,5,0)};if(action!=null)b.Click+=(_,_)=>action();return b;}
 async Task Command(int command){
  var selected=session;if(selected==null||commandPending)return;commandPending=true;
  try{
   bool ok;
   if(command==0)ok=await selected.TrySkipPreviousAsync();
   else if(command==2)ok=await selected.TrySkipNextAsync();
   else if(command is 3 or 4){
    var info=selected.GetPlaybackInfo();bool wantPlaying=command==4;
    bool isPlaying=info.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    if(wantPlaying==isPlaying)return;
    ok=wantPlaying?await selected.TryPlayAsync():await selected.TryPauseAsync();
    if(!ok&&info.Controls.IsPlayPauseToggleEnabled)ok=await selected.TryTogglePlayPauseAsync();
   }
   else{
    var info=selected.GetPlaybackInfo();
    if(info.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing&&info.Controls.IsPauseEnabled)ok=await selected.TryPauseAsync();
    else if(info.PlaybackStatus!=GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing&&info.Controls.IsPlayEnabled)ok=await selected.TryPlayAsync();
    else ok=await selected.TryTogglePlayPauseAsync();
    if(!ok&&info.Controls.IsPlayPauseToggleEnabled)ok=await selected.TryTogglePlayPauseAsync();
   }
   if(!ok){status.Text="Сайт / приложение не приняло команду.";artist.Text=status.Text;}
  }catch{status.Text="Сессия закрылась. Выберите источник заново.";artist.Text=status.Text;}
  finally{commandPending=false;}
 }
 async Task Refresh()
 {
  if(busy||closing)return;
  if(settingsWindow?.IsVisible==true&&!volumeSlider.IsMouseCaptureWithin)RefreshAudioTargets();
  if(manager==null)return;busy=true;
  try{
   var list=manager.GetSessions().ToList();
   if(!list.SequenceEqual(sessions)){
    var old=session;refreshing=true;sessions=list;sources.Items.Clear();
    foreach(var s in sessions){string label=s.SourceAppUserModelId.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&s.SourceAppUserModelId.Contains("music",StringComparison.OrdinalIgnoreCase)?"Яндекс Музыка":s.SourceAppUserModelId;try{var m=await s.TryGetMediaPropertiesAsync();if(!string.IsNullOrWhiteSpace(m.Title))label+=" · "+m.Title;}catch{}sources.Items.Add(label);}
    var index=old==null?-1:sessions.IndexOf(old);
    if(index<0)index=sessions.FindIndex(s=>s.SourceAppUserModelId==preferred);
    if(index<0)index=sessions.FindIndex(s=>s.SourceAppUserModelId.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&s.SourceAppUserModelId.Contains("music",StringComparison.OrdinalIgnoreCase));
    sources.SelectedIndex=index;sources.IsEnabled=sessions.Count>0;session=index>=0?sessions[index]:null;
    if(session!=old){track="";ResetTimeline();}refreshing=false;
   }
   if(session==null){ResetTimeline();SetMediaPlaying(false);title.Text="Включите музыку или видео";artist.Text="Выберите источник в настройках";cover.Source=null;seek.IsEnabled=false;prev.IsEnabled=play.IsEnabled=next.IsEnabled=false;return;}
   var playback=session.GetPlaybackInfo();SynchronizeTimeline(session.GetTimelineProperties(),playback);SetMediaPlaying(playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);play.Content=mediaPlaying?"Ⅱ":"▶";
   prev.IsEnabled=playback.Controls.IsPreviousEnabled;next.IsEnabled=playback.Controls.IsNextEnabled;
   play.IsEnabled=playback.Controls.IsPlayPauseToggleEnabled||(playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing?playback.Controls.IsPauseEnabled:playback.Controls.IsPlayEnabled);
   prev.ToolTip=prev.IsEnabled?"Предыдущий":"Сайт не предоставляет предыдущий трек";next.ToolTip=next.IsEnabled?"Следующий":"Сайт не предоставляет следующий трек";
   var requestedSession=session;var media=await requestedSession.TryGetMediaPropertiesAsync();if(!Equals(requestedSession,session))return;var key=session.SourceAppUserModelId+"|"+media.Title+"|"+media.Artist;
   title.Text=string.IsNullOrEmpty(media.Title)?"Без названия":media.Title;artist.Text=media.Artist;
   if(key!=track){track=key;ResetTimeline();SynchronizeTimeline(requestedSession.GetTimelineProperties(),playback);cover.Source=null;if(media.Thumbnail!=null){using var stream=await media.Thumbnail.OpenReadAsync();if(stream.Size<=8*1024*1024){using var reader=new DataReader(stream);await reader.LoadAsync((uint)stream.Size);var data=new byte[(int)stream.Size];reader.ReadBytes(data);using var memory=new System.IO.MemoryStream(data);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=152;bitmap.StreamSource=memory;bitmap.EndInit();bitmap.Freeze();cover.Source=bitmap;}}}
   status.Text=Topmost?"Закреплён поверх окон":"Закрепление выключено";
   if(auto.IsChecked==true&&!shownByUser&&session.SourceAppUserModelId.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&session.SourceAppUserModelId.Contains("music",StringComparison.OrdinalIgnoreCase)){
    var handles=new List<IntPtr>();
    foreach(var p in Process.GetProcesses()){using(p){try{if(p.ProcessName.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&p.ProcessName.Contains("music",StringComparison.OrdinalIgnoreCase)&&p.MainWindowHandle!=IntPtr.Zero)handles.Add(p.MainWindowHandle);}catch{}}}
    var windows=handles.ToArray();
    if(windows.Length>0){if(windows.All(h=>IsIconic(h)||!IsWindowVisible(h)))Show();else Hide();}
    else status.Text="Автопоказ: окно Яндекс Музыки не найдено";
   }
  }catch{SetMediaPlaying(false);status.Text="Ожидание медиаисточника…";}finally{busy=false;refreshing=false;}
 }
 void Save(){try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!);System.IO.File.WriteAllText(settingsPath,System.Text.Json.JsonSerializer.Serialize(new Settings(Left,Top,Topmost,auto.IsChecked==true,preferred,audioTarget,audioSource)));}catch{}}
 void ImportLegacySettings(){
  var target=System.IO.Path.GetDirectoryName(settingsPath)!;
  var legacy=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!,"YandexMiniPlayer");
  foreach(var name in new[]{"settings.json","appearance.json"}){
   var oldFile=System.IO.Path.Combine(legacy,name);var newFile=System.IO.Path.Combine(target,name);
   if(System.IO.File.Exists(oldFile)&&!System.IO.File.Exists(newFile)){
    try{System.IO.Directory.CreateDirectory(target);System.IO.File.Copy(oldFile,newFile);}
    catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
   }
  }
 }
 void LoadSettings(){try{var s=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(settingsPath));if(s!=null){Left=Math.Clamp(s.X,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width);Top=Math.Clamp(s.Y,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Height);Topmost=s.Pin;preferred=s.Source;audioTarget=s.AudioTarget??"";audioSource=s.AudioSource??"";auto.IsChecked=s.Auto;}}catch{} }
 [STAThread] public static void Main(string[] args){
  if(args.Length==2&&args[0]=="--self-test"){
   var applicationTest=new PlayerApplication();var player=new Player();player.tray.Visible=false;
   try{
    var root=(FrameworkElement)player.Content;root.Measure(new Size(player.Width,player.Height));root.Arrange(new Rect(0,0,player.Width,player.Height));root.UpdateLayout();
    player.seek.Maximum=240;player.SetSeekFromPointer(-100);if(player.seek.Value!=0)throw new Exception("Seek start clamp failed");
    player.SetSeekFromPointer(player.seek.ActualWidth/2);if(Math.Abs(player.seek.Value-120)>0.01)throw new Exception("Seek midpoint failed");
    player.SetSeekFromPointer(player.seek.ActualWidth+100);if(player.seek.Value!=240)throw new Exception("Seek end clamp failed");
    foreach(double fraction in new[]{0.05,0.5,0.95})foreach(double y in new[]{1d,10d,19d}){
     var point=player.seekHit.TranslatePoint(new Point(player.seekHit.ActualWidth*fraction,y),root);
     if(!ReferenceEquals(VisualTreeHelper.HitTest(root,point)?.VisualHit,player.seekHit))throw new Exception("Timeline click surface missed at "+fraction+","+y);
    }
    if(ColorWheel.Hsv(0,1,1)!=Colors.Red||ColorWheel.Hsv(120,1,1)!=Colors.Lime||ColorWheel.Hsv(240,1,1)!=Colors.Blue||ColorWheel.Hsv(0,0,1)!=Colors.White||ColorWheel.Hsv(0,1,0)!=Colors.Black)throw new Exception("Color conversion failed");
    player.Topmost=false;player.UpdatePin();if(((System.Windows.Shapes.Path)player.pinButton!.Content).RenderTransform is not RotateTransform off||off.Angle!=-45)throw new Exception("Unpinned state failed");player.Topmost=true;player.UpdatePin();
    string audio;try{var entries=Audio.List();audio=entries.All(e=>e.Volume>=0&&e.Volume<=100)?"Application audio enumeration: PASS ("+entries.Count+")":"Invalid session volume";}catch(Exception ex){audio="Core Audio unavailable in test session: "+ex.Message;}
    System.IO.File.WriteAllText(args[1],"Seek coordinates: start/middle/end PASS\nTimeline hit targets: 9/9 PASS\nCompact layout: "+player.Width+" x "+player.Height+"\n"+audio);
   }catch(Exception ex){System.IO.File.WriteAllText(args[1],ex.ToString());Environment.ExitCode=1;}finally{player.tray.Dispose();}return;
  }
  if(args.Length==2&&args[0]=="--preview"){
   var app=new PlayerApplication();var player=new Player();player.tray.Visible=false;
   player.title.Text="Название трека";player.artist.Text="Исполнитель · Яндекс Музыка";player.refreshing=true;player.sources.Items.Add("Яндекс Музыка");player.sources.SelectedIndex=0;
   player.seek.Maximum=240;player.seek.Value=95;player.seek.IsEnabled=true;
   player.volumeSync=true;player.volumeSlider.Value=50;player.volumeSync=false;
   if(args[1].Contains("minecraft"))player.appearance=new Appearance("Minecraft","#8BE878");foreach(var candidate in Themes)if(args[1].Contains(candidate))player.appearance=new Appearance(candidate,ThemeAccent(candidate),Round:candidate=="Minecraft"?0:20);player.Topmost=!args[1].Contains("-unpinned");player.ApplyAppearance();
   if(player.deviceFace!=null && player.appearance.Theme is not ("Винил" or "Закат" or "Карманный LCD") && !args[1].Contains("custom")){
    var artwork=new DrawingGroup();using(var dc=artwork.Open()){
     dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(44,53,88),Color.FromRgb(232,156,130),90),null,new Rect(0,0,100,100));
     dc.DrawEllipse(BrushOf("#FFE0A9"),null,new Point(67,37),13,13);
     dc.DrawGeometry(BrushOf("#5C667D"),null,Geometry.Parse("M0,69 L27,40 50,59 61,48 100,75 V100 H0Z"));
     dc.DrawGeometry(BrushOf("#293F51"),null,Geometry.Parse("M0,85 L28,66 45,74 70,52 100,63 V100 H0Z"));
     for(int j=0;j<3;j++)dc.DrawRectangle(BrushOf("#EFDFC5"),null,new Rect(9,10+j*3,22-j*4,1));
    }player.cover.Source=new DrawingImage(artwork);
   }
   var root=(FrameworkElement)player.Content;root.Measure(new Size(player.Width,player.Height));root.Arrange(new Rect(0,0,player.Width,player.Height));root.UpdateLayout();
   if(args[1].Contains("custom")){player.Customize(true);root=(FrameworkElement)player.customizeWindow!.Content;player.Width=player.customizeWindow.Width;player.Height=player.customizeWindow.Height;root.Measure(new Size(player.Width,player.Height));root.Arrange(new Rect(0,0,player.Width,player.Height));root.UpdateLayout();}
   if(args[1].Contains("settings")){
    player.OpenSettings(true);player.sources.IsEnabled=true;
    player.volumeSync=true;player.audioTargets.Items.Add("Spotify");player.audioTargets.SelectedIndex=0;player.audioTargets.IsEnabled=true;player.volumeSync=false;player.volumeSlider.IsEnabled=true;
    root=(FrameworkElement)player.settingsWindow!.Content;player.Width=player.settingsWindow.Width;player.Height=player.settingsWindow.Height;
    root.Measure(new Size(player.Width,player.Height));root.Arrange(new Rect(0,0,player.Width,player.Height));root.UpdateLayout();
   }
   var image=new RenderTargetBitmap((int)player.Width,(int)player.Height,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=System.IO.File.Create(args[1]))encoder.Save(file);player.tray.Dispose();return;
  }
  using var mutex=new System.Threading.Mutex(true,"Local\\MiniPlayer",out var created);if(!created)return;var application=new PlayerApplication();application.Run(new Player());
 }
}
