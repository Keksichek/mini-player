using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 sealed record PresetEdit(string Before,string After,bool Randomized);
 readonly List<PresetEdit> presetEdits=new();
 int editCursor;
 string? recordedPreset;
 object? gestureOwner;
 readonly object previewDrag=new();
 bool gestureModified;
 Button? undoPresetButton,redoPresetButton,previousRandomButton;

 string CapturePreset(){
  var draft=presetDraft!;
  // Give each new draft a stable identity before its first edit, so undo after saving
  // continues editing the same library entry. Nothing reaches the library until Save/Apply.
  if(draft.Id.Length==0)draft.Id=Guid.NewGuid().ToString("N");
  return JsonSerializer.Serialize(draft);
 }

 void ResetPresetHistory(){
  selectedGroup.Clear();
  workshopClassicId=null;workshopRecoveredDraft=false;workshopSwitchPending=false;
  gestureOwner=null;gestureModified=false;
  presetEdits.Clear();editCursor=0;recordedPreset=null;
 }

 void RecordPresetEdit(bool randomized=false){
  if(presetDraft==null)return;
  if(gestureOwner!=null){
   if(!gestureModified){gestureModified=true;RefreshHistoryButtons();}
   return;
  }
  string next=CapturePreset();
  if(recordedPreset!=null && next!=recordedPreset){
   if(editCursor<presetEdits.Count)presetEdits.RemoveRange(editCursor,presetEdits.Count-editCursor);
   presetEdits.Add(new PresetEdit(recordedPreset,next,randomized));
   if(presetEdits.Count>100)presetEdits.RemoveAt(0);
   editCursor=presetEdits.Count;
  }
  recordedPreset=next;RefreshHistoryButtons();
 }

 void BeginPresetGesture(object? owner=null){
  owner??=previewDrag;
  if(ReferenceEquals(gestureOwner,owner))return;
  FinishPresetGesture();
  gestureOwner=owner;
 }

 void EndPresetGesture(object? owner=null){
  if(ReferenceEquals(gestureOwner,owner??previewDrag))FinishPresetGesture();
 }

 void FinishPresetGesture(){
  gestureOwner=null;
  if(!gestureModified)return;
  gestureModified=false;RecordPresetEdit();
 }

 void GroupTextHistory(TextBox field){
  field.GotKeyboardFocus+=(_,_)=>BeginPresetGesture(field);
  field.LostKeyboardFocus+=(_,_)=>EndPresetGesture(field);
  field.Unloaded+=(_,_)=>EndPresetGesture(field);
 }

 void GroupSliderHistory(Slider slider){
  slider.PreviewMouseLeftButtonDown+=(_,_)=>BeginPresetGesture(slider);
  slider.AddHandler(Mouse.MouseUpEvent,new MouseButtonEventHandler((_,_)=>EndPresetGesture(slider)),true);
  slider.LostMouseCapture+=(_,_)=>EndPresetGesture(slider);
  slider.Unloaded+=(_,_)=>EndPresetGesture(slider);
 }

 void RefreshHistoryButtons(){
  if(undoPresetButton!=null)undoPresetButton.IsEnabled=editCursor>0||gestureModified;
  if(redoPresetButton!=null)redoPresetButton.IsEnabled=editCursor<presetEdits.Count&&!gestureModified;
  if(previousRandomButton!=null)previousRandomButton.IsEnabled=presetEdits.Take(editCursor).Any(step=>step.Randomized);
 }

 void StepPresetHistory(bool redo,bool toRandom=false){
  if(busy||updatePending||presetColorPicker!=null||page!="customize"||customizationStep!="editor")return;
  if(ReferenceEquals(gestureOwner,previewDrag)||gestureOwner is Slider { IsMouseCaptureWithin:true })Mouse.Capture(null);
  FinishPresetGesture();
  int index=editCursor;
  if(redo){if(index==presetEdits.Count)return;index++;}
  else{
   if(index==0)return;
   index--;
   if(toRandom){
    while(index>=0&&!presetEdits[index].Randomized)index--;
    if(index<0)return;
   }
  }
  string snapshot=redo?presetEdits[index-1].After:presetEdits[index].Before;
  presetDraft=JsonSerializer.Deserialize<WidgetPreset>(snapshot)!;
  editCursor=index;recordedPreset=snapshot;
  QueuePresetDraft();Render();
 }

 void AddHistoryButtons(Panel panel){
  var bar=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,10,0,0)};
  undoPresetButton=PresetButton("Отменить",()=>StepPresetHistory(false));
  redoPresetButton=PresetButton("Повторить",()=>StepPresetHistory(true));
  undoPresetButton.ToolTip="Отменить изменение (Ctrl+Z)";redoPresetButton.ToolTip="Повторить изменение (Ctrl+Y)";
  AutomationProperties.SetAcceleratorKey(undoPresetButton,"Ctrl+Z");AutomationProperties.SetAcceleratorKey(redoPresetButton,"Ctrl+Y");
  undoPresetButton.Margin=new Thickness(0,0,3,0);redoPresetButton.Margin=new Thickness(3,0,0,0);
  bar.Children.Add(undoPresetButton);bar.Children.Add(redoPresetButton);panel.Children.Add(bar);
  RefreshHistoryButtons();
 }

 void OnPresetHistoryKey(object sender,KeyEventArgs e){
  if(comparisonOwner!=null)return;
  if(e.Handled||Keyboard.Modifiers!=ModifierKeys.Control||e.Key is not (Key.Z or Key.Y)||
     page!="customize"||customizationStep!="editor"||busy||updatePending||presetColorPicker!=null)return;
  // Preserve the native editing commands, including editable combo boxes.
  if(Keyboard.FocusedElement is TextBox input && (e.Key==Key.Z?input.CanUndo:input.CanRedo))return;
  e.Handled=true;StepPresetHistory(e.Key==Key.Y);
 }
}
