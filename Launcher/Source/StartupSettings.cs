using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 void RenderStartupSettings(){
  var panel=new StackPanel();panel.Children.Add(Label("Автозапуск",17));
  panel.Children.Add(Label("При входе в Windows. Выберите, что запускать автоматически.",12,MutedBrush,new Thickness(0,8,0,14)));
  string[] titles={"Выключен","Автозапуск полностью","Автозапуск только виджетов","Автозапуск только выбранных виджетов","Автозапуск почти полностью"};
  string[] descriptions={"Запускайте лаунчер и виджеты вручную.","Лаунчер и все установленные виджеты.","Все установленные виджеты, без окна лаунчера.","Только отмеченные ниже виджеты, без окна лаунчера.","Лаунчер и отмеченные ниже виджеты."};
  for(int i=0;i<titles.Length;i++){
   int mode=i;bool selected=preferences.AutoStartMode==i;
   var button=ActionButton("",()=>RunAction(()=>{
    int old=preferences.AutoStartMode;preferences.AutoStartMode=mode;
    try{SaveHubPreferences();}catch{preferences.AutoStartMode=old;throw;}return Task.CompletedTask;
   },"Настройки автозапуска сохранены."));
   button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(14,11,14,11);button.Margin=new Thickness(0,0,0,7);
   button.Background=selected?ColorBrush("#30283F"):ColorBrush("#232530");
   var row=new DockPanel();var circle=new Border{Width=17,Height=17,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),BorderBrush=selected?AccentBrush:MutedBrush,Background=selected?AccentBrush:System.Windows.Media.Brushes.Transparent,Margin=new Thickness(0,2,12,0),VerticalAlignment=VerticalAlignment.Top};
   if(selected)circle.Child=new Border{Width=5,Height=5,CornerRadius=new CornerRadius(3),Background=BackgroundBrush,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
   DockPanel.SetDock(circle,Dock.Left);row.Children.Add(circle);var text=new StackPanel();text.Children.Add(Label(titles[i],13,selected?AccentBrush:TextBrush));text.Children.Add(Label(descriptions[i],11,MutedBrush,new Thickness(0,4,0,0)));row.Children.Add(text);button.Content=row;
   System.Windows.Automation.AutomationProperties.SetName(button,titles[i]);panel.Children.Add(button);
  }
  if(preferences.AutoStartMode is 3 or 4){
   panel.Children.Add(Label("Виджеты для автозапуска",13,TextBrush,new Thickness(0,12,0,8)));
   var installed=catalog.Where(widget=>widget.Available&&manager.HasInstallation(widget)).ToArray();
   if(installed.Length==0)panel.Children.Add(Label("В «Моих виджетах» пока пусто. Сначала установите виджет из каталога.",12,MutedBrush));
   var selected=new HashSet<string>(preferences.SelectedWidgets,StringComparer.OrdinalIgnoreCase);
   foreach(var widget in installed){
    bool enabled=selected.Contains(widget.Id);
    var button=ActionButton((enabled?"✓  ":"○  ")+widget.Name,()=>RunAction(()=>{
     var previous=preferences.SelectedWidgets;var next=new HashSet<string>(previous,StringComparer.OrdinalIgnoreCase);if(!next.Remove(widget.Id))next.Add(widget.Id);preferences.SelectedWidgets=next.ToArray();
     try{SaveHubPreferences();}catch{preferences.SelectedWidgets=previous;throw;}return Task.CompletedTask;
    },"Список виджетов для автозапуска сохранён."));
    button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Background=enabled?ColorBrush("#30283F"):ColorBrush("#232530");button.Foreground=enabled?AccentBrush:TextBrush;button.Margin=new Thickness(0,0,0,6);
    System.Windows.Automation.AutomationProperties.SetName(button,"Автозапуск виджета "+widget.Name);panel.Children.Add(button);
   }
   if(installed.Length>0&&!installed.Any(widget=>selected.Contains(widget.Id)))panel.Children.Add(Label(preferences.AutoStartMode==4?"Виджеты не выбраны — автоматически откроется только лаунчер.":"Виджеты не выбраны — автозапуск ничего не откроет.",11,MutedBrush,new Thickness(0,6,0,0)));
  }
  content.Children.Add(Card(panel,new Thickness(25)));
 }
}
