using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetLauncher;

public sealed class HubPreferences
{
 public int AutoStartMode {get;set;}
 public string[] SelectedWidgets {get;set;}=Array.Empty<string>();
 public bool LightMode {get;set;}
 public bool AllowOffscreen {get;set;}=true;
 public int CloseMode {get;set;}
 public string UpdateFeedDirectory {get;set;}="";
 [JsonExtensionData]public Dictionary<string,JsonElement>? ExtraData {get;set;}
 static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
 public static string FilePath(string root)=>Path.Combine(root,"Data","hub-settings.json");

 void Normalize(){
  if(AutoStartMode<0||AutoStartMode>4)AutoStartMode=0;
  if(CloseMode<0||CloseMode>2)CloseMode=0;
  SelectedWidgets=(SelectedWidgets??Array.Empty<string>()).Where(id=>!string.IsNullOrWhiteSpace(id))
   .Select(id=>id.Trim().ToLowerInvariant()).Where(id=>{try{WidgetManager.ValidateId(id);return true;}catch{return false;}})
   .Distinct(StringComparer.OrdinalIgnoreCase).Take(512).ToArray();
 }
 public static HubPreferences Load(string root){
  try{
   string path=FilePath(root);WidgetManager.RejectLinks(path);
   if(!File.Exists(path)||new FileInfo(path).Length>1024*1024)return new();
   var value=JsonSerializer.Deserialize<HubPreferences>(File.ReadAllText(path),Json)??new();value.Normalize();return value;
  }catch(IOException){return new();}catch(UnauthorizedAccessException){return new();}catch(JsonException){return new();}
 }
 public void Save(string root){
  Normalize();string path=FilePath(root);WidgetManager.RejectLinks(path);
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temporary,JsonSerializer.Serialize(this,Json));File.Move(temporary,path,true);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
}
