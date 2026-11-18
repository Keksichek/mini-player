using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 Window? presetColorPicker;
 void OpenPresetColorPicker(string label,string initial,Action<string> changed){
  ConfigurePresetControls();
  presetColorPicker?.Close();
  var dialog=new PresetColorDialog(this,label,initial,changed,
   BackgroundBrush,PanelBrush,TextBrush,MutedBrush,AccentBrush,ColorBrush("#2B2C3A"),ColorBrush("#232530"));
  presetColorPicker=dialog;
  void OwnerClosed(object? sender,EventArgs args)=>dialog.Close();
  Closed+=OwnerClosed;
  dialog.Closed+=(_,_)=>{Closed-=OwnerClosed;if(ReferenceEquals(presetColorPicker,dialog))presetColorPicker=null;};
  dialog.Show();
 }
}

// This dialog keeps the same live brushes as its owner, including its dynamic resources.
internal sealed class PresetColorDialog:Window
{
 readonly Action<string> changed;
 readonly string original;
 readonly PresetSaturationPlane plane=new();
 readonly Slider hue=new(){Minimum=0,Maximum=360,Value=0,SmallChange=1,LargeChange=15};
 readonly TextBox hex;
 readonly TextBox[] rgb=new TextBox[3];
 readonly Border current;
 readonly TextBlock feedback;
 readonly Brush edge,accent;
 bool syncing,accepted;
 double h,s,v;
 Color color;
 internal string SelectedHex=>Hex(color);
 internal PresetSaturationPlane SaturationPlane=>plane;

 internal PresetColorDialog(Window owner,string label,string initial,Action<string> changed,
  Brush background,Brush panel,Brush ink,Brush muted,Brush accent,Brush edge,Brush field){
  this.changed=changed;this.edge=edge;this.accent=accent;
  if(!TryColor(initial,out color))color=Colors.White;
  original=Hex(color);
  Title="Цвет · "+label;Owner=owner;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  Width=Math.Min(470,Math.Max(320,SystemParameters.WorkArea.Width-24));MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-32);
  SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStyle=WindowStyle.None;AllowsTransparency=true;
  Background=Brushes.Transparent;Foreground=ink;FontFamily=owner.FontFamily;FontWeight=owner.FontWeight;FontSize=12;
  foreach(DictionaryEntry entry in owner.Resources)Resources[entry.Key]=entry.Value;
  var shell=new DockPanel{LastChildFill=true};
  Content=new Border{Background=background,BorderBrush=edge,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(15),Child=shell};
  var heading=new Grid{Margin=new Thickness(24,17,16,13),Background=Brushes.Transparent};heading.ColumnDefinitions.Add(new ColumnDefinition());heading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  DockPanel.SetDock(heading,Dock.Top);shell.Children.Add(heading);
  var caption=new TextBlock{Text=Title,FontSize=20,VerticalAlignment=VerticalAlignment.Center,Foreground=ink};heading.Children.Add(caption);
  var close=new Button{Width=34,Height=32,Padding=new Thickness(0),Background=Brushes.Transparent,Content=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 0,0 L 10,10 M 10,0 L 0,10"),Stroke=ink,StrokeThickness=1.5,Width=10,Height=10,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},ToolTip="Закрыть"};
  AutomationProperties.SetName(close,"Закрыть выбор цвета");Grid.SetColumn(close,1);heading.Children.Add(close);close.Click+=(_,_)=>Close();
  heading.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is not Button&&e.ButtonState==MouseButtonState.Pressed){try{DragMove();}catch(InvalidOperationException){}}};
  var bottom=new StackPanel{Margin=new Thickness(24,14,24,22)};DockPanel.SetDock(bottom,Dock.Bottom);shell.Children.Add(bottom);
  feedback=new TextBlock{Text="Изменения сразу видны в превью.",Foreground=muted,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};bottom.Children.Add(feedback);
  var actions=new Grid();actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});actions.ColumnDefinitions.Add(new ColumnDefinition());bottom.Children.Add(actions);
  var cancel=new Button{Content="Отмена",Padding=new Thickness(14,11,14,11),Background=panel,Foreground=ink};AutomationProperties.SetName(cancel,"Отменить выбор цвета");actions.Children.Add(cancel);cancel.Click+=(_,_)=>Close();
  var done=new Button{Content="Готово",Padding=new Thickness(14,11,14,11),Background=accent,Foreground=background,IsDefault=true};AutomationProperties.SetName(done,"Сохранить выбранный цвет");Grid.SetColumn(done,2);actions.Children.Add(done);done.Click+=(_,_)=>{if(!ValidateInputs())return;accepted=true;Close();};
  var body=new StackPanel{Margin=new Thickness(24,0,24,0)};var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(180,MaxHeight-180)};shell.Children.Add(scroll);
  plane.Height=210;plane.Margin=new Thickness(0,4,0,0);body.Children.Add(new Border{CornerRadius=new CornerRadius(9),BorderBrush=edge,BorderThickness=new Thickness(1),Padding=new Thickness(1),Child=plane});
  AutomationProperties.SetName(plane,"Насыщенность и яркость цвета");
  body.Children.Add(new TextBlock{Text="Оттенок",Foreground=muted,Margin=new Thickness(0,16,0,5)});
  var huePanel=new Grid{Height=30};huePanel.Children.Add(new Border{Height=7,VerticalAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(3.5),Margin=new Thickness(5,0,5,0),Background=new LinearGradientBrush(new GradientStopCollection{new(Colors.Red,0),new(Colors.Yellow,1d/6),new(Colors.Lime,2d/6),new(Colors.Cyan,.5),new(Colors.Blue,4d/6),new(Colors.Magenta,5d/6),new(Colors.Red,1)},0)});
  hue.Background=Brushes.Transparent;huePanel.Children.Add(hue);body.Children.Add(huePanel);AutomationProperties.SetName(hue,"Оттенок цвета");
  // Keep the custom thumb and keyboard behavior, but allow the rainbow underneath to stay visible.
  hue.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Slider"><Grid Background="Transparent" Margin="5,0"><Track Name="PART_Track"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width="14" Height="20"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#30FFFFFF" BorderBrush="White" BorderThickness="2" CornerRadius="5"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>
""");
  var fields=new Grid{Margin=new Thickness(0,15,0,0)};fields.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(78)});fields.ColumnDefinitions.Add(new ColumnDefinition());body.Children.Add(fields);
  current=new Border{Background=new SolidColorBrush(color),CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,14,0),MinHeight=60};AutomationProperties.SetName(current,"Выбранный цвет");fields.Children.Add(current);
  var hexGroup=new StackPanel();Grid.SetColumn(hexGroup,1);fields.Children.Add(hexGroup);hexGroup.Children.Add(new TextBlock{Text="HEX",Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,5)});
  hex=Field("HEX цвета",original,8,field,ink);hexGroup.Children.Add(hex);
  var rgbGrid=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Margin=new Thickness(0,14,0,0)};body.Children.Add(rgbGrid);
  for(int index=0;index<3;index++){
   var group=new StackPanel{Margin=new Thickness(0,0,index<2?10:0,0)};rgbGrid.Children.Add(group);string name=new[]{"R · красный","G · зелёный","B · синий"}[index];
   group.Children.Add(new TextBlock{Text=name,FontSize=11,Foreground=muted,Margin=new Thickness(0,0,0,5)});var input=rgb[index]=Field(name,"0",3,field,ink);group.Children.Add(input);
   input.TextChanged+=(_,_)=>{if(syncing)return;if(!ReadRgb(out var rgbColor)){feedback.Text="RGB: целые числа от 0 до 255.";input.BorderBrush=accent;return;}SetColor(rgbColor,true);};
  }
  plane.Changed+=(nextS,nextV)=>{s=nextS;v=nextV;SetColor(PresetSaturationPlane.Hsv(h,s,v),false);};
  hue.ValueChanged+=(_,_)=>{if(syncing)return;h=hue.Value;SetColor(PresetSaturationPlane.Hsv(h,s,v),false);};
  hex.TextChanged+=(_,_)=>{if(syncing)return;if(TryColor(hex.Text,out var next)){SetColor(next,true);}else{feedback.Text="HEX: # и 6 символов от 0 до 9 или A–F.";hex.BorderBrush=accent;}};
  PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};
  Closed+=(_,_)=>{if(!accepted)changed(original);};
  SetColor(color,true,false);
 }
 TextBox Field(string name,string text,int max,Brush background,Brush ink){
  var input=new TextBox{Text=text,MaxLength=max,MinHeight=38,Padding=new Thickness(10,8,10,8),Background=background,Foreground=ink,BorderBrush=edge,VerticalContentAlignment=VerticalAlignment.Center,FontSize=13};
  if(Resources["PresetTextBoxTemplate"] is ControlTemplate template)input.Template=template;
  AutomationProperties.SetName(input,name);return input;
 }
 bool ReadRgb(out Color next){next=default;var values=new byte[3];for(int i=0;i<3;i++)if(!byte.TryParse(rgb[i].Text,NumberStyles.None,CultureInfo.InvariantCulture,out values[i]))return false;next=Color.FromRgb(values[0],values[1],values[2]);return true;}
 bool ValidateInputs(){if(!TryColor(hex.Text,out _)||!ReadRgb(out _)){feedback.Text="Исправьте HEX или RGB перед сохранением.";return false;}return true;}
 void SetColor(Color next,bool updateHsv,bool publish=true){
  color=next;if(updateHsv)ToHsv(next,ref h,out s,out v);
  syncing=true;
  try{hue.Value=h;plane.Set(h,s,v);hex.Text=Hex(next);hex.BorderBrush=edge;rgb[0].Text=next.R.ToString(CultureInfo.InvariantCulture);rgb[1].Text=next.G.ToString(CultureInfo.InvariantCulture);rgb[2].Text=next.B.ToString(CultureInfo.InvariantCulture);foreach(var input in rgb)input.BorderBrush=edge;current.Background=new SolidColorBrush(next);feedback.Text="Изменения сразу видны в превью.";}
  finally{syncing=false;}
  if(publish)changed(Hex(next));
 }
 static string Hex(Color value)=>$"#{value.R:X2}{value.G:X2}{value.B:X2}";
 static bool TryColor(string text,out Color value){value=default;if(text.Length!=7||text[0]!='#'||!int.TryParse(text.AsSpan(1),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out int number))return false;value=Color.FromRgb((byte)(number>>16),(byte)(number>>8),(byte)number);return true;}
 static void ToHsv(Color color,ref double h,out double s,out double v){double r=color.R/255d,g=color.G/255d,b=color.B/255d;v=Math.Max(r,Math.Max(g,b));double min=Math.Min(r,Math.Min(g,b)),delta=v-min;s=v==0?0:delta/v;if(delta!=0){h=60*(v==r?((g-b)/delta)%6:v==g?(b-r)/delta+2:(r-g)/delta+4);if(h<0)h+=360;}}
}

internal sealed class PresetSaturationPlane:FrameworkElement
{
 double hue,saturation,value=1;
 internal event Action<double,double>? Changed;
 public PresetSaturationPlane(){
  Focusable=true;Cursor=Cursors.Cross;
  MouseLeftButtonDown+=(_,e)=>{Focus();CaptureMouse();Pick(e.GetPosition(this));e.Handled=true;};
  MouseMove+=(_,e)=>{if(IsMouseCaptured){Pick(e.GetPosition(this));e.Handled=true;}};
  MouseLeftButtonUp+=(_,e)=>{if(IsMouseCaptured){Pick(e.GetPosition(this));ReleaseMouseCapture();e.Handled=true;}};
  KeyDown+=(_,e)=>{double step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.1:.01;if(e.Key==Key.Left)saturation-=step;else if(e.Key==Key.Right)saturation+=step;else if(e.Key==Key.Up)value+=step;else if(e.Key==Key.Down)value-=step;else return;saturation=Math.Clamp(saturation,0,1);value=Math.Clamp(value,0,1);Changed?.Invoke(saturation,value);InvalidateVisual();e.Handled=true;};
 }
 internal void Set(double h,double s,double v){hue=h;saturation=s;value=v;InvalidateVisual();}
 void Pick(Point point){saturation=Math.Clamp(point.X/Math.Max(1,ActualWidth),0,1);value=1-Math.Clamp(point.Y/Math.Max(1,ActualHeight),0,1);Changed?.Invoke(saturation,value);InvalidateVisual();}
 protected override void OnRender(DrawingContext drawing){
  var rect=new Rect(0,0,ActualWidth,ActualHeight);drawing.PushClip(new RectangleGeometry(rect,7,7));
  drawing.DrawRectangle(new LinearGradientBrush(Colors.White,Hsv(hue,1,1),0),null,rect);drawing.DrawRectangle(new LinearGradientBrush(Colors.Transparent,Colors.Black,90),null,rect);drawing.Pop();
  var point=new Point(Math.Clamp(saturation*ActualWidth,5,Math.Max(5,ActualWidth-5)),Math.Clamp((1-value)*ActualHeight,5,Math.Max(5,ActualHeight-5)));
  drawing.DrawEllipse(null,new Pen(Brushes.Black,3),point,5,5);drawing.DrawEllipse(null,new Pen(Brushes.White,1.5),point,5,5);
  if(IsKeyboardFocused)drawing.DrawRoundedRectangle(null,new Pen(Brushes.White,1),new Rect(1,1,Math.Max(0,ActualWidth-2),Math.Max(0,ActualHeight-2)),7,7);
 }
 internal static Color Hsv(double h,double s,double v){h=(h%360+360)%360;double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;var (r,g,b)=h switch{<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));}
}
