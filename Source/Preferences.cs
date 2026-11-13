using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace MiniPlayer;

public sealed partial class Player
{
 Window? settingsWindow;
 static readonly ControlTemplate preferencesButtonTemplate=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#7653AD'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='.78'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");

 static Window CreatePreferencesWindow(string title,double height)=>new(){
  Title="Mini Player · "+title,
  Width=Math.Min(552,SystemParameters.WorkArea.Width),
  Height=Math.Min(height,SystemParameters.WorkArea.Height),
  WindowStartupLocation=WindowStartupLocation.CenterScreen,
  Topmost=true,
  Background=BrushOf("#F8F6FA"),Foreground=BrushOf("#292535"),
  FontFamily=PlayerApplication.InterfaceFont,FontWeight=FontWeights.Bold
 };

 static Button PreferencesButton(object label)=>new(){
  Content=label,Background=Brushes.White,Foreground=BrushOf("#292535"),
  BorderBrush=BrushOf("#DDD8E4"),Padding=new Thickness(12,8,12,8),Cursor=Cursors.Hand,
  Template=preferencesButtonTemplate
 };

 void OpenSettings(bool preview=false){
  if(settingsWindow!=null){if(!preview){RefreshAudioTargets();settingsWindow.Show();settingsWindow.Activate();}return;}
  var w=CreatePreferencesWindow("Настройки",600);
  settingsWindow=w;
  var ink=w.Foreground;var muted=BrushOf("#645E71");var purple=BrushOf("#6544A0");
  w.Resources["AccentBrush"]=purple;
  var shell=new DockPanel();
  w.Content=new Border{Background=w.Background,Padding=new Thickness(20,14,20,12),Child=shell};
  var header=new StackPanel{Margin=new Thickness(0,0,0,16)};
  DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
  header.Children.Add(new TextBlock{Text="Настройки плеера",FontSize=25});
  header.Children.Add(new TextBlock{Text="Изменения сохраняются автоматически.",FontSize=13,Foreground=muted,TextWrapping=TextWrapping.Wrap});

  var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};
  DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);
  var done=PreferencesButton("Готово");done.Background=purple;done.Foreground=Brushes.White;
  done.IsDefault=true;done.IsCancel=true;done.Click+=(_,_)=>w.Close();
  DockPanel.SetDock(done,Dock.Right);footer.Children.Add(done);
  status.Foreground=muted;status.VerticalAlignment=VerticalAlignment.Center;status.Margin=new Thickness(0,0,12,0);
  footer.Children.Add(status);
  var sections=new StackPanel();
  shell.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=sections});

  StackPanel Section(string title){
   var content=new StackPanel();
   content.Children.Add(new TextBlock{Text=title,FontSize=17,Margin=new Thickness(0,0,0,10),TextWrapping=TextWrapping.Wrap});
   sections.Children.Add(new Border{Background=Brushes.White,CornerRadius=new CornerRadius(12),Padding=new Thickness(16),Margin=new Thickness(2,3,2,8),Child=content});
   return content;
  }
  foreach(var selector in new[]{sources,audioTargets}){
   selector.MinHeight=36;selector.Padding=new Thickness(8,4,8,4);
   selector.Background=Brushes.White;selector.BorderBrush=BrushOf("#DDD8E4");
   selector.Foreground=ink;selector.FontSize=13;selector.MaxDropDownHeight=220;
   selector.HorizontalContentAlignment=HorizontalAlignment.Stretch;
  }
  System.Windows.Automation.AutomationProperties.SetName(sources,"Источник трека и кнопок");
  System.Windows.Automation.AutomationProperties.SetName(audioTargets,"Приложение в микшере Windows");
  System.Windows.Automation.AutomationProperties.SetName(volumeSlider,"Громкость приложения");
  var media=Section("Источник трека и кнопок");media.Children.Add(sources);
  var empty=new TextBlock{Text="Включите музыку или видео, чтобы приложение появилось в списке.",FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
  empty.SetBinding(VisibilityProperty,new Binding("Items.IsEmpty"){Source=sources,Converter=new BooleanToVisibilityConverter()});
  media.Children.Add(empty);

  var mixer=Section("Приложение в микшере Windows");mixer.Children.Add(audioTargets);
  mixer.Children.Add(new TextBlock{Text="Громкость приложения",FontSize=13,Margin=new Thickness(0,12,0,2)});
  var volume=new DockPanel();
  DockPanel.SetDock(volumeLabel,Dock.Right);volume.Children.Add(volumeLabel);volume.Children.Add(volumeSlider);
  mixer.Children.Add(volume);
  audioHint.Foreground=muted;audioHint.Margin=new Thickness(0,6,0,0);mixer.Children.Add(audioHint);

  var behavior=Section("Поведение плеера");
  auto.Foreground=ink;auto.FontSize=13;behavior.Children.Add(auto);
  behavior.Children.Add(new TextBlock{Text="Автопоказ при сворачивании Яндекс Музыки.",FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  w.Closing+=(_,e)=>{if(!closing){e.Cancel=true;w.Hide();}};
  w.Closed+=(_,_)=>settingsWindow=null;
  if(!preview){RefreshAudioTargets();w.Show();w.Activate();}
 }

 void RefreshAudioTargets(){
  try{
   volumeSync=true;
   var entries=Audio.List();var source=session?.SourceAppUserModelId??"";
   if(audioSource!=source){audioSource=source;audioTarget="";}
   if(!entries.Any(e=>e.Name==audioTarget))audioTarget=Audio.Match(source,entries)??"";
   if(!entries.Select(e=>e.Name).SequenceEqual(audioTargets.Items.Cast<string>())){
    audioTargets.Items.Clear();foreach(var entry in entries)audioTargets.Items.Add(entry.Name);
   }
   audioTargets.IsEnabled=entries.Count>0;
   audioTargets.SelectedItem=audioTarget;
   var selected=entries.FirstOrDefault(e=>e.Name==audioTarget);
   volumeSlider.IsEnabled=selected!=null;
   audioHint.Text=selected!=null?"":entries.Count==0?"Включите звук в приложении, чтобы оно появилось в микшере.":"Выберите приложение для регулировки громкости.";
   if(selected!=null){volumeSlider.Value=selected.Volume;volumeLabel.Text=$"{selected.Volume:0}%";}
   else volumeLabel.Text="—";
  }catch{
   audioHint.Text="Микшер Windows недоступен.";audioTargets.IsEnabled=false;volumeSlider.IsEnabled=false;volumeLabel.Text="—";
  }finally{volumeSync=false;}
 }
}
