using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WidgetLauncher;

internal static class LauncherCustomizationChecks
{
 const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;
 static LauncherWindow window=null!;
 static string snapshots="";
 static IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
 static IEnumerable<T> Nodes<T>() where T:DependencyObject=>Walk((DependencyObject)window.Content).OfType<T>();
 static void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
 static object? Field(string name)=>typeof(LauncherWindow).GetField(name,Instance)!.GetValue(window);
 static void Call(string name)=>typeof(LauncherWindow).GetMethod(name,Instance,null,Type.EmptyTypes,null)!.Invoke(window,null);
 static WidgetPreset Draft=>(WidgetPreset)Field("presetDraft")!;
 static PresetPreview Preview=>(PresetPreview)Field("presetPreview")!;
 static void Home(){Navigate("customize");for(int i=0;i<4&&(string)Field("customizationStep")! != "home";i++)Click("← Назад");Check((string)Field("customizationStep")! == "home","Back navigation returns to the four entry cards");}
 static void Navigate(string id){((Dictionary<string,Button>)Field("nav")!)[id].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();}
 static T Named<T>(string name) where T:DependencyObject=>Nodes<T>().Single(x=>AutomationProperties.GetName(x)==name);
 static Button Button(string name)=>Nodes<Button>().Single(x=>AutomationProperties.GetName(x)==name||x.Content as string==name);
 static void Click(string name){var button=Button(name);Check(button.IsEnabled,"Action is available: "+name);button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();}
 static void Layout(){var surface=(FrameworkElement)window.Content;surface.Measure(new Size(window.Width,window.Height));surface.Arrange(new Rect(0,0,window.Width,window.Height));surface.UpdateLayout();}
 static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);Layout();}
 static void TypeFont(string value,bool enter=false){var combo=Named<ComboBox>("Шрифт");combo.ApplyTemplate();var edit=(TextBox)combo.Template.FindName("PART_EditableTextBox",combo);var provider=(IValueProvider)new TextBoxAutomationPeer(edit).GetPattern(PatternInterface.Value);provider.SetValue(value);Pump();if(enter){edit.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(edit),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump();}}
 static void ChooseFont(string value){var combo=Named<ComboBox>("Шрифт");var peer=new ComboBoxAutomationPeer(combo);((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).Expand();Pump();var item=peer.GetChildren().Single(x=>x.GetName()==value);((ISelectionItemProvider)item.GetPattern(PatternInterface.SelectionItem)).Select();Pump();}
 static void Toggle(CheckBox check,bool value){check.IsChecked=value;check.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Layout();}
 static double RenderScale(PresetPreview preview){var transform=preview.Surface.TransformToAncestor(preview);return transform.Transform(new Point(1,0)).X-transform.Transform(new Point()).X;}
 static void Snapshot(string name){Layout();var surface=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(snapshots,name+".png"));png.Save(output);}
 static string PixelHash(FrameworkElement surface){surface.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(surface.ActualWidth)),Math.Max(1,(int)Math.Ceiling(surface.ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);byte[] pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);return Convert.ToHexString(SHA256.HashData(pixels));}
 static void CheckWorkshopHistory(){
  string State()=>JsonSerializer.Serialize(Draft);
  void DragPhase(string method)=>typeof(LauncherWindow).GetMethod(method,Instance)!.Invoke(window,new object?[]{null});
  string original=State();
  Check(!Button("Отменить").IsEnabled&&!Button("Повторить").IsEnabled,"A newly opened preset starts with an empty history");
  Click("Детали");Named<ComboBox>("Элемент").SelectedItem="Пауза / воспроизведение";Layout();
  var before=Draft.LayerOrder.ToArray();
  int PlayLayer()=>Preview.Surface.Children.IndexOf(Preview.Surface.Elements["play"]);
  int z=PlayLayer();
  Click("Выше");
  string raised=State();
  Check(!before.SequenceEqual(Draft.LayerOrder)&&PlayLayer()>z,"Raising a layer updates the draft and displayed stacking order");
  Click("Отменить");
  Check(State()==original&&PlayLayer()==z,"Undo restores both the original layer order and its preview");
  Click("Повторить");
  Check(State()==raised&&PlayLayer()>z,"Redo reapplies a layer movement");

  Click("Превью 1:1");var popup=(Window)Field("largePresetWindow")!;popup.Opacity=0;
  var element=Draft.Elements["play"];
  double start=element.X;double y=element.Y;
  var move=typeof(LauncherWindow).GetMethod("MovePresetElement",Instance)!;
  DragPhase("BeginPresetGesture");
  for(int i=1;i<=8;i++)move.Invoke(window,new object[]{"play",start+i,y});
  DragPhase("EndPresetGesture");
  string dragged=State();
  Click("Отменить");var enlarged=(PresetPreview)Field("largePresetPreview")!;
  Check(State()==raised&&Canvas.GetLeft(enlarged.Surface.Elements["play"])==start,"One Undo reverses an entire drag and updates the open 1:1 preview");
  Click("Повторить");Check(State()==dragged&&Canvas.GetLeft(enlarged.Surface.Elements["play"])==start+8,"Redo restores the final drag position in both views");
  popup.Close();Click("Отменить");
  Click("Текст");Named<Slider>("Размер текста").Value=Draft.FontSize==26?27:26;
  Check(!Button("Повторить").IsEnabled,"A new font edit discards the redo branch");
  Click("Отменить");Check(State()==raised,"Undoing a font edit restores its exact previous preset");

  Click("Цвета");
  string oldColor=Draft.BackgroundColor;
  void ColorEdit(bool accept,bool rebuild){
   Click("Выбрать цвет: Фон");var dialog=(Window)Field("presetColorPicker")!;dialog.Opacity=0;dialog.UpdateLayout();
   var hex=Walk(dialog).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="HEX цвета");
   hex.Text="#124356";if(rebuild){Call("Render");Layout();}hex.Text="#234567";
   if(accept)Walk(dialog).OfType<Button>().Single(x=>AutomationProperties.GetName(x)=="Сохранить выбранный цвет").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
   else dialog.Close();
   Layout();
  }
  ColorEdit(false,true);
  Check(State()==raised&&Named<TextBox>("Фон HEX").Text==oldColor&&Button("Повторить").IsEnabled,"Cancel after a palette rerender restores the previous color and preserves redo");
  ColorEdit(true,true);
  Check(Draft.BackgroundColor=="#234567"&&Named<TextBox>("Фон HEX").Text=="#234567","Accepting color edits after an editor rerender synchronizes the current color field");
  Click("Отменить");Check(State()==raised,"All live changes in a color dialog take one undo step");
  Click("Отменить");Check(State()==original&&!Button("Отменить").IsEnabled,"Undo stops at the original preset, including its identity");
  Click("Форма");
 }
 static void CheckLiveTrackPreview(string root,bool withPipe){
  string state=JsonSerializer.Serialize(Draft);
  var encoder=new JpegBitmapEncoder();
  encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1,1,96,96,PixelFormats.Bgr24,null,new byte[]{5,90,180},3)));
  using var buffer=new MemoryStream();encoder.Save(buffer);
  TrackPreviewFrame? frame=new("Тестовый трек","Исполнитель из плеера","0:33 / 2:00",33,120,true,buffer.ToArray());
  string instance=WidgetManager.InstanceNameForData(Path.Combine(root,"Data","mini-player"));
  using var stop=new System.Threading.CancellationTokenSource();
  var server=withPipe?TrackPreviewProtocol.ListenAsync(instance,_=>System.Threading.Tasks.Task.FromResult(System.Threading.Volatile.Read(ref frame)),stop.Token):System.Threading.Tasks.Task.CompletedTask;
  string Title(PresetPreview view)=>((TextBlock)view.Surface.Elements["title"]).Text;
  void Wait(Func<bool> condition,string name){var clock=System.Diagnostics.Stopwatch.StartNew();while(!condition()&&clock.Elapsed.TotalSeconds<5)Pump();Check(condition(),name);}
  void Present(){if(!withPipe)typeof(LauncherWindow).GetMethod("DisplayTrack",Instance)!.Invoke(window,new object?[]{frame,"Нет трека"});}
  try{
   if(withPipe)window.Dispatcher.BeginInvoke((Action)(()=>Click("Мой трек")));
   else{Click("Мой трек");Call("StopTrackPreview");Present();}
   Wait(()=>Title(Preview)=="Тестовый трек",withPipe?"Workshop reads selected track from the player's local preview endpoint":"Workshop shows selected track in live mode");
   Click("Превью 1:1");
   var large=(PresetPreview)Field("largePresetPreview")!;
   var timeline=(Slider)large.Surface.Elements["progress"];
   Check(Title(large)=="Тестовый трек"&&((TextBlock)large.Surface.Elements["artist"]).Text=="Исполнитель из плеера"&&
    ((Image)large.Surface.Elements["cover"]).Source is BitmapSource&&timeline.Maximum==120&&timeline.Value==33&&
    ((TextBlock)large.Surface.Elements["time"]).Text=="0:33 / 2:00","Both previews show live track metadata, actual artwork and playhead");
   System.Threading.Volatile.Write(ref frame,frame! with{Position=40,Playing=false,Artwork=null});Present();
   Wait(()=>timeline.Value==40&&Equals(((Button)large.Surface.Elements["play"]).Tag,false)&&((Image)Preview.Surface.Elements["cover"]).Source==null,
    "Playback and missing album art update in place without rebuilding the preview");
   System.Threading.Volatile.Write(ref frame,null);Present();
   Wait(()=>Title(Preview)=="Название трека"&&Title(large)=="Название трека","Loss of source restores sample content in both previews");
   System.Threading.Volatile.Write(ref frame,new TrackPreviewFrame("Другой источник","","0:00 / 0:00",0,0,false,null));Present();
   Wait(()=>Title(Preview)=="Другой источник"&&((Image)large.Surface.Elements["cover"]).Source==null,"Live mode resumes on a new source without retaining the previous artwork");
   Check(JsonSerializer.Serialize(Draft)==state&&!Button("Отменить").IsEnabled,"Track preview never changes preset contents or adds editing history");
   Click("Пример");
   ((Window)Field("largePresetWindow")!).Close();
  }finally{stop.Cancel();server.GetAwaiter().GetResult();}
 }
 static void CheckPresetComparison(string root,bool classic){
  var store=new PresetStore(root);
  var applied=PresetThemes.Create("Кофе");applied.Resize(410,170);store.Apply(applied);
  string active=File.ReadAllText(store.ActivePath),draft=JsonSerializer.Serialize(Draft);
  int cursor=(int)Field("editCursor")!;
  Click("Превью 1:1");var popup=(Window)Field("largePresetWindow")!;
  var large=(PresetPreview)Field("largePresetPreview")!;
  var button=Walk(popup).OfType<Button>().Single(x=>AutomationProperties.GetName(x)=="Сравнить");
  Image Overlay(PresetPreview view)=>(Image)typeof(PresetPreview).GetField("comparison",Instance)!.GetValue(view)!;
  void Key(RoutedEvent routed){
   var send=button.Dispatcher.InvokeAsync(()=>button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(button),Environment.TickCount,System.Windows.Input.Key.Space){RoutedEvent=routed}));
   while(!send.Task.IsCompleted)Pump();send.Task.GetAwaiter().GetResult();
  }
  try{
   Key(Keyboard.PreviewKeyDownEvent);
   Check(Overlay(Preview).Source?.Width==410&&ReferenceEquals(Overlay(Preview).Source,Overlay(large).Source)&&
    large.Width==454&&large.EditLayer.Visibility==Visibility.Collapsed,"Holding Compare in 1:1 shows the applied size in both previews and hides edit handles");
   Key(Keyboard.PreviewKeyUpEvent);
   Check(Overlay(Preview).Source==null&&large.Width==Draft.Width+44&&Preview.Surface.Visibility==Visibility.Visible,
    "Releasing Space immediately restores the draft at its own size");
   Check(File.ReadAllText(store.ActivePath)==active&&JsonSerializer.Serialize(Draft)==draft&&(int)Field("editCursor")! ==cursor,
    "Comparison leaves the saved appearance, working draft and undo history intact");
   var title=(TextBlock)Preview.Surface.Elements["title"];
   string originalTitle=title.Text;
   void MouseEvent(RoutedEvent routed){button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=routed});}
   MouseEvent(UIElement.PreviewMouseLeftButtonDownEvent);
   Check(button.IsMouseCaptured&&Overlay(Preview).Source is BitmapSource,"Mouse hold captures the pointer and shows the applied preset");
   static string Pixels(BitmapSource source){int stride=(source.PixelWidth*source.Format.BitsPerPixel+7)/8;var pixels=new byte[stride*source.PixelHeight];source.CopyPixels(pixels,stride,0);return Convert.ToHexString(SHA256.HashData(pixels));}
   var before=Pixels((BitmapSource)Overlay(Preview).Source);
   MouseEvent(UIElement.PreviewMouseLeftButtonUpEvent);
   title.Text="Другой трек в том же плеере";
   MouseEvent(UIElement.PreviewMouseLeftButtonDownEvent);
   Check(before!=Pixels((BitmapSource)Overlay(Preview).Source),"Applied comparison uses the exact track currently displayed in the editor");
   button.ReleaseMouseCapture();
   Check(Field("comparisonOwner")==null&&Overlay(large).Source==null,"Losing mouse capture returns both previews to the draft");
   title.Text=originalTitle;
   if(classic){
    store.ClearActive();
    string data=Path.Combine(root,"Data","mini-player");Directory.CreateDirectory(data);
    string file=Path.Combine(data,"appearance.json");
    File.WriteAllText(file,"{\"Theme\":\"Кофе\",\"Accent\":\"#FFA042\",\"Scale\":1.3,\"BackgroundColor\":\"#003050\"}");
    Key(Keyboard.PreviewKeyDownEvent);
    var timeout=System.Diagnostics.Stopwatch.StartNew();while(Overlay(large).Source==null&&timeout.Elapsed.TotalSeconds<7)Pump();
    Check(Overlay(large).Source is BitmapSource {PixelWidth:367} image&&image.PixelHeight==273,"Classic comparison renders the real widget with its saved scale ("+Overlay(large).Source?.Width+"×"+Overlay(large).Source?.Height+", "+button.Content+", "+button.ToolTip+")");
    Check(!File.Exists(Path.Combine(data,"settings.json"))&&!File.Exists(Path.Combine(data,"preset-applied.json")),"Offscreen comparison performs no settings write or media registration");
    Key(Keyboard.PreviewKeyUpEvent);
    File.Delete(file);
    Directory.Delete(data);
   }
   Key(Keyboard.PreviewKeyDownEvent);popup.Close();Layout();
   Check(Field("comparisonOwner")==null&&Overlay(Preview).Source==null,"Closing the window during a hold restores the inline editor");
  }finally{store.ClearActive();if(popup.IsVisible)popup.Close();}
 }

 static void CheckGroupEditing(){
  object? Invoke(string method,params object?[] args)=>typeof(LauncherWindow).GetMethod(method,Instance)!.Invoke(window,args);
  var preset=new WidgetPreset{Width=410,Height=220};preset.Normalize();
  string[] ids={"previous","play","next"};
  double[] x={80,122,200},y={10,24,80},width={30,20,26};
  for(int i=0;i<ids.Length;i++)preset.Elements[ids[i]]=new PresetElement{X=x[i],Y=y[i],Width=width[i],Height=18,Visible=true};
  preset.LockElement("cover",true);preset.Elements["artist"].Visible=false;
  Navigate("customize");Invoke("OpenPreset",preset);Layout();Click("Детали");
  Invoke("SelectPresetElement","previous");Invoke("ToggleGroupMember","play");Invoke("ToggleGroupMember","next");
  Invoke("ToggleGroupMember","cover");Invoke("ToggleGroupMember","artist");Layout();
  var group=(HashSet<string>)Field("selectedGroup")!;
  Check(group.SetEquals(ids),"Ctrl selection groups visible unlocked elements only");
  Click("Превью 1:1");var popup=(Window)Field("largePresetWindow")!;popup.Opacity=0;
  var enlarged=(PresetPreview)Field("largePresetPreview")!;
  bool ShowsGroup(PresetPreview preview)=>Walk(preview.EditLayer).OfType<Border>().Count(b=>b.Tag is string id&&group.Contains(id)&&b.Visibility==Visibility.Visible)==3&&
   preview.EditLayer.Children.OfType<Border>().Where(b=>b.Tag is int).All(b=>b.Visibility==Visibility.Collapsed);
  Check(ShowsGroup(Preview)&&ShowsGroup(enlarged),"Both previews outline every selected element and hide individual resize handles");
  var initial=(Dictionary<string,Rect>)Invoke("SelectionBounds")!;
  var hull=Rect.Empty;foreach(var box in initial.Values)hull.Union(box);
  Invoke("BeginPresetGesture",new object?[]{null});
  Invoke("MovePresetGroup",initial,new Vector(4,5));
  var snap=PresetAlignment.MoveSelection(Draft,initial.Keys,hull.Size,new Point(9000,9000),1,false);
  var shift=snap.Position-hull.TopLeft;
  Invoke("MovePresetGroup",initial,shift);Invoke("EndPresetGesture",new object?[]{null});Layout();
  Check(initial.All(pair=>Draft.Elements[pair.Key].X==pair.Value.X+shift.X&&Draft.Elements[pair.Key].Y==pair.Value.Y+shift.Y&&
   Canvas.GetLeft(enlarged.Surface.Elements[pair.Key])==Canvas.GetLeft(Preview.Surface.Elements[pair.Key]))&&
   Draft.Elements["next"].X+width[2]==Draft.Width&&Draft.Elements["next"].Y+18==Draft.Height,"Dragging the group clamps the whole bounding box and preserves offsets in both previews");
  Click("Отменить");Check(ids.All(id=>Draft.Elements[id].X==initial[id].X&&Draft.Elements[id].Y==initial[id].Y),"One undo restores every group member after a drag");
  Click("Повторить");Check(Draft.Elements["previous"].X==x[0]+shift.X,"Redo restores the group drag");
  Click("Отменить");Click("По горизонтали");
  double gap=Draft.Elements["play"].X-(x[0]+width[0]);
  Check(gap==x[2]-(Draft.Elements["play"].X+width[1])&&Draft.Elements["previous"].X==x[0]&&Draft.Elements["next"].X==x[2]&&Draft.Elements["play"].Y==y[1],"Horizontal spacing preserves the outer edges and handles unequal widths");
  Click("По вертикали");Check(Draft.Elements["play"].Y==45&&Draft.Elements["play"].X==x[0]+width[0]+gap,"Vertical spacing leaves the horizontal axis untouched");
  Click("Отменить");Check(Draft.Elements["play"].Y==y[1]&&Draft.Elements["play"].X!=x[1],"Distribution takes a single undo step");
  Invoke("ToggleGroupMember","next");Layout();Check(!Button("По горизонтали").IsEnabled,"Spacing needs at least three elements");
  Invoke("ToggleGroupMember","previous");Layout();Check(group.Count==0&&Named<ComboBox>("Элемент").SelectedItem as string=="Пауза / воспроизведение","Removing all but one member restores single-element editing");
  popup.Close();
 }
 static void CheckAlignment(){
  var preset=new WidgetPreset{Width=400,Height=220};
  preset.Normalize();
  foreach(var element in preset.Elements.Values)element.Visible=false;
  preset.Elements["play"]=new PresetElement{Width=20,Height=20};
  PresetAlignment.Result Move(double x,double y,double scale=1,bool free=false)=>PresetAlignment.Move(preset,"play",new Point(x,y),scale,free);
  var center=Move(188,97);
  Check(center.Position==new Point(190,100)&&center.VerticalGuide==200&&center.HorizontalGuide==110,"Drag snaps to both center lines of the widget");
  var edges=Move(2,197);
  Check(edges.Position==new Point(0,200)&&edges.VerticalGuide==0&&edges.HorizontalGuide==220,"Canvas edges align with the matching outer control edges");
  Check(Move(186,50,.5).Position.X==190&&Move(186,50,2).VerticalGuide==null,"Snap attraction uses screen distance at different preview zoom levels");
  var free=Move(188,97,1,true);
  Check(free.Position==new Point(188,97)&&free.VerticalGuide==null&&free.HorizontalGuide==null,"Alt leaves the pointer position unchanged and hides both guides");
  Check(Move(-8,230,1,true).Position==new Point(0,200),"Free movement still respects widget bounds");
  preset.Elements["previous"]=new PresetElement{X=80,Y=60,Width=20,Height=20,Locked=true};
  preset.Elements["next"]=new PresetElement{X=200,Y=60,Width=20,Height=20};
  var row=Move(138,63);
  Check(row.Position==new Point(140,60)&&row.VerticalGuide==150,"Pause can be centered between the previous and next buttons off the canvas center");
  var otherEdge=Move(103,12);
  Check(otherEdge.Position.X==100&&otherEdge.VerticalGuide==100,"A locked neighboring element remains an alignment reference");
  preset.Elements["previous"].Visible=false;
  Check(Move(103,12).VerticalGuide==null&&Move(138,63).VerticalGuide==null,"Hidden elements do not supply guides or spacing references");
 }
 static void CheckWidgetWorkshop(string root){
  var originalWindow=window;
  void FreshWindow(){
   window=new LauncherWindow(root,true);
   window.ShowActivated=false;window.ShowInTaskbar=false;window.Opacity=0;
  }
  void Open(){window.OpenMiniPlayerWorkshop();Layout();}
  var store=new PresetStore(root);
  var first=PresetThemes.Create("Лес");first.Name="Мой пресет";store.Save(first);
  store.SaveDraft(first);
  var active=first.Copy();active.Width=404;store.Apply(active);
  var second=PresetThemes.Create("Ретро");second.Name=first.Name;store.Save(second);
  FreshWindow();
  try{
   Open();
   Check(Draft.Id==first.Id&&Draft.Width==404,"Widget opens the exact active snapshot even when its library entry differs");
   Named<TextBox>("Название пресета").Text="Работаю над этим";Layout();
   var originalDraft=Draft;int undoSteps=(int)Field("editCursor")!;
   window.Hide();window.WindowState=WindowState.Minimized;Open();
   Check(window.IsVisible&&window.WindowState==WindowState.Normal&&ReferenceEquals(Draft,originalDraft)&&
    (int)Field("editCursor")! ==undoSteps&&Button("Отменить").IsEnabled,"Reopening the same target restores the window and keeps unsaved edits and undo history");
   store.Apply(second);Open();Click("Остаться");
   Check(Draft.Name=="Работаю над этим"&&!Nodes<Button>().Any(x=>x.Content as string=="Сохранить как копию и перейти"),
    "Staying in an unrelated draft preserves its changes");
   Open();Click("Сохранить как копию и перейти");
   Check(Draft.Id==second.Id&&store.List().Any(p=>p.Id!=first.Id&&p.Name.StartsWith("Работаю над этим"))&&
    store.List().Single(p=>p.Id==first.Id).Name=="Мой пресет","Switching saves a separate copy of the draft and opens the requested preset by ID");
   store.ClearActive();
   string appearance=Path.Combine(root,"Data","mini-player","appearance.json");Directory.CreateDirectory(Path.GetDirectoryName(appearance)!);
   File.WriteAllText(appearance,"{\"Theme\":\"Кофе\"}");Open();
   Check(Draft.Theme=="Кофе"&&Draft.Name=="Кофе"&&Nodes<TextBlock>().Any(x=>x.Text.Contains("Открыта основа темы")),"Classic appearance opens its corresponding workshop template");
   string classicId=Draft.Id;Named<TextBox>("Название пресета").Text="Из кофейной темы";Open();
   Check(Draft.Id==classicId&&Draft.Name=="Из кофейной темы","Reopening the same classic theme keeps local edits");
   window.RequestShutdown();
   store.Apply(second);var old=first.Copy();old.Name="Черновик из прошлой сессии";store.SaveDraft(old);
   FreshWindow();Open();
   Check(Draft.Id==old.Id&&Draft.Name==old.Name&&Button("Сохранить как копию и перейти").IsEnabled,
    "Cold activation protects an unfinished draft from the previous launcher session");
   Click("Сохранить как копию и перейти");
   Check(Draft.Id==second.Id&&store.LoadDraft()?.Id==second.Id&&store.List().Any(p=>p.Name.StartsWith(old.Name)),
    "Restarted launcher preserves the previous draft before switching");
   window.RequestShutdown();
   old.Name="Редактор из обычного меню";store.SaveDraft(old);
   FreshWindow();window.Show();Home();Click("Создать свой пресет");Click("Выбрать виджет: Мини-плеер");
   Open();Check(Draft.Name==old.Name&&Button("Сохранить как копию и перейти").IsEnabled,
    "Widget activation protects yesterday's draft reopened through the normal editor before the next edit");
  }finally{window.RequestShutdown();window=originalWindow;}
 }
 [STAThread]static int Main(string[] args){Application? app=null;try{
  CheckAlignment();
  string root=Path.GetFullPath(args[0]);snapshots=Path.GetFullPath(args[1]);Directory.CreateDirectory(snapshots);
  app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  window=new LauncherWindow(root,true,"customize");window.ShowActivated=false;window.ShowInTaskbar=false;window.Opacity=0;window.Show();Layout();
  Snapshot("customization-home-night");
  Check(!Nodes<TextBlock>().Any(x=>x.Text=="Режим интерфейса")&&!Nodes<Button>().Any(x=>x.Content as string is "Ночной" or "Дневной"),"Customization no longer contains the interface mode selector");
  Check(!Nodes<TextBlock>().Any(x=>x.Text is "01" or "02" or "03"),"Entry cards contain no numbered labels");
  var navigation=(Dictionary<string,Button>)Field("nav")!;
  Check(navigation.Keys.ToArray().SequenceEqual(new[]{"catalog","installed","customize","obs","settings"}),"OBS navigation sits directly after customization");
  Navigate("obs");Check(Nodes<TextBlock>().Any(x=>x.Text=="скоро будет, братва"),"OBS page shows the requested placeholder");Snapshot("obs");
  Navigate("customize");
  foreach(string mode in new[]{"Готовые пресеты","Создать свой пресет","Рандомизировать","Созданные пресеты"}){
   Check(Button(mode).IsVisible,"Entry card is present: "+mode);Click(mode);
   Check(Nodes<TextBlock>().Any(x=>x.Text=="Какой виджет хотите?")&&!Nodes<Slider>().Any(),"Each mode asks which widget to customize: "+mode);
   if(mode=="Готовые пресеты")Snapshot("widget-picker");Click("← Назад");
  }
  Click("Готовые пресеты");Click("Выбрать виджет: Мини-плеер");
  Check(PresetThemes.Names.Length==14&&PresetThemes.Names.All(x=>Button("Пресет: "+x).IsEnabled),"All fourteen mini-player themes are available in the gallery");
  Check(Nodes<Image>().Count(x=>x.Source!=null)==14,"Every stock theme has a rendered thumbnail");Snapshot("preset-gallery-top");
  Nodes<ScrollViewer>().First().ScrollToEnd();Layout();Snapshot("preset-gallery-bottom");Nodes<ScrollViewer>().First().ScrollToTop();
  Click("Пресет: Лаванда");Check(Draft.Theme=="Лаванда","Selecting a stock theme loads its configuration");Check(Named<TextBox>("Название пресета").ActualHeight>=38,"Preset name has a full-height input field");Snapshot("preset-editor-shape-night");
  CheckLiveTrackPreview(root,args.Contains("--check-live-endpoint"));CheckPresetComparison(root,args.Contains("--check-classic"));CheckWorkshopHistory();
  Check(Button("Применить").IsEnabled,"The editor exposes the apply-to-player action");
  Check(Preview.ActualWidth>=300&&Preview.ActualHeight>=280,"Workshop preview has a larger demonstration area");
  Click("Превью 1:1");var enlarged=(Window)Field("largePresetWindow")!;enlarged.Opacity=0;enlarged.UpdateLayout();var realSize=(PresetPreview)Field("largePresetPreview")!;
  var transform=realSize.Surface.TransformToAncestor(realSize);var origin=transform.Transform(new Point());var unit=transform.Transform(new Point(1,1));
  Check(Math.Abs(unit.X-origin.X-1)<.001&&Math.Abs(unit.Y-origin.Y-1)<.001,"Expanded preview renders one DIP per player pixel");
  Check(PixelHash(realSize.Surface)==PixelHash(Preview.Surface),"Expanded and embedded previews use identical player content");enlarged.Close();
  Check(Math.Abs(RenderScale(Preview)-1)<.001,"The workshop initially shows the actual widget size at 100 percent");
  var zoom=Named<Slider>("Масштаб превью");zoom.Value=75;Layout();Check(Math.Abs(RenderScale(Preview)-.75)<.001,"Preview zoom explicitly changes the rendered scale");
  var coverBox=Draft.Elements["cover"];
  var coverCenter=new Point(coverBox.X+coverBox.Width/2,coverBox.Y+coverBox.Height/2);
  var coverAtZoom=Preview.Surface.TranslatePoint(coverCenter,Preview);
  string? picked=Preview.HitElement(Preview.TranslatePoint(coverAtZoom,Preview.EditLayer));
  Check(picked=="cover","Embedded selection hit-tests at 75% zoom in the same coordinates as the rendered cover");
  var beforeSelection=Preview;
  typeof(LauncherWindow).GetMethod("SelectPresetElement",Instance)!.Invoke(window,new object[]{picked!});Layout();
  Check(Field("presetTab") as string=="Детали"&&Equals(Named<ComboBox>("Элемент").SelectedItem,"Обложка")&&ReferenceEquals(beforeSelection,Preview),"Direct selection opens the chosen element's details without replacing the editor viewport");
  Click("Форма");
  string original=PixelHash(Preview);Named<Slider>("Ширина").Value=420;Named<Slider>("Непрозрачность").Value=.61;Layout();
  Check(Math.Abs(RenderScale(Preview)-.75)<.001,"Changing widget dimensions does not silently change preview zoom");
  Named<Slider>("Масштаб превью").Value=100;Layout();
  Check(Draft.Width==420&&Math.Abs(Draft.Opacity-.61)<.001&&PixelHash(Preview)!=original,"Shape and opacity controls change the live preview");
  Named<ComboBox>("Форма").SelectedIndex=3;Layout();Check(Draft.Shape=="ellipse","Shape selector changes the draft");
  var shape=Named<ComboBox>("Форма");Check(new[]{"Звезда","Квадрат","Круг","Ромб","Шестиугольник","Сердце"}.All(x=>shape.Items.Contains(x)),"Workshop offers the new geometric shapes");shape.SelectedItem="Звезда";Layout();Check(Draft.Shape=="star"&&Preview.Surface.ShapeGeometry.Bounds.Width>0,"Star has actual renderable geometry");
  Named<ComboBox>("Форма").SelectedItem="Круг";Layout();Check(Draft.Width==Draft.Height&&Math.Abs(Preview.Surface.ShapeGeometry.Bounds.Width-Preview.Surface.ShapeGeometry.Bounds.Height)<.001,"Selecting circle immediately produces equal rendered dimensions");Snapshot("preset-circle-before-resize");
  Named<ComboBox>("Форма").SelectedItem="Овал";Layout();Check(Draft.Width!=Draft.Height&&Math.Abs(Preview.Surface.ShapeGeometry.Bounds.Width-Preview.Surface.ShapeGeometry.Bounds.Height)>1,"Selecting oval after a circle immediately restores a distinct non-square shape");Snapshot("preset-oval-before-resize");
  Named<Slider>("Ширина").Value=420;Named<Slider>("Высота").Value=200;Layout();
  Click("Цвета");Named<TextBox>("Фон HEX").Text="#102030";Layout();string colored=PixelHash(Preview);
  Check(new[]{"Фон","Акцент","Текст","Рамка"}.All(x=>Button("Выбрать цвет: "+x).Content==null),"All color chips are visual swatches without labels inside");
  Click("Выбрать цвет: Фон");var colorDialog=(Window)Field("presetColorPicker")!;colorDialog.Opacity=0;Check(colorDialog is PresetColorDialog&&colorDialog.WindowStyle==WindowStyle.None,"Color swatch opens the themed picker instead of the native dialog");colorDialog.Close();
  Named<TextBox>("Фон HEX").Text="broken";Layout();Check(Draft.BackgroundColor=="#102030"&&PixelHash(Preview)==colored,"An incomplete color entry preserves the valid preview");
  Named<TextBox>("Фон HEX").Text="#D7AF93";Layout();Check(Draft.BackgroundColor=="#D7AF93"&&PixelHash(Preview)!=colored,"A valid color entry updates the live preview");
  Click("Текст");Named<TextBox>("Размер текста (px)").Text="32";var font=Named<ComboBox>("Шрифт");
  Check(font.IsEditable&&font.Template.FindName("PART_EditableTextBox",font) is TextBox&&!Nodes<TextBox>().Any(x=>AutomationProperties.GetName(x)=="Поиск шрифта"),"Font selection and typing share one editable dropdown without a separate search field");
  TypeFont("conso");Check(font.Items.Count>0&&font.Items.Cast<string>().All(x=>x.Contains("conso",StringComparison.OrdinalIgnoreCase))&&font.Items.Contains("Consolas"),"Typing in the dropdown filters installed fonts by a partial name");
  TypeFont("Consolas",true);Check(Draft.FontFamily=="Consolas","A fully typed installed font name can be applied with Enter");
  ChooseFont("Arial");Check(Draft.FontFamily=="Arial","Reopening after an exact font search can select another font without clearing the input");TypeFont("Consolas",true);
  TypeFont("font-that-does-not-exist-qa");Check(font.Items.Count==0&&Draft.FontFamily=="Consolas","Unknown font input shows no matches and preserves the valid font");
  TypeFont("");Check(font.Items.Count>1,"Clearing the editable dropdown restores the installed font list");ChooseFont("Arial");Check(Draft.FontFamily=="Arial"&&((TextBox)font.Template.FindName("PART_EditableTextBox",font)).Text=="Arial","Choosing a font from the expanded dropdown updates the same input row");TypeFont("Consolas",true);
  var beforeFont=JsonSerializer.Serialize(Draft.Elements);var pauseBefore=Preview.Surface.Elements["play"].RenderSize;
  Named<TextBox>("Размер текста (px)").Text="60";Layout();Check(Draft.FontSize==60&&JsonSerializer.Serialize(Draft.Elements)==beforeFont&&Preview.Surface.Elements["play"].RenderSize==pauseBefore,"Changing text to 60 px leaves every element position and size unchanged");
  Named<TextBox>("Размер текста (px)").Text="120";Layout();Check(Draft.FontSize==60&&Named<TextBox>("Размер текста (px)").Text=="60","Text-size entry clamps values above 60 px");
  Check(Named<TextBlock>("Уведомление о размере шрифта").IsVisible&&Named<TextBlock>("Уведомление о размере шрифта").Text.Contains("60"),"Exceeding the font-size limit shows a visible 60 px notice");
  Snapshot("preset-font-limit-night");
  Check(Named<Slider>("Размер текста").Value==60,"The font slider follows the validated typed size");Named<Slider>("Размер текста").Value=22;Layout();Check(Draft.FontSize==22&&Named<TextBox>("Размер текста (px)").Text=="22","Dragging the font-size slider updates the text field and preview");Named<TextBox>("Размер текста (px)").Text="60";
  Named<TextBox>("Размер текста (px)").Text="not a number";Check(Draft.FontSize==60,"Invalid text-size input preserves the last valid size");Named<TextBox>("Размер текста (px)").Text="32";
  Check(Draft.FontSize==32&&Draft.FontFamily=="Consolas"&&Preview.ActualWidth>0,"Text controls support large fonts without invalidating the preview");Snapshot("preset-editor-text-night");
  Click("Детали");var element=Named<ComboBox>("Элемент");Check(element.Items.Count==WidgetPreset.ElementIds.Length,"Every existing player element has independent layout controls");element.SelectedItem="Пауза / воспроизведение";Layout();Named<TextBox>("Положение X").Text="120";Named<TextBox>("Положение Y").Text="80";Named<TextBox>("Ширина элемента").Text="48";Named<TextBox>("Высота элемента").Text="40";Layout();
  var pause=Draft.Elements["play"];var rendered=Preview.Surface.Elements["play"];Check(pause.X==120&&pause.Y==80&&pause.Width==48&&pause.Height==40&&Canvas.GetLeft(rendered)==120&&Canvas.GetTop(rendered)==80&&rendered.Width==48&&rendered.Height==40,"Layout fields reposition and resize the actual preview control independently");
  Named<TextBox>("Положение X").Text=(Draft.Width-50).ToString();
  Named<TextBox>("Ширина элемента").Text="145";
  Check(pause.X==Draft.Width-50&&pause.Width==50&&Named<TextBox>("Ширина элемента").Text=="50"&&Canvas.GetLeft(Preview.Surface.Elements["play"])==pause.X,"Width immediately respects the edited X and the field agrees with the rendered position");
  Named<TextBox>("Положение X").Text="120";Named<TextBox>("Ширина элемента").Text="48";
  Click("Превью 1:1");
  var second=(PresetPreview)Field("largePresetPreview")!;
  var inlineControls=Preview.Surface.Elements.ToDictionary(x=>x.Key,x=>x.Value);
  var popupControls=second.Surface.Elements.ToDictionary(x=>x.Key,x=>x.Value);
  var initialBox=new Rect(pause.X,pause.Y,pause.Width,pause.Height);
  var setBounds=typeof(LauncherWindow).GetMethod("SetPresetBounds",Instance)!;
  for(int corner=0;corner<4;corner++){
   var bounds=LauncherWindow.ResizeFromCorner(initialBox,corner,new Vector(7,5),new Size(Draft.Width,Draft.Height));
   setBounds.Invoke(window,new object[]{"play",bounds});
   Check(second.Surface.Elements["play"].Width==bounds.Width && Preview.Surface.Elements["play"].Height==bounds.Height && Draft.Elements["play"].X==bounds.X,"Corner "+corner+" resizes both previews and the draft");
  }
  setBounds.Invoke(window,new object[]{"play",initialBox});
  bool HasGrips(PresetPreview view)=>view.EditLayer.Children.OfType<Border>().Count(x=>x.Tag is int && x.Visibility==Visibility.Visible)==4;
  Check(HasGrips(Preview)&&HasGrips(second),"Both previews show four resize handles for the selected element");
  foreach(var view in new[]{Preview,second}){
   var guide=(Border)typeof(PresetPreview).GetField("verticalGuide",Instance)!.GetValue(view)!;
   view.SetAlignmentGuides(90,null);
   Check(guide.Visibility==Visibility.Visible&&!guide.IsHitTestVisible&&Math.Abs(Canvas.GetLeft(guide)+guide.Width/2-90)<.001&&guide.Height==Draft.Height,"Preview guide shares the widget coordinates without intercepting input");
   view.SetAlignmentGuides(null,null);
   Check(guide.Visibility==Visibility.Collapsed,"Ending alignment removes its temporary guide from the preview");
  }
  var move=typeof(LauncherWindow).GetMethod("MovePresetElement",Instance)!;
  for(int step=0;step<90;step++)move.Invoke(window,new object[]{"play",31d+step,80d});
  Call("SavePresetDraft");
  Check(inlineControls.All(x=>ReferenceEquals(x.Value,Preview.Surface.Elements[x.Key]))&&popupControls.All(x=>ReferenceEquals(x.Value,second.Surface.Elements[x.Key]))&&Canvas.GetLeft(inlineControls["play"])==120&&Canvas.GetLeft(popupControls["play"])==120&&new PresetStore(root).LoadDraft()!.Elements["play"].X==120,"Repeated drag steps move both previews and persist without replacing any rendered controls");
  ((Window)Field("largePresetWindow")!).Close();
  Named<ComboBox>("Элемент").SelectedItem="Исполнитель";Layout();var artist=Named<CheckBox>("Показывать элемент");artist.IsChecked=false;artist.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Layout();Check(!Draft.ShowArtist&&!Draft.Elements["artist"].Visible,"Detail visibility controls modify the selected element");Snapshot("preset-editor-details-night");
  Named<ComboBox>("Элемент").SelectedItem="Пауза / воспроизведение";Layout();Toggle(Named<CheckBox>("Закрепить элемент"),true);
  Check(Draft.Elements["play"].Locked&&new[]{"Положение X","Положение Y","Ширина элемента","Высота элемента"}.All(x=>!Named<TextBox>(x).IsEnabled)&&!Named<CheckBox>("Показывать элемент").IsEnabled,"Locking an element disables all of its placement, sizing and visibility controls");
  string lockedPause=JsonSerializer.Serialize(Draft.Elements["play"]),lockedPausePixels=PixelHash(Preview.Surface.Elements["play"]);
  Check(Preview.EditLayer.Children.OfType<Border>().Where(x=>x.Tag is int).All(x=>x.Visibility==Visibility.Collapsed),"A locked item has no draggable resize grips");
  typeof(LauncherWindow).GetMethod("SetPresetBounds",Instance)!.Invoke(window,new object[]{"play",new Rect(0,0,9,9)});
  Check(JsonSerializer.Serialize(Draft.Elements["play"])==lockedPause,"Direct geometry edits preserve a locked item's position and size");
  Named<ComboBox>("Элемент").SelectedItem="Название трека";Layout();Toggle(Named<CheckBox>("Закрепить элемент"),true);string lockedTitle=JsonSerializer.Serialize(Draft.Elements["title"]),lockedTitlePixels=PixelHash(Preview.Surface.Elements["title"]);
  Click("Текст");Named<Slider>("Размер текста").Value=40;TypeFont("Arial",true);Layout();
  Check(JsonSerializer.Serialize(Draft.Elements["play"])==lockedPause&&JsonSerializer.Serialize(Draft.Elements["title"])==lockedTitle&&PixelHash(Preview.Surface.Elements["title"])==lockedTitlePixels,"Global font changes preserve locked element geometry and typography");
  Click("Цвета");Named<TextBox>("Акцент HEX").Text="#D011C5";Named<TextBox>("Текст HEX").Text="#16CD38";Layout();
  Check(PixelHash(Preview.Surface.Elements["play"])==lockedPausePixels&&PixelHash(Preview.Surface.Elements["title"])==lockedTitlePixels,"Global color changes preserve the locked button and title appearance");
  string beforeLockedRandom=JsonSerializer.Serialize(Draft);Click("Рандомизировать ещё");
  Check(JsonSerializer.Serialize(Draft.Elements["play"])==lockedPause&&JsonSerializer.Serialize(Draft.Elements["title"])==lockedTitle,"Randomization preserves locked elements exactly");Click("Вернуть предыдущий");Check(JsonSerializer.Serialize(Draft)==beforeLockedRandom,"Undo restores the whole preset including its locks");
  Click("Текст");Named<TextBox>("Размер текста (px)").Text="32";TypeFont("Consolas",true);
  Click("Детали");Named<ComboBox>("Элемент").SelectedItem="Пауза / воспроизведение";Layout();Toggle(Named<CheckBox>("Закрепить элемент"),false);Check(!Draft.Elements["play"].Locked&&Named<TextBox>("Положение X").IsEnabled,"Unlocking restores manual editing");Toggle(Named<CheckBox>("Закрепить элемент"),true);Snapshot("preset-editor-locked-night");
  Click("Фон");Check(Button("Загрузить изображение / GIF").IsEnabled,"The editor exposes image and GIF import");
  byte[] pixels={40,150,220,255,190,40,90,255,80,180,60,255,200,110,20,255};var media=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,pixels,8);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(media));string imagePath=Path.Combine(root,"own-preview-fixture.png");using(var output=File.Create(imagePath))png.Save(output);
  Draft.MediaPath=new PresetStore(root).ImportMedia(imagePath);Call("RefreshPresetPreview");Layout();string background=PixelHash(Preview);
  Named<Slider>("Непрозрачность фона").Value=.12;Named<ComboBox>("Заполнение").SelectedIndex=2;Layout();Check(Draft.MediaFit=="stretch"&&Math.Abs(Draft.MediaOpacity-.12)<.001&&PixelHash(Preview)!=background,"Background controls affect imported media in the live preview");Snapshot("preset-editor-media-night");
  Named<TextBox>("Название пресета").Text="QA — мой пресет";Click("Сохранить пресет");var saved=new PresetStore(root).List().Single(x=>x.Name=="QA — мой пресет");
  Check(saved.Width==420&&saved.FontSize==32&&!saved.ShowArtist&&saved.MediaPath==Draft.MediaPath,"Saving preserves geometry, typography, details and imported media");
  string savedJson=JsonSerializer.Serialize(saved);Home();Click("Созданные пресеты");Click("Выбрать виджет: Мини-плеер");Pump();
  var image=Walk(Button("Мой пресет: QA — мой пресет")).OfType<Image>().Single();
  var poster=image.Source as BitmapSource;
  Check(poster!=null&&poster.PixelWidth<=321&&poster.PixelHeight<=145,"Saved preset card loads a bounded thumbnail from the shared renderer");
  var pixelsOfPoster=new byte[poster!.PixelWidth*poster.PixelHeight*4];poster.CopyPixels(pixelsOfPoster,poster.PixelWidth*4,0);
  Check(pixelsOfPoster.Length>100&&pixelsOfPoster.Distinct().Count()>12,"Thumbnail contains rendered artwork and controls");
  Click("В избранное · QA — мой пресет");Check(new PresetStore(root).ListSaved().Single().Favorite,"Gallery star persists across readers");
  Check(Button("Убрать из избранного · QA — мой пресет").ToolTip as string=="Убрать из избранного","Gallery shows active favorite state after toggling");
  Pump();Snapshot("saved-preset-card");
  var galleryStore=new PresetStore(root);
  var extras=Enumerable.Range(0,8).Select(i=>{var item=PresetThemes.Create(PresetThemes.Names[i]);item.Name="Карточка "+i;return galleryStore.Save(item);}).ToArray();
  Home();Click("Созданные пресеты");Click("Выбрать виджет: Мини-плеер");
  int CardCount()=>Nodes<Button>().Count(b=>AutomationProperties.GetName(b).StartsWith("Мой пресет: ",StringComparison.Ordinal));
  Check(CardCount()==8&&!Button("Предыдущая страница").IsEnabled,"Large libraries render only the current page of thumbnails");
  Click("Следующая страница");
  Check(CardCount()==1&&!Button("Следующая страница").IsEnabled,"Last gallery page contains the remaining saved presets");
  Click("В избранное · Карточка 7");
  Check(CardCount()==8&&Button("Убрать из избранного · Карточка 7").IsEnabled,"Starring a preset on another page moves it to the favorites on the first page");
  foreach(var item in extras)galleryStore.Delete(item.Id);
  Click("Мой пресет: QA — мой пресет");
  Check(JsonSerializer.Serialize(Draft)==savedJson,"A saved preset reopens with all settings intact");
  Home();Click("Создать свой пресет");Click("Выбрать виджет: Мини-плеер");Click("Текст");Named<TextBox>("Размер текста (px)").Text="24";
  // The selected editor tab is retained; switch explicitly for additional geometry edits.
  Click("Форма");Named<Slider>("Ширина").Value=398;Named<TextBox>("Название пресета").Text="QA — новый вариант";Click("Сохранить пресет");
  var variants=new PresetStore(root).List();Check(variants.Count==2&&JsonSerializer.Serialize(variants.Single(x=>x.Name=="QA — мой пресет"))==savedJson,"Creating a new preset preserves the original saved variant");
  Navigate("obs");Navigate("customize");Check(Draft.Width==398,"Sidebar navigation preserves an unfinished draft");
  Home();Click("Рандомизировать");Click("Выбрать виджет: Мини-плеер");string beforeRandom=JsonSerializer.Serialize(Draft);Click("Рандомизировать ещё");Check(JsonSerializer.Serialize(Draft)!=beforeRandom,"Randomize generates a new complete configuration");Click("Вернуть предыдущий");Check(JsonSerializer.Serialize(Draft)==beforeRandom,"Undo restores the exact previous random configuration");Snapshot("preset-editor-random-night");
  ((Button)Field("themeToggle")!).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Layout();Check(JsonSerializer.Serialize(Draft)==beforeRandom,"Header day/night switching preserves the editor draft");Snapshot("preset-editor-day");Click("Текст");TypeFont("Consolas",true);Named<TextBox>("Размер текста (px)").Text="61";Layout();Check(Draft.FontSize==60&&Named<TextBlock>("Уведомление о размере шрифта").Text.Contains("60"),"The font control and limit notice work in the day theme");Snapshot("preset-font-limit-day");Home();Snapshot("customization-home-day");
  window.RequestShutdown();window=new LauncherWindow(root,true,"customize");window.ShowActivated=false;window.ShowInTaskbar=false;window.Opacity=0;window.Show();Layout();
  var priorDraft=new PresetStore(root).LoadDraft()!;
  var pinnedBeforeRestart=priorDraft.Elements.First(x=>x.Value.Locked);
  Click("Рандомизировать");Click("Выбрать виджет: Мини-плеер");
  Check(JsonSerializer.Serialize(Draft.Elements[pinnedBeforeRestart.Key])==JsonSerializer.Serialize(pinnedBeforeRestart.Value),"Randomizing first after restart preserves the saved draft's locked geometry and style");
  Click("Вернуть предыдущий");Check(JsonSerializer.Serialize(Draft)==JsonSerializer.Serialize(priorDraft),"The saved draft can be restored after the first randomization following a restart");Home();
  Click("Созданные пресеты");Click("Выбрать виджет: Мини-плеер");Pump();
  var dayStar=Button("Убрать из избранного · QA — мой пресет");
  Check(((SolidColorBrush)dayStar.Foreground).Color==((SolidColorBrush)Field("AccentBrush")!).Color,
   "Favorite uses the active gallery palette in day mode after restarting");
  Snapshot("saved-presets-day");Click("Мой пресет: QA — мой пресет");Check(JsonSerializer.Serialize(Draft)==savedJson,"Saved presets survive a fresh launcher window");
  window.Width=900;window.Height=650;Layout();var scroll=Nodes<ScrollViewer>().First();scroll.ScrollToEnd();Layout();Snapshot("preset-editor-compact");
  var saveButton=Button("Сохранить пресет");var surface=(FrameworkElement)window.Content;var saveBounds=new Rect(saveButton.TranslatePoint(new Point(),surface),saveButton.RenderSize);
  Check(scroll.ScrollableHeight>0&&saveBounds.Top>=48&&saveBounds.Bottom<=surface.ActualHeight,"Compact launcher keeps save reachable by scrolling");
  Check(!Directory.Exists(Path.Combine(root,"Data","mini-player")),"Launcher-only customization does not modify the player's settings");
  Check(Button("Экспорт в файл").IsEnabled&&Button("Скопировать код").IsEnabled,"Editor exposes both forms of preset export");
  Click("Сохранить как копию");
  Check(Draft.Id!=saved.Id&&Draft.Name.Contains("копия")&&JsonSerializer.Serialize(new PresetStore(root).List().Single(p=>p.Id==saved.Id))==savedJson,"Saving as copy switches editing to an independent library entry");
  Click("← Назад");
  Check(Button("Импорт из файла").IsEnabled&&Button("Вставить код").IsEnabled,"Saved gallery offers both import actions");
  string share=PresetExchange.ExportCode(new PresetStore(root),saved);
  Func<PresetStore,WidgetPreset> fromCode=store=>PresetExchange.ImportCode(store,share);
  window.Dispatcher.BeginInvoke((Action)(()=>typeof(LauncherWindow).GetMethod("ImportSharedPreset",Instance)!.Invoke(window,new object[]{fromCode})));
  Pump();
  for(int attempt=0;attempt<100&&(bool)Field("busy")!;attempt++)Pump();
  Check((string)Field("customizationStep")! == "editor"&&Draft.Id!=saved.Id&&Draft.MediaPath==saved.MediaPath&&new PresetStore(root).List().Any(p=>p.Id==Draft.Id),"Background import opens the newly saved preset with its portable image");
  Click("← Назад");
  Click("Мой пресет: QA — мой пресет");
  // Show an untouched generated composition as well as the earlier locked editing examples.
  window.Width=1020;window.Height=760;typeof(LauncherWindow).GetField("presetDraft",Instance)!.SetValue(window,PresetRandomizer.Create(new Random(812),new[]{"Onest","Segoe UI","Consolas"}));Call("Render");Layout();Click("Форма");Click("Вписать");Nodes<ScrollViewer>().First().ScrollToTop();Layout();Snapshot("preset-random-fresh-fit");
  CheckWidgetWorkshop(root);
  CheckGroupEditing();
  Console.WriteLine("TOTAL "+checks+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{window?.RequestShutdown();app?.Shutdown();}}
}
