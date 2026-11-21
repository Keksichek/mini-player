using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WidgetLauncher;

namespace MiniPlayer;

public sealed partial class Player
{
 long lastTrackRead;
 BitmapSource? encodedCover;
 byte[]? workshopArtwork;

 Task ListenForWorkshopAsync(CancellationToken stop)=>
  TrackPreviewProtocol.ListenAsync(LaunchContext.InstanceName,
   token=>Dispatcher.InvokeAsync(CaptureWorkshopTrack,DispatcherPriority.Background,token).Task,stop);

 TrackPreviewFrame? CaptureWorkshopTrack(){
  if(closing||session==null||track.Length==0||lastTrackRead==0||Environment.TickCount64-lastTrackRead>5000){
   encodedCover=null;workshopArtwork=null;return null;
  }
  var bitmap=cover.Source as BitmapSource;
  if(!ReferenceEquals(encodedCover,bitmap)){
   encodedCover=bitmap;workshopArtwork=null;
   if(bitmap!=null){
    try{
     double scale=Math.Min(1,160d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight));
     BitmapSource thumb=scale<1?new TransformedBitmap(bitmap,new ScaleTransform(scale,scale)):bitmap;
     var encoder=new JpegBitmapEncoder{QualityLevel=80};
     encoder.Frames.Add(BitmapFrame.Create(thumb));
     using var stream=new MemoryStream();encoder.Save(stream);
     if(stream.Length<=48*1024)workshopArtwork=stream.ToArray();
    }catch(Exception ex)when(ex is not OutOfMemoryException){ /* Metadata can still be previewed. */ }
   }
  }
  static string Limit(string text)=>text.Length<=1024?text:text[..1024];
  return new(Limit(title.Text),Limit(artist.Text),timeLabel.Text,seek.Value,timelineDuration,mediaPlaying,workshopArtwork);
 }
}
