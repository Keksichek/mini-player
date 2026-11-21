using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Screen=System.Windows.Forms.Screen;

namespace MiniPlayer;

public sealed partial class Player
{
 enum AnchorPoint { Free,TopLeft,Top,TopRight,Left,Right,BottomLeft,Bottom,BottomRight }
 AnchorPoint anchor;
 string anchorMonitor="";
 int anchorGap=16;
 Action? refreshAnchorSettings;

 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
 struct DisplayDevice {
  public int Size;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;
  public int Flags;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string InterfaceId;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
 }
 [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
 static extern bool EnumDisplayDevicesW(string display,uint index,ref DisplayDevice device,uint flags);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr handle);

 sealed record AnchorDisplay(string Id,string Name,Screen Screen);
 AnchorDisplay[]? anchorDisplays;
 bool anchorRefreshPending;
 AnchorDisplay[] AnchorDisplays()=>anchorDisplays??=Screen.AllScreens.Select(screen=>{
  var device=new DisplayDevice{Size=Marshal.SizeOf<DisplayDevice>()};
  EnumDisplayDevicesW(screen.DeviceName,0,ref device,1);
  // The interface name survives Windows renumbering DISPLAY1/DISPLAY2 after unplugging a monitor.
  string id=string.IsNullOrEmpty(device.InterfaceId)?screen.DeviceName:device.InterfaceId;
  string number=screen.DeviceName.Replace(@"\\.\DISPLAY","",StringComparison.OrdinalIgnoreCase);
  string name="Монитор "+number+" · "+screen.Bounds.Width+" × "+screen.Bounds.Height+(screen.Primary?" · основной":"");
  return new AnchorDisplay(id,name,screen);
 }).ToArray();

 static AnchorDisplay? ResolveAnchorDisplay(AnchorDisplay[] displays,string preferred)=>
  displays.FirstOrDefault(d=>string.Equals(d.Id,preferred,StringComparison.OrdinalIgnoreCase))
  ??displays.FirstOrDefault(d=>d.Screen.Primary)??displays.FirstOrDefault();

 static Point AnchorPosition(AnchorPoint point,Rect work,Size window,double gap,double dpi){
  double dx=Math.Max(0,work.Width-window.Width),dy=Math.Max(0,work.Height-window.Height);
  double margin=Math.Round(gap*dpi/96d);
  double x=point switch{
   AnchorPoint.TopLeft or AnchorPoint.Left or AnchorPoint.BottomLeft=>Math.Min(margin,dx),
   AnchorPoint.TopRight or AnchorPoint.Right or AnchorPoint.BottomRight=>Math.Max(0,dx-margin),
   _=>dx/2
  };
  double y=point switch{
   AnchorPoint.TopLeft or AnchorPoint.Top or AnchorPoint.TopRight=>Math.Min(margin,dy),
   AnchorPoint.BottomLeft or AnchorPoint.Bottom or AnchorPoint.BottomRight=>Math.Max(0,dy-margin),
   _=>dy/2
  };
  return new Point(Math.Round(work.Left+x),Math.Round(work.Top+y));
 }

 Point? GetAnchorPosition(IntPtr hwnd,Size window){
  if(anchor==AnchorPoint.Free)return null;
  var target=ResolveAnchorDisplay(AnchorDisplays(),anchorMonitor);
  if(target==null)return null;
  var area=target.Screen.WorkingArea;
  uint dpi=GetDpiForWindow(hwnd);
  return AnchorPosition(anchor,new Rect(area.X,area.Y,area.Width,area.Height),window,anchorGap,dpi==0?96:dpi);
 }

 void RefreshAnchorOnDisplayChange(){
  if(anchorRefreshPending||closing)return;
  anchorRefreshPending=true;
  Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,(Action)(()=>{
   anchorRefreshPending=false;if(closing)return;
   anchorDisplays=null;
   refreshAnchorSettings?.Invoke();UpdateRecoveryArrow();
  }));
 }

 void ClearAnchorForDrag(){
  if(anchor==AnchorPoint.Free)return;
  anchor=AnchorPoint.Free;
  refreshAnchorSettings?.Invoke();
 }

 void AddAnchorSettings(Panel panel){
  ComboBox Picker(string name){
   panel.Children.Add(new TextBlock{Text=name,Foreground=PreferencesMuted,FontSize=12,Margin=new Thickness(0,6,0,4)});
   var picker=new ComboBox{MaxDropDownHeight=320};AutomationProperties.SetName(picker,name);panel.Children.Add(picker);return picker;
  }
  var position=Picker("Привязка");
  position.ItemsSource=new[]{"Свободное положение","Слева вверху","Сверху по центру","Справа вверху","Слева по центру","Справа по центру","Слева внизу","Снизу по центру","Справа внизу"};
  var details=new StackPanel();panel.Children.Add(details);
  var display=new ComboBox{MaxDropDownHeight=220,DisplayMemberPath="Name",SelectedValuePath="Id"};
  details.Children.Add(new TextBlock{Text="Монитор",Foreground=PreferencesMuted,FontSize=12,Margin=new Thickness(0,8,0,4)});
  AutomationProperties.SetName(display,"Монитор привязки");details.Children.Add(display);
  var gapText=new TextBlock{Foreground=PreferencesMuted,FontSize=12,Margin=new Thickness(0,8,0,0)};details.Children.Add(gapText);
  var gap=new Slider{Minimum=0,Maximum=128,TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Height=26};
  AutomationProperties.SetName(gap,"Отступ от края");details.Children.Add(gap);
  var hint=new TextBlock{FontSize=12,Foreground=PreferencesMuted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};details.Children.Add(hint);
  bool syncing=false;
  void Refresh(){
   syncing=true;
   try{
    position.SelectedIndex=(int)anchor;details.Visibility=anchor==AnchorPoint.Free?Visibility.Collapsed:Visibility.Visible;
    var screens=AnchorDisplays();bool missing=anchor!=AnchorPoint.Free&&!screens.Any(d=>d.Id==anchorMonitor);
    var options=screens.Select(d=>new {d.Id,d.Name}).ToList();
    if(missing)options.Add(new{Id=anchorMonitor,Name="Выбранный монитор отключён"});
    display.ItemsSource=options;display.SelectedValue=anchorMonitor;
    gap.Value=anchorGap;gapText.Text=$"Отступ от края · {anchorGap} px";
    hint.Text=missing?"Плеер временно на основном мониторе. При подключении выбранного монитора он вернётся обратно.":"Перетаскивание мышью вернёт свободное положение.";
   }finally{syncing=false;}
  }
  refreshAnchorSettings=Refresh;
  void Apply(){CancelRecoveryAnimation();UpdateRecoveryArrow();Save();}
  position.SelectionChanged+=(_,_)=>{
   if(syncing||position.SelectedIndex<0)return;
   if(anchor==AnchorPoint.Free){
    var current=Screen.FromHandle(new WindowInteropHelper(this).Handle);
    anchorMonitor=AnchorDisplays().FirstOrDefault(d=>d.Screen.DeviceName==current.DeviceName)?.Id??current.DeviceName;
   }
   anchor=(AnchorPoint)position.SelectedIndex;Refresh();Apply();
  };
  display.SelectionChanged+=(_,_)=>{if(syncing||display.SelectedValue is not string id)return;anchorMonitor=id;Refresh();Apply();};
  gap.ValueChanged+=(_,_)=>{if(syncing)return;anchorGap=(int)gap.Value;gapText.Text=$"Отступ от края · {anchorGap} px";Apply();};
  Refresh();
 }
}
