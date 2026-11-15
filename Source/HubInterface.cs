using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace MiniPlayer;

public sealed partial class Player
{
 static Dictionary<string,string>? preferenceDayPalette;
 static Dictionary<string,string> PreferenceDayColors=>preferenceDayPalette??=new(StringComparer.OrdinalIgnoreCase){
  ["#101117"]="#F4F3F8",["#191A23"]="#FFFFFF",["#292B39"]="#E9E6F1",["#2B2C3A"]="#DCD7E6",
  ["#F1F0F7"]="#272330",["#999BAF"]="#70697E",["#B9A1FF"]="#7153B5",["#30283F"]="#E6DDF7",
  ["#F0A2AC"]="#B42345",["#15161E"]="#ECE9F3",["#3B3D50"]="#D0CADB",["#252733"]="#FFFFFF",
  ["#4B4D61"]="#A69CB8",["#D3C3FF"]="#543391",["#383A4B"]="#D9D3E4",["#55586F"]="#B8AEC9",
  ["#232530"]="#F0EDF5",["#923746"]="#D94D68",["#2D2F3D"]="#E0DAEB",["#2B202A"]="#FCECEF",["#57313B"]="#EAC5CE"
 };
 // Bound colors survive style sealing and update already open editor windows.
 sealed class PreferenceTone:DependencyObject{
  public static readonly DependencyProperty ColorProperty=DependencyProperty.Register(nameof(Color),typeof(Color),typeof(PreferenceTone));
  public Color Color{get=>(Color)GetValue(ColorProperty);set=>SetValue(ColorProperty,value);}
  public readonly SolidColorBrush Brush=new();
  public PreferenceTone(Color color){Color=color;BindingOperations.SetBinding(Brush,SolidColorBrush.ColorProperty,new Binding(nameof(Color)){Source=this});}
 }
 static Dictionary<string,PreferenceTone>? preferenceBrushCache;
 static Dictionary<string,PreferenceTone> preferenceBrushes=>preferenceBrushCache??=new(StringComparer.OrdinalIgnoreCase);
 static bool preferencesLightMode;
 bool hubAllowOffscreen=true;
 long hubInterfaceStamp=-1;
 bool HubAllowsOffscreen=>hubAllowOffscreen;

 static Brush PreferencesBrush(string color){
  if(!preferenceBrushes.TryGetValue(color,out var tone)){
   tone=new PreferenceTone((Color)ColorConverter.ConvertFromString(preferencesLightMode&&PreferenceDayColors.TryGetValue(color,out var day)?day:color));preferenceBrushes[color]=tone;
  }
  return tone.Brush;
 }
 static string PreferencesXaml(string markup)=>Regex.Replace(markup,"#[0-9A-Fa-f]{6,8}",match=>PreferenceDayColors.ContainsKey(match.Value)?"{DynamicResource PreferenceColor"+match.Value[1..].ToUpperInvariant()+"}":match.Value);
 static void ApplyPreferencesTheme(bool light){
  preferencesLightMode=light;
  foreach(var entry in preferenceBrushes)entry.Value.Color=(Color)ColorConverter.ConvertFromString(light&&PreferenceDayColors.TryGetValue(entry.Key,out var day)?day:entry.Key);
 }
 static void UpdatePreferencesFrame(Window? window){
  if(window==null)return;var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
  int dark=preferencesLightMode?0:1,border=preferencesLightMode?0x00E6D7DC:0x003A2C2B;
  DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,34,ref border,4);
 }
 void RefreshHubInterface(bool force=false){
  string? root=LaunchContext.LauncherRoot;
  if(root==null)return;
  try{
   string file=Path.Combine(root,"Data","hub-settings.json");
   if(!File.Exists(file))return;
   var info=new FileInfo(file);if(info.Length>1024*1024)return;
   long stamp=info.LastWriteTimeUtc.Ticks;if(!force&&stamp==hubInterfaceStamp)return;
   using var data=JsonDocument.Parse(File.ReadAllText(file));
   bool light=data.RootElement.TryGetProperty("LightMode",out var mode)&&mode.ValueKind==JsonValueKind.True;
   bool allow=!data.RootElement.TryGetProperty("AllowOffscreen",out var offscreen)||offscreen.ValueKind!=JsonValueKind.False;
   hubInterfaceStamp=stamp;hubAllowOffscreen=allow;
   ApplyPreferencesTheme(light);UpdatePreferencesFrame(settingsWindow);UpdatePreferencesFrame(customizeWindow);
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(JsonException){}catch(InvalidOperationException){}
 }
}
