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
 [STAThread]static int Main(string[] args){try{var app=new Application();var fixture=Path.Combine(Path.GetDirectoryName(args[0])!,"editor-test-fixture");Directory.CreateDirectory(Path.Combine(fixture,"Catalog"));File.WriteAllText(Path.Combine(fixture,"Catalog","widgets.json"),"[]");var owner=new LauncherWindow(fixture,true){ShowInTaskbar=false,ShowActivated=false,Opacity=0};typeof(LauncherWindow).GetMethod("ConfigurePresetControls",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner,null);owner.Show();owner.Hide();using var fonts=new ObsBridge(new WidgetManager(fixture));var initial=new ObsAppearance();ObsAppearance live=initial.Copy();int applied=0;var saved=new Dictionary<string,ObsAppearance>();
 var editor=new ObsAppearanceEditor(owner,initial,saved,a=>{live=a.Copy();applied++;},(n,a)=>saved[n]=a.Copy(),new SolidColorBrush(Color.FromRgb(16,17,23)),new SolidColorBrush(Color.FromRgb(35,34,46)),Brushes.White,Brushes.Gray,Brushes.MediumPurple,fonts);
 All<ComboBox>(editor).First().SelectedIndex=4;Check(applied==0&&live.Shape=="rounded"&&initial.Shape=="rounded","Preset changes are drafts; initial/live appearance stays untouched");
 var grid=All<CheckBox>(editor).First(x=>(string)x.Content=="Сетка и привязка");grid.IsChecked=true;grid.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));All<ComboBox>(editor).First(x=>x.ItemsSource is int[]).SelectedItem=16;
 editor.ImportFont(Path.Combine(Environment.CurrentDirectory,"Source","Fonts","Onest-Bold.ttf"));Check(fonts.Settings.Fonts.Count==1&&applied==0,"Font upload is available without changing live appearance");
 var artist=All<CheckBox>(editor).First(x=>(string)x.Content=="Исполнитель");artist.IsChecked=false;artist.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
 All<Button>(editor).First(x=>(string)x.Content=="Сохранить пресет").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(saved.Count==1&&applied==0,"Saving a named preset does not alter the live overlay");
 All<Button>(editor).First(x=>(string)x.Content=="Применить в OBS").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(applied==1&&live.Shape=="circle"&&!live.Parts["artist"].Visible,"Apply commits shape and element visibility");
 Check(live.GridEnabled&&live.GridSize==16&&live.Parts.Values.All(p=>p.X%16==0&&p.Y%16==0),"Grid checkbox and step snap positions only");Check(live.Parts["bar"].Height==5&&live.Parts["bar"].Width==220,"Changing grid never enlarges timeline");Check(live.FontId!="","Uploaded font selection is applied");
 var family=(FontFamily)typeof(ObsAppearanceEditor).GetMethod("PreviewFont",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(editor,null)!; Check(family.GetTypefaces().Any(t=>t.TryGetGlyphTypeface(out var glyph)&&glyph.FontUri.AbsoluteUri.Contains("ObsFonts",StringComparison.OrdinalIgnoreCase)),"Preview loads font from hub without OS installation");
 editor.PointerDown("title","",new Point(0,0),true);editor.PointerDown("duration","",new Point(0,0),true);
 var surface=(FrameworkElement)editor.Content;surface.Measure(new Size(1040,690));surface.Arrange(new Rect(0,0,1040,690));surface.UpdateLayout();var bmp=new RenderTargetBitmap(1040,690,96,96,PixelFormats.Pbgra32);bmp.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var output=File.Create(args[0]))png.Save(output);
 File.WriteAllText(args[0]+".json",JsonSerializer.Serialize(live));
 All<ComboBox>(editor).First().SelectedIndex=1;editor.Close();Check(applied==1&&live.Shape=="circle","Closing an edited draft does not apply it");
 var bad=new ObsAppearance{Width=double.PositiveInfinity,Background="red;url(test)",Shape="invalid"};bad.Parts["title"].X=99999;bad.Normalize();Check(bad.Width==360&&bad.Background=="#191821"&&bad.Parts["title"].X+bad.Parts["title"].Width<=bad.Width,"Appearance normalization bounds positions and rejects CSS injection");
 for(int step=2;step<=32;step++){var a=new ObsAppearance{GridEnabled=true,GridSize=step};var before=a.Copy();a.AlignToGrid();Check(a.Parts.All(pair=>pair.Value.X%step==0&&pair.Value.Y%step==0&&pair.Value.Width==before.Parts[pair.Key].Width&&pair.Value.Height==before.Parts[pair.Key].Height&&pair.Value.X+pair.Value.Width<=a.Width&&pair.Value.Y+pair.Value.Height<=a.Height),"Grid preserves sizes and aligns positions for step "+step);}
 Gestures(owner);
 var free=new ObsAppearance{GridEnabled=false};free.Parts["title"].X=7.5;free.Normalize();Check(free.Parts["title"].X==7.5,"Grid off preserves free positioning");owner.Close();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static ObsAppearance Draft(ObsAppearanceEditor editor)=>(ObsAppearance)typeof(ObsAppearanceEditor).GetField("draft",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
 static int Selected(ObsAppearanceEditor editor)=>((HashSet<string>)typeof(ObsAppearanceEditor).GetField("selection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!).Count;
 static bool Near(double a,double b)=>Math.Abs(a-b)<.00001;
 static void Gestures(Window owner){
  var initial=new ObsAppearance{Width=600,Height=400};initial.Parts["cover"]=new(40,40,120,60);initial.Parts["title"]=new(200,40,240,24);int applies=0;
  var editor=new ObsAppearanceEditor(owner,initial,new(),_=>applies++,(_,_)=>{},Brushes.Black,Brushes.DimGray,Brushes.White,Brushes.Gray,Brushes.MediumPurple);
  editor.PointerDown("cover","",new Point(),true);editor.PointerDown("title","",new Point(),true);Check(Selected(editor)==2,"Shift click adds multiple elements");
  editor.PointerDown("cover","",new Point(),false);editor.PointerMove(new Point(31,47),false);editor.PointerMove(new Point(31,47),false);editor.PointerUp();var a=Draft(editor);
  Check(a.Parts["cover"].X==71&&a.Parts["title"].X==231&&a.Parts["cover"].Y==87&&a.Parts["title"].Y==87&&Selected(editor)==2,"Group drag preserves offsets and repeated moves do not accumulate");
  editor.PointerDown("title","",new Point(),false);editor.PointerUp();Check(Selected(editor)==0,"Single click without dragging clears group selection");
  editor.PointerDown("cover","",new Point(),false);editor.PointerUp();Check(Selected(editor)==1,"Single click selects an element for resizing");
  var p=ObsGeometry.Clone(a.Parts["cover"]);editor.PointerDown("cover","se",new Point(),false);editor.PointerMove(new Point(60,40),true);editor.PointerUp();Check(Near(a.Parts["cover"].Width/a.Parts["cover"].Height,p.Width/p.Height),"Holding Shift during resize preserves aspect ratio");
  p=ObsGeometry.Clone(a.Parts["cover"]);editor.PointerDown("cover","e",new Point(),false);editor.PointerMove(new Point(30,0),false);editor.PointerUp();Check(Near(a.Parts["cover"].Width,p.Width+30)&&Near(a.Parts["cover"].Height,p.Height),"Side handle stretches one axis only");
  editor.ClearSelection();Check(Selected(editor)==0,"Background/right-click action clears selection");
  a.GridEnabled=true;a.GridSize=16;a.AlignToGrid();editor.PointerDown("cover","",new Point(),true);editor.PointerDown("title","",new Point(),true);double gap=a.Parts["title"].X-a.Parts["cover"].X;editor.PointerDown("cover","",new Point(),false);editor.PointerMove(new Point(10000,10000),false);editor.PointerUp();
  Check(Near(a.Parts["title"].X-a.Parts["cover"].X,gap)&&new[]{"cover","title"}.All(k=>a.Parts[k].X%16==0&&a.Parts[k].Y%16==0&&a.Parts[k].X+a.Parts[k].Width<=a.Width&&a.Parts[k].Y+a.Parts[k].Height<=a.Height),"Group stays together and on grid at boundaries");
  double thickness=a.Parts["bar"].Height;editor.PointerDown("bar","e",new Point(),false);editor.PointerMove(new Point(-40,0),false);editor.PointerUp();Check(a.Parts["bar"].Height==thickness,"Horizontal timeline resize never changes thickness");
  a.GridEnabled=false;var before=a.Copy();editor.PointerDown(null,"se",new Point(),false,true);editor.PointerMove(new Point(-100,-80),false);editor.PointerMove(new Point(0,0),false);editor.PointerUp();Check(a.Width==before.Width&&a.Height==before.Height&&a.Parts["title"].X==before.Parts["title"].X,"Frame resize uses starting snapshot and restores content when regrown");
  foreach(string shape in new[]{"rounded","rectangle","circle"}){a.Shape=shape;var original=a.Copy();editor.PointerDown(null,"se",new Point(),false,true);editor.PointerMove(new Point(40,20),false);editor.PointerUp();Check(a.Width==original.Width+40&&a.Height==original.Height+20&&a.Shape==shape,"Mouse resizes both axes of "+shape);}
  a.Shape="circle";before=a.Copy();editor.PointerDown(null,"se",new Point(),false,true);editor.PointerMove(new Point(30,70),true);editor.PointerUp();Check(Near(a.Width/a.Height,before.Width/before.Height),"Shift preserves frame aspect ratio including ellipse");
  foreach(bool grid in new[]{false,true})foreach(string handle in new[]{"nw","n","ne","e","se","s","sw","w"}){var bounds=new ObsAppearance{Width=500,Height=400,GridEnabled=grid,GridSize=16};var part=new ObsPart(100,100,120,60);var resized=ObsGeometry.Resize(bounds,"cover",part,handle,-900,900,true);Check(Near(resized.Width/resized.Height,2)&&resized.X>=0&&resized.Y>=0&&resized.X+resized.Width<=500.001&&resized.Y+resized.Height<=400.001,"Proportional resize bounded: "+handle+" grid="+grid);}
  Check(!All<TextBlock>(editor).Any(t=>t.Text.StartsWith("Ширина элемента:")||t.Text.StartsWith("Высота элемента:")||t.Text.StartsWith("X:")||t.Text.StartsWith("Y:")),"Geometry sliders are removed");Check(applies==0,"Mouse gestures never change live OBS until Apply");editor.Close();
 }

}
