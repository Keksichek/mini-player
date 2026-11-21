using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
namespace WidgetLauncher;
public sealed partial class LauncherWindow
{
 void PresetTypography(StackPanel panel,WidgetPreset preset){
  panel.Children.Add(Label("Шрифт",12,MutedBrush,new Thickness(0,12,0,6)));
  var fonts=PresetFonts();
  var combo=new ComboBox{ItemsSource=fonts,IsEditable=true,IsTextSearchEnabled=false,StaysOpenOnEdit=true,Text=preset.FontFamily,MaxDropDownHeight=230,MinHeight=38,Foreground=TextBrush,Background=ColorBrush("#232530"),BorderBrush=ColorBrush("#3B3D50"),Padding=new Thickness(10,8,10,8)};
  AutomationProperties.SetName(combo,"Шрифт");combo.ToolTip="Выберите шрифт или введите его название";
  combo.Template=(ControlTemplate)XamlReader.Parse(UiXaml("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ComboBox">
 <Grid>
  <Border Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="9"/>
  <Grid Margin="{TemplateBinding Padding}"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="24"/></Grid.ColumnDefinitions>
   <TextBox Name="PART_EditableTextBox" Background="Transparent" Foreground="{TemplateBinding Foreground}" BorderThickness="0" Padding="0" VerticalContentAlignment="Center" Style="{x:Null}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
   <ToggleButton Grid.Column="1" Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><Path Data="M 1,1 L 5,5 L 9,1" Stroke="#999BAF" StrokeThickness="1.5" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
  </Grid>
  <Popup Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False">
   <Border Width="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}" Background="#191A23" BorderBrush="#3B3D50" BorderThickness="1" CornerRadius="10" Padding="4" Margin="0,5,0,0">
    <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter/></ScrollViewer>
   </Border>
  </Popup>
 </Grid>
 <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#B9A1FF"/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>
"""));
  panel.Children.Add(combo);
  var hint=Label("",11,MutedBrush,new Thickness(0,6,0,0));AutomationProperties.SetName(hint,"Результат поиска шрифта");panel.Children.Add(hint);
  bool updating=false;string committed=preset.FontFamily;
  void Commit(string font){preset.FontFamily=font;committed=font;hint.Text="";RefreshPresetPreview();}
  void SelectFont(string font){updating=true;try{combo.Text=font;combo.SelectedItem=font;}finally{updating=false;}Commit(font);combo.IsDropDownOpen=false;}
  combo.DropDownOpened+=(_,_)=>{
   if(updating||!string.Equals(combo.Text.Trim(),committed,StringComparison.CurrentCultureIgnoreCase))return;
   updating=true;
   try{combo.ItemsSource=fonts;combo.SelectedItem=committed;combo.Text=committed;}
   finally{updating=false;}
  };
  combo.SelectionChanged+=(_,_)=>{if(!updating&&combo.SelectedItem is string font)SelectFont(font);};
  combo.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler((sender,args)=>{
   if(updating||args.OriginalSource is not TextBox input)return;
   string query=input.Text;int caret=input.CaretIndex;int selection=input.SelectionLength;
   var matches=fonts.Where(x=>x.Contains(query.Trim(),StringComparison.CurrentCultureIgnoreCase)).ToArray();
   updating=true;try{combo.ItemsSource=matches;combo.SelectedIndex=-1;combo.Text=query;input.Text=query;input.Select(Math.Min(caret,query.Length),Math.Min(selection,Math.Max(0,query.Length-caret)));}finally{updating=false;}
   string? exact=fonts.FirstOrDefault(x=>x.Equals(query.Trim(),StringComparison.CurrentCultureIgnoreCase));
   if(exact!=null)Commit(exact);else hint.Text=matches.Length==0?"Такого шрифта нет среди установленных.":"Выберите шрифт из списка или допишите название.";
   if(input.IsKeyboardFocusWithin)combo.IsDropDownOpen=matches.Length>0&&exact==null;
  }));
  combo.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){string? exact=fonts.FirstOrDefault(x=>x.Equals(combo.Text.Trim(),StringComparison.CurrentCultureIgnoreCase));if(exact!=null)SelectFont(exact);else if(combo.SelectedItem is string selected)SelectFont(selected);e.Handled=true;}else if(e.Key==Key.Escape){SelectFont(committed);e.Handled=true;}};
  combo.LostKeyboardFocus+=(_,_)=>{if(!combo.IsKeyboardFocusWithin&&!combo.IsDropDownOpen){string? exact=fonts.FirstOrDefault(x=>x.Equals(combo.Text.Trim(),StringComparison.CurrentCultureIgnoreCase));if(exact!=null)SelectFont(exact);else{updating=true;combo.Text=committed;updating=false;}}};
  panel.Children.Add(Label("Размер текста (px)",12,MutedBrush,new Thickness(0,14,0,6)));
  var size=PresetTextBox("Размер текста (px)",preset.FontSize.ToString("0.##",CultureInfo.CurrentCulture));size.MaxLength=6;panel.Children.Add(size);
  var slider=new Slider{Minimum=1,Maximum=60,TickFrequency=1,IsSnapToTickEnabled=true,Value=preset.FontSize,Height=28,Margin=new Thickness(0,7,0,0)};AutomationProperties.SetName(slider,"Размер текста");GroupSliderHistory(slider);panel.Children.Add(slider);
  var warning=Label("",12,ColorBrush("#F0A2AC"),new Thickness(0,5,0,0));AutomationProperties.SetName(warning,"Уведомление о размере шрифта");panel.Children.Add(warning);
  bool sync=false;
  size.TextChanged+=(_,_)=>{if(sync)return;if(!double.TryParse(size.Text.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value)||value<1){warning.Text="Введите размер от 1 до 60 px.";return;}bool over=value>60;value=Math.Min(60,value);sync=true;try{slider.Value=value;if(over){size.Text="60";size.CaretIndex=size.Text.Length;}}finally{sync=false;}preset.FontSize=value;warning.Text=over?"Максимальный размер — 60 px. Значение уменьшено до 60.":"";RefreshPresetPreview();};
  slider.ValueChanged+=(_,_)=>{if(sync)return;sync=true;size.Text=slider.Value.ToString("0.##",CultureInfo.CurrentCulture);sync=false;preset.FontSize=slider.Value;warning.Text="";RefreshPresetPreview();};
  size.LostKeyboardFocus+=(_,_)=>{if(!double.TryParse(size.Text.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)||value<1)size.Text=preset.FontSize.ToString("0.##",CultureInfo.CurrentCulture);};
 }
}
