using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WidgetLauncher;

namespace MiniPlayer;

public sealed partial class Player
{
 WidgetPreset? customPreset;
 PresetSurface? customSurface;
 bool customPresetSuspended;
 long customPresetStamp=-1,customPresetLength=-1;
 string customPresetHash="";
 string? CustomPresetRoot=>LaunchContext.LauncherRoot;

 bool RefreshCustomPreset(bool force=false){
  string? root=CustomPresetRoot;if(root==null)return true;
  if(!force&&customizeWindow!=null)return true;
  string hash="";
  try{
   var store=new PresetStore(root);string path=store.ActivePath;
   bool exists=File.Exists(path);var info=exists?new FileInfo(path):null;
   long stamp=info?.LastWriteTimeUtc.Ticks??0,length=info?.Length??0;
   if(!force&&stamp==customPresetStamp&&length==customPresetLength)return true;
   if(force&&customizeWindow!=null)customizeWindow.Close();
   if(!exists){
    customPresetStamp=0;customPresetLength=0;customPresetHash="";
    if(customPreset!=null){customPreset=null;customPresetSuspended=false;ApplyAppearance();}
    return true;
   }
   if(length>65536)throw new IOException("Файл пресета слишком большой.");
   byte[] bytes=File.ReadAllBytes(path);hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
   var candidate=JsonSerializer.Deserialize<WidgetPreset>(bytes,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new IOException("Пресет пуст.");candidate.Normalize();
   InstallCustomPreset(candidate);
   customPresetStamp=stamp;customPresetLength=length;customPresetHash=hash;
   WritePresetAcknowledgement(root,hash,true,"");return true;
  }catch(Exception ex){
   status.Text="Не удалось применить пресет: "+ex.Message;
   try{WritePresetAcknowledgement(root,hash,false,ex.Message);}catch{}
   return false;
  }
 }
 static void WritePresetAcknowledgement(string root,string hash,bool success,string error){
  string folder=Path.Combine(root,"Data","mini-player");LaunchContext.RejectLinks(folder);Directory.CreateDirectory(folder);
  string path=Path.Combine(folder,"preset-applied.json"),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Недопустимый файл подтверждения.");
  try{File.WriteAllText(temporary,JsonSerializer.Serialize(new{Sha256=hash,Success=success,Error=error}));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 void DisposeCustomSurface(){customSurface?.Dispose();customSurface=null;}
 void ApplySavedAppearance(PresetStore store,WidgetPreset preset){
  using var applyLock=store.AcquireApplyLock();
  InstallAndStorePreset(store,preset);
 }
 void InstallAndStorePreset(PresetStore store,WidgetPreset preset){
  var previous=customPreset;var previousSurface=customSurface;bool suspended=customPresetSuspended;
  try{
   InstallCustomPreset(preset);
   store.Apply(preset);
  }catch{
   // A failed decode leaves the old surface in place. If a later step failed,
   // restore the previous visible appearance, including unsaved classic edits.
   if(customSurface!=previousSurface){
    customPreset=previous;customPresetSuspended=suspended;ApplyAppearance();
   }
   throw;
  }
  customPresetStamp=-1;
  try{
   var file=new FileInfo(store.ActivePath);
   customPresetLength=file.Length;customPresetStamp=file.LastWriteTimeUtc.Ticks;
  }catch(IOException){}catch(UnauthorizedAccessException){}
 }
 void InstallCustomPreset(WidgetPreset preset){
  if(CustomPresetRoot is not string root||Content is not Border body||body.Child is not StackPanel stack)return;
  var surface=new PresetSurface(root);
  try{
   surface.Configure(preset);
   if(surface.MediaError.Length>0)throw new IOException(surface.MediaError);
   void Bind(FrameworkElement target,DependencyProperty property,object source,string sourceProperty,BindingMode mode=BindingMode.OneWay)=>target.SetBinding(property,new Binding(sourceProperty){Source=source,Mode=mode,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
   Bind(surface.Elements["title"],TextBlock.TextProperty,title,"Text");Bind(surface.Elements["artist"],TextBlock.TextProperty,artist,"Text");Bind(surface.Elements["time"],TextBlock.TextProperty,timeLabel,"Text");Bind(surface.Elements["cover"],Image.SourceProperty,cover,"Source");
   EnableFullTitle((TextBlock)surface.Elements["title"]);
   foreach(var pair in new[]{("previous",prev),("play",play),("next",next),("repeat",repeat)}){
    var button=(Button)surface.Elements[pair.Item1];var source=pair.Item2;
    Bind(button,IsEnabledProperty,source,"IsEnabled");Bind(button,ToolTipProperty,source,"ToolTip");ToolTipService.SetShowOnDisabled(button,true);
    button.Click+=(_,_)=>source.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
   }
   ((Button)surface.Elements["pin"]).Click+=(_,_)=>TogglePin();
   ((Button)surface.Elements["lock"]).Click+=(_,_)=>ToggleMovementLock();
   ((Button)surface.Elements["clickthrough"]).Click+=(_,_)=>ToggleClickThrough();
   ((Button)surface.Elements["hide"]).Click+=(_,_)=>HidePlayer();
   surface.ContextMenu=AttachPlayerMenu((Button)surface.Elements["menu"]);
   var progress=(Slider)surface.Elements["progress"];
   Bind(progress,Slider.MinimumProperty,seek,"Minimum");Bind(progress,Slider.MaximumProperty,seek,"Maximum");Bind(progress,Slider.ValueProperty,seek,"Value",BindingMode.TwoWay);Bind(progress,IsEnabledProperty,seek,"IsEnabled");progress.ToolTip="Перемотка";
   progress.PreviewMouseLeftButtonDown+=(_,_)=>{if(progress.IsEnabled){seekTrack=track;seeking=true;}};
   progress.PreviewMouseLeftButtonUp+=async(_,_)=>{if(seeking){seeking=false;await Seek();}};
   progress.LostMouseCapture+=async(_,_)=>{if(seeking&&Mouse.LeftButton==MouseButtonState.Released){seeking=false;await Seek();}};
   progress.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End){seekTrack=track;seeking=true;}};
   progress.PreviewKeyUp+=async(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End){seeking=false;await Seek();}};
   progress.LostKeyboardFocus+=(_,_)=>{if(!progress.IsMouseCaptureWithin)seeking=false;};
   var volume=(Slider)surface.Elements["volume"];
   Bind(volume,Slider.MinimumProperty,volumeSlider,"Minimum");Bind(volume,Slider.MaximumProperty,volumeSlider,"Maximum");Bind(volume,Slider.ValueProperty,volumeSlider,"Value",BindingMode.TwoWay);Bind(volume,IsEnabledProperty,volumeSlider,"IsEnabled");Bind(volume,ToolTipProperty,volumeSlider,"ToolTip");
  }catch{surface.Dispose();throw;}
  CancelRecoveryAnimation();HideRecoveryControls();DisposeCustomSurface();
  if(deviceFace!=null)stack.Children.Remove(deviceFace);
  stack.Children[0].Visibility=stack.Children[1].Visibility=Visibility.Collapsed;
  peakReader.Dispose();platterRotation=null;sceneMotion=null;activeMeter=null;
  facePin=faceLock=faceRepeat=faceMenu=faceHide=faceClickThrough=null;
  customPreset=preset.Copy();customPresetSuspended=false;customSurface=surface;deviceFace=surface;faceVolume=(Slider)surface.Elements["volume"];
  body.Background=Brushes.Transparent;body.BorderBrush=Brushes.Transparent;body.BorderThickness=new Thickness(0);body.Padding=new Thickness(0);body.CornerRadius=new CornerRadius(0);body.LayoutTransform=Transform.Identity;body.Opacity=1;
  stack.Children.Insert(2,surface);SizePlayer();UpdateCustomPresetState();UpdateClickThroughButtons();SyncMotion();
  if(IsLoaded)Dispatcher.BeginInvoke((Action)UpdateRecoveryArrow,System.Windows.Threading.DispatcherPriority.Loaded);
 }
 void UpdateCustomPresetState(){
  var surface=customSurface;if(surface==null)return;
  surface.SetButtonIcon("play",mediaPlaying?"pause":"play");surface.SetButtonIcon("pin",Topmost?"on":"off");surface.SetButtonIcon("lock",movementLocked?"locked":"unlocked");
  surface.SetButtonIcon("repeat",repeatMode==Windows.Media.MediaPlaybackAutoRepeatMode.Track?"one":repeatMode==Windows.Media.MediaPlaybackAutoRepeatMode.List?"all":"none");
  surface.Elements["pin"].ToolTip=Topmost?"Открепить плеер":"Закрепить поверх окон";
  surface.Elements["lock"].ToolTip=movementLocked?"Разрешить перемещение плеера":"Запретить перемещение плеера";
 }
 void CommitStandardAppearance(){
  if(CustomPresetRoot is string root)new PresetStore(root).ClearActive();
  customPreset=null;customPresetSuspended=false;customPresetStamp=0;customPresetLength=0;customPresetHash="";
 }
}
