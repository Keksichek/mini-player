using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WidgetLauncher;

namespace MiniPlayer;

public sealed partial class Player
{
 // Run without showing a window or entering the normal media, settings and hotkey lifecycle.
 static int RenderComparisonFrame(){
  try{
   LaunchContext.Configure(Array.Empty<string>());
   if(LaunchContext.LauncherRoot==null)return 1;
   using var input=Console.OpenStandardInput();
   using var request=new MemoryStream();
   var chunk=new byte[8192];int read;
   while((read=input.Read(chunk))>0){
    if(request.Length+read>256*1024)return 1;
    request.Write(chunk,0,read);
   }
   var frame=JsonSerializer.Deserialize<TrackPreviewFrame>(request.ToArray());
   if(frame==null)return 1;
   var app=new PlayerApplication();
   var widget=new Player(false);
   try{
    widget.LoadClassicAppearance();
    widget.ApplyAppearance();
    widget.title.Text=frame.Title;widget.artist.Text=frame.Artist;
    widget.SetMediaPlaying(frame.Playing);widget.play.Content=frame.Playing?"Ⅱ":"▶";
    widget.seek.Maximum=frame.Duration;widget.seek.Value=frame.Position;widget.timeLabel.Text=frame.Time;
    widget.prev.IsEnabled=widget.next.IsEnabled=widget.play.IsEnabled=widget.seek.IsEnabled=true;
    widget.volumeSlider.IsEnabled=true;widget.volumeSync=true;widget.volumeSlider.Value=50;
    if(frame.Artwork is {Length:>0} bytes){
     using var buffer=new MemoryStream(bytes,false);
     var thumb=new PngBitmapDecoder(buffer,BitmapCreateOptions.None,BitmapCacheOption.OnDemand).Frames[0];
     if(thumb.PixelWidth<=160&&thumb.PixelHeight<=160)widget.cover.Source=new WriteableBitmap(thumb);
    }
    var body=(FrameworkElement)widget.Content;
    var size=new Size(widget.Width,widget.Height);body.Measure(size);body.Arrange(new Rect(size));body.UpdateLayout();
    var image=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
    image.Render(body);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
    using var png=new MemoryStream();encoder.Save(png);
    using var writer=new BinaryWriter(Console.OpenStandardOutput());
    writer.Write((int)png.Length);writer.Write(png.GetBuffer(),0,(int)png.Length);
   }finally{widget.tray.Dispose();}
   return 0;
  }catch(Exception ex)when(ex is not OutOfMemoryException){return 1;}
 }
}
