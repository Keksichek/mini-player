using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using WidgetLauncher;
static class PresetUpgradeChecks {
 static int count;static void Check(bool yes,string label){if(!yes)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
 static async Task<int> Main(string[] args){
  var root=Path.GetFullPath(args[0]);using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"launcher-install.json")));if(!marker.RootElement.GetProperty("TestInstallation").GetBoolean())return 2;
  var manager=new WidgetManager(root);var widget=manager.ReadCatalog().Single(w=>w.Id=="mini-player");string app=Path.GetDirectoryName(manager.Executable(widget))!,format=Path.Combine(app,"preset-format.json"),dll=Path.Combine(app,"MiniPlayer.dll"),package=Path.Combine(root,"Packages",widget.Package),backup=package+".own-test-backup";
  try{
   if(!manager.IsInstalled(widget))await manager.InstallAsync(widget);await manager.StopAsync(widget);
   string extra=Path.Combine(app,"own-extra.txt"),settings=Path.Combine(manager.DataDirectory(widget),"own-settings.txt");Directory.CreateDirectory(Path.GetDirectoryName(settings)!);File.WriteAllText(extra,"extra fixture");File.WriteAllText(settings,"settings fixture");string initial=Hash(dll);File.Delete(format);
   await PresetWidgetUpgrade.EnsureAsync(manager,widget);
   Check(manager.IsRunning(widget)&&File.Exists(format),"Old installed widget receives preset capability and restarts");Check(Hash(dll)==initial&&File.ReadAllText(extra)=="extra fixture"&&File.ReadAllText(settings)=="settings fixture","Upgrade preserves settings and extra installed files");
   string active=Path.Combine(root,"Data","Customization","mini-player","active.json");Directory.CreateDirectory(Path.GetDirectoryName(active)!);File.WriteAllText(active,JsonSerializer.Serialize(new{Name="Own IPC fixture",Shape="star",Width=420,Height=280,FontSize=60}));
   string acknowledgement=Path.Combine(manager.DataDirectory(widget),"preset-applied.json");if(File.Exists(acknowledgement))File.Delete(acknowledgement);
   using(var signal=System.Threading.EventWaitHandle.OpenExisting(manager.InstanceName(widget)+".ApplyPreset"))signal.Set();
   bool applied=false;for(int attempt=0;attempt<60;attempt++){if(File.Exists(acknowledgement)){using var ack=JsonDocument.Parse(File.ReadAllText(acknowledgement));applied=ack.RootElement.GetProperty("Success").GetBoolean()&&ack.RootElement.GetProperty("Sha256").GetString()==Hash(active).ToLowerInvariant();if(applied)break;}await Task.Delay(100);}
   Check(applied,"A running player receives the actual ApplyPreset event and acknowledges the exact preset hash");
   string markerText=File.ReadAllText(format);await PresetWidgetUpgrade.EnsureAsync(manager,widget);Check(File.ReadAllText(format)==markerText&&Hash(dll)==initial,"Compatible widget is not repeatedly replaced");
   File.Copy(package,backup);File.Delete(format);
   using(var zip=ZipFile.Open(package,ZipArchiveMode.Update)){zip.GetEntry("App/MiniPlayer.dll")!.Delete();var entry=zip.CreateEntry("App/MiniPlayer.dll");using var output=entry.Open();using var input=File.OpenRead(args[1]);input.CopyTo(output);}
   bool rejected=false;try{await PresetWidgetUpgrade.EnsureAsync(manager,widget);}catch(IOException){rejected=true;}
   Check(rejected&&Hash(dll)==initial&&!File.Exists(format),"Failed new startup restores prior DLL and capability marker");Check(manager.IsRunning(widget),"Failed upgrade resumes previously running player");Check(File.ReadAllText(extra)=="extra fixture"&&File.ReadAllText(settings)=="settings fixture","Rollback preserves user settings and extra installed files");
   Console.WriteLine("TOTAL "+count+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{await manager.StopAsync(widget);if(File.Exists(backup)){File.Move(backup,package,true);}}
 }
}
