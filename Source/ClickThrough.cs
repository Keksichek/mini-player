using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace MiniPlayer;

public sealed partial class Player
{
 const int ExtendedStyle=-20, TransparentToMouse=0x20;
 bool clickThrough;
 Button? clickThroughButton,faceClickThrough;
 Forms.ToolStripMenuItem? restoreMouseItem;
 const string EnablePassThrough="Включить клики насквозь";
 const string DisablePassThrough="Выключить клики насквозь";
 const string PassThroughHelp="Чтобы вернуть управление, дважды нажмите на значок ×2 на плеере. Это также можно сделать через значок плеера в трее.";
 Window? mouseRestoreWindow;
 string? mouseRestoreAccent;

 [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)]
 static extern int ReadExtendedStyle(IntPtr hwnd,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)]
 static extern int WriteExtendedStyle(IntPtr hwnd,int index,int style);

 void ToggleClickThrough()=>SetClickThrough(!clickThrough);
 void SetClickThrough(bool enabled,bool persist=true){
  var handle=new WindowInteropHelper(this).Handle;
  if(handle!=IntPtr.Zero){
   int style=ReadExtendedStyle(handle,ExtendedStyle);
   if(style==0&&Marshal.GetLastPInvokeError()!=0){status.Text="Не удалось прочитать режим окна.";return;}
   // AllowsTransparency already gives the player a layered HWND. Only change its mouse transparency.
   int next=enabled?style|TransparentToMouse:style&~TransparentToMouse;
   if(next!=style&&WriteExtendedStyle(handle,ExtendedStyle,next)==0&&Marshal.GetLastPInvokeError()!=0){
    status.Text="Windows не удалось изменить режим кликов.";return;
   }
  }
  clickThrough=enabled;
  UpdateClickThroughButtons();
  if(restoreMouseItem!=null)restoreMouseItem.Visible=enabled;
  if(IsLoaded)UpdateRecoveryArrow();
  if(persist)Save();
 }
 void AddTrayRestore(Forms.ContextMenuStrip menu){
  restoreMouseItem=new Forms.ToolStripMenuItem("Выключить клики насквозь"){Visible=clickThrough};
  restoreMouseItem.Click+=(_,_)=>Dispatcher.Invoke(()=>SetClickThrough(false));
  menu.Items.Add(restoreMouseItem);
 }

 // The player HWND passes input to the app below. Only this small owned window
 // accepts clicks, so no system-wide mouse listener is needed.
 Window CreateMouseRestoreWindow(){
  var button=new Button{Content="×2",Focusable=false,Cursor=System.Windows.Input.Cursors.Hand,
   Padding=new Thickness(0),FontSize=11,FontWeight=FontWeights.Bold,ToolTip="Двойной щелчок — вернуть управление плеером",
   Template=(ControlTemplate)XamlReader.Parse("""
   <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding Foreground}" BorderThickness="1" CornerRadius="7">
     <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Border>
   </ControlTemplate>
   """)};
  AutomationProperties.SetName(button,"Вернуть управление мышью");
  AutomationProperties.SetHelpText(button,PassThroughHelp);
  // The accessibility Invoke action also works for users operating the badge without a mouse.
  button.Click+=(_,_)=>SetClickThrough(false);
  var badge=new Window{Title="Mini Player · Вернуть управление мышью",Owner=this,ShowInTaskbar=false,
   ShowActivated=false,Focusable=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
   AllowsTransparency=true,Background=Brushes.Transparent,Width=24,Height=21,Left=Left,Top=Top,
   FontFamily=FontFamily,Content=button};
  badge.SourceInitialized+=(_,_)=>{
   var hwnd=new WindowInteropHelper(badge).Handle;
   const int noActivate=0x08000000;
   WriteExtendedStyle(hwnd,ExtendedStyle,ReadExtendedStyle(hwnd,ExtendedStyle)|noActivate);
   HwndSource.FromHwnd(hwnd)?.AddHook(MouseRestoreInput);
  };
  badge.IsVisibleChanged+=(_,_)=>{firstBadgeClick=false;badgePressed=false;};
  return badge;
 }

 bool firstBadgeClick,badgePressed,secondBadgeClick;
 long badgeClickTime;
 System.Drawing.Point badgePressPoint;
 IntPtr MouseRestoreInput(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  const int leftDown=0x0201,leftUp=0x0202,doubleClick=0x0203;
  if(!clickThrough||message is not (leftDown or leftUp or doubleClick))return IntPtr.Zero;
  handled=true;
  var coords=lParam.ToInt64();
  var point=new System.Drawing.Point(unchecked((short)coords),unchecked((short)(coords>>16)));
  var area=Forms.SystemInformation.DoubleClickSize;
  bool near=Math.Abs(point.X-badgePressPoint.X)<=area.Width/2 && Math.Abs(point.Y-badgePressPoint.Y)<=area.Height/2;
  if(message!=leftUp){
   long now=Environment.TickCount64;
   secondBadgeClick=firstBadgeClick && near && now-badgeClickTime<=Forms.SystemInformation.DoubleClickTime;
   badgePressPoint=point;badgeClickTime=now;badgePressed=true;firstBadgeClick=false;
  }else if(badgePressed){
   badgePressed=false;firstBadgeClick=near&&!secondBadgeClick;
   if(near&&secondBadgeClick)SetClickThrough(false);
  }
  return IntPtr.Zero;
 }

 void UpdateMouseRestoreWindow(){
  if(!clickThrough||closing||!IsLoaded||!IsVisible||WindowState!=WindowState.Normal){
   if(mouseRestoreWindow?.IsVisible==true)mouseRestoreWindow.Hide();
   return;
  }
  if(!GetWindowRect(new WindowInteropHelper(this).Handle,out var player))return;
  var anchor=customSurface?.Elements["clickthrough"]??faceClickThrough??clickThroughButton;
  Point center=new(player.Left+player.Bounds.Width/2,player.Top+player.Bounds.Height*.3);
  if(anchor is {IsVisible:true,ActualWidth:>0,ActualHeight:>0}){
   var local=new Point(anchor.ActualWidth/2,anchor.ActualHeight/2);
   if(customSurface==null||customSurface.ShapeGeometry.FillContains(anchor.TranslatePoint(local,customSurface)))
    center=anchor.PointToScreen(local);
  }
  var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X,(int)center.Y));
  var area=new Rect(screen.WorkingArea.X,screen.WorkingArea.Y,screen.WorkingArea.Width,screen.WorkingArea.Height);
  var visible=Rect.Intersect(player.Bounds,area);
  if(visible.IsEmpty)visible=area;
  var badge=mouseRestoreWindow??=CreateMouseRestoreWindow();
  var accent=customPreset?.AccentColor??appearance.Accent;
  if(mouseRestoreAccent!=accent){
   var button=(Button)badge.Content;button.Background=BrushOf(accent);button.Foreground=AccentInk(accent);mouseRestoreAccent=accent;
  }
  if(badge.Topmost!=Topmost)badge.Topmost=Topmost;
  var hwnd=new WindowInteropHelper(badge).EnsureHandle();
  if(GetWindowRect(hwnd,out var bounds)){
   double width=bounds.Bounds.Width,height=bounds.Bounds.Height;
   // A sliver of the widget can be narrower than the badge. Keep the whole target on screen.
   double left=Math.Clamp(center.X-width/2,visible.Left,Math.Max(visible.Left,visible.Right-width));
   double top=Math.Clamp(center.Y-height/2,visible.Top,Math.Max(visible.Top,visible.Bottom-height));
   int x=(int)Math.Round(Math.Clamp(left,area.Left,Math.Max(area.Left,area.Right-width)));
   int y=(int)Math.Round(Math.Clamp(top,area.Top,Math.Max(area.Top,area.Bottom-height)));
   if(x!=bounds.Left||y!=bounds.Top)SetWindowPos(hwnd,IntPtr.Zero,x,y,0,0,KeepSizeAndActivation);
  }
  if(!badge.IsVisible)badge.Show();
 }
 void UpdateClickThroughButtons(){
  string action=clickThrough?DisablePassThrough:EnablePassThrough;
  foreach(var button in new[]{clickThroughButton,faceClickThrough}){
   if(button==null)continue;
   Brush ink=clickThrough?AccentInk(appearance.Accent):button.Foreground;
   button.Content=PlayerIcons.ClickThrough(clickThrough,ink);
   button.Background=clickThrough?BrushOf(appearance.Accent):button==faceClickThrough?UtilitySurface():Brushes.Transparent;
   button.ToolTip=action+". "+PassThroughHelp;
   AutomationProperties.SetName(button,action);
  }
  if(customSurface!=null){
   customSurface.SetButtonIcon("clickthrough",clickThrough?"on":"off");
   var button=customSurface.Elements["clickthrough"];
   button.ToolTip=action+". "+PassThroughHelp;
   AutomationProperties.SetName(button,action);
  }
 }
}
