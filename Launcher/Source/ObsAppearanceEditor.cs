using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WidgetLauncher;
public sealed partial class LauncherWindow
{
 Window? obsEditor;
 void OpenObsEditor(){if(obs==null)return;if(obsEditor!=null){obsEditor.Activate();return;}
  ConfigurePresetControls();
  var editor=new ObsAppearanceEditor(this,obs.Entry("mini-player").Appearance??new(),obs.Settings.PlayerPresets,
   appearance=>obs.ApplyAppearance(appearance), (name,appearance)=>obs.SavePreset(name,appearance),BackgroundBrush,PanelBrush,TextBrush,MutedBrush,AccentBrush,obs);
  obsEditor=editor;editor.Closed+=(_,_)=>obsEditor=null;editor.Show();
 }
}
public sealed class ObsAppearanceEditor:Window
{
 readonly ObsBridge? fontStore;readonly Dictionary<string,FontFamily> loadedFonts=new();readonly CheckBox gridToggle=new();readonly ComboBox gridStep=new();
 ObsAppearance draft;readonly Action<ObsAppearance> apply;readonly Action<string,ObsAppearance> savePreset;readonly Dictionary<string,ObsAppearance> saved;
 readonly StackPanel controls=new();readonly Canvas canvas=new(),surface=new(),adorners=new(),viewport=new(){ClipToBounds=true};readonly Border stage=new();readonly TextBlock dimensions=new(),feedback=new();readonly Brush panel,ink,muted,accent;readonly Dictionary<string,string> labels=new(){["cover"]="Обложка",["title"]="Название",["artist"]="Исполнитель",["elapsed"]="Текущее время",["duration"]="Длительность",["bar"]="Шкала"};
 readonly HashSet<string> selection=new();readonly Dictionary<string,ObsPart> gestureParts=new();
 string selected="title",resizeHandle="";Point start;double previewScale=1,gestureScale=1,gestureLeft,gestureTop;bool dragging,moved,clearOnClick,frameResize;ObsAppearance? gestureFrame;Window? colorDialog;
 public ObsAppearanceEditor(Window owner,ObsAppearance initial,Dictionary<string,ObsAppearance> presets,Action<ObsAppearance> apply,Action<string,ObsAppearance> savePreset,Brush background,Brush panel,Brush ink,Brush muted,Brush accent,ObsBridge? fontStore=null){
  foreach(DictionaryEntry resource in owner.Resources)Resources[resource.Key]=resource.Value;
  this.fontStore=fontStore;Owner=owner;this.apply=apply;this.savePreset=savePreset;this.saved=presets;this.panel=panel;this.ink=ink;this.muted=muted;this.accent=accent;draft=initial.Copy();
  Title="Оформление плеера для OBS";Width=Math.Min(1080,SystemParameters.WorkArea.Width-32);Height=Math.Min(760,SystemParameters.WorkArea.Height-32);MinWidth=Math.Min(720,Width);MinHeight=Math.Min(500,Height);WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=background;Foreground=ink;FontFamily=owner.FontFamily;
  var root=new DockPanel{Margin=new Thickness(20)};Content=root;var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);feedback.Foreground=muted;feedback.Margin=new Thickness(0,10,0,8);footer.Children.Add(feedback);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);
  actions.Children.Add(Button("Закрыть",()=>Close()));actions.Children.Add(Button("Применить в OBS",()=>{try{apply(draft.Copy());feedback.Text="Применено. Ссылка прежняя; размер источника OBS: "+SizeText();}catch(Exception ex){feedback.Text="Не удалось сохранить: "+ex.Message;}},true));
  var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(320)});grid.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(grid);
  var scroll=new ScrollViewer{Content=controls,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,20,0)};grid.Children.Add(scroll);
  var right=new DockPanel();Grid.SetColumn(right,1);grid.Children.Add(right);
  var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);right.Children.Add(heading);heading.Children.Add(new TextBlock{Text="Предпросмотр",FontSize=22,FontWeight=FontWeights.SemiBold});heading.Children.Add(new TextBlock{Text="Shift + щелчок — выбрать несколько. Перетаскивайте выделенное мышью.\nМаркеры меняют размер; Shift при растягивании сохраняет пропорции.\nЩелчок без перетаскивания или правая кнопка — снять выделение.",TextWrapping=TextWrapping.Wrap,Foreground=muted,Margin=new Thickness(0,8,0,14)});dimensions.TextWrapping=TextWrapping.Wrap;heading.Children.Add(dimensions);
  var gridTools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};heading.Children.Add(gridTools);gridToggle.Content="Сетка и привязка";gridToggle.Foreground=ink;gridToggle.VerticalAlignment=VerticalAlignment.Center;gridToggle.Click+=(_,_)=>{draft.GridEnabled=gridToggle.IsChecked==true;draft.AlignToGrid();BuildControls();Draw();};gridTools.Children.Add(gridToggle);
  gridTools.Children.Add(new TextBlock{Text="Шаг",Foreground=muted,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,0,8,0)});gridStep.ItemsSource=new[]{2,4,8,12,16,24,32};gridStep.Width=76;gridStep.SelectionChanged+=(_,_)=>{if(gridStep.SelectedItem is int step&&draft.GridSize!=step){draft.GridSize=step;draft.AlignToGrid();BuildControls();Draw();}};gridTools.Children.Add(gridStep);
  gridTools.Children.Add(new TextBlock{Text="px",Foreground=muted,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0)});
  stage.Background=new SolidColorBrush(Color.FromRgb(45,46,55));stage.CornerRadius=new CornerRadius(14);stage.Padding=new Thickness(18);stage.Margin=new Thickness(0,15,0,0);right.Children.Add(stage);
  stage.Child=viewport;viewport.Children.Add(surface);surface.Children.Add(canvas);surface.Children.Add(adorners);Canvas.SetLeft(canvas,16);Canvas.SetTop(canvas,16);adorners.IsHitTestVisible=true;
  viewport.SizeChanged+=(_,_)=>{if(!dragging)Draw();};
  surface.MouseMove+=(_,e)=>PointerMove(e.GetPosition(viewport),(Keyboard.Modifiers&ModifierKeys.Shift)!=0);
  surface.MouseLeftButtonUp+=(_,e)=>{if(dragging){PointerUp();e.Handled=true;}};
  surface.LostMouseCapture+=(_,_)=>{if(dragging){dragging=false;Draw();}};
  stage.MouseLeftButtonDown+=(_,_)=>ClearSelection();stage.PreviewMouseRightButtonDown+=(_,e)=>{ClearSelection();e.Handled=true;};
  Closed+=(_,_)=>colorDialog?.Close();PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){if(selection.Count>0)ClearSelection();else Close();e.Handled=true;}};
  feedback.Text="Изменения пока только в предпросмотре.";BuildControls();Draw();
 }
 Button Button(string text,Action action,bool primary=false){var b=new Button{Content=text,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,8),Background=primary?accent:panel,Foreground=primary?Brushes.Black:ink,BorderThickness=new Thickness(0)};b.Click+=(_,_)=>action();return b;}
 void Heading(string text)=>controls.Children.Add(new TextBlock{Text=text,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,16,0,8)});
 string SizeText()=>$"{Math.Ceiling(draft.Width+8)} × {Math.Ceiling(draft.Height+8)}";
 void Combo(string label,string[] values,int selectedIndex,Action<int> changed){controls.Children.Add(new TextBlock{Text=label,Foreground=muted,Margin=new Thickness(0,5,0,4)});var c=new ComboBox{ItemsSource=values,SelectedIndex=selectedIndex,Padding=new Thickness(6),Margin=new Thickness(0,0,0,5)};c.SelectionChanged+=(_,_)=>{if(c.SelectedIndex>=0)changed(c.SelectedIndex);};controls.Children.Add(c);}
 void Slider(string label,double value,double min,double max,Action<double> change){
  var text=new TextBlock{Text=$"{label}: {value:0.##}",Foreground=muted,Margin=new Thickness(0,8,0,0)};controls.Children.Add(text);var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=max>1,Margin=new Thickness(0,6,0,2)};
  slider.ValueChanged+=(_,_)=>{change(slider.Value);text.Text=$"{label}: {slider.Value:0.##}";Draw();};controls.Children.Add(slider);
 }
 void ColorButton(string label,Func<string> get,Action<string> set){var b=Button(label+"  "+get(),()=>{});b.Click+=(_,_)=>{colorDialog?.Close();var picker=new PresetColorDialog(this,label,get(),value=>{set(value);b.Content=label+"  "+value;Draw();},Background,panel,ink,muted,accent,Brushes.DimGray,panel);colorDialog=picker;picker.ShowDialog();};controls.Children.Add(b);}
 void BuildControls(){controls.Children.Clear();Heading("Компоновки и пресеты");
  var presets=new[]{"Горизонтальная","Вертикальная","Квадратная","Круглая"};var keys=new[]{"wide","vertical","square","circle"};Combo("Начать с готового варианта",new[]{"Выберите вариант…"}.Concat(presets).ToArray(),0,i=>{if(i==0)return;draft=ObsAppearance.Preset(keys[i-1]);BuildControls();Draw();});
  if(saved.Count>0){var names=saved.Keys.OrderBy(x=>x).ToArray();Combo("Мои пресеты",new[]{"Выберите пресет…"}.Concat(names).ToArray(),0,i=>{if(i==0)return;draft=saved[names[i-1]].Copy();BuildControls();Draw();});}
  string suggested="Мой пресет";for(int n=2;saved.ContainsKey(suggested);n++)suggested="Мой пресет "+n;var name=new TextBox{Text=suggested,Margin=new Thickness(0,8,0,8),Padding=new Thickness(6),MaxLength=60};name.Background=panel;name.Foreground=ink;name.BorderBrush=muted;if(Resources["PresetTextBoxTemplate"] is ControlTemplate template)name.Template=template;controls.Children.Add(name);controls.Children.Add(Button("Сохранить пресет",()=>{try{if(string.IsNullOrWhiteSpace(name.Text))throw new Exception("Введите имя пресета.");savePreset(name.Text.Trim(),draft.Copy());feedback.Text="Пресет сохранён. Для изменения эфира нажмите «Применить».";BuildControls();}catch(Exception ex){feedback.Text=ex.Message;}}));
  Heading("Корпус");var shapes=new[]{"rounded","rectangle","circle"};Combo("Форма",new[]{"Скруглённый прямоугольник","Прямоугольник","Круг / овал"},Array.IndexOf(shapes,draft.Shape),i=>{draft.Shape=shapes[i];draft.Normalize();BuildControls();Draw();});
  controls.Children.Add(new TextBlock{Text="Размер корпуса меняется маркерами внешней рамки в предпросмотре.",TextWrapping=TextWrapping.Wrap,Foreground=muted});if(draft.Shape=="rounded")Slider("Скругление",draft.Radius,0,100,v=>draft.Radius=v);Slider("Непрозрачность фона",draft.Opacity,0,1,v=>draft.Opacity=v);Slider("Толщина рамки",draft.BorderWidth,0,8,v=>draft.BorderWidth=v);
  Heading("Элементы");foreach(var key in ObsAppearance.Keys){var k=key;var check=new CheckBox{Content=labels[k],IsChecked=draft.Parts[k].Visible,Foreground=ink,Margin=new Thickness(0,4,0,4)};check.Click+=(_,_)=>{draft.Parts[k].Visible=check.IsChecked==true;Draw();};controls.Children.Add(check);}
  if(selection.Count==1){selected=selection.First();var part=draft.Parts[selected];Heading(labels[selected]);controls.Children.Add(new TextBlock{Text="Положение и размеры — мышью в предпросмотре.",TextWrapping=TextWrapping.Wrap,Foreground=muted});if(selected!="cover"&&selected!="bar")Slider("Размер текста",part.FontSize,8,48,v=>part.FontSize=v);}
  else controls.Children.Add(new TextBlock{Text=selection.Count>1?$"Выбрано элементов: {selection.Count}. Перетащите любой из них, чтобы переместить всю группу.":"Выберите элемент в предпросмотре. Shift + щелчок добавляет его к выделению.",TextWrapping=TextWrapping.Wrap,Foreground=muted,Margin=new Thickness(0,10,0,0)});
  Heading("Цвета и шрифт");ColorButton("Фон",()=>draft.Background,v=>draft.Background=v);ColorButton("Рамка",()=>draft.Border,v=>draft.Border=v);ColorButton("Название",()=>draft.Text,v=>draft.Text=v);ColorButton("Исполнитель и время",()=>draft.Secondary,v=>draft.Secondary=v);ColorButton("Шкала",()=>draft.Accent,v=>draft.Accent=v);var fonts=new[]{"Segoe UI","Arial","Verdana","Georgia","Consolas"};var custom=fontStore?.Settings.Fonts.Keys.OrderBy(id=>fontStore.Settings.Fonts[id]).ToArray()??Array.Empty<string>();var fontNames=fonts.Concat(custom.Select(id=>fontStore!.Settings.Fonts[id]+" (свой)")).ToArray();int fontIndex=Array.IndexOf(custom,draft.FontId);Combo("Шрифт",fontNames,fontIndex>=0?fonts.Length+fontIndex:Array.IndexOf(fonts,draft.Font),i=>{draft.FontId=i>=fonts.Length?custom[i-fonts.Length]:"";if(i<fonts.Length)draft.Font=fonts[i];Draw();});
  if(fontStore!=null)controls.Children.Add(Button("Загрузить шрифт…",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Title="Выберите шрифт",Filter="Шрифты TTF и OTF|*.ttf;*.otf",CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;try{ImportFont(dialog.FileName);}catch(Exception ex){feedback.Text="Не удалось загрузить шрифт: "+ex.Message;}}));
  controls.Children.Add(new TextBlock{Text="Длинные названия и имена исполнителей плавно прокручиваются в OBS автоматически.",TextWrapping=TextWrapping.Wrap,Foreground=muted,Margin=new Thickness(0,8,0,12)});controls.Children.Add(Button("Сбросить макет",()=>{draft=new();BuildControls();Draw();}));
 }
 public void ImportFont(string path){if(fontStore==null)throw new InvalidOperationException("Хранилище шрифтов недоступно.");if(new FileInfo(path).Length>20*1024*1024)throw new InvalidDataException("Размер шрифта не должен превышать 20 МБ.");_ = new GlyphTypeface(new Uri(Path.GetFullPath(path)));string id=fontStore.ImportFont(path);draft.FontId=id;BuildControls();Draw();feedback.Text="Шрифт загружен. Нажмите «Применить в OBS», чтобы использовать его в эфире.";}
 FontFamily PreviewFont(){if(draft.FontId!=""&&fontStore!=null){if(loadedFonts.TryGetValue(draft.FontId,out var family))return family;try{var path=fontStore.FontPath(draft.FontId);if(path!=null){family=Fonts.GetFontFamilies(new Uri(path)).First();loadedFonts[draft.FontId]=family;return family;}}catch{}}return new FontFamily(draft.Font);}
 Brush Brush(string value)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
 internal void ClearSelection(){dragging=false;surface.ReleaseMouseCapture();selection.Clear();BuildControls();Draw();}
 internal void PointerDown(string? key,string handle,Point pointer,bool shift,bool frame=false){
  if(key!=null&&handle==""&&shift){if(!selection.Add(key))selection.Remove(key);selected=key;BuildControls();Draw();return;}
  clearOnClick=key!=null&&handle==""&&(selection.Contains(key)||selection.Count>1);
  if(key!=null&&!selection.Contains(key)){selection.Clear();selection.Add(key);}if(key!=null)selected=key;
  frameResize=frame;resizeHandle=handle;start=pointer;gestureScale=previewScale;gestureLeft=Canvas.GetLeft(surface);gestureTop=Canvas.GetTop(surface);gestureFrame=draft.Copy();gestureParts.Clear();foreach(var id in selection)gestureParts[id]=ObsGeometry.Clone(draft.Parts[id]);moved=false;dragging=true;surface.CaptureMouse();Draw();
 }
 internal void PointerMove(Point pointer,bool shift){
  if(!dragging||gestureFrame==null)return;var delta=pointer-start;if(!moved&&Math.Abs(delta.X)<3&&Math.Abs(delta.Y)<3)return;moved=true;double dx=delta.X/gestureScale,dy=delta.Y/gestureScale;
  if(frameResize){ObsGeometry.ResizeFrame(draft,gestureFrame,resizeHandle,dx,dy,shift);Canvas.SetLeft(surface,gestureLeft+(resizeHandle.Contains('w')?(gestureFrame.Width-draft.Width)*gestureScale:0));Canvas.SetTop(surface,gestureTop+(resizeHandle.Contains('n')?(gestureFrame.Height-draft.Height)*gestureScale:0));}
  else if(resizeHandle!="")draft.Parts[selected]=ObsGeometry.Resize(draft,selected,gestureParts[selected],resizeHandle,dx,dy,shift);
  else ObsGeometry.Move(draft,gestureParts,dx,dy);
  Draw();
 }
 internal void PointerUp(){if(!dragging)return;bool clear=!moved&&clearOnClick&&!frameResize;dragging=false;surface.ReleaseMouseCapture();if(clear)selection.Clear();BuildControls();Draw();}
 void Begin(string? key,string handle,MouseButtonEventArgs e,bool frame=false){PointerDown(key,handle,e.GetPosition(viewport),(Keyboard.Modifiers&ModifierKeys.Shift)!=0,frame);e.Handled=true;}
 void FitPreview(){if(dragging)return;double w=viewport.ActualWidth,h=viewport.ActualHeight;previewScale=w>0&&h>0?Math.Min(1,Math.Min(w/surface.Width,h/surface.Height)):1;surface.RenderTransform=new ScaleTransform(previewScale,previewScale);Canvas.SetLeft(surface,Math.Max(0,(w-surface.Width*previewScale)/2));Canvas.SetTop(surface,Math.Max(0,(h-surface.Height*previewScale)/2));}
 void Outline(double x,double y,double w,double h,bool group=false){var outline=new System.Windows.Shapes.Rectangle{Width=w,Height=h,Stroke=accent,StrokeThickness=1/previewScale,IsHitTestVisible=false};if(group)outline.StrokeDashArray=new DoubleCollection{4,3};Canvas.SetLeft(outline,x+16);Canvas.SetTop(outline,y+16);adorners.Children.Add(outline);}
 void Handles(double x,double y,double w,double h,string? key,bool frame){
  foreach(var handle in new[]{"nw","n","ne","e","se","s","sw","w"}){
   double px=handle.Contains('w')?x:handle.Contains('e')?x+w:x+w/2,py=handle.Contains('n')?y:handle.Contains('s')?y+h:y+h/2;
   double size=10/previewScale;var grip=new Border{Width=size,Height=size,Background=frame?panel:accent,BorderBrush=frame?muted:ink,BorderThickness=new Thickness(1/previewScale),CornerRadius=new CornerRadius(2),Cursor=handle is "n" or "s"?Cursors.SizeNS:handle is "w" or "e"?Cursors.SizeWE:handle is "nw" or "se"?Cursors.SizeNWSE:Cursors.SizeNESW,ToolTip=(frame?"Корпус":"Элемент")+" · потяните; Shift — сохранить пропорции"};
   Canvas.SetLeft(grip,px+16-size/2);Canvas.SetTop(grip,py+16-size/2);string direction=handle;grip.MouseLeftButtonDown+=(_,e)=>Begin(key,direction,e,frame);adorners.Children.Add(grip);
  }
 }
 void Draw(){feedback.Text="Предпросмотр. Для передачи изменений в OBS нажмите «Применить».";draft.Normalize();selection.RemoveWhere(k=>!draft.Parts[k].Visible);gridToggle.IsChecked=draft.GridEnabled;gridStep.SelectedItem=draft.GridSize;canvas.Children.Clear();adorners.Children.Clear();canvas.Width=draft.Width;canvas.Height=draft.Height;surface.Width=draft.Width+32;surface.Height=draft.Height+32;adorners.Width=surface.Width;adorners.Height=surface.Height;FitPreview();
  bool ellipse=draft.Shape=="circle";double radius=draft.Shape=="rectangle"?0:draft.Radius;canvas.Clip=ellipse?new EllipseGeometry(new Rect(0,0,draft.Width,draft.Height)):new RectangleGeometry(new Rect(0,0,draft.Width,draft.Height),radius,radius);
  var background=(SolidColorBrush)Brush(draft.Background);background.Opacity=draft.Opacity;
  if(ellipse)canvas.Children.Add(new System.Windows.Shapes.Ellipse{Width=draft.Width,Height=draft.Height,Fill=background,Stroke=Brush(draft.Border),StrokeThickness=draft.BorderWidth});
  else canvas.Children.Add(new Border{Width=draft.Width,Height=draft.Height,CornerRadius=new CornerRadius(radius),Background=background,BorderBrush=Brush(draft.Border),BorderThickness=new Thickness(draft.BorderWidth)});
  if(draft.GridEnabled){var lines=new PathGeometry();for(double x=draft.GridSize;x<draft.Width;x+=draft.GridSize)lines.AddGeometry(new LineGeometry(new Point(x,0),new Point(x,draft.Height)));for(double y=draft.GridSize;y<draft.Height;y+=draft.GridSize)lines.AddGeometry(new LineGeometry(new Point(0,y),new Point(draft.Width,y)));canvas.Children.Add(new System.Windows.Shapes.Path{Data=lines,Stroke=Brushes.White,StrokeThickness=.6,Opacity=.24,IsHitTestVisible=false});}
  foreach(var key in ObsAppearance.Keys){var p=draft.Parts[key];if(!p.Visible)continue;FrameworkElement item;
   if(key=="cover")item=new Border{CornerRadius=new CornerRadius(12),Background=new LinearGradientBrush(Color.FromRgb(105,66,142),Color.FromRgb(32,29,55),90),Child=new TextBlock{Text="♫",FontSize=36,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
   else if(key=="bar"){var grid=new Grid();grid.Children.Add(new Border{Background=Brush("#40394F"),CornerRadius=new CornerRadius(4)});grid.Children.Add(new Border{Background=Brush(draft.Accent),CornerRadius=new CornerRadius(4),Width=p.Width*.39,HorizontalAlignment=HorizontalAlignment.Left});item=grid;}
   else item=new TextBlock{Text=key switch{"title"=>"Название трека","artist"=>"Исполнитель","elapsed"=>"1:23",_=>"3:34"},FontFamily=PreviewFont(),FontSize=p.FontSize,FontWeight=key=="title"?FontWeights.SemiBold:FontWeights.Normal,Foreground=Brush(key=="title"?draft.Text:draft.Secondary),TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=key=="duration"?TextAlignment.Right:TextAlignment.Left};
   var hit=new Border{Background=Brushes.Transparent,Child=item,Width=p.Width,Height=p.Height,Cursor=Cursors.SizeAll};Canvas.SetLeft(hit,p.X);Canvas.SetTop(hit,p.Y);string id=key;hit.MouseLeftButtonDown+=(_,e)=>Begin(id,"",e);canvas.Children.Add(hit);
  }
  Outline(0,0,draft.Width,draft.Height,true);Handles(0,0,draft.Width,draft.Height,null,true);
  foreach(var key in selection){var p=draft.Parts[key];Outline(p.X,p.Y,p.Width,p.Height);}
  if(selection.Count==1){string key=selection.First();var p=draft.Parts[key];Handles(p.X,p.Y,p.Width,p.Height,key,false);}
  else if(selection.Count>1){var parts=selection.Select(k=>draft.Parts[k]).ToArray();double x=parts.Min(p=>p.X),y=parts.Min(p=>p.Y);Outline(x,y,parts.Max(p=>p.X+p.Width)-x,parts.Max(p=>p.Y+p.Height)-y,true);}
  dimensions.Text="Размер источника в OBS: "+SizeText()+"  •  "+(selection.Count==0?"ничего не выбрано":selection.Count==1?labels[selection.First()]:"выбрано: "+selection.Count)+(draft.GridEnabled?"  •  привязка "+draft.GridSize+" px":"  •  свободно");
 }
}
