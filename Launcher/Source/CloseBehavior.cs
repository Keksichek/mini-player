using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 bool bypassClose,closeChoicePending;
 Window? closeChoiceWindow;
 System.Windows.Forms.NotifyIcon? launcherTray;
 System.Drawing.Icon? launcherTrayImage;

 void ConfigureCloseBehavior(){
  Closing+=(_,e)=>{
   if(preview||bypassClose||e.Cancel)return;
   if(updatePending){e.Cancel=true;message="Дождитесь завершения обновления.";notice.Text=message;return;}
   if(preferences.CloseMode==2)return;
   e.Cancel=true;
   if(closeChoicePending){closeChoiceWindow?.Activate();return;}
   closeChoicePending=true;
   Dispatcher.BeginInvoke((Action)(()=>{
    try{
     if(bypassClose)return;
     if(preferences.CloseMode==1)HideLauncherToTray();else OpenCloseChoice();
    }finally{closeChoicePending=false;}
   }));
  };
 }

 public void PrepareForSystemShutdown(){bypassClose=true;closeChoiceWindow?.Close();}

 void HideLauncherToTray(){
  try{
   if(launcherTray==null){
    string icon=Path.Combine(manager.Root,"Launcher.ico");
    launcherTrayImage=File.Exists(icon)?new System.Drawing.Icon(icon):(System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    launcherTray=new System.Windows.Forms.NotifyIcon{Icon=launcherTrayImage,Text="VibeWidgetsHub alpha"};
    var menu=new System.Windows.Forms.ContextMenuStrip();
    menu.Items.Add("Открыть лаунчер",null,(_,_)=>Dispatcher.Invoke(BringForward));
    menu.Items.Add("Настройки",null,(_,_)=>Dispatcher.Invoke(()=>{page="settings";Render();BringForward();}));
    menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
    menu.Items.Add("Закрыть полностью",null,(_,_)=>Dispatcher.Invoke(RequestShutdown));
    launcherTray.ContextMenuStrip=menu;
    launcherTray.DoubleClick+=(_,_)=>Dispatcher.Invoke(BringForward);
   }
   launcherTray.Visible=true;Hide();
  }catch(Exception ex){message="Не удалось свернуть в трей: "+ex.Message;Render();}
 }

 void DisposeLauncherTray(){
  if(launcherTray!=null){launcherTray.Visible=false;launcherTray.ContextMenuStrip?.Dispose();launcherTray.Dispose();launcherTray=null;}
  launcherTrayImage?.Dispose();launcherTrayImage=null;
 }

 void RenderCloseSettings(){
  var panel=new StackPanel();panel.Children.Add(Label("Закрытие окна",17));
  panel.Children.Add(Label("Что делать при нажатии крестика. Работающие виджеты продолжат работать.",12,MutedBrush,new Thickness(0,8,0,14)));
  var buttons=new WrapPanel();panel.Children.Add(buttons);
  string[] names={"Спрашивать каждый раз","Сворачивать в трей","Закрывать полностью"};
  for(int i=0;i<names.Length;i++){
   int mode=i;
   var button=ActionButton(names[i],()=>RunAction(()=>{
    int previous=preferences.CloseMode;preferences.CloseMode=mode;
    try{SaveHubPreferences();}catch{preferences.CloseMode=previous;throw;}return Task.CompletedTask;
   },"Действие при закрытии сохранено."));
   button.Padding=new Thickness(12,10,12,10);button.Margin=new Thickness(0,0,7,7);
   button.Background=preferences.CloseMode==mode?ColorBrush("#30283F"):ColorBrush("#232530");
   button.Foreground=preferences.CloseMode==mode?AccentBrush:TextBrush;
   System.Windows.Automation.AutomationProperties.SetName(button,names[i]);buttons.Children.Add(button);
  }
  content.Children.Add(Card(panel,new Thickness(25)));
 }

 Window CreateCloseChoice(){
  var dialog=new Window{
   Title="VibeWidgetsHub · Закрытие",Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,
   Width=Math.Min(520,SystemParameters.WorkArea.Width),SizeToContent=SizeToContent.Height,
   ResizeMode=ResizeMode.NoResize,WindowStyle=WindowStyle.None,ShowInTaskbar=false,
   Background=BackgroundBrush,Foreground=TextBrush,FontFamily=FontFamily,FontWeight=FontWeight,FontSize=13,Icon=Icon,UseLayoutRounding=true
  };
  dialog.Resources.MergedDictionaries.Add(Resources);
  WindowChrome.SetWindowChrome(dialog,new WindowChrome{CaptionHeight=44,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
  dialog.SourceInitialized+=(_,_)=>{int dark=preferences.LightMode?0:1,border=preferences.LightMode?0x00E6D7DC:0x003A2C2B,corners=2;var handle=new WindowInteropHelper(dialog).Handle;DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,34,ref border,4);DwmSetWindowAttribute(handle,33,ref corners,4);};
  var shell=new DockPanel();
  var caption=new DockPanel{Height=44,Background=ColorBrush("#15161E")};DockPanel.SetDock(caption,Dock.Top);shell.Children.Add(caption);
  var close=new Button{Content=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"),Width=12,Height=12,Stroke=TextBrush,StrokeThickness=1.4,Stretch=Stretch.Uniform},Width=40,Height=32,Margin=new Thickness(0,0,7,0),Background=Brushes.Transparent,Padding=new Thickness(0),ToolTip="Отмена"};
  System.Windows.Automation.AutomationProperties.SetName(close,"Отмена закрытия");WindowChrome.SetIsHitTestVisibleInChrome(close,true);close.Click+=(_,_)=>dialog.Close();DockPanel.SetDock(close,Dock.Right);caption.Children.Add(close);
  caption.Children.Add(new TextBlock{Text="VibeWidgetsHub",FontSize=12,Foreground=TextBrush,Margin=new Thickness(22,0,0,0),VerticalAlignment=VerticalAlignment.Center});
  var body=new StackPanel{Margin=new Thickness(26,22,26,24)};shell.Children.Add(body);
  body.Children.Add(Label("Закрыть лаунчер?",25));
  body.Children.Add(Label("Можно оставить его в трее возле часов или завершить работу лаунчера. Виджеты продолжат работать.",12,MutedBrush,new Thickness(0,10,0,20)));
  var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());body.Children.Add(row);
  var tray=ActionButton("В трей",()=>{} ,true);tray.Margin=new Thickness(0,0,8,0);tray.IsDefault=true;row.Children.Add(tray);
  var exit=ActionButton("Закрыть полностью",()=>{});exit.Foreground=ColorBrush("#F0A2AC");Grid.SetColumn(exit,1);row.Children.Add(exit);
  var remember=new CheckBox{Content="Не показывать больше",Margin=new Thickness(0,20,0,0)};body.Children.Add(remember);
  body.Children.Add(Label("В случае чего это можно изменить в главных настройках лаунчера → Закрытие окна.",11,MutedBrush,new Thickness(0,8,0,0)));
  var error=Label("",12,ColorBrush("#F0A2AC"),new Thickness(0,12,0,0));error.Visibility=Visibility.Collapsed;body.Children.Add(error);
  bool Choose(int mode){
   if(remember.IsChecked==true){int previous=preferences.CloseMode;preferences.CloseMode=mode;try{SaveHubPreferences();}catch(Exception ex){preferences.CloseMode=previous;error.Text="Не удалось запомнить выбор: "+ex.Message;error.Visibility=Visibility.Visible;return false;}}
   dialog.Tag=mode;dialog.Close();return true;
  }
  tray.Click+=(_,_)=>Choose(1);exit.Click+=(_,_)=>Choose(2);
  dialog.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;dialog.Close();}};
  dialog.Content=new Border{Background=BackgroundBrush,BorderBrush=ColorBrush("#2B2C3A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=shell};
  return dialog;
 }

 void OpenCloseChoice(){
  if(closeChoiceWindow!=null){closeChoiceWindow.Activate();return;}
  var dialog=CreateCloseChoice();closeChoiceWindow=dialog;
  try{dialog.ShowDialog();}
  finally{closeChoiceWindow=null;}
  if(bypassClose||dialog.Tag is not int mode)return;
  if(mode==1)HideLauncherToTray();else RequestShutdown();
 }
}
