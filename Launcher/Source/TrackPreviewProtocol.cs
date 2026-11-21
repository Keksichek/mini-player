using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetLauncher;

// Transient preview data. Neither process writes it into a preset or the media library.
internal sealed record TrackPreviewFrame(string Title,string Artist,string Time,double Position,double Duration,bool Playing,byte[]? Artwork);

internal static class TrackPreviewProtocol
{
 const int MaxBytes=96*1024;
 static string PipeName(string instance){
  using var process=System.Diagnostics.Process.GetCurrentProcess();
  return instance.Replace(@"Local\","")+".WorkshopTrack.v1."+process.SessionId;
 }

 internal static async Task ListenAsync(string instance,Func<CancellationToken,Task<TrackPreviewFrame?>> capture,CancellationToken stop){
  while(!stop.IsCancellationRequested){
   try{
    using var pipe=new NamedPipeServerStream(PipeName(instance),PipeDirection.Out,1,PipeTransmissionMode.Byte,
     PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
    await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
    using var request=CancellationTokenSource.CreateLinkedTokenSource(stop);
    request.CancelAfter(TimeSpan.FromSeconds(2));
    var frame=await capture(request.Token).ConfigureAwait(false);
    byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(frame);
    if(bytes.Length>MaxBytes)bytes=JsonSerializer.SerializeToUtf8Bytes(frame! with{Artwork=null});
    byte[] length=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(length,bytes.Length);
    await pipe.WriteAsync(length,request.Token).ConfigureAwait(false);
    await pipe.WriteAsync(bytes,request.Token).ConfigureAwait(false);
   }catch(OperationCanceledException)when(stop.IsCancellationRequested){return;}
   catch(OperationCanceledException){ /* The reader left the workshop. */ }
   catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){
    try{await Task.Delay(300,stop).ConfigureAwait(false);}catch(OperationCanceledException){return;}
   }
  }
 }

 internal static async Task<TrackPreviewFrame?> ReadAsync(string instance,CancellationToken cancellation){
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
  deadline.CancelAfter(TimeSpan.FromSeconds(2));
  using var pipe=new NamedPipeClientStream(".",PipeName(instance),PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
  await pipe.ConnectAsync(400,deadline.Token).ConfigureAwait(false);
  byte[] length=new byte[4];
  await pipe.ReadExactlyAsync(length,deadline.Token).ConfigureAwait(false);
  int size=BinaryPrimitives.ReadInt32LittleEndian(length);
  if(size is <=0 or >MaxBytes)throw new InvalidDataException("Некорректный размер данных трека.");
  byte[] bytes=new byte[size];
  await pipe.ReadExactlyAsync(bytes,deadline.Token).ConfigureAwait(false);
  return JsonSerializer.Deserialize<TrackPreviewFrame>(bytes);
 }
}
