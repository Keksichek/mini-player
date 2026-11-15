using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 void SaveHubPreferences(){
   var previous=HubPreferences.Load(manager.Root);
   preferences.Save(manager.Root);
   try{if(!preview)Autostart.Apply(manager.Root,preferences);}
   catch{try{previous.Save(manager.Root);}catch{}throw;}
   if(!preview)foreach(var widget in catalog.Where(x=>x.Available&&manager.IsRunning(x)))
    try{if(EventWaitHandle.TryOpenExisting(manager.InstanceName(widget)+".HubSettings",out var signal))using(signal)signal.Set();}catch{ /* Widgets also poll the saved file. */ }
 }

 void RenderScreenSettings(){
  var body=new StackPanel();body.Children.Add(Label("Границы экрана",17));
  body.Children.Add(Label("Разрешать виджетам выходить за края монитора",13,TextBrush,new Thickness(0,12,0,6)));
  var row=new DockPanel();var state=Label(preferences.AllowOffscreen?"ВКЛ":"ВЫКЛ",12,MutedBrush,new Thickness(12,0,0,0));state.VerticalAlignment=VerticalAlignment.Center;
  var toggle=new CheckBox{IsChecked=preferences.AllowOffscreen,VerticalAlignment=VerticalAlignment.Center,Width=44,Height=26,ToolTip="Выход виджетов за края монитора"};
  System.Windows.Automation.AutomationProperties.SetName(toggle,"Разрешать выход за экран");
  toggle.Template=(ControlTemplate)XamlReader.Parse(UiXaml("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="CheckBox">
 <Grid Background="Transparent"><Border Name="Rail" Background="#383A4B" CornerRadius="13"/><Ellipse Name="Knob" Width="18" Height="18" Fill="#F1F0F7" HorizontalAlignment="Left" Margin="4"/></Grid>
 <ControlTemplate.Triggers>
  <Trigger Property="IsChecked" Value="True"><Setter TargetName="Rail" Property="Background" Value="#B9A1FF"/><Setter TargetName="Knob" Property="Fill" Value="#101117"/><Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/></Trigger>
  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Rail" Property="BorderBrush" Value="#F1F0F7"/><Setter TargetName="Rail" Property="BorderThickness" Value="1"/></Trigger>
  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
"""));
  row.Children.Add(toggle);row.Children.Add(state);body.Children.Add(row);
  body.Children.Add(Label("Если выключено, виджет остаётся целиком в рабочей области ближайшего монитора.",12,MutedBrush,new Thickness(0,10,0,0)));
  toggle.Click+=(_,_)=>RunAction(()=>{bool old=preferences.AllowOffscreen;preferences.AllowOffscreen=toggle.IsChecked==true;try{SaveHubPreferences();}catch{preferences.AllowOffscreen=old;throw;}return Task.CompletedTask;},"Настройки границ экрана сохранены.");
  content.Children.Add(Card(body,new Thickness(25)));
 }

 sealed class FolderOwner(IntPtr handle):System.Windows.Forms.IWin32Window{public IntPtr Handle=>handle;}
 void RequestRelocation(){
  if(busy||updatePending||preview)return;
  using var picker=new System.Windows.Forms.FolderBrowserDialog{Description="Куда перенести VibeWidgetsHub alpha вместе со всеми виджетами",UseDescriptionForTitle=true,ShowNewFolderButton=true,InitialDirectory=Path.GetDirectoryName(manager.Root)??manager.Root};
  if(picker.ShowDialog(new FolderOwner(new WindowInteropHelper(this).Handle))!=System.Windows.Forms.DialogResult.OK)return;
  string parent=Path.GetFullPath(picker.SelectedPath),destination=Path.GetFileName(Path.TrimEndingDirectorySeparator(parent)).Equals("VibeWidgetsHub alpha",StringComparison.OrdinalIgnoreCase)?parent:Path.Combine(parent,"VibeWidgetsHub alpha");
  if(MessageBox.Show(this,"Перенести лаунчер, все виджеты и их настройки?\n\nИз: "+manager.Root+"\nВ: "+destination+"\n\nЛаунчер и работающие виджеты закроются и запустятся из новой папки.","Перенос VibeWidgetsHub",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
  RunAction(()=>{
   string helper=Path.Combine(manager.Root,"Relocate.exe");if(!File.Exists(helper))throw new IOException("Программа переноса отсутствует. Обновите лаунчер установщиком в эту же папку.");
   var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=manager.Root};
   foreach(string value in new[]{"--relocate",manager.Root,parent,"--resume",string.Join(',',catalog.Where(x=>x.Available&&manager.IsRunning(x)).Select(x=>x.Id))})start.ArgumentList.Add(value);
   using var process=Process.Start(start)??throw new IOException("Не удалось начать перенос.");return Task.CompletedTask;
  },"Перенос запускается. Дождитесь повторного открытия лаунчера.");
 }
}
