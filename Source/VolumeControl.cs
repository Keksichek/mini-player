using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace MiniPlayer;

public sealed partial class Player
{
 const double VolumeFooterHeight=28;
 Slider? faceVolume;
 bool VolumeIsBeingEdited=>volumeSlider.IsMouseCaptureWithin||faceVolume?.IsMouseCaptureWithin==true;

 void ResetVolumeSource(){audioTarget="";audioSource="";volumeSlider.IsEnabled=false;volumeSlider.ToolTip="Выберите источник и включите в нём звук для регулировки громкости.";}

 void AddVolumeFooter(Canvas face,double top,Brush ink,bool sceneBackground){
  var panel=new Grid{Margin=new Thickness(9,0,8,0)};
  panel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(23)});panel.ColumnDefinitions.Add(new ColumnDefinition());panel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(39)});
  var tint=sceneBackground&&appearance.Theme!="Ретро"?"#24000000":"#18FFFFFF";
  var footer=new Border{Width=face.Width-20,Height=24,CornerRadius=new CornerRadius(7),Background=BrushOf(tint),Child=panel};
  Canvas.SetLeft(footer,10);Canvas.SetTop(footer,top+2);face.Children.Add(footer);
  panel.Children.Add(new ContentControl{Content=PlayerIcons.Speaker(ink),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left,IsHitTestVisible=false});
  faceVolume=new Slider{Minimum=0,Maximum=100,SmallChange=1,LargeChange=10,IsMoveToPointEnabled=true,Height=24,VerticalAlignment=VerticalAlignment.Center};
  faceVolume.Resources["VolumeInk"]=ink;faceVolume.Resources["VolumeAccent"]=BrushOf(appearance.Accent);
  faceVolume.Template=(ControlTemplate)XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Slider">
 <Grid Background="Transparent" Margin="3,0">
  <Border Height="3" Background="{DynamicResource VolumeInk}" Opacity=".22" CornerRadius="1.5" VerticalAlignment="Center"/>
  <Track Name="PART_Track">
   <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="{DynamicResource VolumeAccent}" CornerRadius="1.5" VerticalAlignment="Center"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
   <Track.Thumb><Thumb Width="10" Height="10"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="{DynamicResource VolumeInk}" Stroke="{DynamicResource VolumeAccent}" StrokeThickness="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
   <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
  </Track>
 </Grid>
 <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>
""");
  faceVolume.SetBinding(Slider.ValueProperty,new Binding("Value"){Source=volumeSlider,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
  faceVolume.SetBinding(IsEnabledProperty,new Binding("IsEnabled"){Source=volumeSlider});faceVolume.SetBinding(ToolTipProperty,new Binding("ToolTip"){Source=volumeSlider});
  ToolTipService.SetShowOnDisabled(faceVolume,true);System.Windows.Automation.AutomationProperties.SetName(faceVolume,"Громкость выбранного источника");
  Grid.SetColumn(faceVolume,1);panel.Children.Add(faceVolume);
  var percent=new TextBlock{Foreground=ink,FontSize=10,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right};
  var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextProperty,new Binding("Value"){Source=volumeSlider,StringFormat="{0:0}%"}));
  var unavailable=new DataTrigger{Binding=new Binding("IsEnabled"){Source=volumeSlider},Value=false};unavailable.Setters.Add(new Setter(TextBlock.TextProperty,"—"));style.Triggers.Add(unavailable);percent.Style=style;
  Grid.SetColumn(percent,2);panel.Children.Add(percent);
 }
}
