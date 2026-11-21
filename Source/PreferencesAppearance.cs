using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace MiniPlayer;

public sealed partial class Player
{
 // The same palette and window treatment as VibeWidgetsHub, shared by both editors.
 static readonly Brush PreferencesBackground=PreferencesBrush("#101117"),PreferencesPanel=PreferencesBrush("#191A23"),
  PreferencesSurface=PreferencesBrush("#292B39"),PreferencesBorder=PreferencesBrush("#2B2C3A"),
  PreferencesText=PreferencesBrush("#F1F0F7"),PreferencesMuted=PreferencesBrush("#999BAF"),
  PreferencesAccent=PreferencesBrush("#B9A1FF"),PreferencesSelected=PreferencesBrush("#30283F"),PreferencesDanger=PreferencesBrush("#F0A2AC");

 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);

 static readonly ControlTemplate preferencesButtonTemplate=(ControlTemplate)XamlReader.Parse(PreferencesXaml("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
 <Border Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" Padding="{TemplateBinding Padding}">
  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Opacity" Value=".82"/></Trigger>
  <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value=".65"/></Trigger>
  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#B9A1FF"/></Trigger>
  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
"""));

 static ResourceDictionary PreferencesResources(){var resources=(ResourceDictionary)XamlReader.Parse(PreferencesXaml("""
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
"""));foreach(string color in PreferenceDayColors.Keys)resources["PreferenceColor"+color[1..]]=PreferencesBrush(color);return resources;}

 Window CreatePreferencesWindow(string title,double height){
  RefreshHubInterface();
  var area=SystemParameters.WorkArea;
  var w=new Window{
   Title="Mini Player · 0.1 · "+title,Width=Math.Min(600,area.Width),Height=Math.Min(height,area.Height),
   MinWidth=Math.Min(520,area.Width),MinHeight=Math.Min(460,area.Height),
   WindowStartupLocation=WindowStartupLocation.CenterScreen,WindowStyle=WindowStyle.None,Topmost=true,
   Background=PreferencesBackground,Foreground=PreferencesText,FontFamily=PlayerApplication.InterfaceFont,FontWeight=FontWeights.Bold,FontSize=13,
   Resources=PreferencesResources(),UseLayoutRounding=true
  };
  WindowChrome.SetWindowChrome(w,new WindowChrome{CaptionHeight=48,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
  string icon=Path.Combine(AppContext.BaseDirectory,"Player.ico");if(File.Exists(icon))w.Icon=BitmapFrame.Create(new Uri(icon));
  w.SourceInitialized+=(_,_)=>{
   var handle=new WindowInteropHelper(w).Handle;int dark=preferencesLightMode?0:1,border=preferencesLightMode?0x00E6D7DC:0x003A2C2B,corners=2;
   DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,34,ref border,4);DwmSetWindowAttribute(handle,33,ref corners,4);
  };
  return w;
 }

 static Button PreferencesButton(object label)=>new(){
  Content=label,Background=PreferencesSurface,Foreground=PreferencesText,BorderBrush=PreferencesBorder,BorderThickness=new Thickness(1),
  Padding=new Thickness(14,9,14,9),Cursor=System.Windows.Input.Cursors.Hand,Template=preferencesButtonTemplate,HorizontalContentAlignment=HorizontalAlignment.Center
 };
 static void FocusPreferencesWindow(Window w){if(w.WindowState==WindowState.Minimized)w.WindowState=WindowState.Normal;w.Show();w.Activate();}
 static Border PreferencesCard(UIElement content)=>new(){Background=PreferencesPanel,BorderBrush=PreferencesBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Padding=new Thickness(18),Margin=new Thickness(2,3,8,9),Child=content};
 static void MarkPreferencesButton(Button button,bool selected){button.Background=selected?PreferencesSelected:PreferencesSurface;button.BorderBrush=selected?PreferencesAccent:PreferencesBorder;button.Foreground=selected?PreferencesAccent:PreferencesText;}

 static FrameworkElement PreferencesShell(Window window,UIElement content){
  var shell=new DockPanel();
  var caption=new DockPanel{Height=48,Background=PreferencesBrush("#15161E"),LastChildFill=true};DockPanel.SetDock(caption,Dock.Top);shell.Children.Add(caption);
  var controls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)};DockPanel.SetDock(controls,Dock.Right);caption.Children.Add(controls);
  Button ChromeButton(string name,string path,Action action,bool close=false){
   var button=new Button{Width=40,Height=32,Padding=new Thickness(0),Background=Brushes.Transparent,ToolTip=name,Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(path),Stroke=PreferencesText,StrokeThickness=1.4,Width=12,Height=12,Stretch=Stretch.Uniform}};
   button.Template=(ControlTemplate)XamlReader.Parse(PreferencesXaml("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Name='Frame' Background='{TemplateBinding Background}' CornerRadius='7'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='"+(close?"#923746":"#2D2F3D")+"'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='Background' Value='#2D2F3D'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter Property='Opacity' Value='.65'/></Trigger></ControlTemplate.Triggers></ControlTemplate>"));
   System.Windows.Automation.AutomationProperties.SetName(button,name);WindowChrome.SetIsHitTestVisibleInChrome(button,true);button.Click+=(_,_)=>action();controls.Children.Add(button);return button;
  }
  ChromeButton("Свернуть","M 1,7 H 11",()=>window.WindowState=WindowState.Minimized);
  var maximize=ChromeButton("Развернуть","M 1,1 H 11 V 11 H 1 Z",()=>window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);
  window.StateChanged+=(_,_)=>{
   maximize.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(window.WindowState==WindowState.Maximized?"M 3,3 H 11 V 11 H 3 Z M 1,8 V 1 H 8":"M 1,1 H 11 V 11 H 1 Z"),Stroke=PreferencesText,StrokeThickness=1.4,Width=12,Height=12,Stretch=Stretch.Uniform};
   maximize.ToolTip=window.WindowState==WindowState.Maximized?"Восстановить":"Развернуть";System.Windows.Automation.AutomationProperties.SetName(maximize,(string)maximize.ToolTip);
  };
  ChromeButton("Закрыть","M 1,1 L 11,11 M 11,1 L 1,11",window.Close,true);
  var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(22,0,0,0),VerticalAlignment=VerticalAlignment.Center};
  brand.Children.Add(new TextBlock{Text="Mini Player",FontSize=12,Foreground=PreferencesText,VerticalAlignment=VerticalAlignment.Center});
  brand.Children.Add(new Border{Background=PreferencesSelected,CornerRadius=new CornerRadius(5),Padding=new Thickness(7,3,7,3),Margin=new Thickness(9,0,0,0),Child=new TextBlock{Text="0.1",FontSize=10,Foreground=PreferencesAccent}});caption.Children.Add(brand);
  shell.Children.Add(content);
  return new Border{Background=PreferencesBackground,BorderBrush=PreferencesBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=shell};
 }
}
