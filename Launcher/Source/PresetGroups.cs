using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 bool CanGroup(string id)=>presetDraft!=null&&presetDraft.IsElementVisible(id)&&!presetDraft.Elements[id].Locked;

 void PruneGroup(){
  if(selectedGroup.Count==0)return;
  selectedGroup.RemoveWhere(id=>!CanGroup(id));
  if(selectedGroup.Count>0&&!selectedGroup.Contains(selectedPresetElement))selectedPresetElement=selectedGroup.First();
  if(selectedGroup.Count==1)selectedGroup.Clear();
 }

 void ToggleGroupMember(string id){
  if(!CanGroup(id))return;
  if(presetTab!="Детали"||!CanGroup(selectedPresetElement)){SelectPresetElement(id);return;}
  if(selectedGroup.Count==0)selectedGroup.Add(selectedPresetElement);
  if(!selectedGroup.Remove(id))selectedGroup.Add(id);
  string primary=selectedGroup.Contains(id)?id:selectedGroup.FirstOrDefault()??id;
  if(selectedGroup.Count<=1)selectedGroup.Clear();
  UpdatePresetSelection(primary,true);
 }

 Dictionary<string,Rect> SelectionBounds()=>selectedGroup.ToDictionary(id=>id,id=>{
  var item=presetDraft!.Elements[id];
  return new Rect(item.X,item.Y,item.Width,item.Height);
 });

 void MovePresetGroup(Dictionary<string,Rect> initial,Vector offset){
  if(busy||updatePending||initial.Count<2||!initial.Keys.All(CanGroup))return;
  bool changed=false;
  foreach(var pair in initial){
   Rect target=pair.Value;target.Offset(offset);
   changed|=WritePresetBounds(pair.Key,target);
  }
  if(changed)FinishGeometryChange();
 }

 void AddGroupControls(Panel panel,WidgetPreset preset){
  panel.Children.Add(Label($"Выбрано элементов: {selectedGroup.Count}",15,TextBrush,new Thickness(0,12,0,4)));
  panel.Children.Add(Label(string.Join(", ",preset.LayerOrder.Where(selectedGroup.Contains).Select(PresetSurface.Label)),12,MutedBrush));
  panel.Children.Add(Label("Перетащи любой из выбранных элементов. Ctrl + щелчок добавляет или убирает элемент. Alt отключает привязку.",11,MutedBrush,new Thickness(0,8,0,8)));
  var bounds=SelectionBounds();
  panel.Children.Add(Label("Равные интервалы",12,TextBrush,new Thickness(0,8,0,4)));
  foreach(bool horizontal in new[]{true,false}){
   var button=PresetButton(horizontal?"По горизонтали":"По вертикали",()=>SpaceSelectedElements(horizontal));
   double total=bounds.Values.Sum(box=>horizontal?box.Width:box.Height);
   button.IsEnabled=selectedGroup.Count>=3&&total<=(horizontal?preset.Width:preset.Height);
   button.ToolTip=selectedGroup.Count<3?"Выбери хотя бы три элемента":button.IsEnabled?"Распределить элементы с одинаковыми промежутками":"По этой оси элементы не помещаются без перекрытия";
   ToolTipService.SetShowOnDisabled(button,true);
   button.Margin=new Thickness(0,4,0,0);panel.Children.Add(button);
  }
 }

 void SpaceSelectedElements(bool horizontal){
  if(busy||updatePending||presetDraft==null||selectedGroup.Count<3||!selectedGroup.All(CanGroup))return;
  var boxes=SelectionBounds();
  double Start(Rect box)=>horizontal?box.X:box.Y;
  double Length(Rect box)=>horizontal?box.Width:box.Height;
  double occupied=boxes.Values.Sum(Length);
  double canvas=horizontal?presetDraft.Width:presetDraft.Height;
  if(occupied>canvas)return;
  double first=boxes.Values.Min(Start);
  double last=boxes.Values.Max(box=>Start(box)+Length(box));
  // Keep the outer edges steady when there is room. Spread an overlapping
  // cluster just enough for a small gap, keeping it inside the widget.
  double span=last-first;
  if(span<occupied)span=Math.Min(canvas,occupied+8*(boxes.Count-1));
  double gap=(span-occupied)/(boxes.Count-1);
  double position=Math.Clamp(first,0,Math.Max(0,canvas-span));
  bool changed=false;
  foreach(string id in presetDraft.LayerOrder.Where(boxes.ContainsKey).OrderBy(id=>Start(boxes[id]))){
   Rect box=boxes[id];double value=Math.Clamp(position,0,canvas-Length(box));
   if(horizontal)box.X=value;else box.Y=value;
   changed|=WritePresetBounds(id,box);
   position+=Length(box)+gap;
  }
  if(changed)FinishGeometryChange();
 }
}
