using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
namespace WidgetLauncher;
public sealed partial class LauncherWindow
{
 bool presetControlsConfigured;
 void ConfigurePresetControls(){
  if(presetControlsConfigured)return;presetControlsConfigured=true;
  var styles=(ResourceDictionary)XamlReader.Parse(UiXaml("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Style TargetType="ToolTip">
  <Setter Property="Background" Value="#252733"/><Setter Property="Foreground" Value="#F1F0F7"/>
  <Setter Property="BorderBrush" Value="#3B3D50"/><Setter Property="Padding" Value="10,7"/>
 </Style>
 <Style TargetType="ScrollBar">
  <Setter Property="Width" Value="10"/><Setter Property="MinWidth" Value="0"/><Setter Property="MaxWidth" Value="10"/><Setter Property="Background" Value="Transparent"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
   <Grid Background="Transparent" Margin="0,5">
    <Track Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True">
     <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb MinHeight="32"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Name="Grip" Background="#4B4D61" Width="4" CornerRadius="2" Margin="0,2"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Background" Value="#B9A1FF"/></Trigger><Trigger Property="IsDragging" Value="True"><Setter TargetName="Grip" Property="Background" Value="#D3C3FF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
    </Track>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ScrollViewer">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollViewer">
   <Grid Background="{TemplateBinding Background}">
    <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
    <ScrollContentPresenter Name="PART_ScrollContentPresenter" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" CanContentScroll="{TemplateBinding CanContentScroll}"/>
    <ScrollBar Name="PART_VerticalScrollBar" Grid.Column="1" Width="10" Orientation="Vertical" Style="{DynamicResource {x:Type ScrollBar}}" Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}" Value="{TemplateBinding VerticalOffset}" Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}"/>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="Slider">
  <Setter Property="Height" Value="28"/><Setter Property="IsMoveToPointEnabled" Value="True"/><Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
   <Grid Background="Transparent" Margin="5,0">
    <Border Height="4" Background="#383A4B" CornerRadius="2" VerticalAlignment="Center"/>
    <Track Name="PART_Track">
     <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="4" Background="#B9A1FF" CornerRadius="2" VerticalAlignment="Center"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb Width="12" Height="12"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Name="Grip" Fill="#F1F0F7" Stroke="#B9A1FF" StrokeThickness="2"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Fill" Value="#B9A1FF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
    </Track>
    <Border Name="FocusFrame" BorderBrush="#B9A1FF" BorderThickness="1" CornerRadius="6" Margin="-4,0" Visibility="Collapsed" IsHitTestVisible="False"/>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="FocusFrame" Property="Visibility" Value="Visible"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="CheckBox">
  <Setter Property="Foreground" Value="#F1F0F7"/><Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
   <Grid Background="Transparent">
    <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
    <Border Name="Box" Width="17" Height="17" CornerRadius="5" BorderBrush="#55586F" BorderThickness="1" Background="#232530" VerticalAlignment="Center">
     <Path Name="Check" Data="M 3,8 L 6,11 L 12,4" Stroke="#101117" StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Visibility="Collapsed"/>
    </Border>
    <ContentPresenter Grid.Column="1" Margin="10,3,0,3" VerticalAlignment="Center" RecognizesAccessKey="True"/>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="#B9A1FF"/><Setter TargetName="Box" Property="BorderBrush" Value="#B9A1FF"/><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger>
    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#B9A1FF"/></Trigger>
    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderThickness" Value="2"/><Setter TargetName="Box" Property="BorderBrush" Value="#F1F0F7"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ComboBoxItem">
  <Setter Property="Foreground" Value="#F1F0F7"/><Setter Property="Background" Value="Transparent"/><Setter Property="Padding" Value="11,9"/>
  <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
   <Border Name="Frame" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="7" Margin="2,1">
    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" ClipToBounds="True"><ContentPresenter.Resources><Style TargetType="TextBlock"><Setter Property="TextTrimming" Value="CharacterEllipsis"/></Style></ContentPresenter.Resources></ContentPresenter>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="#30283F"/><Setter Property="Foreground" Value="#B9A1FF"/></Trigger>
    <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="#292B39"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ComboBox">
  <Setter Property="Background" Value="#232530"/><Setter Property="Foreground" Value="#F1F0F7"/><Setter Property="BorderBrush" Value="#3B3D50"/>
  <Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="12,9"/><Setter Property="MinHeight" Value="38"/><Setter Property="Cursor" Value="Hand"/>
  <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
   <Grid>
    <Border Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9"/>
    <ToggleButton Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
     <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"/></ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <Grid Margin="{TemplateBinding Padding}" IsHitTestVisible="False">
     <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="20"/></Grid.ColumnDefinitions>
     <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" VerticalAlignment="Center" HorizontalAlignment="Stretch" Margin="0,0,8,0" ClipToBounds="True"><ContentPresenter.Resources><Style TargetType="TextBlock"><Setter Property="TextTrimming" Value="CharacterEllipsis"/></Style></ContentPresenter.Resources></ContentPresenter>
     <Path Grid.Column="1" Data="M 1,1 L 5,5 L 9,1" Stroke="#999BAF" StrokeThickness="1.5" HorizontalAlignment="Right" VerticalAlignment="Center"/>
    </Grid>
    <Popup Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
     <Border Width="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}" Background="#191A23" BorderBrush="#3B3D50" BorderThickness="1" CornerRadius="10" Padding="4" Margin="0,5,0,0">
      <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter Name="ItemsPresenter" KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
     </Border>
    </Popup>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#B9A1FF"/></Trigger>
    <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#B9A1FF"/></Trigger>
    <Trigger Property="IsDropDownOpen" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#B9A1FF"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>
"""));
  foreach(var type in new[]{typeof(Slider),typeof(ComboBox),typeof(ComboBoxItem)})if(styles.Contains(type))Resources[type]=styles[type];
  Resources["PresetTextBoxTemplate"]=(ControlTemplate)XamlReader.Parse(UiXaml("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TextBox">
 <Border Name="Frame" CornerRadius="9" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}">
  <ScrollViewer Name="PART_ContentHost" VerticalAlignment="Center"/>
 </Border>
 <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#B9A1FF"/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>
"""));
 }
}
