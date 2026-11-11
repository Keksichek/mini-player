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
using WApplication = System.Windows.Application;

namespace YandexMiniPlayer;
public sealed partial class Player : Window
{
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 Button? pinButton;
 readonly TextBlock title=new(){Text="Включите трек в Яндекс Музыке",FontSize=13,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock artist=new(){Text="Ожидание подключения…",FontSize=12,Foreground=Brushes.LightSlateGray,TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock status=new(){Text="",FontSize=10,Foreground=Brushes.LightSlateGray,TextWrapping=TextWrapping.Wrap};
 readonly System.Windows.Controls.Image cover=new(){Width=52,Height=52,Stretch=Stretch.UniformToFill};
 readonly ComboBox sources=new(){MinWidth=200,MaxWidth=300};
 readonly CheckBox auto=new(){Content="Показывать при сворачивании",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(0,8,0,0)};
 readonly WButton play=new(){Content="▶",Width=58,Height=36};
 readonly WButton prev=new(){Content="⏮",Width=44,Height=36};
 readonly WButton next=new(){Content="⏭",Width=44,Height=36};
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
 readonly System.Windows.Forms.NotifyIcon tray;
 GlobalSystemMediaTransportControlsSessionManager? manager;
 GlobalSystemMediaTransportControlsSession? session;
 List<GlobalSystemMediaTransportControlsSession> sessions=new();
 bool refreshing,busy,closing,shownByUser=false; string track="";
 readonly string settingsPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YandexMiniPlayer","settings.json");
 record Settings(double X,double Y,bool Pin,bool Auto,string Source);
 string preferred="";
 readonly Slider volumeSlider=new(){Minimum=0,Maximum=100,Width=130,Height=22,IsMoveToPointEnabled=true};
 readonly TextBlock volumeLabel=new(){Width=40,VerticalAlignment=VerticalAlignment.Center};
 bool volumeSync=false;
 readonly TextBlock audioHint=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
 readonly ComboBox audioTargets=new(){MinWidth=200,MaxWidth=300};
 string audioTarget="",audioSource="";
 readonly Border seekHit=new(){Background=Brushes.Transparent,Cursor=System.Windows.Input.Cursors.Hand};
 readonly StackPanel details=new(){Margin=new Thickness(0,6,0,0)};
 readonly Slider seek=new(){Minimum=0,Maximum=1,IsEnabled=false,Height=20,IsMoveToPointEnabled=true};
 readonly TextBlock timeLabel=new(){Text="0:00 / 0:00",FontSize=10,Foreground=Brushes.Gray};
 bool expanded=false,seeking=false,updatingSeek=false,commandPending=false;
 string seekTrack="";
 void SetSeekFromPointer(double x){seek.Value=seek.Minimum+Math.Clamp((x-9)/Math.Max(1,seek.ActualWidth-18),0,1)*(seek.Maximum-seek.Minimum);}
 void SetExpanded(bool value){expanded=value;details.Visibility=value?Visibility.Visible:Visibility.Collapsed;Width=(deviceFace!=null?Math.Max(deviceFace.Width+16+2*appearance.BorderWidth,value?350:0):value?350:300)*appearance.Scale;Height=(deviceFace!=null?deviceFace.Height+16+2*appearance.BorderWidth+(value?216:0):value?350:144)*appearance.Scale;Top=Math.Min(Top,SystemParameters.WorkArea.Bottom-Height);}
 async Task Seek(){
  if(session==null||!seek.IsEnabled||seekTrack!=track)return;
  try{if(!await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(seek.Value).Ticks))status.Text="Яндекс Музыка отклонила перемотку.";}catch{status.Text="Перемотка недоступна.";}
 }
 public Player()
 {
  Title="mini payer";Width=380;Height=260;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=true;
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
  var expand=Button("⋯",()=>SetExpanded(!expanded));expand.ToolTip="Развернуть / свернуть настройки";DockPanel.SetDock(expand,Dock.Right);header.Children.Add(expand);
  var pin=Button("",()=>{Topmost=!Topmost;UpdatePin();Save();});pinButton=pin;UpdatePin();DockPanel.SetDock(pin,Dock.Right);header.Children.Add(pin);
  var name=new TextBlock{Text="mini payer",Foreground=Brushes.SlateGray,FontSize=10,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(name);stack.Children.Add(header);
  var row=new DockPanel();var art=new Border{Width=52,Height=52,Background=new SolidColorBrush(Color.FromRgb(39,57,76)),Margin=new Thickness(0,0,10,0),Child=cover};DockPanel.SetDock(art,Dock.Left);row.Children.Add(art);
  var info=new StackPanel();info.Children.Add(title);info.Children.Add(artist);var controls=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,Margin=new Thickness(0,6,0,0)};
  foreach(var b in new[]{prev,play,next}){b.Margin=new Thickness(0,0,6,0);b.Height=26;b.Width=36;controls.Children.Add(b);}
  foreach(var text in new[]{"♡","⊘","☷"}){var unavailable=Button(text,()=>{});unavailable.Width=27;unavailable.MinWidth=27;unavailable.Margin=new Thickness(0,0,3,0);unavailable.IsEnabled=false;unavailable.ToolTip=text=="☷"?"Очередь Яндекс Музыки недоступна через системный медиапульт":"Лайки и дизлайки требуют прямого подключения к Яндекс Музыке";ToolTipService.SetShowOnDisabled(unavailable,true);details.Children.Add(unavailable); unavailable.Visibility=Visibility.Collapsed;}
  info.Children.Add(controls);var seekContainer=new Grid{Height=20};seekContainer.Children.Add(seek);seekContainer.Children.Add(seekHit);info.Children.Add(seekContainer);info.Children.Add(timeLabel);row.Children.Add(info);stack.Children.Add(row);
  seekHit.PreviewMouseLeftButtonDown+=(_,e)=>{if(!seek.IsEnabled)return;seeking=true;seekTrack=track;seekHit.CaptureMouse();SetSeekFromPointer(e.GetPosition(seek).X);e.Handled=true;};
  seekHit.PreviewMouseMove+=(_,e)=>{if(seeking&&e.LeftButton==System.Windows.Input.MouseButtonState.Pressed){SetSeekFromPointer(e.GetPosition(seek).X);e.Handled=true;}};
  seekHit.PreviewMouseLeftButtonUp+=async(_,e)=>{if(seeking){SetSeekFromPointer(e.GetPosition(seek).X);seeking=false;seekHit.ReleaseMouseCapture();e.Handled=true;await Seek();}};
  seekHit.LostMouseCapture+=async(_,_)=>{if(seeking&&System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Released){seeking=false;await Seek();}};
  seek.PreviewKeyDown+=(_,_)=>{seekTrack=track;};seek.PreviewKeyUp+=async(_,e)=>{if(e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Home or System.Windows.Input.Key.End)await Seek();};
  seek.ValueChanged+=(_,_)=>{if(!updatingSeek)timeLabel.Text=$"{TimeSpan.FromSeconds(seek.Value):m\\:ss} / {TimeSpan.FromSeconds(seek.Maximum):m\\:ss}";};
  prev.Click+=async(_,_)=>await Command(0);play.Click+=async(_,_)=>await Command(1);next.Click+=async(_,_)=>await Command(2);
  var volume=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,Margin=new Thickness(0,10,0,6)};
  volume.Children.Add(new TextBlock{Text="Громкость приложения",Foreground=Brushes.DimGray,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});
  volumeSlider.Template=seek.Template;volume.Children.Add(volumeSlider);volume.Children.Add(volumeLabel);details.Children.Add(new TextBlock{Text="Источник трека и кнопок",FontWeight=FontWeights.SemiBold});details.Children.Add(sources);details.Children.Add(new TextBlock{Text="Приложение в микшере Windows",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,2)});details.Children.Add(audioTargets);details.Children.Add(volume);details.Children.Add(audioHint);audioTargets.ToolTip="Выберите аудиоприложение. Для браузера меняется громкость всех его вкладок.";audioTargets.SelectionChanged+=(_,_)=>{if(!volumeSync)audioTarget=audioTargets.SelectedItem as string??"";};
  volumeSlider.ValueChanged+=(_,_)=>{volumeLabel.Text=$"{volumeSlider.Value:0}%";if(!volumeSync){try{Audio.Set(audioTarget,volumeSlider.Value);}catch{audioHint.Text="Не удалось изменить громкость приложения.";}}};
  sources.SelectionChanged+=(_,_)=>{if(refreshing)return;session=sources.SelectedIndex>=0?sessions[sources.SelectedIndex]:null;SetMediaPlaying(false);preferred=session?.SourceAppUserModelId??"";track="";Save();};details.Children.Add(auto);details.Children.Add(status);stack.Children.Add(details);
  Width=410;Foreground=Brushes.DimGray;SetExpanded(false);
  auto.Checked+=(_,_)=>{shownByUser=false;Save();};auto.Unchecked+=(_,_)=>{shownByUser=true;Save();};
  tray=new(){Icon=new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory,"Player.ico")),Text="mini payer",Visible=true};
  var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add("Показать плеер",null,(_,_)=>Dispatcher.Invoke(()=>{shownByUser=true;Show();Activate();}));menu.Items.Add("Кастомизация",null,(_,_)=>Dispatcher.Invoke(()=>Customize()));menu.Items.Add("Выход",null,(_,_)=>Dispatcher.Invoke(()=>{closing=true;Close();}));tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>{shownByUser=true;Show();Activate();};
  motionTimer.Tick+=(_,_)=>RenderMotion();
  IsVisibleChanged+=(_,_)=>SyncMotion();
  Closing+=(_,_)=>{motionTimer.Stop();Save();timer.Stop();tray.Dispose();};
  Loaded+=async(_,_)=>{LoadSettings();LoadAppearance();try{manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();await Refresh();timer.Start();}catch(Exception ex){status.Text="Нет доступа к медиасессиям Windows: "+ex.Message;}};
  timer.Tick+=async(_,_)=>await Refresh();
 }
 static WButton Button(string label,Action action){var b=new WButton{Content=label,MinWidth=30,Height=26,Padding=new Thickness(5,0,5,0)};b.Click+=(_,_)=>action();return b;}
 async Task Command(int command){
  var selected=session;if(selected==null||commandPending)return;commandPending=true;
  try{
   bool ok;
   if(command==0)ok=await selected.TrySkipPreviousAsync();
   else if(command==2)ok=await selected.TrySkipNextAsync();
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
  if(busy||closing||manager==null)return;busy=true;
  try{
   if(expanded&&!volumeSlider.IsMouseCaptureWithin){try{
 volumeSync=true;var entries=Audio.List();var source=session?.SourceAppUserModelId??"";
 if(audioSource!=source){audioSource=source;audioTarget="";}
 if(!entries.Any(e=>e.Name==audioTarget))audioTarget=Audio.Match(source,entries)??"";
 if(!entries.Select(e=>e.Name).SequenceEqual(audioTargets.Items.Cast<string>())){audioTargets.Items.Clear();foreach(var entry in entries)audioTargets.Items.Add(entry.Name);}
 audioTargets.SelectedItem=audioTarget;var selectedAudio=entries.FirstOrDefault(e=>e.Name==audioTarget);
 volumeSlider.IsEnabled=selectedAudio!=null;audioHint.Text=selectedAudio!=null?"":entries.Count==0?"Включите звук в Яндекс Музыке, чтобы она появилась в микшере.":"Выберите приложение для регулировки громкости.";if(selectedAudio!=null){volumeSlider.Value=selectedAudio.Volume;volumeLabel.Text=$"{selectedAudio.Volume:0}%";}else volumeLabel.Text="—";
 }catch{audioHint.Text="Микшер Windows недоступен.";volumeSlider.IsEnabled=false;volumeLabel.Text="—";}finally{volumeSync=false;}}
   var list=manager.GetSessions().ToList();
   if(!list.SequenceEqual(sessions)){
    var old=session;refreshing=true;sessions=list;sources.Items.Clear();
    foreach(var s in sessions){string label=s.SourceAppUserModelId.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&s.SourceAppUserModelId.Contains("music",StringComparison.OrdinalIgnoreCase)?"Яндекс Музыка":s.SourceAppUserModelId;try{var m=await s.TryGetMediaPropertiesAsync();if(!string.IsNullOrWhiteSpace(m.Title))label+=" · "+m.Title;}catch{}sources.Items.Add(label);}
    var index=old==null?-1:sessions.IndexOf(old);
    if(index<0)index=sessions.FindIndex(s=>s.SourceAppUserModelId==preferred);
    if(index<0)index=sessions.FindIndex(s=>s.SourceAppUserModelId.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&s.SourceAppUserModelId.Contains("music",StringComparison.OrdinalIgnoreCase));
    sources.SelectedIndex=index;session=index>=0?sessions[index]:null;track="";refreshing=false;
   }
   if(session==null){SetMediaPlaying(false);title.Text="Включите Яндекс Музыку";artist.Text="Источник: ⋯ → список приложений";cover.Source=null;seek.IsEnabled=false;prev.IsEnabled=play.IsEnabled=next.IsEnabled=false;return;}
   var playback=session.GetPlaybackInfo();SetMediaPlaying(playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);play.Content=mediaPlaying?"Ⅱ":"▶";
   prev.IsEnabled=playback.Controls.IsPreviousEnabled;next.IsEnabled=playback.Controls.IsNextEnabled;
   play.IsEnabled=playback.Controls.IsPlayPauseToggleEnabled||(playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing?playback.Controls.IsPauseEnabled:playback.Controls.IsPlayEnabled);
   prev.ToolTip=prev.IsEnabled?"Предыдущий":"Сайт не предоставляет предыдущий трек";next.ToolTip=next.IsEnabled?"Следующий":"Сайт не предоставляет следующий трек";
   var timeline=session.GetTimelineProperties();
   if(!seeking){updatingSeek=true;seek.Minimum=Math.Max(0,timeline.MinSeekTime.TotalSeconds);seek.Maximum=Math.Max(seek.Minimum+1,timeline.MaxSeekTime.TotalSeconds>0?timeline.MaxSeekTime.TotalSeconds:timeline.EndTime.TotalSeconds);seek.IsEnabled=playback.Controls.IsPlaybackPositionEnabled&&timeline.EndTime>TimeSpan.Zero;seek.Value=Math.Clamp(timeline.Position.TotalSeconds,seek.Minimum,seek.Maximum);timeLabel.Text=$"{TimeSpan.FromSeconds(seek.Value):m\\:ss} / {timeline.EndTime:m\\:ss}";updatingSeek=false;}
   var requestedSession=session;var media=await requestedSession.TryGetMediaPropertiesAsync();if(!Equals(requestedSession,session))return;var key=session.SourceAppUserModelId+"|"+media.Title+"|"+media.Artist;
   title.Text=string.IsNullOrEmpty(media.Title)?"Яндекс Музыка":media.Title;artist.Text=media.Artist;
   if(key!=track){track=key;cover.Source=null;if(media.Thumbnail!=null){using var stream=await media.Thumbnail.OpenReadAsync();if(stream.Size<=8*1024*1024){using var reader=new DataReader(stream);await reader.LoadAsync((uint)stream.Size);var data=new byte[(int)stream.Size];reader.ReadBytes(data);using var memory=new System.IO.MemoryStream(data);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=152;bitmap.StreamSource=memory;bitmap.EndInit();bitmap.Freeze();cover.Source=bitmap;}}}
   status.Text=Topmost?"Закреплён поверх окон":"Закрепление выключено";
   if(auto.IsChecked==true&&!shownByUser&&session.SourceAppUserModelId.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&session.SourceAppUserModelId.Contains("music",StringComparison.OrdinalIgnoreCase)){
    var handles=new List<IntPtr>();
    foreach(var p in Process.GetProcesses()){using(p){try{if(p.ProcessName.Contains("yandex",StringComparison.OrdinalIgnoreCase)&&p.ProcessName.Contains("music",StringComparison.OrdinalIgnoreCase)&&p.MainWindowHandle!=IntPtr.Zero)handles.Add(p.MainWindowHandle);}catch{}}}
    var windows=handles.ToArray();
    if(windows.Length>0){if(windows.All(h=>IsIconic(h)||!IsWindowVisible(h)))Show();else Hide();}
    else status.Text="Автопоказ: окно Яндекс Музыки не найдено";
   }
  }catch{SetMediaPlaying(false);status.Text="Ожидание Яндекс Музыки…";}finally{busy=false;refreshing=false;}
 }
 void Save(){try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!);System.IO.File.WriteAllText(settingsPath,System.Text.Json.JsonSerializer.Serialize(new Settings(Left,Top,Topmost,auto.IsChecked==true,preferred)));}catch{}}
 void LoadSettings(){try{var s=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(settingsPath));if(s!=null){Left=Math.Clamp(s.X,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width);Top=Math.Clamp(s.Y,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Height);Topmost=s.Pin;preferred=s.Source;auto.IsChecked=s.Auto;}}catch{} }
 [STAThread] public static void Main(string[] args){
  if(args.Length==2&&args[0]=="--self-test"){
   var applicationTest=new WApplication();var player=new Player();player.tray.Visible=false;
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
   var app=new WApplication();var player=new Player();player.tray.Visible=false;
   player.title.Text="Название трека";player.artist.Text="Исполнитель · Яндекс Музыка";player.refreshing=true;player.sources.Items.Add("Яндекс Музыка");player.sources.SelectedIndex=0;
   player.seek.Maximum=240;player.seek.Value=95;player.seek.IsEnabled=true;
   player.volumeSync=true;player.volumeSlider.Value=50;player.volumeSync=false;
   if(args[1].Contains("minecraft"))player.appearance=new Appearance("Minecraft","#8BE878");foreach(var candidate in Themes)if(args[1].Contains(candidate))player.appearance=new Appearance(candidate,ThemeAccent(candidate),Round:candidate=="Minecraft"?0:20);player.ApplyAppearance();if(args[1].Contains("expanded"))player.SetExpanded(true);
   var root=(FrameworkElement)player.Content;root.Measure(new Size(player.Width,player.Height));root.Arrange(new Rect(0,0,player.Width,player.Height));root.UpdateLayout();
   if(args[1].Contains("custom")){player.Customize(true);root=(FrameworkElement)player.customizeWindow!.Content;player.Width=player.customizeWindow.Width;player.Height=player.customizeWindow.Height;root.Measure(new Size(player.Width,player.Height));root.Arrange(new Rect(0,0,player.Width,player.Height));root.UpdateLayout();}
   var image=new RenderTargetBitmap((int)player.Width,(int)player.Height,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=System.IO.File.Create(args[1]))encoder.Save(file);player.tray.Dispose();return;
  }
  using var mutex=new System.Threading.Mutex(true,"Local\\YandexMiniPlayer",out var created);if(!created)return;var application=new WApplication();application.Run(new Player());
 }
}
