using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Windows.Media.Control;

namespace MiniPlayer;

public sealed partial class Player
{
 ContextMenu? sourceMenu;

 void EnableSourcePicker(TextBlock titleLabel){
  titleLabel.Focusable=true;
  titleLabel.Cursor=Cursors.Hand;
  System.Windows.Automation.AutomationProperties.SetHelpText(titleLabel,"Выбрать источник трека");
  Point? pressed=null;
  titleLabel.MouseLeftButtonDown+=(_,e)=>pressed=titleLabel.PointToScreen(e.GetPosition(titleLabel));
  titleLabel.MouseLeftButtonUp+=(_,e)=>{
   if(pressed is not Point start)return;
   pressed=null;
   Vector distance=titleLabel.PointToScreen(e.GetPosition(titleLabel))-start;
   if(Math.Abs(distance.X)>SystemParameters.MinimumHorizontalDragDistance ||
      Math.Abs(distance.Y)>SystemParameters.MinimumVerticalDragDistance)return;
   e.Handled=true;OpenSourcePicker(titleLabel);
  };
  titleLabel.KeyDown+=(_,e)=>{
   if(e.Key!=Key.Enter && e.Key!=Key.Space)return;
   e.Handled=true;OpenSourcePicker(titleLabel);
  };
 }

 void OpenSourcePicker(FrameworkElement anchor){
  if(sourceMenu!=null)sourceMenu.IsOpen=false;
  var popup=new ContextMenu{PlacementTarget=anchor,Placement=PlacementMode.Bottom,MaxHeight=300};
  sourceMenu=popup;
  System.Windows.Automation.AutomationProperties.SetName(popup,"Источник трека и кнопок");
  popup.Closed+=(_,_)=>{if(sourceMenu==popup)sourceMenu=null;};
  FillSourcePicker();
  popup.IsOpen=true;
  _=ReloadMediaSources();
 }

 void FillSourcePicker(){
  if(sourceMenu is not ContextMenu popup)return;
  popup.Items.Clear();
  for(int i=0;i<sessions.Count;i++){
   var candidate=sessions[i];
   string label=i<sources.Items.Count?sources.Items[i]?.ToString()??candidate.SourceAppUserModelId:candidate.SourceAppUserModelId;
   var entry=new MenuItem{Header=new TextBlock{Text=label,MaxWidth=350,TextTrimming=TextTrimming.CharacterEllipsis},IsCheckable=true,IsChecked=Equals(candidate,session),ToolTip=label};
   entry.Click+=(_,_)=>SelectQuickSource(candidate);
   popup.Items.Add(entry);
  }
  if(sessions.Count==0)popup.Items.Add(new MenuItem{Header="Нет источников · включите музыку или видео",IsEnabled=false});
 }

 void SelectQuickSource(GlobalSystemMediaTransportControlsSession selected){
  int index=sessions.IndexOf(selected);
  if(index>=0)sources.SelectedIndex=index;
 }
}
