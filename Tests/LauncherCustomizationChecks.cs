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
 [STAThread]static int Main(string[] args){Application? app=null;try{
  string root=Path.GetFullPath(args[0]);snapshots=Path.GetFullPath(args[1]);Directory.CreateDirectory(snapshots);
  app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  window=new LauncherWindow(root,true,"customize");window.ShowActivated=false;window.ShowInTaskbar=false;window.Opacity=0;window.Show();Layout();
  Snapshot("customization-home-night");
  Check(!Nodes<TextBlock>().Any(x=>x.Text=="Режим интерфейса")&&!Nodes<Button>().Any(x=>x.Content as string is "Ночной" or "Дневной"),"Customization no longer contains the interface mode selector");
  Check(!Nodes<TextBlock>().Any(x=>x.Text is "01" or "02" or "03"),"Entry cards contain no numbered labels");
  var navigation=(Dictionary<string,Button>)Field("nav")!;
  Check(navigation.Keys.ToArray().SequenceEqual(new[]{"catalog","installed","customize","obs","settings"}),"OBS navigation sits directly after customization");
  Navigate("obs");Check(Nodes<TextBox>().Any(x=>AutomationProperties.GetName(x)=="Порт OBS")&&Nodes<Button>().Any(x=>x.Content as string=="Сохранить порт"),"OBS page exposes actual connection controls");Snapshot("obs");
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
  string savedJson=JsonSerializer.Serialize(saved);Home();Click("Созданные пресеты");Click("Выбрать виджет: Мини-плеер");Click("Мой пресет: QA — мой пресет");
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
  Click("Созданные пресеты");Click("Выбрать виджет: Мини-плеер");Click("Мой пресет: QA — мой пресет");Check(JsonSerializer.Serialize(Draft)==savedJson,"Saved presets survive a fresh launcher window");
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
  Console.WriteLine("TOTAL "+checks+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{window?.RequestShutdown();app?.Shutdown();}}
}
