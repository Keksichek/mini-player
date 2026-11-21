using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WidgetLauncher;

namespace MiniPlayer;

public sealed partial class Player
{
 Action AddAppearancePresets(StackPanel header,Window dialog,string root,Action<WidgetPreset> selected,Func<bool> hasPreview,Action savePreview){
  var row=new Grid{Margin=new Thickness(0,10,0,0)};
  row.ColumnDefinitions.Add(new ColumnDefinition());
  row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  var choices=new ComboBox{MinWidth=0,VerticalAlignment=VerticalAlignment.Stretch};
  AutomationProperties.SetName(choices,"Сохранённые пресеты");
  row.Children.Add(choices);
  var workshop=PreferencesButton("Открыть в мастерской");
  workshop.Margin=new Thickness(8,0,0,0);Grid.SetColumn(workshop,1);row.Children.Add(workshop);
  header.Children.Add(row);
  var feedback=new TextBlock{Foreground=PreferencesMuted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0),Visibility=Visibility.Collapsed};
  header.Children.Add(feedback);
  void Feedback(string message){feedback.Text=message;feedback.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible;}

  bool loading=false;
  void LoadChoices(){
   loading=true;
   try{
    choices.Items.Clear();
    var prompt=new ComboBoxItem{Content="Выбрать пресет",IsEnabled=false};
    choices.Items.Add(prompt);choices.SelectedIndex=0;
    foreach(var (preset,favorite) in new PresetStore(root).ListSaved())
     choices.Items.Add(new ComboBoxItem{Content=(favorite?"★ ":"")+preset.Name,Tag=preset});
    if(choices.Items.Count==1)prompt.Content="Нет сохранённых пресетов";
   }catch(Exception ex){Feedback("Не удалось загрузить пресеты. "+ex.Message);}
   finally{loading=false;}
  }
  LoadChoices();
  choices.DropDownOpened+=(_,_)=>LoadChoices();
  choices.SelectionChanged+=(_,_)=>{
   if(loading||choices.SelectedItem is not ComboBoxItem{Tag:WidgetPreset preset})return;
   try{
    var store=new PresetStore(root);
    ApplySavedAppearance(store,preset);
    selected(preset);
    Feedback("");
   }catch(Exception ex){Feedback("Не удалось применить пресет. "+ex.Message);}
   choices.SelectedIndex=0;
  };
  workshop.Click+=(_,_)=>{
   try{
    if(hasPreview())savePreview();
    var start=new ProcessStartInfo(Path.Combine(root,"Launcher.exe")){WorkingDirectory=root,UseShellExecute=false};
    start.ArgumentList.Add("--edit-mini-player-preset");
    using var process=Process.Start(start)??throw new IOException("Не удалось запустить лаунчер.");
    dialog.Close();
   }catch(Exception ex){Feedback("Не удалось открыть мастерскую. "+ex.Message);}
  };
  return ()=>{
   workshop.Content=hasPreview()?"Сохранить и открыть в мастерской":"Открыть в мастерской";
   Feedback("");
  };
 }
}
