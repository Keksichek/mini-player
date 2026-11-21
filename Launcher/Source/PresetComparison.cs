using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 Button? comparisonOwner;
 CancellationTokenSource? comparisonRequest;
 string? classicCacheKey;
 BitmapSource? classicCache;
 const string CompareHint="Удерживайте кнопку мыши или пробел, чтобы увидеть применённое оформление";

 Button ComparisonButton(){
  Button button=null!;
  // UI Automation can invoke the button again to return to the draft.
  button=ActionButton("Сравнить",()=>{if(comparisonOwner==button)EndPresetComparison();else _=BeginPresetComparisonAsync(button);});
  button.ToolTip=CompareHint;
  AutomationProperties.SetName(button,"Сравнить");
  AutomationProperties.SetHelpText(button,(string)button.ToolTip);
  button.PreviewMouseLeftButtonDown+=(_,e)=>{
   e.Handled=true;
   if(comparisonOwner!=null||presetPreview==null||presetDraft==null||busy||updatePending)return;
   button.Focus();
   if(button.CaptureMouse())_=BeginPresetComparisonAsync(button);
  };
  void Finish(){if(comparisonOwner==button)EndPresetComparison();}
  button.PreviewMouseLeftButtonUp+=(_,e)=>{e.Handled=true;Finish();};
  button.LostMouseCapture+=(_,_)=>Finish();
  button.LostKeyboardFocus+=(_,_)=>Finish();
  button.PreviewKeyDown+=(_,e)=>{
   if(e.Key!=Key.Space)return;
   e.Handled=true;if(!e.IsRepeat)_=BeginPresetComparisonAsync(button);
  };
  button.PreviewKeyUp+=(_,e)=>{if(e.Key==Key.Space){e.Handled=true;Finish();}};
  Window? host=null;
  void Deactivate(object? sender,EventArgs e)=>Finish();
  button.Loaded+=(_,_)=>{host=Window.GetWindow(button);if(host!=null){host.Deactivated+=Deactivate;host.Closed+=Deactivate;}};
  button.Unloaded+=(_,_)=>{if(host!=null){host.Deactivated-=Deactivate;host.Closed-=Deactivate;}Finish();};
  return button;
 }

 async Task BeginPresetComparisonAsync(Button button){
  if(comparisonOwner!=null||presetPreview==null||presetDraft==null||busy||updatePending)return;
  comparisonOwner=button;
  using var request=new CancellationTokenSource();
  comparisonRequest=request;
  button.Content="Сравниваем…";
  try{
   var active=Presets.LoadActive();
   var draft=presetPreview.Surface.Elements;
   var art=((Image)draft["cover"]).Source;
   var progress=(Slider)draft["progress"];
   var frame=new TrackPreviewFrame(((TextBlock)draft["title"]).Text,((TextBlock)draft["artist"]).Text,
    ((TextBlock)draft["time"]).Text,progress.Value,progress.Maximum,previewMyTrack&&previewTrack?.Playing==true,null);
   BitmapSource snapshot;
   if(active!=null){
    using var surface=new PresetSurface(manager.Root,true);
    surface.Configure(active);
    if(surface.MediaError.Length!=0)throw new IOException(surface.MediaError);
    var parts=surface.Elements;
    ((TextBlock)parts["title"]).Text=frame.Title;((TextBlock)parts["artist"]).Text=frame.Artist;
    ((TextBlock)parts["time"]).Text=frame.Time;((Image)parts["cover"]).Source=art;
    var slider=(Slider)parts["progress"];slider.Maximum=frame.Duration;slider.Value=frame.Position;
    surface.SetButtonIcon("play",frame.Playing?"pause":"play");
    surface.FlowDirection=presetPreview.FlowDirection;
    var size=new Size(surface.Width,surface.Height);surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
    snapshot=RenderSnapshot(surface,size);
   }else{
    snapshot=await ReadClassicSnapshotAsync(frame with{Artwork=EncodeComparisonCover(art)},request.Token);
   }
   if(request.IsCancellationRequested)return;
   presetPreview.CompareWith(snapshot);
   if(largePresetPreview!=null){
    largePresetPreview.Width=snapshot.Width+44;largePresetPreview.Height=snapshot.Height+44;
    largePresetPreview.CompareWith(snapshot);
   }
   button.Content="Применено";
  }catch(OperationCanceledException)when(request.IsCancellationRequested){}
  catch(Exception ex)when(ex is not OutOfMemoryException){
   if(!request.IsCancellationRequested){
    button.Content="Недоступно";button.ToolTip=ex.Message;
    if(presetStatus!=null)presetStatus.Text="Сравнение недоступно. "+ex.Message;
   }
  }finally{if(comparisonRequest==request)comparisonRequest=null;}
 }

 void EndPresetComparison(){
  if(comparisonOwner==null)return;
  var button=comparisonOwner;comparisonOwner=null;
  comparisonRequest?.Cancel();comparisonRequest=null;
  button.Content="Сравнить";button.ToolTip=CompareHint;
  if(button.IsMouseCaptured)button.ReleaseMouseCapture();
  presetPreview?.CompareWith(null);largePresetPreview?.CompareWith(null);
  if(largePresetPreview!=null&&presetDraft!=null){largePresetPreview.Width=presetDraft.Width+44;largePresetPreview.Height=presetDraft.Height+44;}
 }

 static BitmapSource RenderSnapshot(Visual visual,Size size){
  var image=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
  image.Render(visual);image.Freeze();return image;
 }
 static byte[]? EncodeComparisonCover(ImageSource? cover){
  if(cover==null||cover.Width<=0||cover.Height<=0)return null;
  double scale=160/Math.Max(cover.Width,cover.Height);
  var size=new Size(Math.Max(1,cover.Width*scale),Math.Max(1,cover.Height*scale));
  var art=new DrawingVisual();using(var pen=art.RenderOpen())pen.DrawImage(cover,new Rect(size));
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(RenderSnapshot(art,size)));
  using var buffer=new MemoryStream();encoder.Save(buffer);return buffer.ToArray();
 }

 async Task<BitmapSource> ReadClassicSnapshotAsync(TrackPreviewFrame frame,CancellationToken stop){
  var widget=catalog.Single(w=>w.Id=="mini-player");
  var dll=Path.ChangeExtension(manager.Executable(widget),".dll");
  var runtime=Path.Combine(manager.Root,"Runtime","dotnet.exe");
  var marker=Path.Combine(Path.GetDirectoryName(dll)!,"preset-format.json");
  WidgetManager.RejectLinks(dll);WidgetManager.RejectLinks(runtime);WidgetManager.RejectLinks(marker);
  if(!File.Exists(dll)||!File.Exists(marker))throw new IOException("Установите мини-плеер в хабе для сравнения встроенной темы.");
  using(var format=JsonDocument.Parse(File.ReadAllBytes(marker))){
   if(format.RootElement.GetProperty("Version").GetInt32()<8)throw new IOException("Обновите мини-плеер, чтобы сравнить встроенную тему.");
  }
  string appearance=Path.Combine(manager.DataDirectory(widget),"appearance.json");WidgetManager.RejectLinks(appearance);
  byte[] data=JsonSerializer.SerializeToUtf8Bytes(frame);
  string key=Convert.ToHexString(SHA256.HashData(data))+File.GetLastWriteTimeUtc(appearance).Ticks+":"+File.GetLastWriteTimeUtc(dll).Ticks;
  if(classicCacheKey==key&&classicCache!=null)return classicCache;
  var start=new ProcessStartInfo(runtime){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(dll)!};
  start.ArgumentList.Add(dll);start.ArgumentList.Add("--compare-frame");
  using var helper=Process.Start(start)??throw new IOException("Не удалось запустить предпросмотр темы.");
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);deadline.CancelAfter(TimeSpan.FromSeconds(5));
  try{
   await helper.StandardInput.BaseStream.WriteAsync(data,deadline.Token);
   helper.StandardInput.Close();
   var output=helper.StandardOutput.BaseStream;
   byte[] header=new byte[4];await output.ReadExactlyAsync(header,deadline.Token);
   int size=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
   if(size<=0||size>4*1024*1024)throw new InvalidDataException("Не удалось прочитать изображение темы.");
   byte[] bytes=new byte[size];await output.ReadExactlyAsync(bytes,deadline.Token);
   await helper.WaitForExitAsync(deadline.Token);
   using var input=new MemoryStream(bytes,false);
   var image=BitmapFrame.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
   image.Freeze();classicCache=image;classicCacheKey=key;return image;
  }catch(OperationCanceledException)when(!stop.IsCancellationRequested){
   throw new IOException("Мини-плеер не успел подготовить изображение. Удержите кнопку ещё раз.");
  }finally{
   if(!helper.HasExited){try{helper.Kill();}catch(InvalidOperationException){}}
  }
 }
}
