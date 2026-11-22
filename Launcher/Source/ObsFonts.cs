using System;
using System.IO;
using System.Security.Cryptography;
namespace WidgetLauncher;
public sealed partial class ObsBridge
{
 const int MaxFontBytes=20*1024*1024;
 public string? FontPath(string id){lock(gate){if(!ObsAppearance.ValidFontId(id)||!Settings.Fonts.ContainsKey(id))return null;var path=Path.Combine(manager.Root,"Data","ObsFonts",id);WidgetManager.RejectLinks(path);return File.Exists(path)&&new FileInfo(path).Length<=MaxFontBytes?path:null;}}
 public string ImportFont(string source){
  var extension=Path.GetExtension(source).ToLowerInvariant();if(extension is not (".ttf" or ".otf"))throw new InvalidDataException("Выберите файл TTF или OTF.");
  if(new FileInfo(source).Length>MaxFontBytes)throw new InvalidDataException("Размер шрифта не должен превышать 20 МБ.");
  byte[] data=File.ReadAllBytes(source);if(data.Length<12||data.Length>MaxFontBytes||!((data[0]==0&&data[1]==1&&data[2]==0&&data[3]==0)||(data[0]==79&&data[1]==84&&data[2]==84&&data[3]==79)))throw new InvalidDataException("Не удалось прочитать шрифт TTF/OTF.");
  string id=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()+extension;
  lock(gate){var folder=Path.Combine(manager.Root,"Data","ObsFonts");WidgetManager.RejectLinks(folder);Directory.CreateDirectory(folder);var path=Path.Combine(folder,id);WidgetManager.RejectLinks(path);bool existed=File.Exists(path);Settings.Fonts.TryGetValue(id,out var old);
   if(!existed)File.WriteAllBytes(path,data);Settings.Fonts[id]=Path.GetFileNameWithoutExtension(source);
   try{Save();}catch{if(old==null)Settings.Fonts.Remove(id);else Settings.Fonts[id]=old;if(!existed)File.Delete(path);throw;}return id;
  }
 }
}
