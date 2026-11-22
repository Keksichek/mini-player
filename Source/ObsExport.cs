using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MiniPlayer;
public sealed partial class Player
{
 bool obsValid;
 ImageSource? obsCover;string obsImage="";
 void ExportObs(){
  if(LaunchContext.LauncherRoot==null)return;
  try{var folder=LaunchContext.DataDirectory;if(!File.Exists(Path.Combine(folder,".obs-enabled")))return;
   if(!ReferenceEquals(obsCover,cover.Source)){obsCover=cover.Source;obsImage="";if(obsCover is BitmapSource bitmap){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);if(stream.Length<1000000)obsImage="data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());}}
   var state=new{Version=1,Title=session==null||!obsValid?"":title.Text,Subtitle=artist.Text,Image=obsImage,Progress=timelineDuration>0?Math.Clamp(seek.Value/timelineDuration,0,1):0,UpdatedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()};
   var path=Path.Combine(folder,"obs.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state));File.Move(path+".tmp",path,true);
  }catch{}
 }
}
