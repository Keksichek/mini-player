using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PolishChecks
{
 const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static Type playerType=null!;
 static object player=null!;
 static int checks;
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 static object? Field(string name)=>playerType.GetField(name,Instance)!.GetValue(player);
 static void Set(string name,object? value)=>playerType.GetField(name,Instance)!.SetValue(player,value);
 static object? Invoke(string name,params object[] args)=>playerType.GetMethod(name,Instance)!.Invoke(player,args);
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static IEnumerable<DependencyObject> Walk(DependencyObject node)
 {
  yield return node;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;
 }
 static void Layout(FrameworkElement root,double width,double height)
 {
  root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
 }
 static Rect Bounds(FrameworkElement item,Visual parent)=>new(item.TranslatePoint(new Point(),(UIElement)parent),item.RenderSize);
 static void Pump(){var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);}
 static void Snapshot(FrameworkElement surface,string path){surface.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(path);png.Save(output);}
 static Button NamedButton(Window w,string name)=>Walk((FrameworkElement)w.Content).OfType<Button>().Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)==name);
 static void CheckEditorChrome(Window editor,string name){
  editor.ShowActivated=false;editor.ShowInTaskbar=false;editor.Opacity=0;editor.Show();editor.UpdateLayout();
  var handle=new System.Windows.Interop.WindowInteropHelper(editor).Handle;
  foreach(string action in new[]{"Свернуть","Развернуть","Закрыть"}){
   var button=NamedButton(editor,action);var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
   Check(SendMessage(handle,0x84,IntPtr.Zero,new IntPtr(((int)point.X&0xFFFF)|(((int)point.Y&0xFFFF)<<16))).ToInt32()==1,name+" title button receives mouse input: "+action);
  }
  var drag=editor.PointToScreen(new Point(210,24));
  Check(SendMessage(handle,0x84,IntPtr.Zero,new IntPtr(((int)drag.X&0xFFFF)|(((int)drag.Y&0xFFFF)<<16))).ToInt32()==2,name+" title bar permits native dragging");
  var maximize=NamedButton(editor,"Развернуть");maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(editor.WindowState==WindowState.Maximized,name+" maximizes");
  maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(editor.WindowState==WindowState.Normal,name+" restores");
  NamedButton(editor,"Свернуть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(editor.WindowState==WindowState.Minimized,name+" minimizes");editor.WindowState=WindowState.Normal;
 }
 static byte[] Picture(FrameworkElement icon,int size=24)
 {
  Layout(icon,size,size);var image=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);image.Render(icon);
  byte[] pixels=new byte[size*size*4];image.CopyPixels(pixels,size*4,0);return pixels;
 }
 static void CheckGlyph(Type icons,string name,object first,object second)
 {
  var method=icons.GetMethods(Static).Single(x=>x.Name==name);
  FrameworkElement Create(object value)=>(FrameworkElement)method.Invoke(null,new[]{value,Brushes.Black})!;
  var a=Picture(Create(first));var b=Picture(Create(second));
  Check(a.Where((_,i)=>i%4==3).Any(x=>x!=0),name+" icon is drawn");
  Check(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)),name+" states have different images");
 }
 [STAThread] static int Main(string[] args)
 {
  Application? application=null;
  try{
   string scratch=Path.GetFullPath(args[0]);Directory.CreateDirectory(scratch);
   string path=Path.Combine(scratch,"settings.json");
   var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll"));
   playerType=assembly.GetType("MiniPlayer.Player",true)!;
   Check(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion=="0.1","Player informational version is 0.1");
   application=(Application)Activator.CreateInstance(assembly.GetType("MiniPlayer.PlayerApplication",true)!,true)!;
   player=Activator.CreateInstance(playerType)!;Set("settingsPath",path);
   ((System.Windows.Forms.NotifyIcon)Field("tray")!).Visible=false;
   var window=(Window)player;
   File.WriteAllText(path,"{\"X\":50,\"Y\":50,\"Pin\":true,\"Auto\":true,\"Source\":\"\"}");
   Invoke("LoadSettings");
   Check(!(bool)Field("movementLocked")!,"Old settings default to movable player");
   Invoke("ToggleMovementLock");
   Check((bool)Field("movementLocked")!&&window.Topmost,"Movement lock is independent of always-on-top");
   using(var saved=JsonDocument.Parse(File.ReadAllText(path)))Check(saved.RootElement.GetProperty("MovementLocked").GetBoolean(),"Movement lock persists in settings");
   Invoke("TogglePin");
   Check((bool)Field("movementLocked")!&&!window.Topmost,"Pin toggle does not unlock movement");
   Set("movementLocked",false);Invoke("LoadSettings");
   Check((bool)Field("movementLocked")!,"Saved movement lock loads again");
   Invoke("ToggleMovementLock");
   Check(!(bool)Field("movementLocked")!&&!window.Topmost,"Unlock does not change always-on-top");
   var canDrag=playerType.GetProperty("CanDragPlayer",Instance);
   if(canDrag!=null){Set("movementLocked",true);Check(!(bool)canDrag.GetValue(player)!,"Locked player rejects drag initiation");Set("movementLocked",false);Check((bool)canDrag.GetValue(player)!,"Unlocked player accepts drag initiation");}
   var icons=assembly.GetType("MiniPlayer.PlayerIcons",true)!;
   CheckGlyph(icons,"Pin",false,true);CheckGlyph(icons,"MovementLock",false,true);CheckGlyph(icons,"ClickThrough",false,true);
   var repeatMethod=icons.GetMethods(Static).Single(x=>x.Name=="Repeat");
   var repeatType=repeatMethod.GetParameters()[0].ParameterType;
   var none=Enum.Parse(repeatType,"None");var list=Enum.Parse(repeatType,"List");var track=Enum.Parse(repeatType,"Track");
   CheckGlyph(icons,"Repeat",none,list);CheckGlyph(icons,"Repeat",list,track);
   ((TextBlock)Field("title")!).Text="POLISH CHECK TITLE";((TextBlock)Field("artist")!).Text="POLISH CHECK ARTIST";
   var themes=(string[])playerType.GetField("Themes",Static)!.GetValue(null)!;
   string snapshots=Path.Combine(scratch,"Snapshots");Directory.CreateDirectory(snapshots);
   foreach(string theme in themes){
    File.WriteAllText(Path.Combine(scratch,"appearance.json"),JsonSerializer.Serialize(new{Theme=theme,Accent="#8BE878"}));Invoke("LoadAppearance");
    var root=(FrameworkElement)window.Content;Layout(root,window.Width,window.Height);
    var face=(Canvas)Field("deviceFace")!;
    var utilities=new[]{"facePin","faceLock","faceClickThrough","faceRepeat","faceMenu","faceHide"}.Select(x=>(Button)Field(x)!).ToArray();
    var rects=utilities.Select(x=>Bounds(x,face)).ToArray();
    Check(rects.All(x=>x.Width>=19&&x.Height>=19&&x.Left>=-.01&&x.Top>=-.01&&x.Right<=face.Width+.01&&x.Bottom<=face.Height+.01),theme+" utility buttons fit inside player");
    Check(rects.SelectMany((rect,i)=>rects.Skip(i+1).Select(other=>Rect.Intersect(rect,other))).All(x=>x.IsEmpty||x.Width*x.Height<.01),theme+" utility buttons do not overlap");
    var nextButton=face.Children.OfType<Button>().Single(x=>x.ToolTip as string=="Следующий трек");
    var nextBounds=Bounds(nextButton,face);var repeatBounds=Bounds((Button)Field("faceRepeat")!,face);
    Check(repeatBounds.Left>=nextBounds.Right+2&&repeatBounds.Left-nextBounds.Right<=18&&Math.Abs((repeatBounds.Top+repeatBounds.Height/2)-(nextBounds.Top+nextBounds.Height/2))<1,theme+" repeat sits beside transport controls");
    Check(utilities.All(x=>x.Content is FrameworkElement&&x.Content is not TextBlock),theme+" utility buttons use images");
    var captions=Walk(face).OfType<TextBlock>().Where(x=>x.Text is "POLISH CHECK TITLE" or "POLISH CHECK ARTIST").Select(x=>Bounds(x,face)).ToArray();
    Check(rects.All(rect=>captions.All(caption=>{var overlap=Rect.Intersect(rect,caption);return overlap.IsEmpty||overlap.Width*overlap.Height<.01;})),theme+" utility buttons do not cover track captions");
    Check(!Walk(face).OfType<TextBlock>().Any(x=>x.Text.Contains("Закреп")||x.Text.Contains("Откреп")||x.Text=="⋯"),theme+" pin labels and ellipsis are absent");
    var menu=face.ContextMenu!;
    Check(menu.Items.OfType<MenuItem>().Select(x=>x.Header as string).SequenceEqual(new[]{"Настройки","Кастомизация","Скрыть"}),theme+" context menu contains no movement lock");
    Check(!menu.Items.OfType<MenuItem>().Any(x=>System.Windows.Automation.AutomationProperties.GetName(x).Contains("Закреп")||System.Windows.Automation.AutomationProperties.GetName(x).Contains("Откреп")||x.Header is string s&&(s.Contains("Закреп")||s.Contains("Откреп"))),theme+" right-click menu has no pin action");
    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.Width),(int)Math.Ceiling(window.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(snapshots,theme+".png")))encoder.Save(output);
   }
   Set("shownByUser",false);((Button)Field("faceHide")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check((bool)Field("shownByUser")!&&!window.IsVisible&&!(bool)Field("closing")!,"Eye hides player and suppresses automatic reappearance");
   Invoke("OpenSettings",true);
   var settings=(Window)Field("settingsWindow")!;var panel=(FrameworkElement)settings.Content;Layout(panel,settings.Width,settings.Height);
   var nodes=Walk(panel).ToArray();
   void SnapshotSettings(string name){
    panel.UpdateLayout();var image=new RenderTargetBitmap((int)settings.Width,(int)settings.Height,96,96,PixelFormats.Pbgra32);image.Render(panel);
    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(scratch,name));png.Save(output);
   }
   SnapshotSettings("settings-source.png");
   Check(nodes.OfType<ComboBox>().Contains((ComboBox)Field("sources")!)&&!nodes.OfType<TextBlock>().Any(x=>x.Text.Contains("Приложение в микшере")),"Settings retain source selector without mixer application selector");
   Check(((ComboBox)Field("sources")!).IsEnabled,"Source picker remains interactive when no sessions are available");
   Check(nodes.OfType<Button>().Any(x=>x.Content as string=="Обновить"),"Source list can be refreshed manually");
   Check(nodes.OfType<Slider>().Any(x=>ReferenceEquals(x,Field("volumeSlider"))),"Settings retain automatic source volume control");
   Check(!nodes.OfType<TextBlock>().Any(x=>x.Text.Contains("микшер",StringComparison.OrdinalIgnoreCase)),"Windows mixer section text is removed");
   foreach(string action in new[]{"Закрепить (","Открепить ("}){
    var name=action[..^2];
    var label=nodes.OfType<TextBlock>().Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)==name);
    var text=string.Concat(label.Inlines.OfType<System.Windows.Documents.Run>().Select(x=>x.Text));
    Check(text.StartsWith(action,StringComparison.Ordinal)&&text.EndsWith(")",StringComparison.Ordinal)&&label.Inlines.OfType<System.Windows.Documents.InlineUIContainer>().Any(x=>x.Child is System.Windows.Shapes.Path),action+"symbol) hotkey label is readable");
   }
   nodes.OfType<ScrollViewer>().First().ScrollToEnd();panel.UpdateLayout();SnapshotSettings("settings-hotkeys.png");
   settings.Width=settings.MinWidth;settings.Height=settings.MinHeight;Layout(panel,settings.Width,settings.Height);SnapshotSettings("settings-compact.png");
   var editorScroll=Walk(panel).OfType<ScrollViewer>().First();editorScroll.ScrollToEnd();panel.UpdateLayout();
   var done=nodes.OfType<Button>().Single(x=>x.Content as string=="Готово");
   Check(editorScroll.ScrollableHeight>0&&editorScroll.VerticalOffset>0&&Bounds(done,panel).Bottom<=panel.ActualHeight,"Compact settings keep scrolling and Done reachable");
   settings.Width=600;settings.Height=720;editorScroll.ScrollToTop();Layout(panel,settings.Width,settings.Height);
   CheckEditorChrome(settings,"Settings");
   // Exercise the custom dropdown and slider through their accessibility peers.
   var selector=(ComboBox)Field("sources")!;Set("refreshing",true);
   selector.Items.Add("Музыкальное приложение");selector.Items.Add("Второй источник");selector.SelectedIndex=0;
   var toggle=Walk(selector).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single();
   var togglePeer=new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(toggle);
   ((System.Windows.Automation.Provider.IToggleProvider)togglePeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();Pump();settings.UpdateLayout();
   Check(selector.IsDropDownOpen,"Custom source dropdown opens using its toggle");
   var popup=(System.Windows.Controls.Primitives.Popup)selector.Template.FindName("PART_Popup",selector);
   Snapshot((FrameworkElement)popup.Child,Path.Combine(scratch,"settings-source-dropdown.png"));
   var itemPeer=new System.Windows.Automation.Peers.ComboBoxAutomationPeer(selector).GetChildren().Single(x=>x.GetName()=="Второй источник");
   ((System.Windows.Automation.Provider.ISelectionItemProvider)itemPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)).Select();
   Check(selector.SelectedIndex==1,"Custom source dropdown retains item selection");selector.IsDropDownOpen=false;selector.Items.Clear();Set("refreshing",false);
   Set("volumeSync",true);var volume=(Slider)Field("volumeSlider")!;volume.IsEnabled=true;
   ((System.Windows.Automation.Provider.IRangeValueProvider)new System.Windows.Automation.Peers.SliderAutomationPeer(volume).GetPattern(System.Windows.Automation.Peers.PatternInterface.RangeValue)).SetValue(63);
   Check(Math.Abs(((Slider)Field("faceVolume")!).Value-63)<.01,"Preferences volume still updates the player slider");Set("volumeSync",false);
   NamedButton(settings,"Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check(!settings.IsVisible&&ReferenceEquals(settings,Field("settingsWindow")),"Settings title close hides the reusable window");
   var beforeAppearance=Field("appearance")!;string beforeFile=File.ReadAllText(Path.Combine(scratch,"appearance.json"));
   Invoke("Customize",true);var editor=(Window)Field("customizeWindow")!;
   FrameworkElement editorPanel=(FrameworkElement)editor.Content;Layout(editorPanel,editor.Width,editor.Height);
   void EditorButton(string label)=>Walk((FrameworkElement)editor.Content).OfType<Button>().Single(x=>x.Content as string==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Snapshot(editorPanel,Path.Combine(scratch,"appearance-themes.png"));
   EditorButton("Цвета");editorPanel.UpdateLayout();Snapshot(editorPanel,Path.Combine(scratch,"appearance-colors.png"));
   var brightness=Walk(editorPanel).OfType<Slider>().Single();brightness.Value=.7;
   Check(!Equals(beforeAppearance,Field("appearance")),"Palette controls still update the live appearance");
   EditorButton("Размер и детали");editorPanel.UpdateLayout();Snapshot(editorPanel,Path.Combine(scratch,"appearance-details.png"));
   var scale=Walk(editorPanel).OfType<Slider>().Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="Масштаб");scale.Value=1.15;
   Check(Math.Abs((double)Field("appearance")!.GetType().GetProperty("Scale")!.GetValue(Field("appearance"))!-1.15)<.001,"Dark size slider still changes scale");
   editor.Width=editor.MinWidth;editor.Height=editor.MinHeight;Layout(editorPanel,editor.Width,editor.Height);var detailsScroll=Walk(editorPanel).OfType<ScrollViewer>().First();detailsScroll.ScrollToEnd();editorPanel.UpdateLayout();
   Check(detailsScroll.ScrollableHeight>0&&detailsScroll.VerticalOffset>0&&Walk(editorPanel).OfType<Button>().Any(x=>x.Content as string=="Сохранить"&&Bounds(x,editorPanel).Bottom<=editorPanel.ActualHeight),"Compact appearance keeps details and Save accessible");Snapshot(editorPanel,Path.Combine(scratch,"appearance-compact.png"));
   editor.Width=600;editor.Height=720;Layout(editorPanel,editor.Width,editor.Height);CheckEditorChrome(editor,"Appearance");
   NamedButton(editor,"Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check(Field("customizeWindow")==null&&Equals(beforeAppearance,Field("appearance"))&&File.ReadAllText(Path.Combine(scratch,"appearance.json"))==beforeFile,"Appearance title close cancels unsaved changes");
   Invoke("Customize",true);editor=(Window)Field("customizeWindow")!;editorPanel=(FrameworkElement)editor.Content;Layout(editorPanel,editor.Width,editor.Height);
   NamedButton(editor,"Лаванда").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));EditorButton("Сохранить");
   using(var savedTheme=JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch,"appearance.json"))))Check(savedTheme.RootElement.GetProperty("Theme").GetString()=="Лаванда"&&Field("customizeWindow")==null,"Appearance Save persists the selected theme and closes the editor");
   var repeat=(Button)Field("repeat")!;
   Check(!repeat.IsEnabled,"Repeat starts disabled without supported media source");
   if(args.Length>1){
    string payload=Path.GetFullPath(args[1]);const string version="0.3";
    using(var catalog=JsonDocument.Parse(File.ReadAllText(Path.Combine(payload,"Catalog","widgets.json"))))Check(catalog.RootElement[0].GetProperty("Version").GetString()=="0.1","Catalog identifies player as 0.1");
    var launcherAssembly=Assembly.LoadFrom(Path.Combine(payload,"Launcher.dll"));
    Check(launcherAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion==version,"Launcher informational version keeps all digits");
    var launcher=(Window)Activator.CreateInstance(launcherAssembly.GetType("WidgetLauncher.LauncherWindow",true)!,new object[]{payload,true,"catalog"})!;
    try{
     var surface=(FrameworkElement)launcher.Content;Layout(surface,launcher.Width,launcher.Height);
     Check(launcher.WindowStyle==WindowStyle.None&&System.Windows.Shell.WindowChrome.GetWindowChrome(launcher)!=null,"Launcher uses its own dark window frame with native resize behavior");
     Check(launcher.Icon!=null,"Launcher has an application icon instead of the default .NET icon");
     foreach(string action in new[]{"Свернуть","Развернуть","Закрыть"})Check(Walk(surface).OfType<Button>().Any(x=>System.Windows.Automation.AutomationProperties.GetName(x)==action),"Launcher window button: "+action);
     Check(Walk(surface).OfType<TextBlock>().Any(x=>x.Text==version),"Launcher shows numeric version without prefix");
     launcher.Width=900;launcher.Height=650;Layout(surface,launcher.Width,launcher.Height);
     var scroll=Walk(surface).OfType<ScrollViewer>().First();
     Check(scroll.ScrollableHeight>0,"Compact launcher keeps overflowing catalog scrollable");
     var scrollBar=Walk(surface).OfType<System.Windows.Controls.Primitives.ScrollBar>().First(x=>x.Visibility==Visibility.Visible);
     Check(scrollBar.ActualWidth<=10.01,"Launcher scrollbar is slim ("+scrollBar.ActualWidth.ToString("0.0000")+")");
     var thumb=Walk(scrollBar).OfType<System.Windows.Controls.Primitives.Thumb>().Single();
     Check(thumb.Template.FindName("Grip",thumb) is Border grip&&grip.Width==4,"Launcher scrollbar uses the dark custom thumb");
     scroll.ScrollToEnd();surface.UpdateLayout();Check(scroll.VerticalOffset>0,"Dark scrollbar keeps catalog scrolling functional");scroll.ScrollToTop();surface.UpdateLayout();
     var bitmap=new RenderTargetBitmap(900,650,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(scratch,"hub-launcher-compact.png")))png.Save(file);
     launcher.ShowActivated=false;launcher.ShowInTaskbar=false;launcher.Opacity=0;launcher.Show();launcher.UpdateLayout();
     var handle=new System.Windows.Interop.WindowInteropHelper(launcher).Handle;
     Button WindowButton(string name)=>Walk((FrameworkElement)launcher.Content).OfType<Button>().Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)==name);
     foreach(string name in new[]{"Свернуть","Развернуть","Закрыть"}){
      var button=WindowButton(name);var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
      var coordinates=new IntPtr(((int)point.X&0xFFFF)|(((int)point.Y&0xFFFF)<<16));
      Check(SendMessage(handle,0x84,IntPtr.Zero,coordinates).ToInt32()==1,"Custom title button accepts mouse input: "+name);
     }
     var maximizeButton=WindowButton("Развернуть");maximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(launcher.WindowState==WindowState.Maximized,"Custom maximize button works");
     maximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(launcher.WindowState==WindowState.Normal,"Custom restore button works");
     WindowButton("Свернуть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(launcher.WindowState==WindowState.Minimized,"Custom minimize button works");
     launcher.WindowState=WindowState.Normal;WindowButton("Закрыть").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!launcher.IsVisible,"Custom close button works");
    }finally{launcher.Close();}
   }
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{
   if(player!=null){try{Set("closing",true);((Window)player).Close();}catch{}try{((IDisposable)Field("tray")!).Dispose();}catch{}}
   if(application!=null)application.Shutdown();
  }
 }
}
