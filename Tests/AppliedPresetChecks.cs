using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class AppliedPresetChecks
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
 static int count;
 static object player=null!;static Type type=null!;static string root="";
 static object? Get(string n)=>type.GetField(n,Flags)!.GetValue(player);
 static void Set(string n,object value)=>type.GetField(n,Flags)!.SetValue(player,value);
 static object? Call(string n,params object[] args)=>type.GetMethod(n,Flags)!.Invoke(player,args);
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
 static Dictionary<string,FrameworkElement> Elements()=>((IReadOnlyDictionary<string,FrameworkElement>)Get("customSurface")!.GetType().GetProperty("Elements")!.GetValue(Get("customSurface"))!).ToDictionary(x=>x.Key,x=>x.Value);
 static void Layout(){var w=(Window)player;var surface=(FrameworkElement)w.Content;surface.Measure(new Size(w.Width,w.Height));surface.Arrange(new Rect(0,0,w.Width,w.Height));surface.UpdateLayout();}
 static string Active=>Path.Combine(root,"Data","Customization","mini-player","active.json");
 static void CheckSourceProfiles(){
  string library=Path.Combine(Path.GetDirectoryName(Active)!,"Presets");
  Directory.CreateDirectory(library);
  string id=Guid.NewGuid().ToString("N"),file=Path.Combine(library,id+".json");
  void SaveProfile(string name)=>File.WriteAllText(file,JsonSerializer.Serialize(new{Id=id,Name=name,Theme="Лес",Width=390,Height=210}));
  string ActiveName(){using var doc=JsonDocument.Parse(File.ReadAllText(Active));return doc.RootElement.GetProperty("Name").GetString()!;}

  WritePreset();Call("RefreshCustomPreset",true);SaveProfile("Для браузера");
  Call("SetSourcePreset","Spotify.exe","theme:Винил");
  Check(ActiveName()=="Винил","Assigning a ready preset changes the selected source immediately "+Get("sourcePresetError"));
  var surface=Get("customSurface");Call("UpdateSourcePreset","spotify.EXE");
  Check(ReferenceEquals(surface,Get("customSurface")),"Same application with different casing does not rebuild on polling");
  using(var guard=new FileStream(Path.Combine(Path.GetDirectoryName(Active)!,"apply.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   Call("SetSourcePreset","browser.exe",id);
   Check(ReferenceEquals(surface,Get("customSurface")),"A simultaneous workshop Apply keeps control of the active appearance");
  }
  Call("UpdateSourcePreset","browser.exe");
  Check(ActiveName()=="Для браузера","Switching to a second application uses its saved preset");
  SaveProfile("Правка в мастерской");File.Copy(file,Active,true);Call("RefreshCustomPreset",true);
  Call("UpdateSourcePreset","browser.exe");
  Check(ActiveName()=="Правка в мастерской","Applying an updated source preset from the workshop keeps the updated variant");
  Call("UpdateSourcePreset","");Check(ActiveName()=="Own integration preset","Disconnect restores the original manual appearance after two mapped sources");
  type.GetField("sourceAssignments",Flags)!.SetValue(player,null);type.GetField("lastPresetSource",Flags)!.SetValue(player,null);
  Call("UpdateSourcePreset","BROWSER.EXE");
  Check(ActiveName()=="Правка в мастерской","Bindings survive reload and resolve updated saved versions by ID");
  string manual=Guid.NewGuid().ToString("N");
  File.WriteAllText(Active,JsonSerializer.Serialize(new{Id=manual,Name="Новый общий"}));Call("RefreshCustomPreset",true);
  Call("UpdateSourcePreset","spotify.exe");Call("UpdateSourcePreset","unmapped.exe");
  Check(ActiveName()=="Новый общий","A new manual choice made while a profile was active becomes the general appearance");
  Call("CommitStandardAppearance");Call("ApplyAppearance");
  Call("UpdateSourcePreset","spotify.exe");File.Delete(file);Call("UpdateSourcePreset","browser.exe");
  Check(!File.Exists(Active)&&Get("customSurface")==null&&((string)Get("sourcePresetError")!).Length>0,"Deleted assignment restores the previous classic appearance and reports the problem");
  Call("UpdateSourcePreset","spotify.exe");Call("SetSourcePreset","spotify.exe","");
  Check(!File.Exists(Active),"Removing an assignment immediately restores the general classic theme");
 }
 static void CheckQuickPresetSelection(){
  var library=Path.Combine(Path.GetDirectoryName(Active)!,"Presets");Directory.CreateDirectory(library);
  string id=Guid.NewGuid().ToString("N"),brokenId=Guid.NewGuid().ToString("N"),altId=Guid.NewGuid().ToString("N");
  string saved=Path.Combine(library,id+".json");
  File.WriteAllText(saved,JsonSerializer.Serialize(new{Id=id,Name="Быстрая смена",Width=490,Height=215}));
  File.WriteAllText(Path.Combine(library,altId+".json"),JsonSerializer.Serialize(new{Id=altId,Name="Второй пресет",Width=310,Height=170}));
  File.WriteAllText(Path.Combine(library,brokenId+".json"),JsonSerializer.Serialize(new{Id=brokenId,Name="Утерянный фон",MediaPath="Media/"+new string('a',64)+".png"}));
  var favorites=Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Active)!,"Favorites"));
  File.WriteAllText(Path.Combine(favorites.FullName,altId+".json"),"true");
  var libraryBytes=File.ReadAllBytes(saved);
  Window Open(){
   Call("Customize",true);var window=(Window)Get("customizeWindow")!;
   var body=(FrameworkElement)window.Content;body.Measure(new Size(window.Width,window.Height));
   body.Arrange(new Rect(0,0,window.Width,window.Height));body.UpdateLayout();return window;
  }
  IEnumerable<T> Find<T>(Window window) where T:DependencyObject=>Walk((FrameworkElement)window.Content).OfType<T>();
  void Click(Window window,string label)=>Find<Button>(window).Single(b=>b.Content as string==label||System.Windows.Automation.AutomationProperties.GetName(b)==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  void Select(Window window,string name){
   var list=Find<ComboBox>(window).Single();list.SelectedItem=list.Items.OfType<ComboBoxItem>().Single(option=>Equals(option.Content,name)||Equals(option.Content,"★ "+name));
  }
  bool Caption(Window window,string value)=>Find<TextBlock>(window).Any(b=>b.Text==value);
  var editor=Open();
  Check(Equals(Find<ComboBox>(editor).Single().Items.OfType<ComboBoxItem>().First(option=>option.Tag!=null).Content,"★ Второй пресет"),"Widget lists a starred preset ahead of alphabetically earlier presets");
  Check(Caption(editor,"Активный пресет · Own integration preset"),"Appearance shows the active preset even when it is absent from the library");
  Select(editor,"Быстрая смена");
  Check(((Window)player).Width==490&&Caption(editor,"Активный пресет · Быстрая смена"),"Selecting a saved preset updates the live widget and active name immediately");
  var committed=File.ReadAllBytes(Active);
  var surface=Get("customSurface");
  Select(editor,"Утерянный фон");
  Check(File.ReadAllBytes(Active).SequenceEqual(committed)&&ReferenceEquals(Get("customSurface"),surface)&&Caption(editor,"Активный пресет · Быстрая смена"),"A missing background preserves the last active preset and visible controls");
  using(var fileLock=File.Open(Path.Combine(Path.GetDirectoryName(Active)!,"apply.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
   Select(editor,"Второй пресет");
   Check(Find<TextBlock>(editor).Any(b=>b.Text.Contains("другом окне"))&&ReferenceEquals(Get("customSurface"),surface),"Concurrent launcher apply is reported without interrupting the active widget");
  }
  // Deny replacement only; readers can still see the previous active file.
  using(var fileLock=File.Open(Active,FileMode.Open,FileAccess.Read,FileShare.Read)){
   Select(editor,"Второй пресет");
   Check(((Window)player).Width==490&&Caption(editor,"Активный пресет · Быстрая смена")&&Find<TextBlock>(editor).Any(b=>b.Text.StartsWith("Не удалось применить пресет")),"Unable to write active file restores the current appearance");
  }
  Click(editor,"Сохранить");
  Check(Get("customizeWindow")==null&&File.ReadAllBytes(Active).SequenceEqual(committed),"Plain Save keeps a newly activated preset");
  editor=Open();Click(editor,"Лаванда");
  Check(Caption(editor,"Предпросмотр темы · Лаванда")&&Find<Button>(editor).Any(b=>Equals(b.Content,"Сохранить и открыть в мастерской")),"Classic preview is identified and the workshop action explains that it will save");
  Click(editor,"Отмена");
  Check(((Window)player).Width==490&&File.ReadAllBytes(Active).SequenceEqual(committed),"Cancelling a classic preview returns to the quick-selected preset");
  editor=Open();Select(editor,"Быстрая смена");editor.Close();
  Check(((Window)player).Width==490&&File.ReadAllBytes(saved).SequenceEqual(libraryBytes),"Closing the window after a quick switch retains the selection and library file");
  editor=Open();Click(editor,"Лаванда");Click(editor,"Сохранить");
  Check(!File.Exists(Active)&&Get("customSurface")==null&&File.ReadAllBytes(saved).SequenceEqual(libraryBytes),"Explicit classic Save switches to the classic theme and keeps saved presets");
 }
 static void WritePreset(int font=16,string shape="rounded"){Directory.CreateDirectory(Path.GetDirectoryName(Active)!);File.WriteAllText(Active,JsonSerializer.Serialize(new{Id="",Name="Own integration preset",Theme="Лаванда",Width=420,Height=260,FontSize=font,Shape=shape,Opacity=.83,TextColor="#ABCDEF",BackgroundColor="#172331"}));}
 static void CheckAck(bool success){using var ack=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Data","mini-player","preset-applied.json")));Check(ack.RootElement.GetProperty("Success").GetBoolean()==success&&ack.RootElement.GetProperty("Sha256").GetString()==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Active))).ToLowerInvariant(),"Acknowledgement matches the exact applied bytes and result");}
 static void CheckOverlappingControls(){
  byte[] original=File.ReadAllBytes(Active);
  var ids=Elements().Keys.Where(id=>id!="pin"&&id!="lock").ToArray();
  var box=new {X=20,Y=32,Width=50,Height=28,Visible=true};
  try{
   foreach(bool pinOnTop in new[]{true,false}){
    string[] order=ids.Concat(pinOnTop?new[]{"lock","pin"}:new[]{"pin","lock"}).ToArray();
    File.WriteAllText(Active,JsonSerializer.Serialize(new{LayerOrder=order,Elements=new{pin=box,@lock=box}}));
    Check((bool)Call("RefreshCustomPreset",true)!,"Player accepts a saved layer order");
    var surface=(Canvas)Get("customSurface")!;
    // WPF routes pointer input only on a visible visual tree. Host the real player
    // content off-screen without starting its media sessions or global shortcuts.
    var window=(Window)player;
    var body=window.Content;window.Content=null;
    var host=new Window{WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false,
     Left=-30000,Top=-30000,Width=window.Width,Height=window.Height,Content=body};
    try{
     host.Show();host.UpdateLayout();
     var hit=surface.InputHitTest(new Point(45,46)) as DependencyObject;
     while(hit!=null && hit is not Button)hit=VisualTreeHelper.GetParent(hit);
     Check(ReferenceEquals(hit,Elements()[order[^1]]),"Topmost overlapping button receives input: "+order[^1]);
    }finally{host.Content=null;host.Close();window.Content=body;}
   }
  }finally{File.WriteAllBytes(Active,original);Call("RefreshCustomPreset",true);Layout();}
 }
 [STAThread]static int Main(string[] args){Application? app=null;try{
  root=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(root,"Data","mini-player"));
  var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll"));type=assembly.GetType("MiniPlayer.Player",true)!;
  var context=assembly.GetType("MiniPlayer.LaunchContext",true)!;context.GetProperty("LauncherRoot",Static)!.SetValue(null,root);context.GetProperty("DataDirectory",Static)!.SetValue(null,Path.Combine(root,"Data","mini-player"));
  app=(Application)Activator.CreateInstance(assembly.GetType("MiniPlayer.PlayerApplication",true)!,true)!;app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  player=Activator.CreateInstance(type)!;((System.Windows.Forms.NotifyIcon)Get("tray")!).Visible=false;Call("LoadAppearance");
  WritePreset();Check((bool)Call("RefreshCustomPreset",true)!,"Live apply accepts the launcher preset");Layout();CheckAck(true);
  CheckOverlappingControls();
  var w=(Window)player;Check(w.Width==420&&w.Height==260&&Get("customSurface")==Get("deviceFace"),"Actual player renders the shared surface at exact preset dimensions");
  var elements=Elements();Check(elements.Keys.OrderBy(x=>x).SequenceEqual(new[]{"cover","title","artist","previous","play","next","repeat","pin","lock","clickthrough","hide","menu","progress","time","volume"}.OrderBy(x=>x)),"All editable player elements exist in the live surface");
  ((TextBlock)Get("title")!).Text="Live title";((TextBlock)Get("artist")!).Text="Live artist";((TextBlock)Get("timeLabel")!).Text="1:20 / 5:00";
  Check(((TextBlock)elements["title"]).Text=="Live title"&&((TextBlock)elements["artist"]).Text=="Live artist"&&((TextBlock)elements["time"]).Text=="1:20 / 5:00","Track metadata and clock bind to actual live source controls");
  var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);((Image)Get("cover")!).Source=bitmap;Check(ReferenceEquals(((Image)elements["cover"]).Source,bitmap),"Actual album art reaches the shared surface");
  var seek=(Slider)Get("seek")!;seek.Maximum=300;seek.IsEnabled=true;seek.Value=80;Check(((Slider)elements["progress"]).Value==80,"Live timeline flows to custom progress control");((Slider)elements["progress"]).Value=90;Check(seek.Value==90,"Custom progress changes reach the seek source");
  var volume=(Slider)Get("volumeSlider")!;volume.IsEnabled=true;volume.Value=34;Check(((Slider)elements["volume"]).Value==34,"Live source volume flows to custom slider");((Slider)elements["volume"]).Value=61;Check(volume.Value==61,"Custom volume changes reach existing audio control");
  ((Button)Get("play")!).IsEnabled=false;Check(!elements["play"].IsEnabled,"Unavailable media commands remain disabled");
  bool pin=w.Topmost;((Button)elements["pin"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(w.Topmost!=pin,"Custom pin button controls actual window topmost state");
  ((Button)elements["lock"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check((bool)Get("movementLocked")!,"Custom movement lock controls actual dragging permission");
  ((Button)elements["clickthrough"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check((bool)Get("clickThrough")!,"Custom button enables click-through");((System.Windows.Forms.ToolStripMenuItem)Get("restoreMouseItem")!).PerformClick();Check(!(bool)Get("clickThrough")!,"Tray restores custom player mouse input without an assigned hotkey");
  ((Button)elements["pin"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check((bool)Get("movementLocked")!,"Pin remains independent of movement lock in custom layout");
  Check(((Canvas)Get("deviceFace")!).ContextMenu!.Items.OfType<MenuItem>().Select(x=>x.Header as string).SequenceEqual(new[]{"Настройки","Кастомизация","Скрыть"}),"Right-click settings and customization stay accessible");
  var previousSurface=Get("customSurface");Call("RefreshCustomPreset",false);Check(ReferenceEquals(previousSurface,Get("customSurface")),"Unchanged preset polling does not rebuild the surface");
  var sizes=elements.ToDictionary(x=>x.Key,x=>(x.Value.Width,x.Value.Height,Canvas.GetLeft(x.Value),Canvas.GetTop(x.Value)));
  WritePreset(60);Check((bool)Call("RefreshCustomPreset",true)!,"Font size 60 applies to the live player");Layout();elements=Elements();
  Check(((TextBlock)elements["title"]).FontSize==60,"Live title uses exact requested font size");Check(elements.All(x=>sizes[x.Key]==(x.Value.Width,x.Value.Height,Canvas.GetLeft(x.Value),Canvas.GetTop(x.Value))),"Changing font size leaves all element rectangles unchanged");
  foreach(string shape in new[]{"star","square","circle","diamond","hexagon","heart"}){WritePreset(16,shape);Check((bool)Call("RefreshCustomPreset",true)!,"Live player accepts shape: "+shape);Layout();}
  previousSurface=Get("customSurface");File.WriteAllText(Active,"{");Check(!(bool)Call("RefreshCustomPreset",true)!&&ReferenceEquals(previousSurface,Get("customSurface")),"Corrupt incoming preset leaves previous live layout intact");CheckAck(false);
  WritePreset();Call("RefreshCustomPreset",true);Call("Customize",true);var editor=(Window)Get("customizeWindow")!;editor.Close();Check(Get("customSurface")!=null&&File.Exists(Active),"Cancelling standard appearance retains active custom preset");
  Call("CommitStandardAppearance");Call("ApplyAppearance");Check(Get("customSurface")==null&&!File.Exists(Active)&&Get("deviceFace")!=null,"Returning to standard appearance removes only the active selection and restores classic player");
  WritePreset();Call("RefreshCustomPreset",true);CheckQuickPresetSelection();
  CheckSourceProfiles();
  Console.WriteLine("TOTAL "+count+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(player!=null){try{Set("closing",true);((Window)player).Close();}catch{}try{((System.Windows.Forms.NotifyIcon)Get("tray")!).Dispose();}catch{}}app?.Shutdown();}}
}
