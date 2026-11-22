using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace WidgetLauncher;
public sealed partial class LauncherWindow
{
 ObsBridge? obs;
 void ConfigureObs(){obs=new ObsBridge(manager);if(!preview){try{if(obs.Settings.Widgets.Any(x=>x.Value.Enabled))obs.Start();}catch(Exception ex){message="OBS: "+ex.Message;}}Closed+=(_,_)=>obs.Dispose();}
 void RenderObs(){
  if(obs==null)return;ConfigurePresetControls();
  content.Children.Add(Label("Добавьте в OBS источник «Браузер» и вставьте ссылку. Хаб должен оставаться запущенным; его можно свернуть в трей. Фон за карточкой прозрачный. Рекомендуемый размер: 368 × 116, для заметок — 640 × 480.",13,MutedBrush,new Thickness(0,0,0,18)));
  var portPanel=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,24)};var portLabel=Label("Порт",13,MutedBrush);portLabel.VerticalAlignment=VerticalAlignment.Center;portLabel.Margin=new Thickness(0,0,12,0);portPanel.Children.Add(portLabel);
  var port=new TextBox{Text=obs.Settings.Port.ToString(),Width=100,Height=40,Padding=new Thickness(12,0,12,0),MaxLength=5,VerticalContentAlignment=VerticalAlignment.Center,Background=PanelBrush,Foreground=TextBrush,CaretBrush=TextBrush,SelectionBrush=AccentBrush,BorderBrush=ColorBrush("#2B2C3A"),Template=(ControlTemplate)Resources["PresetTextBoxTemplate"]};System.Windows.Automation.AutomationProperties.SetName(port,"Порт OBS");portPanel.Children.Add(port);
  var change=ActionButton("Сохранить порт",()=>{if(!int.TryParse(port.Text,out int n)||n<1024||n>65535){message="Укажите порт от 1024 до 65535.";Render();return;}obs.Settings.Port=n;obs.Save();message="Порт сохранён. Перезапустите хаб и обновите ссылки в OBS.";Render();});change.Height=40;change.Padding=new Thickness(18,0,18,0);change.VerticalAlignment=VerticalAlignment.Center;change.Margin=new Thickness(12,0,0,0);change.IsEnabled=!obs.Running;port.IsEnabled=!obs.Running;port.Opacity=port.IsEnabled?1:.5;portPanel.Children.Add(change);content.Children.Add(portPanel);
  foreach(var widget in catalog.Where(w=>w.Available)){
   var panel=new StackPanel();panel.Children.Add(Label(widget.Name,21));bool supported=widget.Id=="notes"||widget.ObsProtocol==1;
   if(!supported){panel.Children.Add(Label("Разработчик ещё не добавил поддержку OBS (протокол 1).",12,MutedBrush));content.Children.Add(Card(panel,new Thickness(22)));continue;}
   if(!manager.IsInstalled(widget)){panel.Children.Add(Label("Сначала установите виджет в каталоге.",12,MutedBrush));content.Children.Add(Card(panel,new Thickness(22)));continue;}
   var entry=obs.Entry(widget.Id);
   if(widget.Id=="notes"){
    panel.Children.Add(Label("В эфир попадёт только выбранная запись. Рисунки и другие записи не передаются.",12,MutedBrush,new Thickness(0,10,0,10)));
    var select=new ComboBox{Margin=new Thickness(0,0,0,12)};select.Items.Add(new ComboBoxItem{Content="Не выбрано",Tag=""});select.SelectedIndex=0;
    foreach(var note in obs.Notes()){var item=new ComboBoxItem{Content=note.Text.Length>90?note.Text[..90]+"…":note.Text,Tag=note.Id};select.Items.Add(item);if(note.Id==entry.NoteId)select.SelectedItem=item;}
    select.SelectionChanged+=(_,_)=>{entry.NoteId=(string)((ComboBoxItem)select.SelectedItem).Tag;obs.Save();};panel.Children.Add(select);var reload=ActionButton("Обновить список заметок",()=>Render());reload.HorizontalAlignment=HorizontalAlignment.Left;reload.Margin=new Thickness(0,0,0,12);panel.Children.Add(reload);
   }else panel.Children.Add(Label("Запустите виджет. Данные обновляются раз в секунду. Это отдельная карточка для эфира, без кнопок управления.",12,MutedBrush,new Thickness(0,10,0,12)));
   var row=new Grid();
   void AddAction(Button button){if(row.ColumnDefinitions.Count>0)row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});int column=row.ColumnDefinitions.Count;row.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetColumn(button,column);button.Height=44;button.Margin=new Thickness(0);button.Padding=new Thickness(10,0,10,0);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.VerticalAlignment=VerticalAlignment.Center;button.FontSize=12;row.Children.Add(button);}
   var toggle=ActionButton(entry.Enabled?"Отключить OBS":"Включить OBS",()=>{try{if(widget.Id=="notes"&&!entry.Enabled&&entry.NoteId.Length==0)throw new InvalidOperationException("Сначала выберите запись для показа.");obs.Enable(widget,!entry.Enabled);message="";}catch(Exception ex){message="Не удалось включить OBS: "+ex.Message;}Render();},true);AddAction(toggle);
   var copy=ActionButton("Скопировать ссылку",()=>{Clipboard.SetText(obs.Url(widget.Id));message="Ссылка скопирована. Вставьте её в источник «Браузер» в OBS.";notice.Text=message;notice.Visibility=Visibility.Visible;});copy.IsEnabled=entry.Enabled&&obs.Running;AddAction(copy);if(widget.Id=="mini-player"){var customize=ActionButton("Настроить оформление",OpenObsEditor);AddAction(customize);}panel.Children.Add(row);
   if(entry.Enabled){var link=new TextBox{Text=obs.Url(widget.Id),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};panel.Children.Add(link);}
   content.Children.Add(Card(panel,new Thickness(22)));
  }
 }
}
