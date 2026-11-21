using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WidgetLauncher;

namespace MiniPlayer;

public sealed partial class Player
{
 sealed record SourcePresetChoice(string Key,string Label);
 // During a swap either ID can be on disk after an interruption. An unrelated
 // active ID means the user has already applied a new general appearance.
 sealed record SourcePresetOverride(string PresetId,string? PreviousId,WidgetPreset? General);
 const string AssignmentsFile="source-presets.json",OverrideFile="source-preset-override.json";
 Dictionary<string,string>? sourceAssignments;
 string? lastPresetSource;
 Action? refreshSourcePresetPicker;
 string sourcePresetError="";

 static string ProfilePath(string name){
  string path=Path.Combine(LaunchContext.DataDirectory,name);PresetPaths.RejectLinks(path);return path;
 }
 static T? ReadProfileData<T>(string name) where T:class{
  string path=ProfilePath(name);
  if(!File.Exists(path))return null;
  if(new FileInfo(path).Length>256*1024)throw new InvalidDataException("Файл настроек пресетов слишком велик.");
  return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path))??throw new InvalidDataException("Файл настроек пресетов пуст.");
 }
 static void WriteProfileData<T>(string name,T value){
  string path=ProfilePath(name);Directory.CreateDirectory(LaunchContext.DataDirectory);
  byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value);
  if(bytes.Length>256*1024)throw new InvalidDataException("Недостаточно места в списке привязок.");
  string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}finally{File.Delete(temp);}
 }
 Dictionary<string,string> SourceAssignments=>sourceAssignments??=new(
  ReadProfileData<Dictionary<string,string>>(AssignmentsFile)??new(),StringComparer.OrdinalIgnoreCase);

 static WidgetPreset ResolveSourcePreset(PresetStore store,string key){
  const string prefix="theme:";
  if(key.StartsWith(prefix,StringComparison.Ordinal)&&PresetThemes.Names.Contains(key[prefix.Length..])){
   var theme=PresetThemes.Create(key[prefix.Length..]);
   theme.Id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("mini-player/"+key)))[..32].ToLowerInvariant();
   // The first Apply from the workshop saves a customized version of this template.
   return store.LoadSaved(theme.Id)??theme;
  }
  return store.LoadSaved(key)??throw new IOException("Сохранённый пресет удалён. Выберите другой.");
 }

 void RestoreSourceDefault(PresetStore store,SourcePresetOverride previous){
  if(previous.General!=null)InstallAndStorePreset(store,previous.General);
  else{CommitStandardAppearance();ApplyAppearance();}
 }

 void ChangeSourceAppearance(PresetStore store,string key){
  var previous=ReadProfileData<SourcePresetOverride>(OverrideFile);
  if(previous==null&&key.Length==0)return;
  using var guard=store.AcquireApplyLock();
  var current=store.LoadActive();
  bool owned=previous!=null&&current!=null&&(current.Id==previous.PresetId||current.Id==previous.PreviousId);
  if(key.Length==0){
   if(owned)RestoreSourceDefault(store,previous!);
   if(previous!=null)File.Delete(ProfilePath(OverrideFile));
   return;
  }
  try{
   var target=ResolveSourcePreset(store,key);
   var general=owned?previous!.General:current;
   var pending=new SourcePresetOverride(target.Id,owned?current!.Id:null,general);
   WriteProfileData(OverrideFile,pending);
   InstallAndStorePreset(store,target);
   if(pending.PreviousId!=null)WriteProfileData(OverrideFile,pending with{PreviousId=null});
  }catch{
   if(owned)RestoreSourceDefault(store,previous!);
   File.Delete(ProfilePath(OverrideFile));
   throw;
  }
 }

 void UpdateSourcePreset(string source){
  if(CustomPresetRoot is not string root||customizeWindow!=null||
     string.Equals(lastPresetSource,source,StringComparison.OrdinalIgnoreCase))return;
  lastPresetSource=source;sourcePresetError="";
  try{
   string key=SourceAssignments.GetValueOrDefault(source)??"";
   ChangeSourceAppearance(new PresetStore(root),key);
  }catch(IOException ex)when(ex.InnerException is IOException cause&&(cause.HResult&0xffff) is 32 or 33){
   lastPresetSource=null; // The launcher is applying. Retry after its acknowledgement.
   return;
  }catch(Exception ex){
   sourcePresetError="Не удалось переключить оформление. "+ex.Message;status.Text=sourcePresetError;
  }
  refreshSourcePresetPicker?.Invoke();
 }

 void SetSourcePreset(string source,string key){
  if(source.Length==0)return;
  var updated=new Dictionary<string,string>(SourceAssignments,StringComparer.OrdinalIgnoreCase);
  if(key.Length==0)updated.Remove(source);else updated[source]=key;
  WriteProfileData(AssignmentsFile,updated);sourceAssignments=updated;
  lastPresetSource=null;UpdateSourcePreset(source);
 }

 void AddSourcePresetPicker(Panel panel){
  panel.Children.Add(new TextBlock{Text="Пресет для источника",FontSize=13,Margin=new Thickness(0,12,0,5)});
  var choices=new ComboBox{DisplayMemberPath=nameof(SourcePresetChoice.Label),SelectedValuePath=nameof(SourcePresetChoice.Key),MaxDropDownHeight=240};
  AutomationProperties.SetName(choices,"Пресет для источника");panel.Children.Add(choices);
  var hint=new TextBlock{FontSize=12,Foreground=PreferencesMuted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)};panel.Children.Add(hint);
  bool loading=false;
  void RefreshChoices(){
   loading=true;
   try{
    string source=session?.SourceAppUserModelId??"";
    var options=new List<SourcePresetChoice>{new("","Общее оформление")};
    if(source.Length>0){
     var saved=new PresetStore(CustomPresetRoot!).ListSaved();
     options.AddRange(saved.Where(item=>item.Favorite).Select(item=>new SourcePresetChoice(item.Preset.Id,"★ "+item.Preset.Name)));
     options.AddRange(PresetThemes.Names.Select(name=>new SourcePresetChoice("theme:"+name,"Готовый · "+name)));
     options.AddRange(saved.Where(item=>!item.Favorite).Select(item=>new SourcePresetChoice(item.Preset.Id,"Мой · "+item.Preset.Name)));
    }
    string key=SourceAssignments.GetValueOrDefault(source)??"";
    if(!options.Any(p=>p.Key==key))options.Add(new(key,"Недоступный пресет"));
    choices.ItemsSource=options;choices.SelectedValue=key;choices.IsEnabled=source.Length>0;
    hint.Text=sourcePresetError.Length>0?sourcePresetError:source.Length==0?"Сначала выберите источник трека.":
     source+" · При смене источника применяется его пресет. Без привязки возвращается общее оформление.";
   }catch(Exception ex){choices.IsEnabled=false;hint.Text="Не удалось загрузить привязки. "+ex.Message;}
   finally{loading=false;}
  }
  refreshSourcePresetPicker=RefreshChoices;
  choices.DropDownOpened+=(_,_)=>RefreshChoices();
  choices.SelectionChanged+=(_,_)=>{
   if(loading||choices.SelectedValue is not string key||session?.SourceAppUserModelId is not string source)return;
   try{SetSourcePreset(source,key);}catch(Exception ex){RefreshChoices();hint.Text="Не удалось сохранить привязку. "+ex.Message;}
  };
  RefreshChoices();
 }
}
