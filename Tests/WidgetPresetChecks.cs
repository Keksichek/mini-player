using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WidgetLauncher;

static class WidgetPresetChecks
{
 static int count;
 static bool Same(WidgetPreset? a,WidgetPreset? b){if(a==null||b==null)return a==b;var x=a.Copy();var y=b.Copy();x.Normalize();y.Normalize();return JsonSerializer.Serialize(x)==JsonSerializer.Serialize(y);}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Reject(Action action,string name){bool failed=false;try{action();}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException or JsonException){failed=true;}Check(failed,name);}
 static void WritePng(string path){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255},8)));using var stream=File.Create(path);png.Save(stream);}
 static bool Overlaps(PresetElement a,PresetElement b)=>Math.Min(a.X+a.Width,b.X+b.Width)-Math.Max(a.X,b.X)>.01&&Math.Min(a.Y+a.Height,b.Y+b.Height)-Math.Max(a.Y,b.Y)>.01;
 static bool RoundBoundsContain(WidgetPreset p,PresetElement e)=>new[]{e.X,e.X+e.Width}.All(x=>new[]{e.Y,e.Y+e.Height}.All(y=>Math.Pow((x-p.Width/2)/(p.Width/2),2)+Math.Pow((y-p.Height/2)/(p.Height/2),2)<=1.001));
 static double Luminance(string color){double C(int offset){double x=Convert.ToInt32(color.Substring(offset,2),16)/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return .2126*C(1)+.7152*C(3)+.0722*C(5);}
 [STAThread]static int Main(string[] args){try{
  string scratch=Path.GetFullPath(args[0]);Directory.CreateDirectory(scratch);string hub=Path.Combine(scratch,"Hub");Directory.CreateDirectory(hub);var store=new PresetStore(hub);
  Check(store.LoadDraft()==null&&store.List().Count==0,"A new launcher has no invented saved presets");
  Check(PresetThemes.Names.Length==14&&PresetThemes.Names.Distinct().Count()==14,"All fourteen player themes have starter presets");
  foreach(string theme in PresetThemes.Names){var preset=PresetThemes.Create(theme);preset.Normalize();Check(preset.Theme==theme&&WidgetPreset.ValidColor(preset.AccentColor)&&WidgetPreset.ValidColor(preset.BackgroundColor),"Theme can be edited: "+theme);}
  var draft=PresetThemes.Create("Лаванда");draft.Name="Свой лавандовый пресет";draft.Shape="ellipse";draft.Opacity=.45;draft.FontFamily="Consolas";draft.MediaX=.2;store.SaveDraft(draft);
  Check(Same(store.LoadDraft(),draft),"Draft retains all configured fields after reopening");
  var saved=store.Save(draft);Check(saved.Id.Length==32&&draft.Id==saved.Id&&Same(store.List().Single(),saved),"Saving creates a stable preset identity and reloads it");
  saved.Name="Переименован";saved.Opacity=.65;store.Save(saved);Check(store.List().Count==1&&store.List().Single().Name=="Переименован","Saving an edited preset replaces only that preset");
  string png=Path.Combine(scratch,"own.png");WritePng(png);string imported=store.ImportMedia(png);string importedFile=store.ResolveMedia(imported);File.Delete(png);
  Check(File.Exists(importedFile)&&store.MediaFiles().SequenceEqual(new[]{imported}),"Imported media survives deleting the source image");
  saved.MediaPath=imported;store.Save(saved);var reopened=store.List().Single();Check(reopened.MediaPath==imported&&!Path.IsPathRooted(imported),"Media references are relative and persist with presets");
  string movedHub=Path.Combine(scratch,"MovedHub");Directory.CreateDirectory(movedHub);string relative=Path.GetRelativePath(hub,importedFile);string movedFile=Path.Combine(movedHub,relative);Directory.CreateDirectory(Path.GetDirectoryName(movedFile)!);File.Copy(importedFile,movedFile);Check(File.Exists(new PresetStore(movedHub).ResolveMedia(imported)),"The same media reference resolves after moving the hub folder");
  foreach(string bad in new[]{"../outside.png","Media/../../outside.png","C:/secret.png","https://example.com/picture.png","Media/"+new string('a',64)+".png\n"})Reject(()=>store.ResolveMedia(bad),"External or traversing media paths are refused");
  var badPreset=saved.Copy();badPreset.MediaPath="../outside.png";Reject(()=>store.Save(badPreset),"A corrupt media path cannot be saved over a valid preset");Check(Same(store.List().Single(),reopened),"A failed save leaves the previous preset intact");
  Reject(()=>store.Delete("../draft"),"Delete cannot address unrelated files");
  var values=saved.Copy();values.Width=double.NaN;values.Height=double.PositiveInfinity;values.FontFamily="https://invalid/font.ttf#Font";values.Opacity=-10;values.BackgroundColor="garbage";values.Normalize();Check(double.IsFinite(values.Width)&&double.IsFinite(values.Height)&&values.Opacity==.15&&values.FontFamily=="Onest"&&WidgetPreset.ValidColor(values.BackgroundColor),"Malformed numeric, color and font values have bounded fallbacks");
  string broken=Path.Combine(store.Root,"Presets",Guid.NewGuid().ToString("N")+".json");File.WriteAllText(broken,"{broken");Check(store.List().Count==1&&File.ReadAllText(broken)=="{broken","A broken preset does not hide valid presets or get overwritten");
  string fake=Path.Combine(scratch,"fake.gif");File.WriteAllText(fake,"not an image");Reject(()=>store.ImportMedia(fake),"Invalid image bytes are rejected before import");
  string disguised=Path.Combine(scratch,"disguised.jpg");WritePng(disguised);Reject(()=>store.ImportMedia(disguised),"A misleading image extension is rejected");
  string huge=Path.Combine(scratch,"huge.png");using(var stream=File.Create(huge))stream.SetLength(PresetStore.MaximumMediaBytes+1);Reject(()=>store.ImportMedia(huge),"Oversized media is rejected before decoding");
  string gif=Path.Combine(scratch,"own.gif");var encoder=new GifBitmapEncoder();using(var pngStream=File.OpenRead(importedFile)){var frame=BitmapFrame.Create(pngStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);encoder.Frames.Add(frame);encoder.Frames.Add(frame);}using(var stream=File.Create(gif))encoder.Save(stream);
  string animated=store.ImportMedia(gif);Check(animated.EndsWith(".gif")&&File.Exists(store.ResolveMedia(animated)),"Animated GIF files can be imported for preview");
  ExchangeChecks(scratch,store,saved,animated);
  var random=new Random(12);var generated=Enumerable.Range(0,120).Select(_=>PresetRandomizer.Create(random,new[]{"Onest","Consolas"},new[]{imported,animated})).ToArray();
  Check(generated.All(x=>x.Width>=240&&x.Width<=640&&x.Height>=140&&x.Height<=420&&WidgetPreset.ValidColor(x.TextColor)),"Random presets stay inside validated layout limits");
  Check(generated.All(x=>x.Elements.Values.All(e=>e.X>=0&&e.Y>=0&&e.X+e.Width<=x.Width+.001&&e.Y+e.Height<=x.Height+.001))&&generated.Select(x=>x.Elements["play"].X).Distinct().Count()>1,"Randomization also varies element layouts within the widget bounds");
  foreach(string property in new[]{"Theme","Shape","Width","Height","CornerRadius","BorderWidth","Opacity","FontFamily","FontSize","FontWeight","BackgroundColor","AccentColor","TextColor","BorderColor","CoverSize","ShowCover","ShowArtist","MediaPath","MediaFit","MediaOpacity","MediaX","MediaY"}){
   var info=typeof(WidgetPreset).GetProperty(property)!;Check(generated.Select(x=>info.GetValue(x)).Distinct().Count()>1,"Randomization varies "+property);
  }
  var layouts=Enumerable.Range(0,1500).Select(seed=>PresetRandomizer.Create(new Random(seed),new[]{"Onest","Consolas","Wingdings"})).ToArray();
  Check(layouts.All(p=>new[]{"title","previous","play","next","repeat","progress","time","volume","pin","lock","clickthrough","hide","menu"}.All(p.IsElementVisible)),"Randomization always retains essential information and player controls across 1500 seeds");
  Check(layouts.All(p=>{var elements=p.Elements.Where(x=>p.IsElementVisible(x.Key)).Select(x=>x.Value).ToArray();return elements.SelectMany((a,i)=>elements.Skip(i+1).Select(b=>!Overlaps(a,b))).All(x=>x);}),"Aligned random groups never overlap across 1500 seeds");
  Check(layouts.All(p=>p.Elements.Values.All(e=>e.X>=0&&e.Y>=0&&e.X+e.Width<=p.Width+.01&&e.Y+e.Height<=p.Height+.01))&&layouts.Where(p=>p.Shape is "circle" or "ellipse" or "pill").All(p=>p.Elements.Values.All(e=>RoundBoundsContain(p,e))),"Random elements fit the visible shape including curved corners across 1500 seeds");
  Check(layouts.All(p=>p.FontSize>=14&&p.FontSize<=18&&p.FontFamily!="Wingdings"&&p.Opacity>=.94&&p.MediaOpacity<=.2),"Random typography and opacity remain readable with quiet optional backgrounds");
  Check(layouts.All(p=>{double a=Luminance(p.BackgroundColor),b=Luminance(p.TextColor);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05)>=4.5;}),"Every random palette provides readable text contrast");
  var geometry=new WidgetPreset{Width=440,Height=260};geometry.Normalize();Check(geometry.SetShape("circle")&&geometry.Width==420&&geometry.Height==420,"Choosing circle creates a true circle within the height limit");Check(geometry.SetShape("ellipse")&&geometry.Width==440&&geometry.Height==260,"Returning to oval restores its distinct previous dimensions");
  var lockedPreset=PresetRandomizer.Create(new Random(43));lockedPreset.LockElement("title",true);lockedPreset.LockElement("play",true);lockedPreset.LockElement("cover",true);var originalLocked=lockedPreset.Copy();
  string lockedRecords=JsonSerializer.Serialize(originalLocked.Elements.Where(x=>x.Value.Locked).ToDictionary(x=>x.Key,x=>x.Value));
  lockedPreset.TextColor="#FF0000";lockedPreset.AccentColor="#0000FF";lockedPreset.FontFamily="Arial";lockedPreset.FontSize=60;lockedPreset.Opacity=.2;lockedPreset.ShowCover=!lockedPreset.ShowCover;lockedPreset.ShowControls=false;lockedPreset.Normalize();
  Check(lockedPreset.EffectiveStyle("title")==originalLocked.EffectiveStyle("title")&&lockedPreset.EffectiveStyle("play")==originalLocked.EffectiveStyle("play")&&lockedPreset.IsElementVisible("cover")==originalLocked.IsElementVisible("cover")&&lockedPreset.IsElementVisible("play"),"Locked elements retain their styles and visibility when global settings change");
  var lockedCopies=Enumerable.Range(0,250).Select(seed=>PresetRandomizer.Create(new Random(seed),new[]{"Onest","Consolas"},basis:lockedPreset)).ToArray();
  Check(lockedCopies.All(p=>JsonSerializer.Serialize(p.Elements.Where(x=>x.Value.Locked).ToDictionary(x=>x.Key,x=>x.Value))==lockedRecords&&p.Width==lockedPreset.Width&&p.Height==lockedPreset.Height&&p.Shape==lockedPreset.Shape),"Randomization preserves locked geometry styles and body shape exactly across 250 seeds");
  Check(lockedCopies.All(p=>p.Elements.All(x=>x.Value.X==lockedPreset.Elements[x.Key].X&&x.Value.Y==lockedPreset.Elements[x.Key].Y&&x.Value.Width==lockedPreset.Elements[x.Key].Width&&x.Value.Height==lockedPreset.Elements[x.Key].Height)),"Randomization with anchors never moves free controls onto locked controls");
  Check(lockedCopies.All(p=>{double a=Luminance(p.BackgroundColor),b=Luminance(p.EffectiveStyle("title").TextColor);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05)>=4.5;}),"Random backgrounds preserve readability of a locked text color");
  store.SaveDraft(lockedPreset);Check(Same(store.LoadDraft(),lockedPreset),"Locks and their style snapshots survive draft persistence");store.Apply(lockedPreset);Check(Same(store.LoadActive(),lockedPreset),"Locks and their style snapshots persist for the live player");
  var isolated=lockedPreset.Copy();isolated.Elements["title"].LockedStyle!.FontSize=1;Check(lockedPreset.Elements["title"].LockedStyle!.FontSize!=1,"Copying presets also isolates each locked style snapshot");
  lockedPreset.LockElement("title",false);Check(!lockedPreset.Elements["title"].Locked&&lockedPreset.Elements["title"].LockedStyle==null&&lockedPreset.EffectiveStyle("title").FontSize==60,"Unlocking resumes the current global style");
  var resized=new WidgetPreset{Width=500,Height=300};resized.Normalize();resized.Elements["play"]=new PresetElement{X=410,Y=230,Width=40,Height=35};resized.LockElement("play",true);var anchored=resized.Elements["play"].Copy();Check(!resized.Resize(240,140)&&resized.Width==450&&resized.Height==265&&resized.Elements["play"]==anchored,"Shrinking the body stops at a locked element without moving or resizing it");
  Check(!resized.SetShape("circle")&&resized.Shape=="rounded"&&resized.Elements["play"]==anchored,"An incompatible smaller shape is refused without altering locked geometry");
  resized.Resize(600,380);Check(resized.Elements["play"]==anchored,"Growing the body leaves locked geometry untouched");
  store.Delete(saved.Id);Check(store.List().Count==0&&File.Exists(importedFile)&&store.LoadDraft()!=null,"Deleting a saved preset preserves shared imported media and the draft");
  var active=new WidgetPreset{FontSize=80};active.Normalize();Check(active.FontSize==60&&active.Elements.Count==WidgetPreset.ElementIds.Length&&active.Elements.ContainsKey("clickthrough"),"Shared presets migrate all element positions and clamp font size to 60");
  var copied=active.Copy();copied.Elements["play"].X+=5;Check(copied.Elements["play"].X!=active.Elements["play"].X,"Copying a preset isolates element positions for Undo and saved originals");
  store.Apply(active);Check(Same(store.LoadActive(),active),"Applying atomically persists the same layout for the player");
  var invalid=active.Copy();invalid.MediaPath="Media/"+new string('0',64)+".png";Reject(()=>store.Apply(invalid),"An absent imported background cannot replace the active preset");Check(Same(store.LoadActive(),active),"Failed apply keeps the previously active preset");
  store.ClearActive();Check(store.LoadActive()==null,"The active custom preset can be cleared without deleting saved presets");
  Console.WriteLine("TOTAL "+count+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static void ExchangeChecks(string scratch,PresetStore source,WidgetPreset original,string animated){
  var receiver=new PresetStore(Path.Combine(scratch,"Other PC"));
  var preset=original.Copy();preset.MediaPath=animated;preset.LockElement("play",true);
  string package=Path.Combine(scratch,"shared.vibepreset");
  PresetExchange.ExportFile(source,preset,package);
  var first=PresetExchange.ImportFile(receiver,package);
  Check(first.Id!=original.Id&&Same(first,preset with{Id=first.Id}),"One-file import preserves all settings and locked styles under a fresh identity");
  Check(File.ReadAllBytes(receiver.ResolveMedia(first.MediaPath)).SequenceEqual(File.ReadAllBytes(source.ResolveMedia(animated))),"A second hub receives the original animated GIF bytes");
  var duplicate=receiver.SaveCopy(first);
  duplicate.Elements["play"].X=0;receiver.Save(duplicate);
  Check(first.Id!=duplicate.Id&&duplicate.Name!=first.Name&&Same(receiver.List().Single(x=>x.Id==first.Id),first),"Save as copy leaves the original name and layout intact");
  var again=PresetExchange.ImportFile(receiver,package);
  Check(new[]{first.Id,duplicate.Id,again.Id}.Distinct().Count()==3&&receiver.List().Select(p=>p.Name).Distinct().Count()==3,"Repeated import creates distinguishable presets");
  foreach(string path in new[]{"",original.MediaPath,animated}){
   var draft=preset.Copy();draft.MediaPath=path;
   string code=PresetExchange.ExportCode(source,draft);
   var retrieved=PresetExchange.ImportCode(receiver," "+code+"\n");
   Check(Same(retrieved,draft with{Id=retrieved.Id,Name=retrieved.Name})&&(path==""||File.ReadAllBytes(source.ResolveMedia(path)).SequenceEqual(File.ReadAllBytes(receiver.ResolveMedia(path)))),"Code transfers layout and its optional embedded media: "+Path.GetExtension(path));
  }
  receiver.Apply(first);
  int total=receiver.List().Count;
  Reject(()=>PresetExchange.ImportCode(receiver,"VIBE1:truncated??"),"A damaged share code is rejected");
  var missing=preset with{MediaPath="Media/"+new string('0',64)+".png"};
  byte[] before=File.ReadAllBytes(package);
  Reject(()=>PresetExchange.ExportFile(source,missing,package),"Missing media cannot replace a valid export");
  Check(before.SequenceEqual(File.ReadAllBytes(package)),"Failed export preserves the previous file");
  using(var archive=ZipFile.Open(package,ZipArchiveMode.Update)){
   archive.GetEntry(animated)!.Delete();
   using var replaced=archive.CreateEntry(animated).Open();
   replaced.Write(File.ReadAllBytes(source.ResolveMedia(original.MediaPath)));
  }
  Reject(()=>PresetExchange.ImportFile(receiver,package),"Corrupt background contents are rejected before import");
  Check(total==receiver.List().Count&&Same(first,receiver.LoadActive()),"Invalid imports keep the gallery and active preset intact");
 }
}
