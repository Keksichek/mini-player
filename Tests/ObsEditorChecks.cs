using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using WidgetLauncher;
static class ObsEditorChecks{
 static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T t)yield return t;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in All<T>(child))yield return node;}
 static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
 [STAThread]static int Main(string[] args){try{var app=new Application();var fixture=Path.Combine(Path.GetDirectoryName(args[0])!,"editor-test-fixture");Directory.CreateDirectory(Path.Combine(fixture,"Catalog"));File.WriteAllText(Path.Combine(fixture,"Catalog","widgets.json"),"[]");var owner=new LauncherWindow(fixture,true){ShowInTaskbar=false,ShowActivated=false,Opacity=0};typeof(LauncherWindow).GetMethod("ConfigurePresetControls",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner,null);owner.Show();owner.Hide();var initial=new ObsAppearance();ObsAppearance live=initial.Copy();int applied=0;var saved=new Dictionary<string,ObsAppearance>();
 var editor=new ObsAppearanceEditor(owner,initial,saved,a=>{live=a.Copy();applied++;},(n,a)=>saved[n]=a.Copy(),new SolidColorBrush(Color.FromRgb(16,17,23)),new SolidColorBrush(Color.FromRgb(35,34,46)),Brushes.White,Brushes.Gray,Brushes.MediumPurple);
 All<ComboBox>(editor).First().SelectedIndex=4;Check(applied==0&&live.Shape=="rounded"&&initial.Shape=="rounded","Preset changes are drafts; initial/live appearance stays untouched");
 var artist=All<CheckBox>(editor).First(x=>(string)x.Content=="Исполнитель");artist.IsChecked=false;artist.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
 All<Button>(editor).First(x=>(string)x.Content=="Сохранить пресет").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(saved.Count==1&&applied==0,"Saving a named preset does not alter the live overlay");
 All<Button>(editor).First(x=>(string)x.Content=="Применить в OBS").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(applied==1&&live.Shape=="circle"&&!live.Parts["artist"].Visible,"Apply commits shape and element visibility");
 var surface=(FrameworkElement)editor.Content;surface.Measure(new Size(1040,690));surface.Arrange(new Rect(0,0,1040,690));surface.UpdateLayout();var bmp=new RenderTargetBitmap(1040,690,96,96,PixelFormats.Pbgra32);bmp.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var output=File.Create(args[0]))png.Save(output);
 File.WriteAllText(args[0]+".json",JsonSerializer.Serialize(live));
 All<ComboBox>(editor).First().SelectedIndex=1;editor.Close();Check(applied==1&&live.Shape=="circle","Closing an edited draft does not apply it");
 var bad=new ObsAppearance{Width=double.PositiveInfinity,Background="red;url(test)",Shape="invalid"};bad.Parts["title"].X=99999;bad.Normalize();Check(bad.Width==360&&bad.Background=="#191821"&&bad.Parts["title"].X+bad.Parts["title"].Width<=bad.Width,"Appearance normalization bounds positions and rejects CSS injection");owner.Close();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
