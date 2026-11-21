using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace WidgetLauncher;

// The editor hosts the very same surface used by the real player, at an explicit, stable zoom.
public sealed class PresetPreview:UserControl,IDisposable
{
 readonly Grid stage=new(){MinHeight=210};
 readonly ScrollViewer viewport;
 readonly Border canvasHost;
 readonly Image comparison=new(){Stretch=Stretch.None,Visibility=Visibility.Collapsed,IsHitTestVisible=false};
 readonly Canvas editLayer=new(){Background=Brushes.Transparent,Visibility=Visibility.Collapsed,Focusable=true,FocusVisualStyle=null};
 readonly Border verticalGuide=new(){IsHitTestVisible=false,Visibility=Visibility.Collapsed};
 readonly Border horizontalGuide=new(){IsHitTestVisible=false,Visibility=Visibility.Collapsed};
 double? guideX,guideY;
 readonly Border selection=new(){BorderBrush=Brushes.DodgerBlue,IsHitTestVisible=false};
 readonly Canvas groupOutlines=new(){IsHitTestVisible=false};
 readonly Border[] grips=new Border[4];
 string? selected;
 bool locked;
 internal Canvas EditLayer=>editLayer;
 readonly ScaleTransform scale=new(1,1);
 readonly TextBlock error=new(){Foreground=Brushes.LightSalmon,FontSize=11,Background=Brushes.Black,Padding=new Thickness(8),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Bottom};
 double zoom=1,displayScale=1;
 bool fitToView,disposed;
 public PresetSurface Surface {get;}
 public string MediaError=>Surface.MediaError;
 public int AnimationFrameCount=>Surface.AnimationFrameCount;
 public int AnimationFrameIndex=>Surface.AnimationFrameIndex;
 public bool AnimationRunning=>Surface.AnimationRunning;
 public double DisplayScale=>displayScale;
 public event EventHandler? DisplayScaleChanged;
 public double Zoom {
  get=>zoom;
  set{zoom=double.IsFinite(value)?Math.Clamp(value,.25,2):1;fitToView=false;ApplyScale();}
 }
 public bool FitToView {
  get=>fitToView;
  set{if(fitToView==value)return;if(!value)zoom=displayScale;fitToView=value;ApplyScale();}
 }
 public PresetPreview(string hubRoot){
  Surface=new PresetSurface(hubRoot){IsHitTestVisible=false};
  var drawing=new Grid{LayoutTransform=scale};
  drawing.Children.Add(Surface);drawing.Children.Add(editLayer);drawing.Children.Add(comparison);
  editLayer.Children.Add(verticalGuide);editLayer.Children.Add(horizontalGuide);editLayer.Children.Add(selection);editLayer.Children.Add(groupOutlines);
  for(int i=0;i<grips.Length;i++){
   var grip=new Border{Tag=i,Background=Brushes.White,BorderBrush=Brushes.DodgerBlue,Cursor=i is 0 or 3?Cursors.SizeNWSE:Cursors.SizeNESW};
   grips[i]=grip;editLayer.Children.Add(grip);
  }
  canvasHost=new Border{Child=drawing,Margin=new Thickness(18),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
  // Use the stock viewport structure: the launcher's global vertical-only ScrollViewer template
  // cannot expose horizontal scrolling when a real-sized widget is wider than the preview.
  viewport=new ScrollViewer{Style=null,Content=canvasHost,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,CanContentScroll=false,Background=Brushes.Transparent};
  viewport.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=PreviewScrollBarStyle();
  RefreshScrollBarColors();Loaded+=(_,_)=>RefreshScrollBarColors();
  Content=stage;MinHeight=210;stage.Background=Checkerboard();stage.Children.Add(viewport);stage.Children.Add(error);
  stage.SizeChanged+=(_,_)=>{if(fitToView)ApplyScale();};
  System.Windows.Automation.AutomationProperties.SetName(this,"Предпросмотр пресета мини-плеера");
 }
 public void Update(WidgetPreset value){if(disposed)return;Surface.Configure(value);error.Text=Surface.MediaError;error.Visibility=error.Text.Length==0?Visibility.Collapsed:Visibility.Visible;ApplyScale();}
 internal void CompareWith(ImageSource? snapshot){
  if(disposed)return;
  comparison.Source=snapshot;
  comparison.Visibility=snapshot==null?Visibility.Collapsed:Visibility.Visible;
  Surface.Visibility=snapshot==null?Visibility.Visible:Visibility.Collapsed;
  editLayer.Visibility=snapshot==null?Visibility.Visible:Visibility.Collapsed;
  error.Visibility=snapshot==null&&Surface.MediaError.Length!=0?Visibility.Visible:Visibility.Collapsed;
  ApplyScale();
 }
 internal void SetAlignmentGuides(double? x,double? y){
  guideX=x;guideY=y;
  double thickness=1.5/displayScale;
  verticalGuide.Visibility=x.HasValue?Visibility.Visible:Visibility.Collapsed;
  horizontalGuide.Visibility=y.HasValue?Visibility.Visible:Visibility.Collapsed;
  if(x.HasValue){
   verticalGuide.Width=thickness;verticalGuide.Height=Surface.Height;
   Canvas.SetLeft(verticalGuide,Math.Clamp(x.Value-thickness/2,0,Surface.Width-thickness));Canvas.SetTop(verticalGuide,0);
  }
  if(y.HasValue){
   horizontalGuide.Height=thickness;horizontalGuide.Width=Surface.Width;
   Canvas.SetTop(horizontalGuide,Math.Clamp(y.Value-thickness/2,0,Surface.Height-thickness));Canvas.SetLeft(horizontalGuide,0);
  }
 }
 internal void SelectElement(string? id,bool isLocked,IEnumerable<string>? group=null){
  groupOutlines.Children.Clear();
  if(group!=null)foreach(var member in group)
   groupOutlines.Children.Add(new Border{Tag=member,BorderBrush=Brushes.DodgerBlue});
  selected=id;locked=isLocked;editLayer.Visibility=Visibility.Visible;RefreshSelection();
 }
 internal void RefreshSelection(){
  if(groupOutlines.Children.Count>0){
   selection.Visibility=Visibility.Collapsed;foreach(var grip in grips)grip.Visibility=Visibility.Collapsed;
   foreach(Border outline in groupOutlines.Children){
    var element=Surface.Elements[(string)outline.Tag];
    outline.Visibility=element.Visibility;outline.Width=element.Width;outline.Height=element.Height;
    outline.BorderThickness=new Thickness(1.5/displayScale);
    Canvas.SetLeft(outline,Canvas.GetLeft(element));Canvas.SetTop(outline,Canvas.GetTop(element));
   }
   return;
  }
  if(selected==null||!Surface.Elements.TryGetValue(selected,out var control)||control.Visibility!=Visibility.Visible){
   selection.Visibility=Visibility.Collapsed;foreach(var grip in grips)grip.Visibility=Visibility.Collapsed;return;
  }
  double left=Canvas.GetLeft(control),top=Canvas.GetTop(control),w=control.Width,h=control.Height;
  selection.Visibility=Visibility.Visible;selection.Width=w;selection.Height=h;
  selection.BorderBrush=locked?Brushes.SlateGray:Brushes.DodgerBlue;
  selection.BorderThickness=new Thickness(1.5/displayScale);
  Canvas.SetLeft(selection,left);Canvas.SetTop(selection,top);
  double size=Math.Min(9/displayScale,Math.Min(w,h));
  for(int i=0;i<grips.Length;i++){
   var grip=grips[i];grip.Visibility=locked?Visibility.Collapsed:Visibility.Visible;
   grip.Width=grip.Height=size;grip.BorderThickness=new Thickness(1/displayScale);
   Canvas.SetLeft(grip,left+((i&1)==0?0:w)-size/2);Canvas.SetTop(grip,top+(i<2?0:h)-size/2);
  }
 }
 internal string? HitElement(Point point){
  if(!Surface.ShapeGeometry.FillContains(point))return null;
  for(int i=Surface.LayerOrder.Count-1;i>=0;i--){
   string id=Surface.LayerOrder[i];
   var item=Surface.Elements[id];
   if(item.Visibility==Visibility.Visible&&new Rect(Canvas.GetLeft(item),Canvas.GetTop(item),item.Width,item.Height).Contains(point))return id;
  }
  return null;
 }
 void RefreshScrollBarColors(){
  // Reuse the hub's mutable brushes while retaining standalone preview defaults for tests/tools.
  foreach(string color in new[]{"4B4D61","B9A1FF","D3C3FF"})viewport.Resources["PreviewScroll"+color]=TryFindResource("HubColor"+color) as Brush??new SolidColorBrush((Color)ColorConverter.ConvertFromString("#"+color));
  verticalGuide.Background=horizontalGuide.Background=(Brush)viewport.Resources["PreviewScrollB9A1FF"];
 }
 static Style PreviewScrollBarStyle()=>(Style)XamlReader.Parse("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
 <Setter Property="Width" Value="10"/><Setter Property="Height" Value="Auto"/><Setter Property="MinWidth" Value="0"/><Setter Property="MinHeight" Value="0"/><Setter Property="Background" Value="Transparent"/>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
  <Grid Background="Transparent">
   <Track Name="PART_Track" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True">
    <Track.DecreaseRepeatButton><RepeatButton Name="Decrease" Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb MinHeight="20"><Thumb.Template><ControlTemplate TargetType="Thumb">
     <Border Name="Grip" Background="{DynamicResource PreviewScroll4B4D61}" Width="4" CornerRadius="2" Margin="0,2"/>
     <ControlTemplate.Triggers>
      <DataTrigger Binding="{Binding Orientation,RelativeSource={RelativeSource AncestorType=ScrollBar}}" Value="Horizontal"><Setter TargetName="Grip" Property="Width" Value="Auto"/><Setter TargetName="Grip" Property="Height" Value="4"/><Setter TargetName="Grip" Property="Margin" Value="2,0"/><Setter Property="MinHeight" Value="0"/><Setter Property="MinWidth" Value="20"/></DataTrigger>
      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Background" Value="{DynamicResource PreviewScrollB9A1FF}"/></Trigger>
      <Trigger Property="IsDragging" Value="True"><Setter TargetName="Grip" Property="Background" Value="{DynamicResource PreviewScrollD3C3FF}"/></Trigger>
     </ControlTemplate.Triggers>
    </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton Name="Increase" Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
   </Track>
  </Grid>
  <ControlTemplate.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/><Setter TargetName="Decrease" Property="Command" Value="ScrollBar.PageLeftCommand"/><Setter TargetName="Increase" Property="Command" Value="ScrollBar.PageRightCommand"/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
 <Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="10"/></Trigger></Style.Triggers>
</Style>
""");
 void ApplyScale(){
  if(disposed)return;
  double next=zoom;
  double faceWidth=comparison.Source?.Width??Surface.Width,faceHeight=comparison.Source?.Height??Surface.Height;
  if(fitToView&&double.IsFinite(faceWidth)&&faceWidth>0&&double.IsFinite(faceHeight)&&faceHeight>0){
   double width=stage.ActualWidth>0?stage.ActualWidth:double.IsFinite(Width)?Width:faceWidth+36;
   double height=stage.ActualHeight>0?stage.ActualHeight:double.IsFinite(Height)?Height:faceHeight+36;
   next=Math.Clamp(Math.Min((width-36)/faceWidth,(height-36)/faceHeight),.05,1);
  }
  scale.ScaleX=scale.ScaleY=next;
  if(Math.Abs(displayScale-next)>.00001){displayScale=next;DisplayScaleChanged?.Invoke(this,EventArgs.Empty);}
  RefreshSelection();SetAlignmentGuides(guideX,guideY);
 }
 static DrawingBrush Checkerboard(){
  var drawing=new DrawingGroup();using(var dc=drawing.Open()){
   dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(31,33,43)),null,new Rect(0,0,24,24));
   var light=new SolidColorBrush(Color.FromRgb(39,41,52));dc.DrawRectangle(light,null,new Rect(0,0,12,12));dc.DrawRectangle(light,null,new Rect(12,12,12,12));
  }
  var brush=new DrawingBrush(drawing){Viewport=new Rect(0,0,24,24),ViewportUnits=BrushMappingMode.Absolute,TileMode=TileMode.Tile,Stretch=Stretch.None};brush.Freeze();return brush;
 }
 public void Dispose(){if(disposed)return;disposed=true;Surface.Dispose();stage.Children.Clear();}
}
