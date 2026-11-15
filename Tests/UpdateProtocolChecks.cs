using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using VibeWidgetsUpdates;

[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class UpdateProtocolChecks
{
 static string root,feed;static RSA rsa;static int checks;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Reject(Action action,string label){bool failed=false;try{action();}catch(Exception ex){if(!(ex is IOException)&&!(ex is InvalidDataException)&&!(ex is System.Runtime.Serialization.SerializationException))throw;failed=true;}Check(failed,label);}
 static byte[] Json(UpdateManifest manifest){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(UpdateManifest)).WriteObject(stream,manifest);return stream.ToArray();}}
 static void Sign(byte[] bytes){File.WriteAllBytes(Path.Combine(feed,"update.json"),bytes);File.WriteAllText(Path.Combine(feed,"update.sig"),Convert.ToBase64String(rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)),new UTF8Encoding(false));}
 static UpdateManifest Good(){string path=Path.Combine(feed,"payload.zip");return new UpdateManifest{ProductId=UpdateProtocol.ProductId,Channel=UpdateProtocol.Channel,BuildNumber=1790000000001,Version="0.19999999999999999999999999999999",Package="payload.zip",Sha256=UpdateProtocol.Hex(SHA256.Create().ComputeHash(File.ReadAllBytes(path))),Size=new FileInfo(path).Length,Notes="Локальная проверка\nСохраняем пользовательские настройки."};}
 static int Main(string[] args){
  try{
   AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
   root=Path.GetFullPath(args[0]);feed=Path.Combine(root,"Локальная папка [обновление]");Directory.CreateDirectory(feed);
   rsa=RSA.Create();rsa.FromXmlString(File.ReadAllText(args[1]));
   using(var zip=ZipFile.Open(Path.Combine(feed,"payload.zip"),ZipArchiveMode.Create)){using(var text=new StreamWriter(zip.CreateEntry("Launcher.dll").Open()))text.Write("own package fixture");}
   var value=Good();Sign(Json(value));var parsed=UpdateProtocol.Read(feed);
   Check(parsed.Version==value.Version&&parsed.BuildNumber==value.BuildNumber&&parsed.Notes==value.Notes,"Signed manifest reads exact long version, build number and Unicode notes");
   Check(UpdateProtocol.VerifyPackage(feed,parsed)==Path.Combine(feed,"payload.zip"),"Signature, size and SHA256 validate the real local ZIP");
   Check(!UpdateProtocol.PublicKey().Contains("<D>"),"Embedded update key contains public material only");
   using(var other=RSA.Create()){other.KeySize=2048;Reject(delegate{UpdateProtocol.Read(feed,other.ToXmlString(false));},"A different signing key is rejected");}
   File.AppendAllText(Path.Combine(feed,"update.json")," ");Reject(delegate{UpdateProtocol.Read(feed);},"Any byte change invalidates the signature");Sign(Json(value));
   File.WriteAllText(Path.Combine(feed,"update.sig"),"not-base64");Reject(delegate{UpdateProtocol.Read(feed);},"Malformed signature is rejected");Sign(Json(value));
   var signature=Convert.FromBase64String(File.ReadAllText(Path.Combine(feed,"update.sig")));signature[0]^=1;File.WriteAllText(Path.Combine(feed,"update.sig"),Convert.ToBase64String(signature));Reject(delegate{UpdateProtocol.Read(feed);},"A modified RSA signature is rejected");Sign(Json(value));
   foreach(string name in new[]{"../payload.zip","..\\payload.zip","C:\\payload.zip","payload.zip:stream","https://example.test/payload.zip","payload..zip"}){var bad=Good();bad.Package=name;Sign(Json(bad));Reject(delegate{UpdateProtocol.Read(feed);},"Package path is restricted: "+name);}
   var wrong=Good();wrong.ProductId="OtherApplication";Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Another product is rejected");
   wrong=Good();wrong.Channel="main";Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Another channel is rejected");
   wrong=Good();wrong.BuildNumber=0;Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Zero build number is rejected");
   wrong=Good();wrong.BuildNumber=-1;Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Negative build number is rejected");
   wrong=Good();wrong.Size=UpdateProtocol.MaxPackageSize+1;Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Oversized packages are rejected before reading them");
   wrong=Good();wrong.Sha256="bad";Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Malformed SHA256 is rejected");
   wrong=Good();wrong.Version="0.2\ncommand";Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Control characters in display versions are rejected");
   wrong=Good();wrong.Notes=new string('x',8193);Sign(Json(wrong));Reject(delegate{UpdateProtocol.Read(feed);},"Oversized release notes are rejected");
   Sign(Encoding.UTF8.GetBytes("{\"ProductId\":\"MiniPlayer.WidgetLauncher\"}"));Reject(delegate{UpdateProtocol.Read(feed);},"Required manifest fields cannot be omitted even with a valid signature");
   Sign(Encoding.UTF8.GetBytes("{\"BuildNumber\":1,\"BuildNumber\":2}"));Reject(delegate{UpdateProtocol.Read(feed);},"Duplicate manifest members are rejected");
   Sign(Encoding.UTF8.GetBytes("[not json]"));Reject(delegate{UpdateProtocol.Read(feed);},"Malformed signed JSON is rejected");
   Sign(Json(value));parsed=UpdateProtocol.Read(feed);wrong=Good();wrong.BuildNumber++;Sign(Json(wrong));Reject(delegate{UpdateProtocol.VerifyPackage(feed,parsed);},"A feed replaced after checking must be checked again");
   Sign(Json(value));var original=File.ReadAllBytes(Path.Combine(feed,"payload.zip"));var altered=(byte[])original.Clone();altered[altered.Length-1]^=1;File.WriteAllBytes(Path.Combine(feed,"payload.zip"),altered);Reject(delegate{UpdateProtocol.VerifyPackage(feed,value);},"Same-size package corruption is rejected");
   File.WriteAllBytes(Path.Combine(feed,"payload.zip"),original.Concat(new byte[]{0}).ToArray());Reject(delegate{UpdateProtocol.VerifyPackage(feed,value);},"A package size mismatch is rejected");File.WriteAllBytes(Path.Combine(feed,"payload.zip"),original);
   Sign(Json(value));File.Delete(Path.Combine(feed,"payload.zip"));Reject(delegate{UpdateProtocol.VerifyPackage(feed,value);},"A missing package is rejected");File.WriteAllBytes(Path.Combine(feed,"payload.zip"),original);
   File.WriteAllBytes(Path.Combine(feed,"update.json"),new byte[65537]);Reject(delegate{UpdateProtocol.Read(feed);},"An oversized manifest is rejected without allocating the whole file");
   Sign(Json(value));File.WriteAllBytes(Path.Combine(feed,"update.sig"),new byte[16385]);Reject(delegate{UpdateProtocol.Read(feed);},"An oversized signature file is rejected");Sign(Json(value));
   Reject(delegate{UpdateProtocol.Read("relative-feed");},"Relative local source paths are rejected");
   Reject(delegate{UpdateProtocol.Read(@"\\server\updates");},"Network feed paths are rejected in local mode");
   Check(Directory.GetFiles(feed).OrderBy(x=>x).Select(Path.GetFileName).SequenceEqual(new[]{"payload.zip","update.json","update.sig"}),"Verification creates no cache, state or unrelated files");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(rsa!=null)rsa.Dispose();}
 }
}
