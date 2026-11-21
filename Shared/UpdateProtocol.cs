using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VibeWidgetsUpdates
{
 [DataContract]
 public sealed class UpdateManifest
 {
  public UpdateManifest(){ProductId=String.Empty;Channel=String.Empty;Version=String.Empty;Package=String.Empty;Sha256=String.Empty;Notes=String.Empty;}
  [DataMember(IsRequired=true)]public string ProductId {get;set;}
  [DataMember(IsRequired=true)]public string Channel {get;set;}
  [DataMember(IsRequired=true)]public long BuildNumber {get;set;}
  [DataMember(IsRequired=true)]public string Version {get;set;}
  [DataMember(IsRequired=true)]public string Package {get;set;}
  [DataMember(IsRequired=true)]public string Sha256 {get;set;}
  [DataMember(IsRequired=true)]public long Size {get;set;}
  [DataMember]public string Notes {get;set;}
 }

 public static class UpdateProtocol
 {
  public const string ProductId="MiniPlayer.WidgetLauncher",Channel="keksichek-local";
  public const long MaxPackageSize=2000000000;
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  public static string PublicKey(){
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("UpdatePublicKey.xml")){
    if(stream==null)throw new InvalidDataException("В приложении отсутствует ключ проверки обновлений.");
    using(var reader=new StreamReader(stream,Utf8))return reader.ReadToEnd();
   }
  }
  public static UpdateManifest Read(string feedDirectory){return Read(feedDirectory,PublicKey());}
  // The explicit key overload is also used by isolated signing/verification tests.
  public static UpdateManifest Read(string feedDirectory,string publicKey){
   string root=FeedPath(feedDirectory);byte[] bytes=ReadBounded(Path.Combine(root,"update.json"),65536),signed=ReadBounded(Path.Combine(root,"update.sig"),16384);
   byte[] signature;
   try{signature=Convert.FromBase64String(Utf8.GetString(signed).Trim());}
   catch(FormatException ex){throw new InvalidDataException("Подпись обновления повреждена.",ex);}
   catch(DecoderFallbackException ex){throw new InvalidDataException("Подпись обновления повреждена.",ex);}
   try{
    using(var rsa=RSA.Create()){
     rsa.FromXmlString(publicKey);
     if(rsa.KeySize<2048||signature.Length!=rsa.KeySize/8||!rsa.VerifyData(bytes,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidDataException("Не удалось подтвердить подлинность обновления.");
    }
   }catch(CryptographicException ex){throw new InvalidDataException("Не удалось проверить подпись обновления.",ex);}
   UpdateManifest manifest;
   try{using(var input=new MemoryStream(bytes,false)){var raw=new DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(input);if(!(raw is UpdateManifest))throw new InvalidDataException("Описание обновления повреждено.");manifest=(UpdateManifest)raw;}}
   catch(SerializationException ex){throw new InvalidDataException("Описание обновления повреждено.",ex);}
   catch(System.Xml.XmlException ex){throw new InvalidDataException("Описание обновления повреждено.",ex);}
   catch(InvalidCastException ex){throw new InvalidDataException("Описание обновления повреждено.",ex);}
   Validate(manifest);return manifest;
  }
  public static string VerifyPackage(string feedDirectory,UpdateManifest manifest){return VerifyPackage(feedDirectory,manifest,PublicKey());}
  public static string VerifyPackage(string feedDirectory,UpdateManifest manifest,string publicKey){
   Validate(manifest);var current=Read(feedDirectory,publicKey);
   if(current.ProductId!=manifest.ProductId||current.Channel!=manifest.Channel||current.BuildNumber!=manifest.BuildNumber||current.Version!=manifest.Version||current.Package!=manifest.Package||!String.Equals(current.Sha256,manifest.Sha256,StringComparison.OrdinalIgnoreCase)||current.Size!=manifest.Size||current.Notes!=manifest.Notes)
    throw new InvalidDataException("Источник обновлений изменился. Повторите проверку.");
   string path=Path.Combine(FeedPath(feedDirectory),manifest.Package);NoLinks(path);
   using(var stream=new FileStream(DiskPath(path),FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(stream.Length!=manifest.Size)throw new InvalidDataException("Размер пакета обновления не совпадает с описанием.");
    using(var sha=SHA256.Create())if(!String.Equals(Hex(sha.ComputeHash(stream)),manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Пакет обновления повреждён: контрольная сумма не совпадает.");
   }
   return path;
  }
  public static void Validate(UpdateManifest value){
   if(value==null||value.ProductId!=ProductId||value.Channel!=Channel)throw new InvalidDataException("Это обновление относится к другому приложению или каналу.");
   if(value.BuildNumber<=0||String.IsNullOrWhiteSpace(value.Version)||value.Version.Length>128||HasControl(value.Version)||value.Size<=0||value.Size>MaxPackageSize)
    throw new InvalidDataException("В описании обновления указана некорректная версия или размер.");
   if(String.IsNullOrEmpty(value.Package)||!Regex.IsMatch(value.Package,@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,123}\.zip\z",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase)||value.Package.Contains("..")||value.Package.EndsWith(". ",StringComparison.Ordinal))
    throw new InvalidDataException("В описании обновления указан недопустимый путь пакета.");
   if(String.IsNullOrEmpty(value.Sha256)||!Regex.IsMatch(value.Sha256,@"\A[0-9a-fA-F]{64}\z",RegexOptions.CultureInvariant))throw new InvalidDataException("В описании обновления отсутствует контрольная сумма.");
   if(value.Notes!=null&&value.Notes.Length>8192)throw new InvalidDataException("Описание изменений слишком длинное.");
  }
  static bool HasControl(string value){foreach(char c in value)if(Char.IsControl(c))return true;return false;}
  public static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
  public static string FeedPath(string directory){
   if(String.IsNullOrWhiteSpace(directory)||!Path.IsPathRooted(directory))throw new InvalidDataException("Выберите полную папку локальных обновлений.");
   string root=Path.GetFullPath(directory);
   if(root.StartsWith("\\\\",StringComparison.Ordinal)||(Path.GetPathRoot(root)??String.Empty).Length!=3)throw new InvalidDataException("Локальный источник обновлений должен находиться на диске компьютера.");
   if(root.Length>3)root=root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
   NoLinks(root);if(!Directory.Exists(DiskPath(root)))throw new DirectoryNotFoundException("Папка локальных обновлений не найдена.");return root;
  }
  public static void NoLinks(string path){
   for(string current=Path.GetFullPath(path);!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)??String.Empty){
    try{if((File.GetAttributes(DiskPath(current))&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Источник обновлений не должен проходить через ссылки или перенаправления.");}
    catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
   }
  }
  static string DiskPath(string path){return Path.DirectorySeparatorChar=='\\'&&!path.StartsWith("\\\\?\\",StringComparison.Ordinal)?"\\\\?\\"+path:path;}
  static byte[] ReadBounded(string path,int limit){
   NoLinks(path);using(var file=new FileStream(DiskPath(path),FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(file.Length<=0||file.Length>limit)throw new InvalidDataException("Файл описания или подписи обновления имеет недопустимый размер.");
    using(var output=new MemoryStream()){
     var buffer=new byte[4096];int count;while((count=file.Read(buffer,0,buffer.Length))>0){if(output.Length+count>limit)throw new InvalidDataException("Файл обновления слишком большой.");output.Write(buffer,0,count);}return output.ToArray();
    }
   }
  }
 }
}
