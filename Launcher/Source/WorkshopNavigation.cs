using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 bool workshopRequestDeferred,workshopSwitchPending,workshopRecoveredDraft;
 (string Id,string Theme)? workshopClassicId;

 internal void OpenMiniPlayerWorkshop(){
  closeChoiceWindow?.Close();
  BringForward();
  if(updatePending||pendingStop)return;
  if(busy||presetColorPicker!=null){
   workshopRequestDeferred=true;
   presetColorPicker?.Activate();
   return;
  }
  try{
   var store=new PresetStore(manager.Root);
   var active=store.LoadActive();
   var target=active??ReadClassicPreset();
   var current=presetDraft??store.LoadDraft();
   bool alreadyEditing=page=="customize"&&customizationStep=="editor"&&presetDraft!=null;
   if(current!=null){
    bool isClassic=active==null&&workshopClassicId==(current.Id,target.Theme);
    bool sameTarget=isClassic||(target.Id.Length>0&&target.Id==current.Id);
    bool matches=SameContents(current,target);
    bool fromDisk=presetDraft==null;
    bool edited=fromDisk||workshopRecoveredDraft||presetEdits.Count>0||gestureModified;
    bool hasWork=edited&&!matches&&!DraftAlreadySaved(store,current);
    if(matches||(sameTarget&&hasWork)){
     if(!alreadyEditing)ShowWorkshopDraft(current,active==null,fromDisk&&hasWork);
     else if(workshopSwitchPending){workshopSwitchPending=false;Render();}
     return;
    }
    if(hasWork){
     ConfirmWorkshopSwitch(current,fromDisk||workshopRecoveredDraft);
     return;
    }
   }
   // Browsing a saved preset does not replace the last autosaved draft on disk.
   // Protect that draft as well before edits to a new target can replace it.
   if(presetDraft!=null&&store.LoadDraft() is WidgetPreset disk&&disk.Id!=presetDraft.Id&&
      !SameContents(disk,target)&&!DraftAlreadySaved(store,disk)){
    if(disk.Id.Length>0&&disk.Id==target.Id)ShowWorkshopDraft(disk,false,true);
    else ConfirmWorkshopSwitch(disk,true);
    return;
   }
   ShowWorkshopDraft(target,active==null,false);
  }catch(Exception ex){message="Не удалось открыть оформление мини-плеера. "+ex.Message;Render();}
 }

 void ConfirmWorkshopSwitch(WidgetPreset current,bool recovered){
  if(!ReferenceEquals(current,presetDraft)||page!="customize"||customizationStep!="editor")
   ShowWorkshopDraft(current,false,recovered);
  workshopSwitchPending=true;
  Render();pageScroll.ScrollToTop();
 }

 void ResumeWorkshopRequest(){
  if(!workshopRequestDeferred||busy||presetColorPicker!=null)return;
  workshopRequestDeferred=false;
  Dispatcher.BeginInvoke(OpenMiniPlayerWorkshop);
 }

 // Classic faces use their own renderer. Start from the corresponding workshop template.
 WidgetPreset ReadClassicPreset(){
  string file=Path.Combine(manager.Root,"Data","mini-player","appearance.json");
  WidgetManager.RejectLinks(file);
  string theme="Светлая";
  if(File.Exists(file)&&new FileInfo(file).Length<=65536){
   try{
    using var appearance=JsonDocument.Parse(File.ReadAllBytes(file));
    if(appearance.RootElement.ValueKind==JsonValueKind.Object&&appearance.RootElement.TryGetProperty("Theme",out var value)&&
       value.ValueKind==JsonValueKind.String&&value.GetString() is string name&&PresetThemes.Names.Contains(name))theme=name;
   }catch(JsonException){ /* The classic player also falls back to its default theme. */ }
  }
  return PresetThemes.Create(theme);
 }

 static bool SameContents(WidgetPreset left,WidgetPreset right){
  var a=left.Copy();var b=right.Copy();a.Normalize();b.Normalize();
  // Stock/legacy presets may not have an ID until opened in the editor.
  if(a.Id.Length==0||b.Id.Length==0)a.Id=b.Id="";
  return JsonElement.DeepEquals(JsonSerializer.SerializeToElement(a),JsonSerializer.SerializeToElement(b));
 }

 static bool DraftAlreadySaved(PresetStore store,WidgetPreset draft){
  var saved=store.List().FirstOrDefault(p=>p.Id==draft.Id);
  return saved!=null?SameContents(draft,saved):SameContents(draft,PresetThemes.Create(draft.Theme));
 }

 void ShowWorkshopDraft(WidgetPreset preset,bool classic,bool recovered){
  bool resume=ReferenceEquals(preset,presetDraft);
  if(!resume&&presetDraftDirty){
   SavePresetDraft();
   if(presetDraftDirty)throw new IOException("Не удалось записать предыдущий черновик.");
  }
  page="customize";customizationWidget="mini-player";customizationMode=classic?"presets":"saved";
  if(resume){customizationStep="editor";workshopSwitchPending=false;Render();}
  else{
   OpenPreset(preset);
   workshopRecoveredDraft=recovered;
  }
  if(classic){
   workshopClassicId=(presetDraft!.Id,preset.Theme);
   message="Открыта основа темы «"+preset.Theme+"».";
   notice.Text=message;notice.Visibility=Visibility.Visible;
  }
  pageScroll.ScrollToTop();
 }

 void RenderWorkshopSwitch(){
  var panel=new StackPanel();
  panel.Children.Add(Label("В черновике «"+presetDraft!.Name+"» есть изменения. Сохранить их перед переходом к оформлению из плеера?",14));
  var row=new WrapPanel{Margin=new Thickness(0,10,0,0)};panel.Children.Add(row);
  var proceed=PresetButton("Сохранить как копию и перейти",()=>PresetTry(()=>{
   var active=Presets.LoadActive();var target=active??ReadClassicPreset();
   Presets.SaveCopy(presetDraft);
   ShowWorkshopDraft(target,active==null,false);
   Presets.SaveDraft(presetDraft!);
  }),true);proceed.Margin=new Thickness(0,0,8,0);row.Children.Add(proceed);
  row.Children.Add(PresetButton("Остаться",()=>{workshopSwitchPending=false;Render();}));
  content.Children.Add(Card(panel,new Thickness(16)));
 }
}
