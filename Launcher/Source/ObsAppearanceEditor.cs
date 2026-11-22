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
   appearance=>obs.ApplyAppearance(appearance), (name,appearance)=>obs.SavePreset(name,appearance),BackgroundBrush,PanelBrush,TextBrush,MutedBrush,AccentBrush);
  obsEditor=editor;editor.Closed+=(_,_)=>obsEditor=null;editor.Show();
 }
}
public sealed class ObsAppearanceEditor:Window
{
 ObsAppearance draft;readonly Action<ObsAppearance> apply;readonly Action<string,ObsAppearance> savePreset;readonly Dictionary<string,ObsAppearance> saved;
 readonly StackPanel controls=new();readonly Canvas canvas=new();readonly Border stage=new();readonly TextBlock dimensions=new(),feedback=new();readonly Brush panel,ink,muted,accent;readonly Dictionary<string,string> labels=new(){["cover"]="Обложка",["title"]="Название",["artist"]="Исполнитель",["elapsed"]="Текущее время",["duration"]="Длительность",["bar"]="Шкала"};
 string selected="title";Point start;double initialX,initialY,initialW,initialH;bool dragging,resizing;Window? colorDialog;
 public ObsAppearanceEditor(Window owner,ObsAppearance initial,Dictionary<string,ObsAppearance> presets,Action<ObsAppearance> apply,Action<string,ObsAppearance> savePreset,Brush background,Brush panel,Brush ink,Brush muted,Brush accent){
  foreach(DictionaryEntry resource in owner.Resources)Resources[resource.Key]=resource.Value;
  Owner=owner;this.apply=apply;this.savePreset=savePreset;this.saved=presets;this.panel=panel;this.ink=ink;this.muted=muted;this.accent=accent;draft=initial.Copy();
  Title="Оформление плеера для OBS";Width=Math.Min(1080,SystemParameters.WorkArea.Width-32);Height=Math.Min(760,SystemParameters.WorkArea.Height-32);MinWidth=Math.Min(720,Width);MinHeight=Math.Min(500,Height);WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=background;Foreground=ink;FontFamily=owner.FontFamily;
  var root=new DockPanel{Margin=new Thickness(20)};Content=root;var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);feedback.Foreground=muted;feedback.Margin=new Thickness(0,10,0,8);footer.Children.Add(feedback);
  var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);
  actions.Children.Add(Button("Закрыть",()=>Close()));actions.Children.Add(Button("Применить в OBS",()=>{try{apply(draft.Copy());feedback.Text="Применено. Ссылка прежняя; размер источника OBS: "+SizeText();}catch(Exception ex){feedback.Text="Не удалось сохранить: "+ex.Message;}},true));
  var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(320)});grid.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(grid);
  var scroll=new ScrollViewer{Content=controls,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,20,0)};grid.Children.Add(scroll);
  var right=new DockPanel();Grid.SetColumn(right,1);grid.Children.Add(right);
  var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);right.Children.Add(heading);heading.Children.Add(new TextBlock{Text="Предпросмотр",FontSize=22,FontWeight=FontWeights.SemiBold});heading.Children.Add(new TextBlock{Text="Перетащите элемент. Квадратик в углу меняет его размер.\nЭто демонстрационные данные; в эфире остаётся прежний вид до «Применить».",TextWrapping=TextWrapping.Wrap,Foreground=muted,Margin=new Thickness(0,8,0,14)});heading.Children.Add(dimensions);
  stage.Background=new SolidColorBrush(Color.FromRgb(45,46,55));stage.CornerRadius=new CornerRadius(14);stage.Padding=new Thickness(18);stage.Margin=new Thickness(0,15,0,0);right.Children.Add(stage);
  var view=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly};stage.Child=view;
  canvas.MouseMove+=(_,e)=>{if(!dragging)return;var pos=e.GetPosition(canvas);var part=draft.Parts[selected];if(resizing){part.Width=initialW+pos.X-start.X;part.Height=initialH+pos.Y-start.Y;}else{part.X=initialX+pos.X-start.X;part.Y=initialY+pos.Y-start.Y;}draft.Normalize();Draw();};
  canvas.MouseLeftButtonUp+=(_,_)=>{if(!dragging)return;dragging=false;canvas.ReleaseMouseCapture();BuildControls();};
  canvas.LostMouseCapture+=(_,_)=>dragging=false;Closed+=(_,_)=>colorDialog?.Close();PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
  feedback.Text="Изменения пока только в предпросмотре.";BuildControls();Draw();
 }
 Button Button(string text,Action action,bool primary=false){var b=new Button{Content=text,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,8),Background=primary?accent:panel,Foreground=primary?Brushes.Black:ink,BorderThickness=new Thickness(0)};b.Click+=(_,_)=>action();return b;}
 void Heading(string text)=>controls.Children.Add(new TextBlock{Text=text,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,16,0,8)});
 string SizeText()=>$"{Math.Ceiling(draft.Width+8)} × {Math.Ceiling(draft.Height+8)}";
 void Combo(string label,string[] values,int selectedIndex,Action<int> changed){controls.Children.Add(new TextBlock{Text=label,Foreground=muted,Margin=new Thickness(0,5,0,4)});var c=new ComboBox{ItemsSource=values,SelectedIndex=selectedIndex,Padding=new Thickness(6),Margin=new Thickness(0,0,0,5)};c.SelectionChanged+=(_,_)=>{if(c.SelectedIndex>=0)changed(c.SelectedIndex);};controls.Children.Add(c);}
 void Slider(string label,double value,double min,double max,Action<double> change){var t=new TextBlock{Text=$"{label}: {value:0.##}",Foreground=muted,Margin=new Thickness(0,8,0,0)};controls.Children.Add(t);var s=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=max>1,Margin=new Thickness(0,6,0,2)};s.ValueChanged+=(_,_)=>{change(s.Value);draft.Normalize();t.Text=$"{label}: {s.Value:0.##}";Draw();};controls.Children.Add(s);}
 void ColorButton(string label,Func<string> get,Action<string> set){var b=Button(label+"  "+get(),()=>{});b.Click+=(_,_)=>{colorDialog?.Close();var picker=new PresetColorDialog(this,label,get(),value=>{set(value);b.Content=label+"  "+value;Draw();},Background,panel,ink,muted,accent,Brushes.DimGray,panel);colorDialog=picker;picker.ShowDialog();};controls.Children.Add(b);}
 void BuildControls(){controls.Children.Clear();Heading("Компоновки и пресеты");
  var presets=new[]{"Горизонтальная","Вертикальная","Квадратная","Круглая"};var keys=new[]{"wide","vertical","square","circle"};Combo("Начать с готового варианта",new[]{"Выберите вариант…"}.Concat(presets).ToArray(),0,i=>{if(i==0)return;draft=ObsAppearance.Preset(keys[i-1]);BuildControls();Draw();});
  if(saved.Count>0){var names=saved.Keys.OrderBy(x=>x).ToArray();Combo("Мои пресеты",new[]{"Выберите пресет…"}.Concat(names).ToArray(),0,i=>{if(i==0)return;draft=saved[names[i-1]].Copy();BuildControls();Draw();});}
  string suggested="Мой пресет";for(int n=2;saved.ContainsKey(suggested);n++)suggested="Мой пресет "+n;var name=new TextBox{Text=suggested,Margin=new Thickness(0,8,0,8),Padding=new Thickness(6),MaxLength=60};name.Background=panel;name.Foreground=ink;name.BorderBrush=muted;if(Resources["PresetTextBoxTemplate"] is ControlTemplate template)name.Template=template;controls.Children.Add(name);controls.Children.Add(Button("Сохранить пресет",()=>{try{if(string.IsNullOrWhiteSpace(name.Text))throw new Exception("Введите имя пресета.");savePreset(name.Text.Trim(),draft.Copy());feedback.Text="Пресет сохранён. Для изменения эфира нажмите «Применить».";BuildControls();}catch(Exception ex){feedback.Text=ex.Message;}}));
  Heading("Корпус");var shapes=new[]{"rounded","rectangle","circle"};Combo("Форма",new[]{"Скруглённый прямоугольник","Прямоугольник","Круг"},Array.IndexOf(shapes,draft.Shape),i=>{draft.Shape=shapes[i];draft.Normalize();BuildControls();Draw();});
  Slider("Ширина",draft.Width,180,900,v=>draft.Width=v);if(draft.Shape!="circle")Slider("Высота",draft.Height,90,800,v=>draft.Height=v);if(draft.Shape=="rounded")Slider("Скругление",draft.Radius,0,100,v=>draft.Radius=v);Slider("Непрозрачность фона",draft.Opacity,0,1,v=>draft.Opacity=v);Slider("Толщина рамки",draft.BorderWidth,0,8,v=>draft.BorderWidth=v);
  Heading("Элементы");foreach(var key in ObsAppearance.Keys){var k=key;var check=new CheckBox{Content=labels[k],IsChecked=draft.Parts[k].Visible,Foreground=ink,Margin=new Thickness(0,4,0,4)};check.Click+=(_,_)=>{draft.Parts[k].Visible=check.IsChecked==true;Draw();};controls.Children.Add(check);}
  Combo("Редактируемый элемент",ObsAppearance.Keys.Select(k=>labels[k]).ToArray(),Array.IndexOf(ObsAppearance.Keys,selected),i=>{selected=ObsAppearance.Keys[i];BuildControls();Draw();});var part=draft.Parts[selected];Slider("X",part.X,0,draft.Width-part.Width,v=>part.X=v);Slider("Y",part.Y,0,draft.Height-part.Height,v=>part.Y=v);Slider("Ширина элемента",part.Width,selected=="bar"?16:12,draft.Width,v=>part.Width=v);Slider("Высота элемента",part.Height,selected=="bar"?2:12,draft.Height,v=>part.Height=v);if(selected!="cover"&&selected!="bar")Slider("Размер текста",part.FontSize,8,48,v=>part.FontSize=v);
  Heading("Цвета и шрифт");ColorButton("Фон",()=>draft.Background,v=>draft.Background=v);ColorButton("Рамка",()=>draft.Border,v=>draft.Border=v);ColorButton("Название",()=>draft.Text,v=>draft.Text=v);ColorButton("Исполнитель и время",()=>draft.Secondary,v=>draft.Secondary=v);ColorButton("Шкала",()=>draft.Accent,v=>draft.Accent=v);var fonts=new[]{"Segoe UI","Arial","Verdana","Georgia","Consolas"};Combo("Шрифт",fonts,Array.IndexOf(fonts,draft.Font),i=>{draft.Font=fonts[i];Draw();});controls.Children.Add(Button("Сбросить макет",()=>{draft=new();BuildControls();Draw();}));
 }
 Brush Brush(string value)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
 void Begin(string key,MouseButtonEventArgs e,bool size=false){selected=key;start=e.GetPosition(canvas);var p=draft.Parts[key];initialX=p.X;initialY=p.Y;initialW=p.Width;initialH=p.Height;resizing=size;dragging=true;canvas.CaptureMouse();e.Handled=true;Draw();}
 void Draw(){feedback.Text="Предпросмотр. Для передачи изменений в OBS нажмите «Применить».";draft.Normalize();canvas.Children.Clear();canvas.Width=draft.Width;canvas.Height=draft.Height;double radius=draft.Shape=="circle"?draft.Width/2:draft.Shape=="rectangle"?0:draft.Radius;canvas.Clip=new RectangleGeometry(new Rect(0,0,draft.Width,draft.Height),radius,radius);
  var bg=new Border{Width=draft.Width,Height=draft.Height,CornerRadius=new CornerRadius(radius),Background=Brush(draft.Background),BorderBrush=Brush(draft.Border),BorderThickness=new Thickness(draft.BorderWidth)};((SolidColorBrush)bg.Background).Opacity=draft.Opacity;canvas.Children.Add(bg);
  foreach(var key in ObsAppearance.Keys){var p=draft.Parts[key];if(!p.Visible)continue;FrameworkElement item;
   if(key=="cover"){item=new Border{CornerRadius=new CornerRadius(12),Background=new LinearGradientBrush(Color.FromRgb(105,66,142),Color.FromRgb(32,29,55),90),Child=new TextBlock{Text="♫",FontSize=36,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};}
   else if(key=="bar"){var grid=new Grid();grid.Children.Add(new Border{Background=Brush("#40394F"),CornerRadius=new CornerRadius(4)});grid.Children.Add(new Border{Background=Brush(draft.Accent),CornerRadius=new CornerRadius(4),Width=p.Width*.39,HorizontalAlignment=HorizontalAlignment.Left});item=grid;}
   else item=new TextBlock{Text=key switch{"title"=>"Название трека","artist"=>"Исполнитель","elapsed"=>"1:23",_=>"3:34"},FontFamily=new FontFamily(draft.Font),FontSize=p.FontSize,FontWeight=key=="title"?FontWeights.SemiBold:FontWeights.Normal,Foreground=Brush(key=="title"?draft.Text:draft.Secondary),TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=key=="duration"?TextAlignment.Right:TextAlignment.Left};
   var hit=new Border{Background=Brushes.Transparent,Child=item,Width=p.Width,Height=p.Height,Cursor=Cursors.SizeAll};Canvas.SetLeft(hit,p.X);Canvas.SetTop(hit,p.Y);string id=key;hit.MouseLeftButtonDown+=(_,e)=>Begin(id,e);canvas.Children.Add(hit);
  }
  var s=draft.Parts[selected];if(s.Visible){var outline=new Border{Width=s.Width,Height=s.Height,BorderBrush=accent,BorderThickness=new Thickness(1),IsHitTestVisible=false};Canvas.SetLeft(outline,s.X);Canvas.SetTop(outline,s.Y);canvas.Children.Add(outline);var handle=new Border{Width=10,Height=10,Background=accent,Cursor=Cursors.SizeNWSE};Canvas.SetLeft(handle,s.X+s.Width-10);Canvas.SetTop(handle,s.Y+s.Height-10);handle.MouseLeftButtonDown+=(_,e)=>Begin(selected,e,true);canvas.Children.Add(handle);}
  dimensions.Text="Размер источника в OBS: "+SizeText()+"  •  выбран: "+labels[selected];
 }
}
