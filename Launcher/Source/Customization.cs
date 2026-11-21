using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 string customizationStep="home",customizationMode="presets",customizationWidget="mini-player",presetTab="Форма";
 WidgetPreset? presetDraft;
 PresetPreview? presetPreview;
 TextBlock? presetStatus;
 readonly System.Windows.Threading.DispatcherTimer presetDraftTimer=new(){Interval=TimeSpan.FromMilliseconds(650)};
 bool presetDraftTimerConfigured,presetDraftDirty;
 readonly Stack<WidgetPreset> presetHistory=new();
 PresetStore Presets=>new(manager.Root,customizationWidget);
 static readonly string[] PresetTabs={"Форма","Цвета","Текст","Детали","Фон"};
 void ReleasePresetPreview(){
  presetPreview?.Dispose();presetPreview=null;presetStatus=null;presetDetailPanel=null;presetTabButtons=null;syncGeometryFields.Clear();
  if(page!="customize"||customizationStep!="editor")largePresetWindow?.Close();
 }
 void PresetNavigate(string step){customizationStep=step;message="";Render();}
 void PresetTry(Action action){try{action();}catch(Exception ex){message="Не удалось сохранить изменения: "+ex.Message;Render();}}
 Button PresetButton(string title,Action action,bool accent=false){var b=ActionButton(title,action,accent);AutomationProperties.SetName(b,title);return b;}
 void RenderCustomization(){
  ConfigurePresetControls();
  if(!presetDraftTimerConfigured){presetDraftTimerConfigured=true;presetDraftTimer.Tick+=(_,_)=>SavePresetDraft();Closed+=(_,_)=>{SavePresetDraft();largePresetWindow?.Close();};}
  if(customizationStep=="home"){RenderCustomizationHome();return;}
  var back=PresetButton("← Назад",()=>PresetNavigate(customizationStep=="widget"?"home":customizationStep=="gallery"?"widget":customizationMode is "presets" or "saved"?"gallery":"widget"));back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new Thickness(0,0,0,18);content.Children.Add(back);
  if(customizationStep=="widget"){RenderPresetWidgetPicker();return;}
  if(customizationStep=="gallery"){RenderPresetGallery();return;}
  RenderPresetEditor();
 }
 void RenderCustomizationHome(){
  var choices=new[]{("presets","Готовые пресеты","Все темы мини-плеера."),("create","Создать свой пресет","Форма, цвета, шрифты и свой фон. Всё под твоё настроение."),("random","Рандомизировать","Случайное сочетание настроек — отправная точка для экспериментов."),("saved","Созданные пресеты","Твои сохранённые варианты. Открой, доработай и примени к плееру.")};
  foreach(var option in choices){
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});
   var text=new StackPanel();text.Children.Add(Label(option.Item2,20));text.Children.Add(Label(option.Item3,12,MutedBrush,new Thickness(0,8,0,0)));row.Children.Add(text);
   var arrow=Label("↗",22,AccentBrush);arrow.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(arrow,1);row.Children.Add(arrow);
   var button=PresetButton(option.Item2,()=>{customizationMode=option.Item1;PresetNavigate("widget");});button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(24);button.Background=PanelBrush;button.Margin=new Thickness(0,0,0,14);content.Children.Add(button);
  }
  content.Children.Add(Label("Начни с виджета — дальше выбирай готовое или собирай своё.",12,MutedBrush,new Thickness(0,8,0,0)));
 }
 void RenderPresetWidgetPicker(){
  content.Children.Add(Label("Какой виджет хотите?",22,TextBrush,new Thickness(0,0,0,18)));
  foreach(var widget in catalog.Where(x=>x.Available)){
   var panel=new StackPanel();panel.Children.Add(Label(widget.Name,20));panel.Children.Add(Label(widget.Id=="mini-player"?"14 тем · свой пресет · рандомизация":"Конфигуратор этого виджета появится позже",12,MutedBrush,new Thickness(0,8,0,0)));
   var button=PresetButton("Выбрать виджет: "+widget.Name,()=>{
    customizationWidget=widget.Id;
    if(customizationMode is "presets" or "saved"){PresetNavigate("gallery");return;}
    PresetTry(()=>{
     SavePresetDraft();
     presetDraft??=Presets.LoadDraft();
     presetHistory.Clear();
     if(customizationMode=="random"){
      if(presetDraft!=null)presetHistory.Push(presetDraft.Copy());
      presetDraft=RandomPreset();
     }else{
      presetDraft=presetDraft?.Copy()??PresetThemes.Create("Лаванда");
      presetDraft.Id="";
     }
     Presets.SaveDraft(presetDraft);
     PresetNavigate("editor");
    });
   });
   button.Content=panel;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(24);button.Margin=new Thickness(0,0,0,14);button.IsEnabled=widget.Id=="mini-player";content.Children.Add(button);
  }
  if(!catalog.Any(x=>x.Available))content.Children.Add(Label("Пока нет доступных виджетов в каталоге.",14,MutedBrush));
 }
 void RenderPresetGallery(){
  if(customizationMode=="saved"){RenderSavedPresetGallery();return;}
  content.Children.Add(Label("Мини-плеер · готовые пресеты",20,TextBrush,new Thickness(0,0,0,14)));
  var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};content.Children.Add(grid);
  foreach(string theme in PresetThemes.Names){
   var panel=new StackPanel();string file=Path.Combine(manager.Root,"Assets","Presets",theme+".png");
   if(File.Exists(file)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(file);image.EndInit();image.Freeze();panel.Children.Add(new Image{Source=image,Height=112,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,14)});}
   panel.Children.Add(Label(theme,15));var button=PresetButton("Пресет: "+theme,()=>OpenPreset(PresetThemes.Create(theme)));button.Content=panel;button.Background=PanelBrush;button.Margin=new Thickness(0,0,12,12);button.Padding=new Thickness(14);grid.Children.Add(button);
  }
 }
 void RenderSavedPresetGallery(){
  content.Children.Add(Label("Мини-плеер · созданные пресеты",20,TextBrush,new Thickness(0,0,0,14)));
  AddPresetImportActions(content);
  try{var saved=Presets.List();if(!saved.Any())content.Children.Add(Label("Сохрани первый вариант в конфигураторе — он появится здесь.",12,MutedBrush));foreach(var preset in saved){var button=PresetButton("Мой пресет: "+preset.Name,()=>OpenPreset(preset));button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,8);content.Children.Add(button);}}
  catch(Exception ex){content.Children.Add(Label("Не удалось прочитать пресеты: "+ex.Message,12,ColorBrush("#F0A2AC")));}
 }
 void OpenPreset(WidgetPreset preset){SavePresetDraft();presetDraft=preset.Copy();presetHistory.Clear();PresetNavigate("editor");}
 void QueuePresetDraft(){presetDraftDirty=true;presetDraftTimer.Stop();presetDraftTimer.Start();}
 void SavePresetDraft(){presetDraftTimer.Stop();if(!presetDraftDirty||presetDraft==null)return;try{Presets.SaveDraft(presetDraft);presetDraftDirty=false;}catch(Exception ex){if(presetStatus!=null)presetStatus.Text="Черновик не сохранён: "+ex.Message;}}
 string[] PresetFonts()=>new[]{"Onest"}.Concat(Fonts.SystemFontFamilies.Select(x=>x.Source)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray();
 WidgetPreset RandomPreset()=>PresetRandomizer.Create(Random.Shared,PresetFonts(),Presets.MediaFiles(),presetDraft);
 void RandomizePreset(){PresetTry(()=>{if(presetDraft!=null)presetHistory.Push(presetDraft.Copy());presetDraft=RandomPreset();Presets.SaveDraft(presetDraft);PresetNavigate("editor");});}
 void RefreshPresetPreview(){
  if(presetDraft==null)return;
  try{presetDraft.Normalize();presetPreview?.Update(presetDraft);RefreshLargePresetPreview();QueuePresetDraft();if(presetStatus!=null)presetStatus.Text="Изменения в превью · нажми «Применить» для плеера";}
  catch(Exception ex){if(presetStatus!=null)presetStatus.Text="Не удалось показать фон: "+ex.Message;}
 }
 void RenderPresetEditor(){
  presetDraft??=PresetThemes.Create("Лаванда");
  presetDraft.Normalize();
  var top=new DockPanel{Margin=new Thickness(0,0,0,14)};var badge=Label("МИНИ-ПЛЕЕР",10,AccentBrush);DockPanel.SetDock(badge,Dock.Right);top.Children.Add(badge);top.Children.Add(Label(customizationMode=="random"?"Случайный характер":"Мастерская пресета",22));content.Children.Add(top);
  var editor=new Grid{IsEnabled=!busy&&!updatePending};editor.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(Width>=1000?370:300)});editor.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});editor.ColumnDefinitions.Add(new ColumnDefinition());content.Children.Add(editor);
  var left=new StackPanel();editor.Children.Add(left);
  var previewBox=new StackPanel();previewBox.Children.Add(Label("ПРЕВЬЮ",10,MutedBrush,new Thickness(0,0,0,14)));
  presetPreview=new PresetPreview(manager.Root){Height=290,Zoom=presetPreviewZoom,HorizontalAlignment=HorizontalAlignment.Stretch};presetPreview.Update(presetDraft);AttachPresetDragging(presetPreview);previewBox.Children.Add(presetPreview);
  presetScaleLabel=Label("",11,MutedBrush,new Thickness(0,12,0,0));previewBox.Children.Add(presetScaleLabel);AddPresetZoomControls(previewBox);RefreshLargePresetPreview();
  var expand=PresetButton("Превью 1:1",ShowLargePresetPreview);expand.Margin=new Thickness(0,12,0,0);previewBox.Children.Add(expand);left.Children.Add(Card(previewBox,new Thickness(16)));
  var name=PresetTextBox("Название пресета",presetDraft.Name);name.MaxLength=80;name.TextChanged+=(_,_)=>{presetDraft.Name=name.Text;QueuePresetDraft();};left.Children.Add(Label("Название пресета",12,MutedBrush));name.Margin=new Thickness(0,8,0,10);left.Children.Add(name);
  var save=PresetButton("Сохранить пресет",()=>PresetTry(()=>{if(string.IsNullOrWhiteSpace(presetDraft.Name))throw new IOException("Укажи название пресета.");Presets.Save(presetDraft);Presets.SaveDraft(presetDraft);if(presetStatus!=null)presetStatus.Text="Сохранено в «Созданные пресеты»";}),true);left.Children.Add(save);
  var apply=PresetButton("Применить",()=>RunAction(ApplyCurrentPresetAsync,"Пресет применён к мини-плееру и сохранён в созданных пресетах."),true);apply.Margin=new Thickness(0,8,0,0);left.Children.Add(apply);
  AddPresetExportActions(left);
  var random=PresetButton("Рандомизировать ещё",RandomizePreset);random.Margin=new Thickness(0,8,0,0);left.Children.Add(random);
  var undo=PresetButton("Вернуть предыдущий",()=>{if(presetHistory.Count>0){presetDraft=presetHistory.Pop();QueuePresetDraft();Render();}});undo.Margin=new Thickness(0,8,0,0);undo.IsEnabled=presetHistory.Count>0;left.Children.Add(undo);
  presetStatus=Label("",11,AccentBrush,new Thickness(0,12,0,0));left.Children.Add(presetStatus);
  var right=new StackPanel();Grid.SetColumn(right,2);editor.Children.Add(right);
  var tabs=new WrapPanel{Margin=new Thickness(0,0,0,12)};presetTabButtons=tabs;right.Children.Add(tabs);
  foreach(string tab in PresetTabs){var b=PresetButton(tab,()=>{presetTab=tab;Render();});b.Padding=new Thickness(11,8,11,8);b.Margin=new Thickness(0,0,5,5);b.Background=presetTab==tab?ColorBrush("#30283F"):PanelBrush;b.Foreground=presetTab==tab?AccentBrush:MutedBrush;tabs.Children.Add(b);}
  var fields=new StackPanel();presetDetailPanel=fields;right.Children.Add(Card(fields,new Thickness(18)));RenderPresetFields(fields);
 }
 TextBox PresetTextBox(string name,string value){var text=new TextBox{Text=value,Foreground=TextBrush,Background=ColorBrush("#232530"),BorderBrush=ColorBrush("#3B3D50"),Padding=new Thickness(10,8,10,8),Template=(ControlTemplate)Resources["PresetTextBoxTemplate"],FontSize=12,MinWidth=0,MinHeight=38,VerticalContentAlignment=VerticalAlignment.Center};AutomationProperties.SetName(text,name);return text;}
 void PresetSlider(Panel panel,string label,double min,double max,double step,double value,Action<double> write,string unit=" px",Func<double>? actual=null){
  var line=new DockPanel{Margin=new Thickness(0,12,0,3)};var val=Label("",11,AccentBrush);DockPanel.SetDock(val,Dock.Right);line.Children.Add(val);line.Children.Add(Label(label,12));panel.Children.Add(line);
  var slider=new Slider{Minimum=min,Maximum=max,TickFrequency=step,IsSnapToTickEnabled=true,Value=value,Height=26};AutomationProperties.SetName(slider,label);panel.Children.Add(slider);
  bool syncing=false;void Caption()=>val.Text=unit=="%"?$"{slider.Value*100:0}%":$"{slider.Value:0.##}{unit}";Caption();slider.ValueChanged+=(_,_)=>{if(syncing)return;double requested=slider.Value;write(requested);bool limited=actual!=null&&Math.Abs(actual()-requested)>.01;if(actual!=null){syncing=true;slider.Value=actual();syncing=false;}Caption();RefreshPresetPreview();if(limited&&presetStatus!=null)presetStatus.Text="Размер ограничен закреплёнными элементами.";};
 }
 void PresetChoice(Panel panel,string label,string[] values,string[] names,string value,Action<string> write){
  panel.Children.Add(Label(label,12,MutedBrush,new Thickness(0,12,0,6)));var combo=new ComboBox{ItemsSource=names,SelectedIndex=Math.Max(0,Array.IndexOf(values,value)),MaxDropDownHeight=240,MinWidth=0};AutomationProperties.SetName(combo,label);panel.Children.Add(combo);combo.SelectionChanged+=(_,_)=>{if(combo.SelectedIndex>=0){write(values[combo.SelectedIndex]);RefreshPresetPreview();}};
 }
 void PresetCheck(Panel panel,string name,bool value,Action<bool> write){var check=new CheckBox{Content=name,IsChecked=value,Margin=new Thickness(0,10,0,0)};AutomationProperties.SetName(check,name);panel.Children.Add(check);check.Click+=(_,_)=>{write(check.IsChecked==true);RefreshPresetPreview();};}
 void PresetColor(Panel panel,string label,string value,Action<string> write){
  panel.Children.Add(Label(label,12,MutedBrush,new Thickness(0,12,0,6)));var row=new DockPanel();var chip=PresetButton("Выбрать цвет: "+label,()=>{});chip.Content=null;chip.ToolTip="Выбрать цвет: "+label;chip.Width=38;chip.Height=38;chip.Padding=new Thickness(0);chip.Margin=new Thickness(0,0,8,0);chip.Background=(Brush)new BrushConverter().ConvertFromString(value)!;DockPanel.SetDock(chip,Dock.Left);row.Children.Add(chip);
  var field=PresetTextBox(label+" HEX",value);field.MaxLength=7;row.Children.Add(field);panel.Children.Add(row);
  void SetColor(string color){write(color);chip.Background=(Brush)new BrushConverter().ConvertFromString(color)!;field.BorderBrush=ColorBrush("#3B3D50");RefreshPresetPreview();}
  field.TextChanged+=(_,_)=>{if(System.Text.RegularExpressions.Regex.IsMatch(field.Text,"^#[0-9a-fA-F]{6}$"))SetColor(field.Text);else field.BorderBrush=ColorBrush("#F0A2AC");};
  chip.Click+=(_,_)=>OpenPresetColorPicker(label,WidgetPreset.ValidColor(field.Text)?field.Text:value,color=>field.Text=color);
 }
 void RenderPresetFields(StackPanel panel){
  var p=presetDraft!;
  if(presetTab=="Форма"){
   PresetChoice(panel,"Форма",new[]{"rounded","rectangle","pill","ellipse","cut","star","square","circle","diamond","hexagon","heart"},new[]{"Скруглённая","Прямоугольник","Капсула","Овал","Срезанные углы","Звезда","Квадрат","Круг","Ромб","Шестиугольник","Сердце"},p.Shape,x=>{if(!p.SetShape(x))message="Форма не изменена: сначала открепите элементы, которые выходят за её размеры.";Render();});
   bool equalSides=p.Shape is "square" or "circle";PresetSlider(panel,equalSides?"Сторона":"Ширина",240,equalSides?420:640,1,p.Width,x=>p.Resize(x,p.Height),actual:()=>p.Width);if(!equalSides)PresetSlider(panel,"Высота",140,420,1,p.Height,x=>p.Resize(p.Width,x),actual:()=>p.Height);PresetSlider(panel,"Скругление",0,100,1,p.CornerRadius,x=>p.CornerRadius=x);PresetSlider(panel,"Толщина рамки",0,10,.5,p.BorderWidth,x=>p.BorderWidth=x);PresetSlider(panel,"Непрозрачность",.15,1,.01,p.Opacity,x=>p.Opacity=x,"%");
   panel.Children.Add(Label("Элементы за контуром формы скрыты. Их положение и размеры можно настроить во вкладке «Детали».",11,MutedBrush,new Thickness(0,12,0,0)));
  }else if(presetTab=="Цвета"){
   PresetColor(panel,"Фон",p.BackgroundColor,x=>p.BackgroundColor=x);PresetColor(panel,"Акцент",p.AccentColor,x=>p.AccentColor=x);PresetColor(panel,"Текст",p.TextColor,x=>p.TextColor=x);PresetColor(panel,"Рамка",p.BorderColor,x=>p.BorderColor=x);
  }else if(presetTab=="Текст"){
   PresetTypography(panel,p);
   PresetChoice(panel,"Начертание",new[]{"100","200","300","400","500","600","700","800","900"},new[]{"Тонкое","Очень лёгкое","Лёгкое","Обычное","Среднее","Полужирное","Жирное","Очень жирное","Тяжёлое"},p.FontWeight.ToString(),x=>p.FontWeight=int.Parse(x));
   panel.Children.Add(Label("Доступны Onest и шрифты, установленные в Windows.",11,MutedBrush,new Thickness(0,14,0,0)));
  }else if(presetTab=="Детали"){
   PresetElementFields(panel,p);
  }else{
   var upload=PresetButton("Загрузить изображение / GIF",ImportPresetMedia);upload.Padding=new Thickness(10);panel.Children.Add(upload);
   if(p.MediaPath.Length>0){panel.Children.Add(Label(Path.GetFileName(p.MediaPath),10,MutedBrush,new Thickness(0,8,0,0)));var remove=PresetButton("Убрать фон",()=>{p.MediaPath="";RefreshPresetPreview();Render();});remove.Margin=new Thickness(0,8,0,0);panel.Children.Add(remove);}
   PresetChoice(panel,"Заполнение",new[]{"cover","contain","stretch"},new[]{"Заполнить с обрезкой","Вписать целиком","Растянуть"},p.MediaFit,x=>p.MediaFit=x);PresetSlider(panel,"Непрозрачность фона",0,1,.01,p.MediaOpacity,x=>p.MediaOpacity=x,"%");PresetSlider(panel,"Положение по горизонтали",0,1,.01,p.MediaX,x=>p.MediaX=x,"%");PresetSlider(panel,"Положение по вертикали",0,1,.01,p.MediaY,x=>p.MediaY=x,"%");
   panel.Children.Add(Label("PNG, JPG, BMP и GIF до 20 МБ. Файлы копируются в папку лаунчера. GIF проигрывается в превью. Рандомизация выбирает фон из уже загруженных файлов.",11,MutedBrush,new Thickness(0,12,0,0)));
  }
 }
 void ImportPresetMedia(){
  var picker=new Microsoft.Win32.OpenFileDialog{Title="Фон пресета",Filter="Изображения и GIF|*.png;*.jpg;*.jpeg;*.bmp;*.gif",CheckFileExists=true,Multiselect=false};if(picker.ShowDialog(this)!=true)return;
  PresetTry(()=>{presetDraft!.MediaPath=Presets.ImportMedia(picker.FileName);RefreshPresetPreview();Render();});
 }
}
