using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 bool previewMyTrack;
 DispatcherTimer? trackPreviewTimer;
 CancellationTokenSource? trackPreviewRequest;
 TrackPreviewFrame? previewTrack;
 BitmapSource? previewArtwork;
 TextBlock? trackPreviewHint;

 void AddTrackPreviewControls(Panel panel){
  var row=new UniformGrid{Columns=2,Margin=new Thickness(0,0,0,8)};panel.Children.Add(row);
  Button? sample=null,live=null;
  void Mark(Button button,bool selected){
   button.Background=selected?AccentBrush:ColorBrush("#292B39");button.Foreground=selected?BackgroundBrush:TextBrush;
  }
  void Choose(bool enabled){
   if(previewMyTrack==enabled)return;
   previewMyTrack=enabled;
   Mark(sample!,!enabled);Mark(live!,enabled);
   if(enabled)StartTrackPreview();
   else{StopTrackPreview();DisplayTrack(null,"");}
  }
  sample=PresetButton("Пример",()=>Choose(false));live=PresetButton("Мой трек",()=>Choose(true));
  sample.Margin=new Thickness(0,0,3,0);live.Margin=new Thickness(3,0,0,0);
  live.ToolTip="Показать трек из выбранного источника мини-плеера";
  Mark(sample,!previewMyTrack);Mark(live,previewMyTrack);row.Children.Add(sample);row.Children.Add(live);
  trackPreviewHint=Label("",11,MutedBrush,new Thickness(0,0,0,8));trackPreviewHint.Visibility=Visibility.Collapsed;panel.Children.Add(trackPreviewHint);
  if(previewMyTrack)StartTrackPreview();
 }

 void StartTrackPreview(){
  if(trackPreviewTimer==null){
   trackPreviewTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
   trackPreviewTimer.Tick+=(_,_)=>_=ReadWorkshopTrackAsync();
  }
  trackPreviewTimer.Start();
  _=ReadWorkshopTrackAsync();
 }

 void StopTrackPreview(){
  trackPreviewTimer?.Stop();trackPreviewRequest?.Cancel();trackPreviewRequest=null;
 }

 async Task ReadWorkshopTrackAsync(){
  if(!previewMyTrack||trackPreviewRequest!=null||presetPreview==null)return;
  if((!IsVisible||WindowState==WindowState.Minimized)&&largePresetWindow?.IsVisible!=true)return;
  using var request=new CancellationTokenSource();
  trackPreviewRequest=request;
  try{
   string instance=WidgetManager.InstanceNameForData(Path.Combine(manager.Root,"Data","mini-player"));
   var frame=await TrackPreviewProtocol.ReadAsync(instance,request.Token);
   if(!request.IsCancellationRequested)DisplayTrack(frame,"У источника нет трека. Показан пример.");
  }catch(Exception ex)when(ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or JsonException){
   System.Diagnostics.Debug.WriteLine(ex);
   if(!request.IsCancellationRequested)DisplayTrack(null,"Мини-плеер недоступен. Показан пример.");
  }finally{if(ReferenceEquals(trackPreviewRequest,request))trackPreviewRequest=null;}
 }

 void DisplayTrack(TrackPreviewFrame? frame,string hint){
  if(comparisonOwner!=null)return;
  if(trackPreviewHint!=null){
   trackPreviewHint.Text=frame==null?hint:"";
   trackPreviewHint.Visibility=trackPreviewHint.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
  }
  if(frame==null){
   if(previewTrack!=null&&presetDraft!=null){
    presetPreview?.Update(presetDraft);largePresetPreview?.Update(presetDraft);
   }
   previewTrack=null;previewArtwork=null;
   return;
  }
  if(frame.Artwork==null)previewArtwork=null;
  else if(previewTrack?.Artwork==null||!frame.Artwork.AsSpan().SequenceEqual(previewTrack.Artwork))
   previewArtwork=DecodeTrackCover(frame.Artwork);
  previewTrack=frame;PaintWorkshopTrack();
 }

 static BitmapSource? DecodeTrackCover(byte[] bytes){
  try{
   using var stream=new MemoryStream(bytes,false);
   var decoder=new JpegBitmapDecoder(stream,BitmapCreateOptions.None,BitmapCacheOption.OnDemand);
   var thumb=decoder.Frames[0];
   if(thumb.PixelWidth is <=0 or >160||thumb.PixelHeight is <=0 or >160)return null;
   var copy=new WriteableBitmap(thumb);copy.Freeze();return copy;
  }catch(Exception ex)when(ex is not OutOfMemoryException){return null;}
 }

 void PaintWorkshopTrack(){
  if(!previewMyTrack||previewTrack==null)return;
  void Paint(PresetPreview? view){
   if(view==null)return;
   var frame=previewTrack;var elements=view.Surface.Elements;
   ((TextBlock)elements["title"]).Text=frame.Title;((TextBlock)elements["artist"]).Text=frame.Artist;
   ((Image)elements["cover"]).Source=previewArtwork;((TextBlock)elements["time"]).Text=frame.Time;
   var slider=(Slider)elements["progress"];
   slider.Maximum=frame.Duration>0&&double.IsFinite(frame.Duration)?frame.Duration:1;
   slider.Value=frame.Duration>0&&double.IsFinite(frame.Position)?Math.Clamp(frame.Position,0,slider.Maximum):0;
   var button=(Button)elements["play"];
   if(!Equals(button.Tag,frame.Playing)){view.Surface.SetButtonIcon("play",frame.Playing?"pause":"play");button.Tag=frame.Playing;}
  }
  Paint(presetPreview);Paint(largePresetPreview);
 }
}
