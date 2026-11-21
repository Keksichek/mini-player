using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WidgetLauncher;

static class PresetColorPickerChecks
{
 const BindingFlags Instance=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
 static IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static T Find<T>(Window window,string name)where T:DependencyObject=>Walk((DependencyObject)window.Content).OfType<T>().Single(x=>AutomationProperties.GetName(x)==name);
 static void Layout(Window window){window.UpdateLayout();}
 static void Snapshot(Window window,string path){Layout(window);var surface=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
 [STAThread]static int Main(string[] args){Application? app=null;LauncherWindow? launcher=null;PresetColorDialog? dialog=null;try{
  app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(root,"Catalog"));Directory.CreateDirectory(Path.Combine(root,"Data"));File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),"[]");Directory.CreateDirectory(args[1]);
  launcher=new LauncherWindow(root,true,"customize"){ShowActivated=false,ShowInTaskbar=false,Opacity=0};launcher.Show();launcher.UpdateLayout();
  string value="#D17174";Brush BrushOf(string name)=>(Brush)typeof(LauncherWindow).GetField(name,Instance)!.GetValue(launcher)!;
  Brush Tone(string hex)=>(Brush)typeof(LauncherWindow).GetMethod("ColorBrush",Instance)!.Invoke(launcher,new object[]{hex})!;
  dialog=new PresetColorDialog(launcher,"Фон",value,next=>value=next,BrushOf("BackgroundBrush"),BrushOf("PanelBrush"),BrushOf("TextBrush"),BrushOf("MutedBrush"),BrushOf("AccentBrush"),Tone("#2B2C3A"),Tone("#232530")){ShowActivated=false,Opacity=0};dialog.Show();Layout(dialog);
  Check(dialog.WindowStyle==WindowStyle.None&&dialog.AllowsTransparency&&dialog.Owner==launcher,"Color picker owns a themed borderless window");
  Check(Find<TextBox>(dialog,"HEX цвета").Text=="#D17174"&&Find<TextBox>(dialog,"R · красный").Text=="209","Initial color synchronizes HEX and RGB without changing the draft");
  Find<TextBox>(dialog,"HEX цвета").Text="#12AB34";Check(value=="#12AB34"&&Find<TextBox>(dialog,"G · зелёный").Text=="171","HEX updates preview and RGB immediately");
  Find<TextBox>(dialog,"R · красный").Text="240";Check(value=="#F0AB34"&&Find<TextBox>(dialog,"HEX цвета").Text=="#F0AB34","RGB values update the actual selected color");
  Find<TextBox>(dialog,"HEX цвета").Text="oops";Check(value=="#F0AB34","Invalid HEX is editable but never applied");
  Find<Button>(dialog,"Сохранить выбранный цвет").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(dialog.IsVisible,"Invalid input cannot be accepted silently");
  Find<TextBox>(dialog,"HEX цвета").Text="#D17174";
  Snapshot(dialog,Path.Combine(args[1],"color-picker-night.png"));
  var background=(SolidColorBrush)((Border)dialog.Content).Background;Color dark=background.Color;
  var prefs=(HubPreferences)typeof(LauncherWindow).GetField("preferences",Instance)!.GetValue(launcher)!;prefs.LightMode=true;typeof(LauncherWindow).GetMethod("ApplyHubTheme",Instance)!.Invoke(launcher,null);Layout(dialog);
  Check(background.Color!=dark&&background.Color.R>200,"An already open picker follows day mode immediately");
  Check(Find<TextBox>(dialog,"HEX цвета").Text=="#D17174","Changing interface mode preserves the selected color");Snapshot(dialog,Path.Combine(args[1],"color-picker-day.png"));
  Find<TextBox>(dialog,"HEX цвета").Text="#010203";Find<Button>(dialog,"Отменить выбор цвета").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(value=="#D17174"&&!dialog.IsVisible,"Cancel restores the initial preview color");dialog=null;
  dialog=new PresetColorDialog(launcher,"Акцент",value,next=>value=next,BrushOf("BackgroundBrush"),BrushOf("PanelBrush"),BrushOf("TextBrush"),BrushOf("MutedBrush"),BrushOf("AccentBrush"),Tone("#2B2C3A"),Tone("#232530")){ShowActivated=false,Opacity=0};dialog.Show();Layout(dialog);
  Find<TextBox>(dialog,"HEX цвета").Text="#456789";Find<Button>(dialog,"Сохранить выбранный цвет").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(value=="#456789"&&!dialog.IsVisible,"Done preserves the chosen color");dialog=null;
  Console.WriteLine("TOTAL "+count+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{dialog?.Close();launcher?.Close();app?.Shutdown();}}
}
