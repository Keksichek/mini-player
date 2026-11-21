using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 const string BackupFilter="Резервная копия VibeWidgetsHub (*.vibebackup)|*.vibebackup";
 BackupPreview? selectedBackup;

 void RenderBackupSettings(){
  var body=new StackPanel();body.Children.Add(Label("Резервная копия",17));
  body.Children.Add(Label("Пресеты и их фоны, избранное, привязки к источникам, горячие клавиши и настройки хаба в одном файле.",12,MutedBrush,new Thickness(0,8,0,12)));
  var actions=new WrapPanel();body.Children.Add(actions);
  var create=ActionButton("Создать копию…",ExportHubBackup);create.Margin=new Thickness(0,0,8,6);actions.Children.Add(create);
  actions.Children.Add(ActionButton("Выбрать копию…",ChooseHubBackup));
  if(selectedBackup is {} chosen){
   var files=chosen.Manifest.Files.Keys;
   string names=string.Join(", ",chosen.Manifest.Widgets.Select(id=>catalog.FirstOrDefault(w=>w.Id==id)?.Name??id));
   body.Children.Add(Label(Path.GetFileName(chosen.FileName),16,TextBrush,new Thickness(0,12,0,4)));
   body.Children.Add(Label($"Создана {chosen.Manifest.Created.ToLocalTime():g}\nВиджеты: {(names.Length==0?"нет":names)}\n"+
    $"Пресетов: {files.Count(p=>p.Contains("/Presets/"))}  ·  Фонов: {files.Count(p=>p.Contains("/Media/"))}  ·  В избранном: {files.Count(p=>p.Contains("/Favorites/"))}",13));
   var included=new[]{
    ("hub-settings.json","настройки хаба"),("mini-player/settings.json","настройки и положение плеера"),("mini-player/hotkeys.json","горячие клавиши"),
    ("mini-player/source-presets.json","привязки к источникам"),("mini-player/appearance.json","оформление"),("Customization/mini-player/draft.json","черновик мастерской")};
   var details=included.Where(pair=>files.Contains(pair.Item1)).Select(pair=>pair.Item2).ToArray();
   if(details.Length>0)body.Children.Add(Label("В копии также есть "+string.Join(", ",details)+".",12,MutedBrush,new Thickness(0,6,0,0)));
   body.Children.Add(Label("Текущие настройки и библиотека пресетов будут заменены. Хаб установит недостающие виджеты из своего каталога и перезапустит работавшие виджеты.",12,MutedBrush,new Thickness(0,8,0,10)));
   var confirm=new WrapPanel();body.Children.Add(confirm);
   var restore=ActionButton("Восстановить из копии",()=>RunAction(()=>RestoreHubBackup(chosen),"Резервная копия восстановлена."),true);
   restore.Margin=new Thickness(0,0,8,4);confirm.Children.Add(restore);
   confirm.Children.Add(ActionButton("Отмена",()=>{selectedBackup=null;Render();}));
  }
  content.Children.Add(Card(body,new Thickness(25)));
 }
 void FlushWorkshopDraft(){
  SavePresetDraft();
  if(presetDraftDirty)throw new IOException("Не удалось сохранить текущий черновик мастерской.");
 }
 void ExportHubBackup(){
  if(busy||updatePending)return;
  var picker=new SaveFileDialog{Title="Сохранить копию VibeWidgetsHub",Filter=BackupFilter,DefaultExt=".vibebackup",
   AddExtension=true,FileName=$"VibeWidgetsHub {DateTime.Now:yyyy-MM-dd HH-mm}"};
  if(picker.ShowDialog(this)!=true)return;
  RunAction(async()=>{FlushWorkshopDraft();await Task.Run(()=>HubBackupArchive.Export(manager,picker.FileName));},
   "Копия сохранена в "+picker.FileName);
 }
 void ChooseHubBackup(){
  if(busy||updatePending)return;
  var picker=new OpenFileDialog{Title="Просмотреть резервную копию",Filter=BackupFilter,CheckFileExists=true};
  if(picker.ShowDialog(this)!=true)return;
  ReviewHubBackup(picker.FileName);
 }
 void ReviewHubBackup(string path){
  selectedBackup=null;
  RunAction(async()=>{selectedBackup=await Task.Run(()=>HubBackupArchive.ReadArchive(path));},"");
 }
 async Task RestoreHubBackup(BackupPreview chosen){
  FlushWorkshopDraft();
  bool restored=false;
  try{await HubBackupRestore.RestoreAsync(manager,chosen);restored=true;}
  catch(HubBackupRestore.CommittedException){restored=true;throw;}
  finally{
   selectedBackup=null;
   if(restored){
    presetDraftTimer.Stop();presetDraftDirty=false;presetDraft=null;ResetPresetHistory();
    customizationStep="home";checkedUpdate=null;checkedGitHubUpdate=null;checkedUpdateFeed="";updateMessage="";
    preferences=HubPreferences.Load(manager.Root);ApplyHubTheme();
   }
  }
 }
}
