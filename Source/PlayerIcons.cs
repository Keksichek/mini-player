using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Windows.Media;

namespace MiniPlayer;

// Small vector controls share a consistent stroke in every player theme.
static class PlayerIcons
{
 internal static Path Pin(bool pinned,Brush ink)=>new(){
  Data=Geometry.Parse("M 5,1 H 12 L 11,4 V 7 L 14,10 H 9 L 8,16 L 7,10 H 2 L 5,7 V 4 Z"),
  Width=14,Height=14,Stretch=Stretch.Uniform,Stroke=ink,StrokeThickness=1.25,
  Fill=pinned?ink:Brushes.Transparent,RenderTransformOrigin=new Point(.5,.5),
  RenderTransform=new RotateTransform(pinned?0:-45),IsHitTestVisible=false
 };

 internal static FrameworkElement MovementLock(bool locked,Brush ink){
  var canvas=Surface();
  canvas.Children.Add(Line(locked?"M 5,8 V 5 A 4,4 0 0 1 13,5 V 8":"M 6,8 V 5 A 4,4 0 0 1 14,5",ink));
  var body=new Rectangle{Width=12,Height=9,RadiusX=2,RadiusY=2,Stroke=ink,StrokeThickness=1.5,Fill=locked?ink:Brushes.Transparent};
  Canvas.SetLeft(body,3);Canvas.SetTop(body,8);canvas.Children.Add(body);
  var key=new Ellipse{Width=2.5,Height=2.5,Fill=locked?Brushes.White:ink};
  Canvas.SetLeft(key,7.75);Canvas.SetTop(key,11);canvas.Children.Add(key);
  return canvas;
 }

 internal static FrameworkElement ClickThrough(bool enabled,Brush ink){
  var canvas=Surface();
  canvas.Children.Add(Line("M 3,2 L 15,9 L 9,10 L 6,16 Z",ink));
  if(enabled)canvas.Children.Add(Line("M 12,3 L 16,3 M 14,1 L 16,3 L 14,5",ink));
  return canvas;
 }

 internal static FrameworkElement ContextMenu(Brush ink){
  var canvas=Surface();
  var right=new Path{Data=Geometry.Parse("M 9,1 Q 15,1 15,7 H 9 Z"),Fill=ink,StrokeThickness=0};
  canvas.Children.Add(right);
  canvas.Children.Add(Line("M 9,1 C 5,1 3,3.5 3,7 V 11 C 3,15 5,17 9,17 C 13,17 15,15 15,11 V 7 C 15,3.5 13,1 9,1 Z M 3,7 H 15 M 9,1 V 7",ink));
  return canvas;
 }

 internal static FrameworkElement Eye(Brush ink){
  var canvas=Surface();
  canvas.Children.Add(Line("M 1,9 Q 9,-1 17,9 Q 9,19 1,9 Z",ink));
  var pupil=new Ellipse{Width=5,Height=5,Fill=ink};Canvas.SetLeft(pupil,6.5);Canvas.SetTop(pupil,6.5);canvas.Children.Add(pupil);
  return canvas;
 }

 internal static FrameworkElement Eye(bool hidden,Brush ink){
  var canvas=(Canvas)Eye(ink);if(hidden)canvas.Children.Add(Line("M 2,2 L 16,16",ink));return canvas;
 }

 internal static FrameworkElement Speaker(Brush ink){
  var canvas=Surface();canvas.Children.Add(new Path{Data=Geometry.Parse("M 1,7 H 4 L 8,3 V 15 L 4,11 H 1 Z"),Fill=ink});
  canvas.Children.Add(Line("M 11,6 Q 14,9 11,12 M 14,3 Q 19,9 14,15",ink));return canvas;
 }

 internal static FrameworkElement Repeat(MediaPlaybackAutoRepeatMode mode,Brush ink){
  var canvas=Surface();
  canvas.Children.Add(Line("M 3,8 V 6 Q 3,4 5,4 H 14 M 15,10 V 12 Q 15,14 13,14 H 4",ink));
  canvas.Children.Add(new Path{Data=Geometry.Parse("M 11,1 L 15,4 L 11,7 Z M 7,11 L 3,14 L 7,17 Z"),Fill=ink});
  if(mode==MediaPlaybackAutoRepeatMode.Track)canvas.Children.Add(Line("M 7.5,8 L 9.5,6.5 V 12",ink));
  if(mode==MediaPlaybackAutoRepeatMode.None){canvas.Opacity=.55;canvas.Children.Add(Line("M 2,2 L 16,16",ink));}
  return canvas;
 }

 static Canvas Surface()=>new(){Width=18,Height=18,LayoutTransform=new ScaleTransform(8d/9,8d/9),IsHitTestVisible=false};
 static Path Line(string geometry,Brush ink)=>new(){Data=Geometry.Parse(geometry),Stroke=ink,StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
}
