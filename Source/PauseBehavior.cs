using System;
using System.Windows;
using System.Windows.Controls;

namespace MiniPlayer;

public sealed partial class Player
{
 enum PauseMode { KeepVisible, Dim, HideAfterDelay }
 PauseMode pauseMode;
 bool sourcePaused, hiddenForPause, ignoreCurrentPause;
 long pauseStarted;
 const int PauseDelayMs=5000;

 bool SuppressPauseDisplay => sourcePaused && !ignoreCurrentPause && pauseMode==PauseMode.HideAfterDelay &&
                              Environment.TickCount64-pauseStarted>=PauseDelayMs;

 void ObservePause(bool paused){
  if(sourcePaused!=paused){
   sourcePaused=paused;
   pauseStarted=Environment.TickCount64;
   ignoreCurrentPause=false;
  }
  Opacity=paused && pauseMode==PauseMode.Dim ? .45 : 1;
  if(SuppressPauseDisplay){
   if(IsVisible){hiddenForPause=true;Hide();}
  }else if(hiddenForPause){
   hiddenForPause=false;
   if(auto.IsChecked==true && previousSourceVisibility==false && !shownByUser)return;
   bool activate=ShowActivated;
   try{ShowActivated=false;Show();}finally{ShowActivated=activate;}
  }
 }

 void ManualPauseVisibility(){
  hiddenForPause=false;
  ignoreCurrentPause=sourcePaused;
 }

 void AddPausePreference(StackPanel section){
  section.Children.Add(new TextBlock{Text="Когда выбранный источник на паузе",Margin=new Thickness(0,14,0,6)});
  var options=new ComboBox{MinHeight=36,ItemsSource=new[]{"Оставить как есть","Приглушить плеер","Скрыть через 5 секунд"},SelectedIndex=(int)pauseMode};
  System.Windows.Automation.AutomationProperties.SetName(options,"Поведение на паузе");
  options.SelectionChanged+=(_,_)=>{
   if(options.SelectedIndex<0)return;
   pauseMode=(PauseMode)options.SelectedIndex;
   pauseStarted=Environment.TickCount64;ignoreCurrentPause=false;
   ObservePause(sourcePaused);Save();
  };
  section.Children.Add(options);
 }
}
