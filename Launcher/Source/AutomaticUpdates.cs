using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 readonly DispatcherTimer automaticUpdateTimer=new(){Interval=TimeSpan.FromSeconds(10)};
 bool automaticUpdateChecking;
 void ConfigureAutomaticUpdates(){
  Loaded+=(_,_)=>{if(!preview)automaticUpdateTimer.Start();};
  Closed+=(_,_)=>{automaticUpdateTimer.Stop();networkUpdateCancellation?.Cancel();gitHubUpdates?.Dispose();};
  automaticUpdateTimer.Tick+=async(_,_)=>{automaticUpdateTimer.Interval=TimeSpan.FromHours(6);await RunAutomaticUpdateAsync();};
 }
 async Task RunAutomaticUpdateAsync(){
  if(preview||automaticUpdateChecking||!preferences.AutoUpdateEnabled||preferences.UpdateSourceMode!=0||busy||updatePending||closeChoicePending||System.IO.File.Exists(System.IO.Path.Combine(manager.Root,".update-transaction","journal.json")))return;
  automaticUpdateChecking=true;
  busy=true;Render();
  try{await CheckForUpdatesAsync();if(preferences.AutoUpdateEnabled&&checkedUpdate!=null&&checkedUpdate.BuildNumber>preferences.AutoUpdateAttemptBuild)await InstallCheckedUpdateAsync();}
  catch(OperationCanceledException){updateMessage="Автоматическая загрузка отменена.";}
  catch(Exception ex){updateMessage="Автообновление не завершено: "+ex.Message;}
  finally{automaticUpdateChecking=false;busy=false;Render();if(pendingStop)RequestShutdown();else ResumeWorkshopRequest();}
 }
 void AddAutomaticUpdateControl(StackPanel panel){
  var toggle=new CheckBox{Content=preferences.AutoUpdateEnabled?"Автообновление: ВКЛ":"Автообновление: ВЫКЛ",IsChecked=preferences.AutoUpdateEnabled,Margin=new Thickness(0,0,0,8),IsEnabled=!updatePending&&(!busy||automaticUpdateChecking||networkUpdateCancellation!=null)};
  System.Windows.Automation.AutomationProperties.SetName(toggle,"Автообновление");
  toggle.Click+=(_,_)=>{
   bool previous=preferences.AutoUpdateEnabled;preferences.AutoUpdateEnabled=toggle.IsChecked==true;
   try{SaveHubPreferences();if(!preferences.AutoUpdateEnabled)networkUpdateCancellation?.Cancel();else{automaticUpdateTimer.Interval=TimeSpan.FromSeconds(3);automaticUpdateTimer.Start();}}
   catch(Exception ex){preferences.AutoUpdateEnabled=previous;message=ex.Message;}
   Render();
  };
  panel.Children.Add(toggle);
  panel.Children.Add(Label("Когда включено, хаб проверяет GitHub при запуске и каждые 6 часов, скачивает новую сборку и перезапускается вместе с работающими виджетами. Повторная автоматическая установка той же неудачной сборки блокируется.",11,MutedBrush,new Thickness(0,0,0,12)));
 }
}
