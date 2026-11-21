using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WidgetLauncher;

static class PresetPreviewChecks
{
 static int count;
 const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static byte[] Gif(){
  using var output=new MemoryStream();using var w=new BinaryWriter(output);w.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));w.Write((ushort)2);w.Write((ushort)2);w.Write((byte)0x81);w.Write((byte)0);w.Write((byte)0);
  w.Write(new byte[]{0,0,0,255,0,0,0,255,0,0,0,255});
  void Frame(int x,int y,int width,int height,int disposal,int pixel){
   w.Write(new byte[]{0x21,0xF9,4,(byte)(disposal*4+1),2,0,0,0,0x2C});w.Write((ushort)x);w.Write((ushort)y);w.Write((ushort)width);w.Write((ushort)height);w.Write((byte)0);w.Write((byte)2);
   var data=new List<byte>();int bits=0,used=0;void Code(int code){bits|=code<<used;used+=3;while(used>=8){data.Add((byte)bits);bits>>=8;used-=8;}}
   for(int i=0;i<width*height;i++){Code(4);Code(pixel);}Code(5);if(used>0)data.Add((byte)bits);w.Write((byte)data.Count);w.Write(data.ToArray());w.Write((byte)0);
  }
  Frame(0,0,2,2,1,1);Frame(0,0,1,1,2,3);Frame(1,1,1,1,3,2);Frame(1,0,1,1,1,3);w.Write((byte)0x3B);return output.ToArray();
 }
 static uint[] Pixels(BitmapSource image){var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);uint[] pixels=new uint[image.PixelWidth*image.PixelHeight];converted.CopyPixels(pixels,image.PixelWidth*4,0);return pixels;}
 static Rect ScreenBounds(PresetPreview preview){preview.UpdateLayout();return preview.Surface.TransformToAncestor(preview).TransformBounds(new Rect(0,0,preview.Surface.Width,preview.Surface.Height));}
 static Color ColorOf(Brush brush)=>((SolidColorBrush)brush).Color;
 [STAThread]static int Main(string[] args){
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window? window=null;
  try{
   string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string source=Path.Combine(root,"source.gif");File.WriteAllBytes(source,Gif());var store=new PresetStore(root);
   var preset=PresetThemes.Create("Лаванда");preset.MediaPath=store.ImportMedia(source);
   using var preview=new PresetPreview(root){Width=600,Height=300};preview.Update(preset);Check(preview.MediaError.Length==0&&preview.AnimationFrameCount==4,"GIF decodes all four frames");
   var frames=(IList)typeof(PresetSurface).GetField("frames",Hidden)!.GetValue(preview.Surface)!;
   BitmapSource At(int index)=>(BitmapSource)frames[index]!.GetType().GetField("Item1")!.GetValue(frames[index])!;
   Check(Pixels(At(0)).All(x=>x==0xFFFF0000),"GIF first full frame is composed correctly");
   using(var still=new PresetSurface(root,firstFrameOnly:true)){
    still.Configure(preset);
    Check(still.MediaError.Length==0&&still.AnimationFrameCount==1&&!still.AnimationRunning,"A gallery thumbnail decodes just the first GIF frame");
    var first=((IList)typeof(PresetSurface).GetField("frames",Hidden)!.GetValue(still)!)[0]!;
    var bitmap=(BitmapSource)first.GetType().GetField("Item1")!.GetValue(first)!;
    Check(Pixels(bitmap).SequenceEqual(Pixels(At(0))),"The gallery uses exactly the first composite GIF frame shown on the player");
   }
   Check(Pixels(At(1)).SequenceEqual(new uint[]{0xFF0000FF,0xFFFF0000,0xFFFF0000,0xFFFF0000}),"GIF partial frames retain the previous background");
   Check(Pixels(At(2)).SequenceEqual(new uint[]{0,0xFFFF0000,0xFFFF0000,0xFF00FF00}),"GIF disposal two clears only the previous frame region");
   Check(Pixels(At(3)).SequenceEqual(new uint[]{0,0xFF0000FF,0xFFFF0000,0xFFFF0000}),"GIF disposal three restores the previous canvas");
   Check(!preview.AnimationRunning,"Detached previews do not keep animation timers alive");
   window=new Window{Width=640,Height=350,Content=preview,ShowInTaskbar=false,ShowActivated=false,Opacity=0};window.Show();Pump();Check(preview.AnimationRunning,"A visible preview starts its animation");
   int before=preview.AnimationFrameIndex;typeof(PresetSurface).GetMethod("AdvanceAnimation",Hidden)!.Invoke(preview.Surface,null);Check(preview.AnimationFrameIndex!=(before),"Animation advances the displayed frame");
   window.Content=null;Pump();Check(!preview.AnimationRunning,"Unloading a preview stops its animation");window.Content=preview;Pump();Check(preview.AnimationRunning,"Reloading a preview resumes its animation");
   foreach(string shape in WidgetPreset.Shapes){preset.Shape=shape;preset.Width=240;preset.Height=140;preset.CoverSize=140;preset.FontSize=32;preview.Update(preset);preview.Measure(new Size(600,300));preview.Arrange(new Rect(0,0,600,300));preview.UpdateLayout();var bitmap=new RenderTargetBitmap(600,300,96,96,PixelFormats.Pbgra32);bitmap.Render(preview);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(root,"preview-"+shape+".png"));png.Save(file);Check(preview.ActualWidth==600,"Shape renders at constrained sizes: "+shape);}
   string media=store.ResolveMedia(preset.MediaPath);File.Delete(media);preset.MediaPath="Media/"+new string('0',64)+".gif";preview.Update(preset);Check(preview.MediaError.Length>0&&!preview.AnimationRunning,"Missing media produces a readable preview error and stops animation");
   preset.MediaPath="";preview.Update(preset);Check(preview.MediaError.Length==0,"Removing failed media clears the error");
   const int side=4096;
   string maxPng=Path.Combine(root,"largest-supported.png");
   var encoder=new PngBitmapEncoder();
   encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(side,side,96,96,PixelFormats.BlackWhite,null,new byte[side/8*side],side/8)));
   using(var output=File.Create(maxPng))encoder.Save(output);
   preset.MediaPath=store.ImportMedia(maxPng);preview.Update(preset);
   Check(preview.MediaError.Length==0&&preview.AnimationFrameCount==1,"A supported 4096 by 4096 PNG both imports and renders");
   preset=new WidgetPreset{Width=280,Height=180};preview.Update(preset);Pump();double narrowWidth=ScreenBounds(preview).Width;
   Check(preview.DisplayScale==1&&!preview.FitToView&&Math.Abs(narrowWidth-280)<.01,"Preview starts at actual size instead of silently fitting every widget");
   preset.Resize(640,180);preview.Update(preset);Pump();var viewport=(ScrollViewer)typeof(PresetPreview).GetField("viewport",Hidden)!.GetValue(preview)!;
   Check(preview.DisplayScale==1&&Math.Abs(ScreenBounds(preview).Width/narrowWidth-640d/280)<.001,"Increasing widget width visibly increases the preview at the same scale");
   Check(viewport.ScrollableWidth>0&&viewport.ComputedHorizontalScrollBarVisibility==Visibility.Visible,"A widget wider than the preview can be scrolled horizontally");
   viewport.ScrollToHorizontalOffset(viewport.ScrollableWidth);Pump();Check(viewport.HorizontalOffset>0,"Horizontal scrolling reaches the overflowing part of the widget");
   int scaleEvents=0;preview.DisplayScaleChanged+=(_,_)=>scaleEvents++;preview.Zoom=.5;Pump();
   Check(Math.Abs(ScreenBounds(preview).Width-320)<.01&&preview.DisplayScale==.5&&!preview.FitToView,"Explicit 50 percent zoom scales the whole widget without changing its dimensions");
   preset.Resize(400,200);preview.Update(preset);Pump();Check(preview.DisplayScale==.5&&Math.Abs(ScreenBounds(preview).Width-200)<.01,"Editing widget dimensions preserves the selected preview zoom");
   preview.Zoom=1;preset.Resize(640,420);preview.Update(preset);preview.FitToView=true;Pump();double fitScale=preview.DisplayScale;
   Check(fitScale<1&&ScreenBounds(preview).Height<=preview.ActualHeight&&viewport.ScrollableWidth<1&&viewport.ScrollableHeight<1,"Explicit fit shows the whole oversized widget without overflow");
   preset.Resize(400,200);preview.Update(preset);Pump();Check(preview.FitToView&&preview.DisplayScale>fitScale,"Only explicit fit mode changes scale to follow widget size");
   preview.Zoom=1;Pump();Check(!preview.FitToView&&preview.DisplayScale==1&&scaleEvents>=4,"Actual-size zoom exits fit mode and announces scale changes");
   preset=new WidgetPreset{Width=350,Height=220};preset.Normalize();preset.SetShape("circle");preview.Update(preset);
   Check(preview.Surface.Width==preview.Surface.Height&&preview.Surface.ShapeGeometry is EllipseGeometry,"Circle renders with equal axes immediately");
   preset.SetShape("ellipse");preview.Update(preset);Check(preview.Surface.Width==350&&preview.Surface.Height==220&&preview.Surface.ShapeGeometry is EllipseGeometry,"Switching from circle to oval restores a visibly non-square aspect ratio");
   preset=new WidgetPreset();preset.Normalize();preset.FontSize=12;preview.Update(preset);
   var beforeElements=preview.Surface.Elements.ToDictionary(x=>x.Key,x=>new Rect(Canvas.GetLeft(x.Value),Canvas.GetTop(x.Value),x.Value.Width,x.Value.Height));
   preset.FontSize=60;preview.Update(preset);
   Check(preview.Surface.Elements.All(x=>beforeElements[x.Key]==new Rect(Canvas.GetLeft(x.Value),Canvas.GetTop(x.Value),x.Value.Width,x.Value.Height)),"Changing text from 12 to 60px keeps every element's size and position unchanged");
   Check(((TextBlock)preview.Surface.Elements["title"]).FontSize==60,"The title renders the entered 60px font directly without scaling other controls");
   preset.FontSize=24;preset.FontFamily="Segoe UI";preset.FontWeight=600;preset.Opacity=.8;foreach(string id in new[]{"title","play","progress","time"})preset.LockElement(id,true);preview.Update(preset);
   var lockedTitle=(TextBlock)preview.Surface.Elements["title"];var lockedPlay=(Button)preview.Surface.Elements["play"];var lockedProgress=(Slider)preview.Surface.Elements["progress"];
   var titleBounds=new Rect(Canvas.GetLeft(lockedTitle),Canvas.GetTop(lockedTitle),lockedTitle.Width,lockedTitle.Height);var titleColor=ColorOf(lockedTitle.Foreground);var playColor=ColorOf(lockedPlay.Background);var progressColor=ColorOf((Brush)lockedProgress.Resources["PresetAccent"]);
   preset.FontSize=48;preset.FontFamily="Consolas";preset.FontWeight=300;preset.TextColor="#EE2233";preset.AccentColor="#10CCAA";preset.BackgroundColor="#001122";preset.Opacity=.25;preset.ShowTime=false;preset.Resize(600,360);preview.Update(preset);
   lockedTitle=(TextBlock)preview.Surface.Elements["title"];lockedPlay=(Button)preview.Surface.Elements["play"];lockedProgress=(Slider)preview.Surface.Elements["progress"];
   Check(lockedTitle.FontSize==24&&lockedTitle.FontFamily.Source=="Segoe UI"&&lockedTitle.FontWeight.ToOpenTypeWeight()==600&&ColorOf(lockedTitle.Foreground)==titleColor,"Locked text retains its saved font, weight, size and color after global edits");
   Check(titleBounds==new Rect(Canvas.GetLeft(lockedTitle),Canvas.GetTop(lockedTitle),lockedTitle.Width,lockedTitle.Height),"Locked geometry stays fixed when the widget grows");
   Check(lockedTitle.Opacity==.8&&preview.Surface.Opacity==1&&preview.Surface.Elements["artist"].Opacity==.25,"Locked opacity remains independent while free elements follow global opacity");
   Check(ColorOf(lockedPlay.Background)==playColor&&ColorOf((Brush)lockedProgress.Resources["PresetAccent"])==progressColor,"Locked playback and timeline controls retain their own accent colors");
   preview.Surface.SetButtonIcon("play","pause");Check(ColorOf(((System.Windows.Shapes.Path)lockedPlay.Content).Fill)==titleColor,"Playback state changes keep the locked icon color");
   Check(preview.Surface.Elements["time"].Visibility==Visibility.Visible&&((TextBlock)preview.Surface.Elements["artist"]).FontSize==48*.72,"Locked visibility stays fixed while unlocked text follows new font settings");
   preview.Surface.SetButtonIcon("play","off");
   using(var live=new PresetSurface(root)){live.Configure(preset);Check(live.Width==preview.Surface.Width&&live.Height==preview.Surface.Height&&live.Elements.All(x=>x.Value.Width==preview.Surface.Elements[x.Key].Width&&x.Value.Height==preview.Surface.Elements[x.Key].Height),"Live and preview surfaces use exactly the same dimensions");
    var rendered=new List<byte[]>();foreach(var surface in new[]{live,preview.Surface}){surface.Measure(new Size(surface.Width,surface.Height));surface.Arrange(new Rect(0,0,surface.Width,surface.Height));surface.UpdateLayout();var bitmap=new RenderTargetBitmap((int)surface.Width,(int)surface.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var bytes=new byte[(int)surface.Width*(int)surface.Height*4];bitmap.CopyPixels(bytes,(int)surface.Width*4,0);rendered.Add(bytes);}Check(rendered[0].SequenceEqual(rendered[1]),"Shared preview and live renderer produce identical pixels with the same data");}
   var overlap=new WidgetPreset{Shape="rectangle"};overlap.Normalize();
   foreach(var element in overlap.Elements.Values){element.X=40;element.Y=30;element.Visible=false;}
   overlap.Elements["title"].Visible=overlap.Elements["cover"].Visible=true;
   var hit=new Point(46,36);
   preview.Update(overlap);
   Check(preview.HitElement(hit)=="title","Selecting overlapping elements respects their historical z-order");
   overlap.MoveLayer("cover",1);preview.Update(overlap);
   Check(preview.HitElement(hit)=="cover","Preview selection targets the visible element raised above the title");
   using(var live=new PresetSurface(root)){
    live.Configure(overlap);
    foreach(var surface in new[]{preview.Surface,live}){
     int front=surface.Children.IndexOf(surface.Elements["cover"]),back=surface.Children.IndexOf(surface.Elements["title"]);
     Check(front>back&&back>0&&front<surface.Children.Count-1,"Preview and live stack keep the ordered elements between the background and the frame");
    }
   }
   overlap.Elements["cover"].Visible=false;preview.Update(overlap);
   Check(preview.HitElement(hit)=="title","Hidden cover does not intercept preview selection");
   preview.Dispose();Check(!preview.AnimationRunning&&preview.AnimationFrameCount==0,"Disposal releases decoded animation frames");
   Console.WriteLine("TOTAL "+count+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{window?.Close();app.Shutdown();}
 }
}
