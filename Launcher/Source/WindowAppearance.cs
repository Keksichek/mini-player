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

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 void ConfigureWindowAppearance(){
  PopulateInterfaceResources();
  WindowStyle=WindowStyle.None;
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=48,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
  string icon=Path.Combine(manager.Root,"Player.ico");if(File.Exists(icon))Icon=BitmapFrame.Create(new Uri(icon));
  SourceInitialized+=(_,_)=>{
   var handle=new WindowInteropHelper(this).Handle;int dark=preferences.LightMode?0:1,border=preferences.LightMode?0x00E6D7DC:0x003A2C2B,corners=2;
   DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,34,ref border,4);DwmSetWindowAttribute(handle,33,ref corners,4);
  };
  Resources[typeof(ToolTip)]=(Style)XamlReader.Parse(UiXaml("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ToolTip">
 <Setter Property="Background" Value="#252733"/><Setter Property="Foreground" Value="#F1F0F7"/>
 <Setter Property="BorderBrush" Value="#3B3D50"/><Setter Property="Padding" Value="10,7"/>
</Style>
"""));
  Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=(Style)XamlReader.Parse(UiXaml("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
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
"""));
  Resources[typeof(ScrollViewer)]=(Style)XamlReader.Parse(UiXaml("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollViewer">
 <Setter Property="Background" Value="Transparent"/>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollViewer">
  <Grid Background="{TemplateBinding Background}">
   <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
   <ScrollContentPresenter Name="PART_ScrollContentPresenter" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" CanContentScroll="{TemplateBinding CanContentScroll}"/>
   <ScrollBar Name="PART_VerticalScrollBar" Grid.Column="1" Width="10" Orientation="Vertical" Style="{DynamicResource {x:Type ScrollBar}}" Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}" Value="{TemplateBinding VerticalOffset}" Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}"/>
  </Grid>
 </ControlTemplate></Setter.Value></Setter>
</Style>
"""));
  Resources[typeof(CheckBox)]=(Style)XamlReader.Parse(UiXaml("""
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="CheckBox">
 <Setter Property="Foreground" Value="#F1F0F7"/><Setter Property="Cursor" Value="Hand"/>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
  <Grid Background="Transparent"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
   <Border Name="Box" Width="17" Height="17" CornerRadius="5" BorderBrush="#55586F" BorderThickness="1" Background="#232530" VerticalAlignment="Center"><Path Name="Check" Data="M 3,8 L 6,11 L 12,4" Stroke="#101117" StrokeThickness="2" Visibility="Collapsed"/></Border>
   <ContentPresenter Grid.Column="1" Margin="10,4,0,4" VerticalAlignment="Center" RecognizesAccessKey="True"/>
  </Grid>
  <ControlTemplate.Triggers>
   <Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="#B9A1FF"/><Setter TargetName="Box" Property="BorderBrush" Value="#B9A1FF"/><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger>
   <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#B9A1FF"/></Trigger>
   <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderThickness" Value="2"/><Setter TargetName="Box" Property="BorderBrush" Value="#F1F0F7"/></Trigger>
   <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>
"""));
 }
 FrameworkElement WindowShell(UIElement body){
  var shell=new DockPanel();
  var titleBar=new DockPanel{Height=48,Background=ColorBrush("#15161E"),LastChildFill=true};DockPanel.SetDock(titleBar,Dock.Top);shell.Children.Add(titleBar);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)};
  DockPanel.SetDock(buttons,Dock.Right);titleBar.Children.Add(buttons);
  Button ChromeButton(string name,string path,Action action,bool close=false){
   var icon=new System.Windows.Shapes.Path{Data=Geometry.Parse(path),Stroke=TextBrush,StrokeThickness=1.4,Width=12,Height=12,Stretch=Stretch.Uniform};
   var button=new Button{Content=icon,Width=40,Height=32,Padding=new Thickness(0),Background=Brushes.Transparent,ToolTip=name};
   button.Template=(ControlTemplate)XamlReader.Parse(UiXaml("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Name='Frame' Background='{TemplateBinding Background}' CornerRadius='7'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='"+(close?"#923746":"#2D2F3D")+"'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter Property='Opacity' Value='.65'/></Trigger></ControlTemplate.Triggers></ControlTemplate>"));
   System.Windows.Automation.AutomationProperties.SetName(button,name);WindowChrome.SetIsHitTestVisibleInChrome(button,true);button.Click+=(_,_)=>action();buttons.Children.Add(button);return button;
  }
  ChromeButton("Свернуть","M 1,7 H 11",()=>WindowState=WindowState.Minimized);
  var maximize=ChromeButton("Развернуть","M 1,1 H 11 V 11 H 1 Z",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);
  StateChanged+=(_,_)=>{
   maximize.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(WindowState==WindowState.Maximized?"M 3,3 H 11 V 11 H 3 Z M 1,8 V 1 H 8":"M 1,1 H 11 V 11 H 1 Z"),Stroke=TextBrush,StrokeThickness=1.4,Width=12,Height=12,Stretch=Stretch.Uniform};
   maximize.ToolTip=WindowState==WindowState.Maximized?"Восстановить":"Развернуть";
   System.Windows.Automation.AutomationProperties.SetName(maximize,(string)maximize.ToolTip);
  };
  ChromeButton("Закрыть","M 1,1 L 11,11 M 11,1 L 1,11",Close,true);
  var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(22,0,0,0),VerticalAlignment=VerticalAlignment.Center};
  brand.Children.Add(new TextBlock{Text="VibeWidgetsHub",FontSize=12,Foreground=TextBrush,VerticalAlignment=VerticalAlignment.Center});
  brand.Children.Add(new Border{Background=ColorBrush("#30283F"),CornerRadius=new CornerRadius(5),Padding=new Thickness(7,3,7,3),Margin=new Thickness(9,0,0,0),Child=new TextBlock{Text="alpha",FontSize=10,Foreground=AccentBrush}});
  themeToggle=new Button{Width=32,Height=30,Padding=new Thickness(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Center,Margin=new Thickness(8,0,0,0)};
  WindowChrome.SetIsHitTestVisibleInChrome(themeToggle,true);
  themeToggle.Click+=(_,_)=>{if(busy||updatePending)return;bool previous=preferences.LightMode;try{preferences.LightMode=!previous;SaveHubPreferences();ApplyHubTheme();Render();}catch(Exception ex){preferences.LightMode=previous;ApplyHubTheme();message=ex.Message;Render();}};
  brand.Children.Add(themeToggle);ApplyHubTheme();titleBar.Children.Add(brand);
  shell.Children.Add(body);
  return new Border{Background=BackgroundBrush,BorderBrush=ColorBrush("#2B2C3A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=shell};
 }
}
