using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;

namespace MiniPlayer;

public sealed partial class Player
{
 Window? settingsWindow;
 Action? cancelHotkeyCapture;
 void OpenSettings(bool preview=false){
  if(settingsWindow!=null){if(!preview){RefreshAudioTargets();FocusPreferencesWindow(settingsWindow);_=ReloadMediaSources();}return;}
  var w=CreatePreferencesWindow("Настройки",720);
  settingsWindow=w;
  var ink=PreferencesText;var muted=PreferencesMuted;var purple=PreferencesAccent;
  var shell=new DockPanel();
  w.Content=PreferencesShell(w,new Border{Padding=new Thickness(24,20,18,18),Child=shell});
  var header=new StackPanel{Margin=new Thickness(0,0,0,16)};
  DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
  header.Children.Add(new TextBlock{Text="Настройки плеера",FontSize=27});
  header.Children.Add(new TextBlock{Text="Изменения сохраняются автоматически.",FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});

  var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};
  DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);
  var done=PreferencesButton("Готово");done.Background=purple;done.Foreground=PreferencesBackground;done.MinWidth=100;
  done.IsDefault=true;done.IsCancel=true;done.Click+=(_,_)=>w.Close();
  DockPanel.SetDock(done,Dock.Right);footer.Children.Add(done);
  status.Foreground=muted;status.VerticalAlignment=VerticalAlignment.Center;status.Margin=new Thickness(0,0,12,0);
  footer.Children.Add(status);
  var sections=new StackPanel();
  shell.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=sections});

  StackPanel Section(string title){
   var content=new StackPanel();
   content.Children.Add(new TextBlock{Text=title,FontSize=17,Margin=new Thickness(0,0,0,10),TextWrapping=TextWrapping.Wrap});
   sections.Children.Add(PreferencesCard(content));
   return content;
  }
  foreach(var selector in new[]{sources}){
   selector.MinHeight=38;selector.Padding=new Thickness(12,9,12,9);
   selector.Background=PreferencesBrush("#232530");selector.BorderBrush=PreferencesBrush("#3B3D50");
   selector.Foreground=ink;selector.FontSize=13;selector.MaxDropDownHeight=220;
   selector.HorizontalContentAlignment=HorizontalAlignment.Stretch;
  }
  System.Windows.Automation.AutomationProperties.SetName(sources,"Источник трека и кнопок");
  System.Windows.Automation.AutomationProperties.SetName(volumeSlider,"Громкость приложения");
  var media=Section("Источник трека и кнопок");
  var sourceRow=new DockPanel{LastChildFill=true};
  var reload=PreferencesButton("Обновить");reload.Margin=new Thickness(8,0,0,0);reload.Padding=new Thickness(10,6,10,6);reload.ToolTip="Обновить список музыкальных приложений";
  DockPanel.SetDock(reload,Dock.Right);sourceRow.Children.Add(reload);sourceRow.Children.Add(sources);media.Children.Add(sourceRow);
  reload.Click+=async(_,_)=>{reload.IsEnabled=false;try{await ReloadMediaSources();}finally{reload.IsEnabled=true;}};
  sources.ToolTip="Выберите приложение, чей трек и кнопки будут показаны в плеере.";
  var empty=new TextBlock{Text="Включите музыку или видео, чтобы приложение появилось в списке.",FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
  empty.SetBinding(VisibilityProperty,new Binding("Items.IsEmpty"){Source=sources,Converter=new BooleanToVisibilityConverter()});
  media.Children.Add(empty);
  media.Children.Add(new TextBlock{Text="Чтобы быстро сменить источник, нажмите на название трека в плеере.",FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  if(CustomPresetRoot!=null)AddSourcePresetPicker(media);

  volumeSlider.ClearValue(Control.TemplateProperty);
  var mixer=Section("Громкость");
  var volume=new DockPanel();
  DockPanel.SetDock(volumeLabel,Dock.Right);volume.Children.Add(volumeLabel);volume.Children.Add(volumeSlider);
  mixer.Children.Add(volume);
  audioHint.Foreground=muted;audioHint.Margin=new Thickness(0,6,0,0);mixer.Children.Add(audioHint);

  var behavior=Section("Поведение плеера");
  auto.Foreground=ink;auto.FontSize=13;behavior.Children.Add(auto);
  behavior.Children.Add(new TextBlock{Text="Плеер появляется, когда выбранное приложение свёрнуто, и скрывается, когда его окно открыто. Источник выбирается выше.",FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  AddPausePreference(behavior);
  AddAnchorSettings(Section("Положение на экране"));
  var hotkeys=Section("Горячие клавиши");hotkeys.Children.Add(BuildHotkeySettings());
  hotkeys.Children.Add(new TextBlock{Text=PassThroughHelp,FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  var uninstall=PreferencesButton(new TextBlock{Text=LaunchContext.LauncherRoot==null?"Удалить приложение полностью":"Удалить виджет через лаунчер",TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center});
  uninstall.Foreground=PreferencesDanger;uninstall.Background=PreferencesBrush("#2B202A");uninstall.BorderBrush=PreferencesBrush("#57313B");
  uninstall.FontSize=13;uninstall.Margin=new Thickness(2,3,8,8);uninstall.Padding=new Thickness(12,10,12,10);
  uninstall.Click+=(_,_)=>ConfirmUninstall();sections.Children.Add(uninstall);
  w.IsVisibleChanged+=(_,_)=>{if(!w.IsVisible)cancelHotkeyCapture?.Invoke();};
  w.Closing+=(_,e)=>{if(!closing){e.Cancel=true;w.Hide();}};
  w.Closed+=(_,_)=>settingsWindow=null;
  if(!preview){RefreshAudioTargets();FocusPreferencesWindow(w);_=ReloadMediaSources();}
 }

 FrameworkElement BuildHotkeySettings(){
  var content=new StackPanel();
  var muted=PreferencesMuted;var purple=PreferencesAccent;var errorInk=PreferencesDanger;
  content.Children.Add(new TextBlock{
   Text="Работают в фоне. Нажмите кнопку и нужное сочетание с Ctrl, Alt или Win. F-клавиши и медиаклавиши можно назначать отдельно.",
   FontSize=12,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)
  });
  var message=new TextBlock{
   Text=HotkeyWarning,Foreground=errorInk,FontSize=12,
   TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)
  };
  Button? activeButton=null;
  HotkeyAction activeAction=default;

  string Label(HotkeyAction action)=>hotkeyBindings.TryGetValue(action,out var binding)?HotkeyLabel(binding):"Не назначено";
  void SetLabel(Button button,string text){
   button.Content=new TextBlock{Text=text,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=154};
   button.ToolTip=text;
  }
  void CancelCapture(){
   if(activeButton==null)return;
   SetLabel(activeButton,Label(activeAction));
   activeButton.Background=PreferencesSurface;activeButton.BorderBrush=PreferencesBorder;
   activeButton=null;
   EndHotkeyCapture();
   message.Text=HotkeyWarning;message.Foreground=errorInk;
  }
  cancelHotkeyCapture=CancelCapture;

  foreach(var action in new[]{HotkeyAction.Pause,HotkeyAction.Play,HotkeyAction.Next,HotkeyAction.Previous,HotkeyAction.Pin,HotkeyAction.Unpin,HotkeyAction.Hide,HotkeyAction.Show,HotkeyAction.LockMovement,HotkeyAction.UnlockMovement,HotkeyAction.EnableClickThrough,HotkeyAction.DisableClickThrough}){
   var row=new Grid{Margin=new Thickness(0,3,0,3)};
   row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
   row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   FrameworkElement actionLabel;
   if(action is HotkeyAction.Pin or HotkeyAction.Unpin or HotkeyAction.Hide or HotkeyAction.Show or HotkeyAction.LockMovement or HotkeyAction.UnlockMovement or HotkeyAction.EnableClickThrough or HotkeyAction.DisableClickThrough){
    var label=new TextBlock{FontSize=13,TextWrapping=TextWrapping.Wrap};
    label.Inlines.Add(new Run(HotkeyActionName(action)+" ("));
    var ink=PreferencesText;
    var icon=action is HotkeyAction.Pin or HotkeyAction.Unpin?PlayerIcons.Pin(action==HotkeyAction.Pin,ink):action is HotkeyAction.Hide or HotkeyAction.Show?PlayerIcons.Eye(action==HotkeyAction.Hide,ink):action is HotkeyAction.EnableClickThrough or HotkeyAction.DisableClickThrough?PlayerIcons.ClickThrough(action==HotkeyAction.EnableClickThrough,ink):PlayerIcons.MovementLock(action==HotkeyAction.LockMovement,ink);
    label.Inlines.Add(new InlineUIContainer(icon){BaselineAlignment=BaselineAlignment.Center});
    label.Inlines.Add(new Run(")"));
    System.Windows.Automation.AutomationProperties.SetName(label,HotkeyActionName(action));actionLabel=label;
   }else actionLabel=new TextBlock{Text=HotkeyActionName(action),FontSize=13,TextWrapping=TextWrapping.Wrap};
   actionLabel.VerticalAlignment=VerticalAlignment.Center;actionLabel.HorizontalAlignment=HorizontalAlignment.Left;actionLabel.Margin=new Thickness(0,0,8,0);row.Children.Add(actionLabel);
   var bind=PreferencesButton("");bind.MinWidth=174;bind.MaxWidth=180;
   bind.Padding=new Thickness(8,7,8,7);SetLabel(bind,Label(action));
   Grid.SetColumn(bind,1);row.Children.Add(bind);
   System.Windows.Automation.AutomationProperties.SetName(bind,"Назначить сочетание — "+HotkeyActionName(action));
   bind.Click+=(_,_)=>{
    CancelCapture();
    activeButton=bind;activeAction=action;
    BeginHotkeyCapture();
    SetLabel(bind,"Нажмите сочетание…");
    bind.Background=PreferencesSelected;bind.BorderBrush=purple;
    message.Text="Esc отменяет назначение. Delete очищает сочетание.";message.Foreground=muted;
    bind.Focus();
   };
   bind.PreviewKeyDown+=(_,e)=>{
    if(activeButton!=bind)return;
    var key=e.Key==Key.System?e.SystemKey:e.Key;
    if(key==Key.Tab){CancelCapture();return;}
    e.Handled=true;
    if(key==Key.Escape){CancelCapture();return;}
    if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
    if(e.IsRepeat)return;
    var modifiers=Keyboard.Modifiers;
    HotkeyBinding? binding=(modifiers==ModifierKeys.None&&key is (Key.Delete or Key.Back))?null:new HotkeyBinding(modifiers,key);
    if(!TryAssignHotkey(action,binding,out var error)){message.Text=error;message.Foreground=errorInk;return;}
    CancelCapture();
   };
   bind.LostKeyboardFocus+=(_,_)=>{if(activeButton==bind)CancelCapture();};
   var clear=PreferencesButton("×");clear.Width=28;clear.Padding=new Thickness(0);clear.Margin=new Thickness(6,0,0,0);
   clear.ToolTip="Очистить сочетание — "+HotkeyActionName(action);
   System.Windows.Automation.AutomationProperties.SetName(clear,(string)clear.ToolTip);
   clear.Click+=(_,_)=>{
    CancelCapture();
    if(!TryAssignHotkey(action,null,out var error)){message.Text=error;message.Foreground=errorInk;return;}
    SetLabel(bind,Label(action));message.Text=HotkeyWarning;message.Foreground=errorInk;
   };
   Grid.SetColumn(clear,2);row.Children.Add(clear);content.Children.Add(row);
  }
  content.Children.Add(message);
  return content;
 }

 async System.Threading.Tasks.Task ReloadMediaSources(){
  try{
   manager??=await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
   refreshSourceLabels=true;await Refresh();
  }catch{status.Text="Не удалось получить источники. Откройте музыкальное приложение и нажмите «Обновить».";}
 }

 void RefreshAudioTargets(){
  if(VolumeIsBeingEdited)return;
  try{
   volumeSync=true;
   var entries=Audio.List();var source=session?.SourceAppUserModelId??"";
   audioSource=source;audioTarget=Audio.Match(source,entries)??"";
   if(audioTarget.Length==0&&source.Length>0){var processes=SourceWindowTracker.ResolveProcessNames(source);audioTarget=entries.FirstOrDefault(entry=>processes.Contains(entry.Name,StringComparer.OrdinalIgnoreCase))?.Name??"";}
   var selected=entries.FirstOrDefault(e=>e.Name==audioTarget);
   volumeSlider.IsEnabled=selected!=null;
   audioHint.Text=selected!=null?"Громкость выбранного источника. Для браузера меняется звук всех его вкладок.":"Громкость станет доступна, когда выбранный источник начнёт воспроизведение звука.";
   volumeSlider.ToolTip=audioHint.Text;
   if(selected!=null){volumeSlider.Value=selected.Volume;volumeLabel.Text=$"{selected.Volume:0}%";}
   else volumeLabel.Text="—";
  }catch{
   audioHint.Text="Громкость выбранного источника сейчас недоступна.";volumeSlider.IsEnabled=false;volumeLabel.Text="—";
   volumeSlider.ToolTip=audioHint.Text;
  }finally{volumeSync=false;}
 }
}
