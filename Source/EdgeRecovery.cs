using System;
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
 const double RecoveryThickness=24,RecoveryLength=36;
 Window? recoveryWindow;
 Path? recoveryChevron;

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
   return (new Point(left>=right?work.Left+4:work.Right-handle.Width-4,y),left>=right?0:180);
  }
  double x=Math.Clamp(player.Left+(player.Width-handle.Width)/2,work.Left+4,Math.Max(work.Left+4,work.Right-handle.Width-4));
  return (new Point(x,top>=bottom?work.Top+4:work.Bottom-handle.Height-4),top>=bottom?90:270);
 }

 void UpdateRecoveryArrow(){
  if(closing)return;
  if(!IsLoaded||!IsVisible||WindowState!=WindowState.Normal){recoveryWindow?.Hide();return;}
  var window=new WindowInteropHelper(this).Handle;
  if(!GetWindowRect(window,out var player))return;
  var displays=Screen.AllScreens.Select(s=>new Rect(s.Bounds.X,s.Bounds.Y,s.Bounds.Width,s.Bounds.Height)).ToArray();
  if(FullyOnScreen(player.Bounds,displays)){recoveryWindow?.Hide();return;}

  var area=Screen.FromRectangle(player.DrawingBounds).WorkingArea;
  var work=new Rect(area.X,area.Y,area.Width,area.Height);
  if(recoveryWindow==null){
   recoveryChevron=new Path{
    Data=Geometry.Parse("M 1,1 L 6,6 L 1,11"),Stroke=BrushOf("#F3F1F6"),StrokeThickness=1.5,
    StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,
    Width=8,Height=12,Stretch=Stretch.Uniform,RenderTransformOrigin=new Point(.5,.5)
   };
   var button=new Button{
    Content=recoveryChevron,Background=BrushOf("#E62B2B32"),
    Padding=new Thickness(0),Focusable=false,Cursor=System.Windows.Input.Cursors.Hand,
    Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Name='Frame' Background='{TemplateBinding Background}' CornerRadius='12'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='#F044444D'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Background' Value='#FF222228'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")
   };
   button.ToolTip="Вернуть плеер на экран";
   System.Windows.Automation.AutomationProperties.SetName(button,"Вернуть плеер на экран");
   button.Click+=(_,_)=>ReturnToScreen();
   recoveryWindow=new Window{
    Title="Mini Player · Вернуть на экран",Owner=this,ShowInTaskbar=false,ShowActivated=false,Topmost=true,
    WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,
    Background=Brushes.Transparent,Width=RecoveryThickness,Height=RecoveryLength,Left=Left,Top=Top,Content=button
   };
  }
  var placement=RecoveryPlacement(player.Bounds,work,new Size(RecoveryThickness,RecoveryLength));
  bool vertical=placement.Angle is 0 or 180;
  recoveryWindow.Width=vertical?RecoveryThickness:RecoveryLength;recoveryWindow.Height=vertical?RecoveryLength:RecoveryThickness;
  recoveryChevron!.RenderTransform=new RotateTransform(placement.Angle);
  if(!recoveryWindow.IsVisible)recoveryWindow.Show();
  var handle=new WindowInteropHelper(recoveryWindow).Handle;
  if(!GetWindowRect(handle,out var arrow))return;
  placement=RecoveryPlacement(player.Bounds,work,arrow.Bounds.Size);
  // Both rectangles and SetWindowPos use the same native coordinates, including DPI scaling.
  SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(placement.Position.X),(int)Math.Round(placement.Position.Y),0,0,KeepSizeAndActivation);
 }

 void ReturnToScreen(){
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
