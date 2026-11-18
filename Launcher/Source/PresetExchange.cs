using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace WidgetLauncher;

internal static class PresetExchange
{
 public const string Extension=".vibepreset";
 const string CodePrefix="VIBE1:";
 const int MaximumDescription=65536;
 const int MaximumPackage=(int)PresetStore.MaximumMediaBytes+MaximumDescription+4096;
 const int MaximumCode=4*((MaximumPackage+2)/3)+6;
 sealed record Description(string Format,int Version,WidgetPreset Preset);

 static void WritePackage(PresetStore store,WidgetPreset source,Stream output){
  var preset=source.Copy();
  preset.Normalize();
  preset.Id="";
  byte[]? media=null;
  if(preset.MediaPath.Length>0){
   using var input=File.OpenRead(store.ResolveMedia(preset.MediaPath));
   media=ReadBytes(input,input.Length,(int)PresetStore.MaximumMediaBytes);
   VerifyMediaName(preset.MediaPath,media);
   PresetStore.ValidateMedia(media,Path.GetExtension(preset.MediaPath));
  }
  byte[] description=JsonSerializer.SerializeToUtf8Bytes(new Description("VibeWidgetsHub/mini-player",1,preset));
  if(description.Length>MaximumDescription)throw new InvalidDataException("Описание пресета слишком большое.");
  using var archive=new ZipArchive(output,ZipArchiveMode.Create,true);
  using(var entry=archive.CreateEntry("preset.json",CompressionLevel.Optimal).Open())entry.Write(description);
  if(media!=null){
   using var entry=archive.CreateEntry(preset.MediaPath,CompressionLevel.NoCompression).Open();
   entry.Write(media);
  }
 }

 public static void ExportFile(PresetStore store,WidgetPreset preset,string path){
  string destination=Path.GetFullPath(path);
  string temporary=Path.Combine(Path.GetDirectoryName(destination)!,".vibe-"+Guid.NewGuid().ToString("N")+".tmp");
  try{
   using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))WritePackage(store,preset,output);
   File.Move(temporary,destination,true);
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }

 public static string ExportCode(PresetStore store,WidgetPreset preset){
  using var output=new MemoryStream();
  WritePackage(store,preset,output);
  return CodePrefix+Convert.ToBase64String(output.GetBuffer(),0,(int)output.Length);
 }

 public static WidgetPreset ImportFile(PresetStore store,string path){
  using var input=File.OpenRead(path);
  return ReadPackage(store,input);
 }

 public static WidgetPreset ImportCode(PresetStore store,string code){
  var text=code.AsSpan().Trim();
  if(text.Length>MaximumCode||!text.StartsWith(CodePrefix,StringComparison.Ordinal))
   throw new InvalidDataException("Вставьте полный код пресета с началом VIBE1:.");
  byte[] bytes;
  try{bytes=Convert.FromBase64String(text[CodePrefix.Length..].ToString());}
  catch(FormatException ex){throw new InvalidDataException("Код пресета повреждён или скопирован не полностью.",ex);}
  using var input=new MemoryStream(bytes,false);
  return ReadPackage(store,input);
 }

 static WidgetPreset ReadPackage(PresetStore store,Stream input){
  if(input.Length<=0||input.Length>MaximumPackage)throw new InvalidDataException("Недопустимый размер файла пресета.");
  WidgetPreset preset;
  try{
   using var archive=new ZipArchive(input,ZipArchiveMode.Read,true);
   if(archive.Entries.Count is <1 or >2)throw new InvalidDataException("В файле пресета неожиданный набор данных.");
   var manifest=archive.GetEntry("preset.json")??throw new InvalidDataException("В файле отсутствует описание пресета.");
   using var json=manifest.Open();
   var metadata=JsonSerializer.Deserialize<Description>(ReadBytes(json,manifest.Length,MaximumDescription));
   if(metadata?.Format!="VibeWidgetsHub/mini-player"||metadata.Version!=1||metadata.Preset==null)
    throw new InvalidDataException("Эта версия формата пресета не поддерживается.");
   preset=metadata.Preset;
   preset.Id="";
   preset.Normalize();
   if(preset.MediaPath.Length==0){
    if(archive.Entries.Count!=1)throw new InvalidDataException("В пресете обнаружены лишние данные.");
   }else{
    var attachment=archive.GetEntry(preset.MediaPath)??throw new InvalidDataException("Фон отсутствует в файле пресета.");
    using var stream=attachment.Open();
    byte[] media=ReadBytes(stream,attachment.Length,(int)PresetStore.MaximumMediaBytes);
    VerifyMediaName(preset.MediaPath,media);
    store.StoreMedia(media,Path.GetExtension(preset.MediaPath));
   }
  }catch(JsonException ex){throw new InvalidDataException("Не удалось прочитать описание пресета.",ex);}
  if(store.List().Any(item=>string.Equals(item.Name,preset.Name,StringComparison.CurrentCultureIgnoreCase)))return store.SaveCopy(preset);
  return store.Save(preset);
 }

 static byte[] ReadBytes(Stream input,long length,int limit){
  if(length<=0||length>limit)throw new InvalidDataException("Размер данных пресета выходит за допустимые пределы.");
  byte[] bytes=new byte[(int)length];
  input.ReadExactly(bytes);
  if(input.ReadByte()!=-1)throw new InvalidDataException("Размер данных пресета не совпадает с описанием.");
  return bytes;
 }

 static void VerifyMediaName(string mediaPath,byte[] bytes){
  string digest=Convert.ToHexString(SHA256.HashData(bytes));
  if(!digest.Equals(Path.GetFileNameWithoutExtension(mediaPath),StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Фоновое изображение повреждено.");
 }
}
