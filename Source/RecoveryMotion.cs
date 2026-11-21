using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Screen=System.Windows.Forms.Screen;

namespace MiniPlayer;

public sealed partial class Player
{
 readonly DispatcherTimer recoveryTimer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
 readonly Stopwatch recoveryTime=new();
 bool recovering,recoveryTimerInitialized;
 Point recoveryStart,recoveryDestination;
 Border? recoveryAnimatedBody;
 Transform? recoveryOriginalTransform;
 Point recoveryOriginalOrigin;
 const double RecoveryMilliseconds=240;

 void CancelRecoveryAnimation(){
  recoveryTimer.Stop();recoveryTime.Reset();recovering=false;
  if(recoveryAnimatedBody!=null){
   recoveryAnimatedBody.BeginAnimation(OpacityProperty,null);
   recoveryAnimatedBody.RenderTransform=recoveryOriginalTransform??Transform.Identity;
   recoveryAnimatedBody.RenderTransformOrigin=recoveryOriginalOrigin;
   recoveryAnimatedBody=null;recoveryOriginalTransform=null;
  }
 }

 void RecoverFromEdge(){
  if(closing||recovering)return;
  if(!SystemParameters.ClientAreaAnimation){ReturnToScreen();return;}
  shownByUser=true;Show();
  var window=new WindowInteropHelper(this).Handle;
  if(!GetWindowRect(window,out var player)){ReturnToScreen();return;}
  var work=Screen.FromRectangle(player.DrawingBounds).WorkingArea;
  recoveryStart=player.Bounds.TopLeft;
  recoveryDestination=new Point(Math.Clamp(player.Left,work.Left+8,Math.Max(work.Left+8,work.Right-player.Bounds.Width-8)),Math.Clamp(player.Top,work.Top+8,Math.Max(work.Top+8,work.Bottom-player.Bounds.Height-8)));
  if(recoveryStart==recoveryDestination){ReturnToScreen();return;}
  HideRecoveryControls();recovering=true;
  if(Content is Border body){
   recoveryAnimatedBody=body;recoveryOriginalTransform=body.RenderTransform;recoveryOriginalOrigin=body.RenderTransformOrigin;
   var scale=new ScaleTransform(.985,.985);body.RenderTransformOrigin=new Point(.5,.5);body.RenderTransform=scale;
   var settle=new DoubleAnimation(.985,1,TimeSpan.FromMilliseconds(RecoveryMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,settle);scale.BeginAnimation(ScaleTransform.ScaleYProperty,settle);
   body.BeginAnimation(OpacityProperty,new DoubleAnimation(body.Opacity*.96,body.Opacity,TimeSpan.FromMilliseconds(180)));
  }
  if(!recoveryTimerInitialized){recoveryTimer.Tick+=(_,_)=>AdvanceRecoveryAnimation();recoveryTimerInitialized=true;}
  recoveryTime.Restart();recoveryTimer.Start();
 }

 void AdvanceRecoveryAnimation(){
  if(closing||!IsVisible){CancelRecoveryAnimation();HideRecoveryControls();return;}
  // A changed hub setting takes effect even if it arrives during the return.
  if(!HubAllowsOffscreen){CancelRecoveryAnimation();UpdateRecoveryArrow();Save();return;}
  double progress=Math.Clamp(recoveryTime.Elapsed.TotalMilliseconds/RecoveryMilliseconds,0,1),ease=1-Math.Pow(1-progress,3);
  var handle=new WindowInteropHelper(this).Handle;
  SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(recoveryStart.X+(recoveryDestination.X-recoveryStart.X)*ease),(int)Math.Round(recoveryStart.Y+(recoveryDestination.Y-recoveryStart.Y)*ease),0,0,KeepSizeAndActivation);
  if(progress>=1){CancelRecoveryAnimation();UpdateRecoveryArrow();Save();}
 }
}
