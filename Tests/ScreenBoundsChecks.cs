using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Screen=System.Windows.Forms.Screen;

static class ScreenBoundsChecks
{
 const BindingFlags Instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
 const BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
 [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;public int Width=>Right-Left;public int Height=>Bottom-Top;public Rect Rect=>new(Left,Top,Width,Height);}
 [DllImport("user32.dll",SetLastError=true)]static extern bool GetWindowRect(IntPtr window,out NativeRect rectangle);
 [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 static int checks;static Type type=null!;static object player=null!;
 static object? Field(string name)=>type.GetField(name,Instance)!.GetValue(player);
 static void Set(string name,object value)=>type.GetField(name,Instance)!.SetValue(player,value);
 static object? Call(string name,params object[] arguments)=>type.GetMethod(name,Instance)!.Invoke(player,arguments);
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static NativeRect Bounds(IntPtr handle){if(!GetWindowRect(handle,out var bounds))throw new Exception("GetWindowRect failed");return bounds;}
 static bool Contains(System.Drawing.Rectangle work,NativeRect bounds)=>bounds.Left>=work.Left&&bounds.Top>=work.Top&&bounds.Right<=work.Right&&bounds.Bottom<=work.Bottom;
 static bool RecoverVisible=>(bool)type.GetProperty("RecoveryIsVisible",Instance)!.GetValue(player)!;
 static void Snapshot(Window window,string path,Rect visible){
  var surface=(FrameworkElement)window.Content;surface.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(window.Width),(int)Math.Ceiling(window.Height),96,96,PixelFormats.Pbgra32);image.Render(surface);
  var native=Bounds(new WindowInteropHelper(window).Handle);
  int x=Math.Clamp((int)Math.Round((visible.X-native.Left)*image.PixelWidth/native.Width),0,image.PixelWidth-1),y=Math.Clamp((int)Math.Round((visible.Y-native.Top)*image.PixelHeight/native.Height),0,image.PixelHeight-1);
  int w=Math.Clamp((int)Math.Round(visible.Width*image.PixelWidth/native.Width),1,image.PixelWidth-x),h=Math.Clamp((int)Math.Round(visible.Height*image.PixelHeight/native.Height),1,image.PixelHeight-y);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(new CroppedBitmap(image,new Int32Rect(x,y,w,h))));using var output=File.Create(path);png.Save(output);
 }
 static void CaptureReturnPreview(Window window,IntPtr handle,string path,System.Drawing.Rectangle work){
  var native=Bounds(handle);double factor=native.Width/window.Width;int width=(int)Math.Ceiling(window.Width+32),height=(int)Math.Ceiling(window.Height+24);
  var frames=new List<BitmapFrame>();
  void Frame(ushort delay){
   var body=new RenderTargetBitmap((int)Math.Ceiling(window.Width),(int)Math.Ceiling(window.Height),96,96,PixelFormats.Pbgra32);body.Render((Visual)window.Content);
   var bounds=Bounds(handle);var visual=new DrawingVisual();using(var d=visual.RenderOpen()){
    d.DrawRectangle(new SolidColorBrush(Color.FromRgb(21,22,30)),null,new Rect(0,0,width,height));
    d.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(67,69,86)),1),new Point(11,0),new Point(11,height));
    d.PushClip(new RectangleGeometry(new Rect(12,0,width-12,height)));d.DrawImage(body,new Rect(12+(bounds.Left-work.Left)/factor,12,window.Width,window.Height));d.Pop();
   }
   var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();var metadata=new BitmapMetadata("gif");metadata.SetQuery("/grctlext/Delay",delay);metadata.SetQuery("/grctlext/Disposal",(byte)2);
   if(frames.Count==0){metadata.SetQuery("/appext/application",System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));metadata.SetQuery("/appext/data",new byte[]{3,1,0,0,0});}
   frames.Add(BitmapFrame.Create(image,null,metadata,null));
  }
  Frame(50);Call("RecoverFromEdge");
  while((bool)Field("recovering")!){var wait=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};timer.Tick+=(_,_)=>{timer.Stop();wait.Continue=false;};timer.Start();Dispatcher.PushFrame(wait);Frame(2);}
  Frame(100);var encoder=new GifBitmapEncoder();foreach(var frame in frames)encoder.Frames.Add(frame);using var file=File.Create(path);encoder.Save(file);
 }
 [STAThread]static int Main(string[] args){
  Application? application=null;
  try{
   string root=Path.GetFullPath(args[0]),data=Path.Combine(root,"Data","mini-player");Directory.CreateDirectory(data);
   File.WriteAllText(Path.Combine(root,"launcher-install.json"),"{\"ProductId\":\"MiniPlayer.WidgetLauncher\",\"TestInstallation\":true}");
   string hubFile=Path.Combine(root,"Data","hub-settings.json");
   File.WriteAllText(hubFile,"{\"AllowOffscreen\":false}");
   File.WriteAllText(Path.Combine(data,"settings.json"),"{\"X\":100,\"Y\":100,\"Pin\":false,\"Auto\":false,\"Source\":\"\"}");
   File.WriteAllText(Path.Combine(data,"appearance.json"),"{\"Theme\":\"Лаванда\",\"Accent\":\"#B9A1FF\"}");File.WriteAllText(Path.Combine(data,"hotkeys.json"),"{}");
   var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll"));
   var context=assembly.GetType("MiniPlayer.LaunchContext",true)!;
   context.GetProperty("DataDirectory",Static)!.SetValue(null,data);context.GetProperty("LauncherRoot",Static)!.SetValue(null,root);
   application=(Application)Activator.CreateInstance(assembly.GetType("MiniPlayer.PlayerApplication",true)!,true)!;
   type=assembly.GetType("MiniPlayer.Player",true)!;player=Activator.CreateInstance(type)!;
   Set("busy",true);((System.Windows.Forms.NotifyIcon)Field("tray")!).Visible=false;
   var window=(Window)player;window.ShowActivated=false;window.ShowInTaskbar=false;window.Opacity=0;window.Topmost=false;

   Rect Contain(Rect value,Rect work)=>(Rect)type.GetMethod("ContainInWorkArea",Static)!.Invoke(null,new object[]{value,work})!;
   var negative=new Rect(-1920,-1080,1920,1040);
   Check(Contain(new Rect(-2000,-1200,300,200),negative)==new Rect(-1920,-1080,300,200),"Negative-coordinate top and left edges clamp correctly");
   Check(Contain(new Rect(-5,-5,300,200),negative)==new Rect(-300,-240,300,200),"Negative-coordinate right and bottom edges clamp correctly");
   var inside=new Rect(-1500,-800,300,200);Check(Contain(inside,negative)==inside,"A contained window keeps its position");
   Check(Contain(new Rect(-900,-900,2500,1400),negative)==new Rect(-1920,-1080,2500,1400),"Oversized windows anchor safely without invalid clamp limits");

   window.Show();Pump();((DispatcherTimer)Field("timer")!).Stop();((DispatcherTimer)Field("motionTimer")!).Stop();
   Check(window.IsLoaded&&!(bool)Field("hubAllowOffscreen")!,"Player reads the isolated hub offscreen setting at startup");
   var handle=new WindowInteropHelper(window).Handle;var current=Bounds(handle);
   int minX=Screen.AllScreens.Min(screen=>screen.Bounds.Left),maxX=Screen.AllScreens.Max(screen=>screen.Bounds.Right);
   int minY=Screen.AllScreens.Min(screen=>screen.Bounds.Top),maxY=Screen.AllScreens.Max(screen=>screen.Bounds.Bottom);
   var requests=new[]{(minX-current.Width-300,current.Top,"left"),(maxX+300,current.Top,"right"),(current.Left,minY-current.Height-300,"top"),(current.Left,maxY+300,"bottom")};
   foreach(var request in requests){
    Check(SetWindowPos(handle,IntPtr.Zero,request.Item1,request.Item2,0,0,0x0001|0x0004|0x0010),"Native move request: "+request.Item3);
    var actual=Bounds(handle);var work=Screen.FromRectangle(new System.Drawing.Rectangle(request.Item1,request.Item2,current.Width,current.Height)).WorkingArea;
    Check(Contains(work,actual),"WM_WINDOWPOSCHANGING keeps the player inside the "+request.Item3+" edge");
   }
   foreach(var screen in Screen.AllScreens){
    var work=screen.WorkingArea;int x=work.Left+(work.Width-current.Width)/2,y=work.Top+(work.Height-current.Height)/2;
    SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x0001|0x0004|0x0010);var actual=Bounds(handle);
    Check(actual.Left==x&&actual.Top==y&&Contains(work,actual),"Guard permits normal placement on monitor "+screen.DeviceName);
   }
   File.WriteAllText(hubFile,"{\"AllowOffscreen\":true}");Call("RefreshHubInterface",true);
   int outsideX=minX-current.Width/2;int outsideY=Screen.PrimaryScreen!.WorkingArea.Top+100;
   SetWindowPos(handle,IntPtr.Zero,outsideX,outsideY,0,0,0x0001|0x0004|0x0010);var outside=Bounds(handle);
   Check((bool)Field("hubAllowOffscreen")!&&outside.Left==outsideX,"Enabling the hub toggle allows an offscreen native move");
   Call("UpdateRecoveryArrow");var arrow=(Window?)Field("recoveryWindow");if(arrow!=null)arrow.Opacity=0;
   Check(RecoverVisible,"Allowed offscreen placement keeps the recovery arrow available");
   File.WriteAllText(hubFile,"{\"AllowOffscreen\":false}");Call("RefreshHubInterface",true);Call("UpdateRecoveryArrow");var restored=Bounds(handle);
   Check(!(bool)Field("hubAllowOffscreen")!&&Contains(Screen.FromRectangle(new System.Drawing.Rectangle(restored.Left,restored.Top,restored.Width,restored.Height)).WorkingArea,restored),"Disabling the toggle immediately recovers an already offscreen player");
   Check(!RecoverVisible,"Recovery arrow hides while crossing the screen edge is disabled");

   File.WriteAllText(hubFile,"{\"AllowOffscreen\":true}");Call("RefreshHubInterface",true);
   string snapshots=Path.Combine(root,"Snapshots");Directory.CreateDirectory(snapshots);
   foreach(string theme in (string[])type.GetField("Themes",Static)!.GetValue(null)!){
    File.WriteAllText(Path.Combine(data,"appearance.json"),JsonSerializer.Serialize(new{Theme=theme,Accent="#B9A1FF",Alpha=.75,Scale=theme=="Кофе"?1.25:1}));Call("LoadAppearance");Pump();
    current=Bounds(handle);outsideX=minX-current.Width/2;
    SetWindowPos(handle,IntPtr.Zero,outsideX,outsideY,0,0,0x0001|0x0004|0x0010);Call("UpdateRecoveryArrow");window.UpdateLayout();
    var embedded=(Button)Field("recoveryButton")!;var face=(Canvas)Field("deviceFace")!;
    Check(embedded.Parent==face&&embedded.Visibility==Visibility.Visible&&Field("recoveryWindow") is not Window{IsVisible:true},theme+" recovery arrow belongs to the player canvas");
    var start=embedded.PointToScreen(new Point());var finish=embedded.PointToScreen(new Point(embedded.ActualWidth,embedded.ActualHeight));
    Check(Screen.AllScreens.Any(s=>s.WorkingArea.Contains(new System.Drawing.Rectangle((int)Math.Floor(start.X),(int)Math.Floor(start.Y),(int)Math.Ceiling(finish.X-start.X),(int)Math.Ceiling(finish.Y-start.Y)))),theme+" arrow fits inside the visible monitor area");
    current=Bounds(handle);var work=Screen.FromRectangle(new System.Drawing.Rectangle(current.Left,current.Top,current.Width,current.Height)).WorkingArea;
    Snapshot(window,Path.Combine(snapshots,"edge-"+theme+".png"),Rect.Intersect(current.Rect,new Rect(work.X,work.Y,work.Width,work.Height)));
    double opacity=((Border)window.Content).Opacity;var transform=((Border)window.Content).RenderTransform;
    embedded.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(SystemParameters.ClientAreaAnimation){
     Check((bool)Field("recovering")!&&Bounds(handle).Left==current.Left,theme+" arrow starts a smooth return without teleporting");
     Pump();var intermediate=Bounds(handle);Check(intermediate.Left>current.Left&&intermediate.Left<=work.Left+8,theme+" return advances toward the screen without overshoot");
    }
    for(int i=0;i<20&&(bool)Field("recovering")!;i++)Pump();
    Check(Contains(work,Bounds(handle))&&!RecoverVisible&&!(bool)Field("recovering")!,theme+" return ends on screen and hides both recovery controls");
    Check(Math.Abs(((Border)window.Content).Opacity-opacity)<.001&&ReferenceEquals(((Border)window.Content).RenderTransform,transform)&&!((DispatcherTimer)Field("recoveryTimer")!).IsEnabled,theme+" pop-up restores opacity, transforms and stops its timer");
   }
   current=Bounds(handle);var topScreen=Screen.AllScreens.OrderBy(s=>s.Bounds.Top).First().WorkingArea;var bottomScreen=Screen.AllScreens.OrderByDescending(s=>s.Bounds.Bottom).First().WorkingArea;
   var edgeCases=new[]{(minX-current.Width/2,outsideY,0d,"left"),(maxX-current.Width/2,outsideY,180d,"right"),(topScreen.Left+100,minY-current.Height/2,90d,"top"),(bottomScreen.Left+100,maxY-current.Height/2,270d,"bottom")};
   foreach(var edge in edgeCases){
    SetWindowPos(handle,IntPtr.Zero,edge.Item1,edge.Item2,0,0,0x0001|0x0004|0x0010);Pump();Call("UpdateRecoveryArrow");window.UpdateLayout();
    if(!RecoverVisible||((RotateTransform)((System.Windows.Shapes.Path)Field("recoveryButtonChevron")!).RenderTransform).Angle!=edge.Item3)Console.WriteLine("Direction diagnostics: "+edge+" actual="+Bounds(handle).Rect+" angle="+((RotateTransform)((System.Windows.Shapes.Path)Field("recoveryButtonChevron")!).RenderTransform).Angle+" fallback="+((Window?)Field("recoveryWindow"))?.IsVisible);
    Check(RecoverVisible&&((RotateTransform)((System.Windows.Shapes.Path)Field("recoveryButtonChevron")!).RenderTransform).Angle==edge.Item3,"Embedded arrow points inward at the "+edge.Item4+" edge");
    current=Bounds(handle);var work=Screen.FromRectangle(new System.Drawing.Rectangle(current.Left,current.Top,current.Width,current.Height)).WorkingArea;
    Snapshot(window,Path.Combine(snapshots,"edge-direction-"+edge.Item4+".png"),Rect.Intersect(current.Rect,new Rect(work.X,work.Y,work.Width,work.Height)));
    Call("ReturnToScreen");
   }
   current=Bounds(handle);SetWindowPos(handle,IntPtr.Zero,minX-current.Width-100,outsideY,0,0,0x0001|0x0004|0x0010);Call("UpdateRecoveryArrow");
   var fallback=(Window)Field("recoveryWindow")!;fallback.Opacity=0;
   Check(fallback.IsVisible&&((Button)fallback.Content).Background!=null&&fallback.Topmost==window.Topmost,"Completely hidden player keeps a themed edge tab with matching pin state");
   ((Button)fallback.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(int i=0;i<20&&(bool)Field("recovering")!;i++)Pump();
   Check(!fallback.IsVisible&&Contains(Screen.FromHandle(handle).WorkingArea,Bounds(handle)),"The themed edge tab also returns the hidden player");
   if(SystemParameters.ClientAreaAnimation){
    current=Bounds(handle);SetWindowPos(handle,IntPtr.Zero,minX-current.Width/2,outsideY,0,0,0x0001|0x0004|0x0010);Call("UpdateRecoveryArrow");Call("RecoverFromEdge");Call("HidePlayer");Pump();
    Check(!window.IsVisible&&!(bool)Field("recovering")!&&!((DispatcherTimer)Field("recoveryTimer")!).IsEnabled,"Hiding during return cancels motion and does not resurrect the widget");
    Call("ReturnToScreen");current=Bounds(handle);SetWindowPos(handle,IntPtr.Zero,minX-current.Width/2,outsideY,0,0,0x0001|0x0004|0x0010);Call("UpdateRecoveryArrow");Call("RecoverFromEdge");
    File.WriteAllText(hubFile,"{\"AllowOffscreen\":false}");Call("RefreshHubInterface",true);Pump();
    Check(!(bool)Field("recovering")!&&Contains(Screen.FromHandle(handle).WorkingArea,Bounds(handle))&&!RecoverVisible,"Disabling offscreen mode during motion safely clamps the widget");
   }
   File.WriteAllText(hubFile,"{\"AllowOffscreen\":true}");Call("RefreshHubInterface",true);
   File.WriteAllText(Path.Combine(data,"appearance.json"),"{\"Theme\":\"Лаванда\",\"Accent\":\"#B38DE3\"}");Call("LoadAppearance");((TextBlock)Field("title")!).Text="Название трека";((TextBlock)Field("artist")!).Text="Исполнитель";Pump();current=Bounds(handle);
   var previewScreen=Screen.AllScreens.OrderBy(s=>s.Bounds.Left).First().WorkingArea;
   SetWindowPos(handle,IntPtr.Zero,previewScreen.Left-current.Width/2,previewScreen.Top+100,0,0,0x0001|0x0004|0x0010);Pump();Call("UpdateRecoveryArrow");window.UpdateLayout();
   CaptureReturnPreview(window,handle,Path.Combine(snapshots,"recovery-pop-preview.gif"),previewScreen);
   string presetFolder=Path.Combine(root,"Data","Customization","mini-player");
   Directory.CreateDirectory(presetFolder);
   File.WriteAllText(Path.Combine(presetFolder,"active.json"),"{\"Width\":420,\"Height\":420,\"Shape\":\"circle\"}");
   Call("RefreshCustomPreset",true);
   window.UpdateLayout();
   var circleFace=(Canvas)Field("customSurface")!;
   var origin=circleFace.PointToScreen(new Point());
   var faceBounds=new Rect(origin,circleFace.PointToScreen(new Point(circleFace.Width,circleFace.Height)));
   var clippedCorner=new Rect(origin,circleFace.PointToScreen(new Point(46,46)));
   Check(!(bool)Call("ShowEmbeddedRecovery",faceBounds,clippedCorner,180d)!,
    "A transparent corner of the circular player leaves recovery to the external tab");
   var halfCircle=new Rect(origin,circleFace.PointToScreen(new Point(circleFace.Width/2,circleFace.Height)));
   Check((bool)Call("ShowEmbeddedRecovery",faceBounds,halfCircle,180d)!,"A visible half-circle keeps its recovery arrow embedded");
   var circleArrow=(Button)Field("recoveryButton")!;
   var buttonBounds=new Rect(Canvas.GetLeft(circleArrow),Canvas.GetTop(circleArrow),circleArrow.Width,circleArrow.Height);
   Check(circleFace.Clip.FillContains(new RectangleGeometry(buttonBounds)),"Entire embedded recovery button fits the shape");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{if(player!=null){try{Set("closing",true);((Window)player).Close();}catch{}}application?.Shutdown();}
 }
}
