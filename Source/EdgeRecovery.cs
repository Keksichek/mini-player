using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Screen=System.Windows.Forms.Screen;

namespace MiniPlayer;

public sealed partial class Player
{
 [StructLayout(LayoutKind.Sequential)]
 struct NativeRect {
  public int Left,Top,Right,Bottom;
  public readonly Rect Bounds=>new(Left,Top,Right-Left,Bottom-Top);
  public readonly System.Drawing.Rectangle DrawingBounds=>System.Drawing.Rectangle.FromLTRB(Left,Top,Right,Bottom);
 }
 [DllImport("user32.dll",SetLastError=true)]
 static extern bool GetWindowRect(IntPtr window,out NativeRect rectangle);
 [DllImport("user32.dll",SetLastError=true)]
 static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
 const uint KeepSizeAndActivation=0x0001|0x0004|0x0010;
 const double RecoveryThickness=22,RecoveryLength=32;
 Window? recoveryWindow;
 Path? recoveryChevron;
 Button? recoveryButton,recoveryTab;
 Path? recoveryButtonChevron;
 bool RecoveryIsVisible=>recoveryButton?.Visibility==Visibility.Visible||recoveryWindow?.IsVisible==true;
 [StructLayout(LayoutKind.Sequential)]struct WindowPosition{public IntPtr Window,InsertAfter;public int X,Y,Width,Height;public uint Flags;}
 static Rect ContainInWorkArea(Rect window,Rect work)=>new(Math.Clamp(window.Left,work.Left,Math.Max(work.Left,work.Right-window.Width)),Math.Clamp(window.Top,work.Top,Math.Max(work.Top,work.Bottom-window.Height)),window.Width,window.Height);
 void InitializeScreenBoundsGuard(){HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(GuardScreenBounds);}
 IntPtr GuardScreenBounds(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(message!=0x0046||HubAllowsOffscreen||closing||!IsLoaded||lParam==IntPtr.Zero)return IntPtr.Zero;
  if(!GetWindowRect(window,out var current))return IntPtr.Zero;
  var position=Marshal.PtrToStructure<WindowPosition>(lParam);
  var requested=new Rect((position.Flags&2)!=0?current.Left:position.X,(position.Flags&2)!=0?current.Top:position.Y,(position.Flags&1)!=0?current.Bounds.Width:Math.Max(1,position.Width),(position.Flags&1)!=0?current.Bounds.Height:Math.Max(1,position.Height));
  var area=Screen.FromRectangle(new System.Drawing.Rectangle((int)requested.X,(int)requested.Y,(int)requested.Width,(int)requested.Height)).WorkingArea;
  var contained=ContainInWorkArea(requested,new Rect(area.X,area.Y,area.Width,area.Height));
  if(contained.TopLeft!=requested.TopLeft){position.X=(int)contained.X;position.Y=(int)contained.Y;position.Flags&=~2u;Marshal.StructureToPtr(position,lParam,false);}
  return IntPtr.Zero;
 }

 static bool FullyOnScreen(Rect player,Rect[] displays){
  double visible=0;
  foreach(var display in displays){
   var intersection=Rect.Intersect(player,display);
   if(!intersection.IsEmpty)visible+=intersection.Width*intersection.Height;
  }
  return visible>=player.Width*player.Height;
 }

 static (Point Position,double Angle) RecoveryPlacement(Rect player,Rect work,Size handle){
  double left=Math.Max(0,work.Left-player.Left),right=Math.Max(0,player.Right-work.Right);
  double top=Math.Max(0,work.Top-player.Top),bottom=Math.Max(0,player.Bottom-work.Bottom);
  if(Math.Max(left,right)>=Math.Max(top,bottom)){
   double y=Math.Clamp(player.Top+(player.Height-handle.Height)/2,work.Top+4,Math.Max(work.Top+4,work.Bottom-handle.Height-4));
   return (new Point(left>=right?work.Left:work.Right-handle.Width,y),left>=right?0:180);
  }
  double x=Math.Clamp(player.Left+(player.Width-handle.Width)/2,work.Left+4,Math.Max(work.Left+4,work.Right-handle.Width-4));
  return (new Point(x,top>=bottom?work.Top:work.Bottom-handle.Height),top>=bottom?90:270);
 }

 void HideRecoveryControls(){if(recoveryButton!=null)recoveryButton.Visibility=Visibility.Collapsed;recoveryWindow?.Hide();}

 Button CreateRecoveryButton(out Path chevron){
  chevron=new Path{Data=Geometry.Parse("M 1,1 L 6,6 L 1,11"),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=7,Height=11,Stretch=Stretch.Uniform,RenderTransformOrigin=new Point(.5,.5),IsHitTestVisible=false};
  var button=new Button{Content=chevron,Padding=new Thickness(0),Focusable=false,Cursor=System.Windows.Input.Cursors.Hand,ToolTip="Вернуть плеер на экран",BorderThickness=new Thickness(1),Visibility=Visibility.Collapsed,
   Template=(ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
 <Border Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{DynamicResource RecoveryCorners}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
 <ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource RecoveryAccent}"/></Trigger>
  <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value=".72"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
""")};
  System.Windows.Automation.AutomationProperties.SetName(button,"Вернуть плеер на экран");
  button.Click+=(_,_)=>RecoverFromEdge();return button;
 }

 void StyleRecoveryButton(Button button,Path chevron,double angle,bool edgeTab){
  string paper=appearance.BackgroundColor??(appearance.Theme switch{"Винил"=>"#C2A9B2","Закат"=>"#F8E8DF","Карманный LCD"=>"#C9C8B6",_=>StudioFace.For(appearance.Theme).Paper});
  bool pixel=appearance.Theme is "Minecraft" or "Терминал";
  button.Background=BrushOf(paper);button.BorderBrush=BrushOf(appearance.BorderColor??appearance.Accent);
  button.Resources["RecoveryAccent"]=BrushOf(appearance.Accent);
  double r=pixel?2:7;
  button.Resources["RecoveryCorners"]=!edgeTab?new CornerRadius(r):angle switch{0=>new CornerRadius(0,r,r,0),180=>new CornerRadius(r,0,0,r),90=>new CornerRadius(0,0,r,r),_=>new CornerRadius(r,r,0,0)};
  chevron.Stroke=AccentInk(paper);chevron.RenderTransform=new RotateTransform(angle);
 }

 bool ShowEmbeddedRecovery(Rect player,Rect work,double angle){
  if(deviceFace==null||!deviceFace.IsLoaded||deviceFace.ActualWidth<=0)return false;
  var origin=deviceFace.PointToScreen(new Point());var end=deviceFace.PointToScreen(new Point(deviceFace.Width,deviceFace.Height));
  double scaleX=(end.X-origin.X)/deviceFace.Width,scaleY=(end.Y-origin.Y)/deviceFace.Height;
  if(scaleX<=0||scaleY<=0)return false;
  var visible=Rect.Intersect(new Rect(origin,end),work);if(visible.IsEmpty)return false;
  bool vertical=angle is 0 or 180;
  double width=(vertical?RecoveryThickness:RecoveryLength)*scaleX,height=(vertical?RecoveryLength:RecoveryThickness)*scaleY;
  double gapX=5*scaleX,gapY=5*scaleY;
  if(visible.Width<width+2*gapX||visible.Height<height+2*gapY)return false;
  if(recoveryButton==null)recoveryButton=CreateRecoveryButton(out recoveryButtonChevron);
  if(recoveryButton.Parent!=deviceFace){if(recoveryButton.Parent is Canvas old)old.Children.Remove(recoveryButton);deviceFace.Children.Add(recoveryButton);Panel.SetZIndex(recoveryButton,1000);}
  double x=angle==0?visible.Left+gapX:angle==180?visible.Right-width-gapX:visible.Left+(visible.Width-width)/2;
  double y=angle==90?visible.Top+gapY:angle==270?visible.Bottom-height-gapY:visible.Top+(visible.Height-height)/2;
  // Keep the recovery affordance away from transport, text and volume controls.
  var occupied=new List<(Rect Bounds,double Weight)>();
  void Collect(DependencyObject node){
   if(node==recoveryButton)return;
   if(node is FrameworkElement element&&element.IsVisible&&element.ActualWidth>0&&element.ActualHeight>0&&node is System.Windows.Controls.Button or Slider or TextBlock or FaceMeter){
    var a=element.PointToScreen(new Point());var b=element.PointToScreen(new Point(element.ActualWidth,element.ActualHeight));
    occupied.Add((new Rect(a,b),node is System.Windows.Controls.Button or Slider?3:1));
    if(node is System.Windows.Controls.Button or Slider)return;
   }
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Collect(VisualTreeHelper.GetChild(node,i));
  }
  Collect(deviceFace);
  double Score(double u,double v){var bounds=new Rect(u,v,width,height);double cost=0;foreach(var item in occupied){var overlap=Rect.Intersect(bounds,item.Bounds);if(!overlap.IsEmpty)cost+=overlap.Width*overlap.Height*item.Weight;}return cost+.01*(Math.Abs(u-x)+Math.Abs(v-y));}
  double bestX=x,bestY=y,best=Score(x,y);
  double start=vertical?visible.Top+gapY:visible.Left+gapX,endAt=vertical?visible.Bottom-height-gapY:visible.Right-width-gapX;
  for(double at=start;at<=endAt+.01;at+=4*(vertical?scaleY:scaleX)){double u=vertical?x:at,v=vertical?at:y,cost=Score(u,v);if(cost<best){best=cost;bestX=u;bestY=v;}}
  x=bestX;y=bestY;
  var local=deviceFace.PointFromScreen(new Point(x,y));
  recoveryButton.Width=width/scaleX;recoveryButton.Height=height/scaleY;
  Canvas.SetLeft(recoveryButton,local.X);Canvas.SetTop(recoveryButton,local.Y);
  StyleRecoveryButton(recoveryButton,recoveryButtonChevron!,angle,false);
  recoveryButton.Visibility=Visibility.Visible;recoveryWindow?.Hide();return true;
 }

 void UpdateRecoveryArrow(){
  if(closing)return;
  if(!IsLoaded||!IsVisible||WindowState!=WindowState.Normal){CancelRecoveryAnimation();HideRecoveryControls();return;}
  if(recovering){HideRecoveryControls();return;}
  var window=new WindowInteropHelper(this).Handle;
  if(!GetWindowRect(window,out var player))return;
  if(!HubAllowsOffscreen){
   CancelRecoveryAnimation();HideRecoveryControls();var boundsArea=Screen.FromRectangle(player.DrawingBounds).WorkingArea;
   var contained=ContainInWorkArea(player.Bounds,new Rect(boundsArea.X,boundsArea.Y,boundsArea.Width,boundsArea.Height));
   if(contained.TopLeft!=player.Bounds.TopLeft)SetWindowPos(window,IntPtr.Zero,(int)contained.X,(int)contained.Y,0,0,KeepSizeAndActivation);
   return;
  }
  var displays=Screen.AllScreens.Select(s=>new Rect(s.Bounds.X,s.Bounds.Y,s.Bounds.Width,s.Bounds.Height)).ToArray();
  if(FullyOnScreen(player.Bounds,displays)){HideRecoveryControls();return;}

  var area=Screen.FromRectangle(player.DrawingBounds).WorkingArea;
  var work=new Rect(area.X,area.Y,area.Width,area.Height);
  var placement=RecoveryPlacement(player.Bounds,work,new Size(RecoveryThickness,RecoveryLength));
  if(ShowEmbeddedRecovery(player.Bounds,work,placement.Angle))return;
  if(recoveryButton!=null)recoveryButton.Visibility=Visibility.Collapsed;
  if(recoveryWindow==null){
   recoveryTab=CreateRecoveryButton(out recoveryChevron);recoveryTab.Visibility=Visibility.Visible;
   recoveryWindow=new Window{
    Title="Mini Player · Вернуть на экран",Owner=this,ShowInTaskbar=false,ShowActivated=false,
    WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,
    Background=Brushes.Transparent,Width=RecoveryThickness,Height=RecoveryLength,Left=Left,Top=Top,Content=recoveryTab
   };
  }
  bool vertical=placement.Angle is 0 or 180;
  recoveryWindow.Width=(vertical?RecoveryThickness:RecoveryLength)*appearance.Scale;recoveryWindow.Height=(vertical?RecoveryLength:RecoveryThickness)*appearance.Scale;
  recoveryWindow.Topmost=Topmost;recoveryWindow.Opacity=appearance.Alpha;
  StyleRecoveryButton(recoveryTab!,recoveryChevron!,placement.Angle,true);
  if(!recoveryWindow.IsVisible)recoveryWindow.Show();
  var handle=new WindowInteropHelper(recoveryWindow).Handle;
  if(!GetWindowRect(handle,out var arrow))return;
  placement=RecoveryPlacement(player.Bounds,work,arrow.Bounds.Size);
  // Both rectangles and SetWindowPos use the same native coordinates, including DPI scaling.
  SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(placement.Position.X),(int)Math.Round(placement.Position.Y),0,0,KeepSizeAndActivation);
 }

 void ReturnToScreen(){
  CancelRecoveryAnimation();HideRecoveryControls();
  shownByUser=true;Show();
  var window=new WindowInteropHelper(this).Handle;
  if(GetWindowRect(window,out var player)){
   var work=Screen.FromRectangle(player.DrawingBounds).WorkingArea;
   int x=Math.Clamp(player.Left,work.Left+8,Math.Max(work.Left+8,work.Right-(int)player.Bounds.Width-8));
   int y=Math.Clamp(player.Top,work.Top+8,Math.Max(work.Top+8,work.Bottom-(int)player.Bounds.Height-8));
   SetWindowPos(window,IntPtr.Zero,x,y,0,0,KeepSizeAndActivation);
  }
  UpdateRecoveryArrow();Save();Activate();
 }
}
