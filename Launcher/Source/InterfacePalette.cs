using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 static readonly Dictionary<string,string> DayColors=new(StringComparer.OrdinalIgnoreCase){
  ["#101117"]="#F4F3F8",["#191A23"]="#FFFFFF",["#292B39"]="#E9E6F1",["#2B2C3A"]="#DCD7E6",
  ["#F1F0F7"]="#272330",["#999BAF"]="#70697E",["#B9A1FF"]="#7153B5",["#30283F"]="#E6DDF7",
  ["#F0A2AC"]="#B42345",["#15161E"]="#ECE9F3",["#272835"]="#DCD7E6",["#3B3D50"]="#D0CADB",
  ["#252733"]="#FFFFFF",["#4B4D61"]="#A69CB8",["#D3C3FF"]="#543391",["#383A4B"]="#D9D3E4",
  ["#55586F"]="#B8AEC9",["#232530"]="#F0EDF5",["#923746"]="#D94D68",["#2D2F3D"]="#E0DAEB",
  ["#2B202A"]="#FCECEF",["#57313B"]="#EAC5CE",["#302A45"]="#E8DFF4",["#1C2130"]="#E9EDF8",
  ["#203A31"]="#DAEFE4",["#A0E2BD"]="#296B4C",["#777B91"]="#81758E"
 };
 // A binding keeps shared brushes mutable when WPF seals styles and templates.
 sealed class InterfaceTone:DependencyObject{
  public static readonly DependencyProperty ColorProperty=DependencyProperty.Register(nameof(Color),typeof(Color),typeof(InterfaceTone));
  public Color Color{get=>(Color)GetValue(ColorProperty);set=>SetValue(ColorProperty,value);}
  public readonly SolidColorBrush Brush=new();
  public InterfaceTone(Color color){Color=color;BindingOperations.SetBinding(Brush,SolidColorBrush.ColorProperty,new Binding(nameof(Color)){Source=this});}
 }
 readonly Dictionary<string,InterfaceTone> interfaceBrushes=new(StringComparer.OrdinalIgnoreCase);
 Button? themeToggle;
 Color ParseThemeColor(string color)=>(Color)ColorConverter.ConvertFromString(preferences.LightMode&&DayColors.TryGetValue(color,out var light)?light:color);
 Brush ColorBrush(string color){if(!interfaceBrushes.TryGetValue(color,out var tone)){tone=new InterfaceTone(ParseThemeColor(color));interfaceBrushes[color]=tone;}return tone.Brush;}
 string UiXaml(string markup)=>Regex.Replace(markup,"#[0-9A-Fa-f]{6,8}",match=>DayColors.ContainsKey(match.Value)?"{DynamicResource HubColor"+match.Value[1..].ToUpperInvariant()+"}":match.Value);
 void PopulateInterfaceResources(){foreach(string color in DayColors.Keys)Resources["HubColor"+color[1..]]=ColorBrush(color);}
 void ApplyHubTheme(){
  foreach(var entry in interfaceBrushes)entry.Value.Color=ParseThemeColor(entry.Key);
  var handle=new WindowInteropHelper(this).Handle;if(handle!=IntPtr.Zero){int dark=preferences.LightMode?0:1,border=preferences.LightMode?0x00E6D7DC:0x003A2C2B;DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,34,ref border,4);}
  if(themeToggle!=null){
   themeToggle.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse(preferences.LightMode?"M 14,10 A 6.5,6.5 0 0 1 6,2 A 6.5,6.5 0 1 0 14,10 Z":"M 8,4 A 4,4 0 1 0 8,12 A 4,4 0 1 0 8,4 M 8,0 V 2 M 8,14 V 16 M 0,8 H 2 M 14,8 H 16 M 2,2 L 3.5,3.5 M 12.5,12.5 L 14,14 M 2,14 L 3.5,12.5 M 12.5,3.5 L 14,2"),Stroke=TextBrush,StrokeThickness=1.5,Width=16,Height=16,Stretch=Stretch.Uniform};
   themeToggle.ToolTip=preferences.LightMode?"Включить ночной режим":"Включить дневной режим";System.Windows.Automation.AutomationProperties.SetName(themeToggle,(string)themeToggle.ToolTip);
  }
 }
}
