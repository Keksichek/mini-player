using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace WidgetLauncher;

public sealed record PresetElementStyle
{
 public string BackgroundColor {get;set;}="#F9F7F0";
 public string AccentColor {get;set;}="#7FA6D9";
 public string TextColor {get;set;}="#252831";
 public string BorderColor {get;set;}="#7FA6D9";
 public string FontFamily {get;set;}="Onest";
 public double FontSize {get;set;}=16;
 public int FontWeight {get;set;}=700;
 public double Opacity {get;set;}=1;
 public PresetElementStyle Copy()=>this with{};
}

public sealed record PresetElement
{
 public double X {get;set;}
 public double Y {get;set;}
 public double Width {get;set;}=32;
 public double Height {get;set;}=32;
 public bool Visible {get;set;}=true;
 public bool Locked {get;set;}
 public PresetElementStyle? LockedStyle {get;set;}
 public PresetElement Copy()=>this with{LockedStyle=LockedStyle?.Copy()};
}

// Shared by the editor and the live mini-player.
public sealed record WidgetPreset
{
 public string Id {get;set;}="";
 public string Name {get;set;}="Мой пресет";
 public string Theme {get;set;}="Светлая";
 public string Shape {get;set;}="rounded";
 public double Width {get;set;}=350;
 public double Height {get;set;}=220;
 public double LastFreeWidth {get;set;}=350;
 public double LastFreeHeight {get;set;}=220;
 public double CornerRadius {get;set;}=20;
 public double BorderWidth {get;set;}=1;
 public double Opacity {get;set;}=1;
 public string FontFamily {get;set;}="Onest";
 public double FontSize {get;set;}=16;
 public int FontWeight {get;set;}=700;
 public string BackgroundColor {get;set;}="#F9F7F0";
 public string AccentColor {get;set;}="#7FA6D9";
 public string TextColor {get;set;}="#252831";
 public string BorderColor {get;set;}="#7FA6D9";
 public double CoverSize {get;set;}=64;
 public bool ShowCover {get;set;}=true;
 public bool ShowArtist {get;set;}=true;
 public bool ShowTime {get;set;}=true;
 public bool ShowProgress {get;set;}=true;
 public bool ShowControls {get;set;}=true;
 public string MediaPath {get;set;}="";
 public string MediaFit {get;set;}="cover";
 public double MediaOpacity {get;set;}=.65;
 public double MediaX {get;set;}=.5;
 public double MediaY {get;set;}=.5;
 public Dictionary<string,PresetElement> Elements {get;set;}=new();
 // Back to front, matching the original drawing order for presets saved before layers existed.
 static readonly string[] DefaultLayerOrder={"cover","title","artist","time","previous","play","next","repeat","pin","lock","clickthrough","hide","menu","progress","volume"};
 public string[] LayerOrder {get;set;}=DefaultLayerOrder.ToArray();
 public static readonly string[] ElementIds={"cover","title","artist","previous","play","next","repeat","pin","lock","clickthrough","hide","menu","progress","time","volume"};
 public static readonly string[] Shapes={"rounded","rectangle","pill","ellipse","cut","star","square","circle","diamond","hexagon","heart"};
 public static readonly string[] Fits={"cover","contain","stretch"};
 public WidgetPreset Copy()=>this with{Elements=(Elements??new()).Where(x=>x.Value!=null).ToDictionary(x=>x.Key,x=>x.Value.Copy()),LayerOrder=(LayerOrder??DefaultLayerOrder).ToArray()};
 public bool CanMoveLayer(string id,int direction){
  if(direction is not (-1 or 1))return false;
  int index=Array.IndexOf(LayerOrder,id),other=index+direction;
  return index>=0 && other>=0 && other<LayerOrder.Length &&
         Elements.TryGetValue(id,out var selected) && !selected.Locked &&
         Elements.TryGetValue(LayerOrder[other],out var neighbor) && !neighbor.Locked;
 }
 public bool MoveLayer(string id,int direction){
  if(!CanMoveLayer(id,direction))return false;
  int index=Array.IndexOf(LayerOrder,id),other=index+direction;
  (LayerOrder[index],LayerOrder[other])=(LayerOrder[other],LayerOrder[index]);
  return true;
 }
 public PresetElementStyle EffectiveStyle(string id)=>Elements.TryGetValue(id,out var element)&&element.Locked&&element.LockedStyle!=null?element.LockedStyle:new PresetElementStyle{BackgroundColor=BackgroundColor,AccentColor=AccentColor,TextColor=TextColor,BorderColor=BorderColor,FontFamily=FontFamily,FontSize=FontSize,FontWeight=FontWeight,Opacity=Opacity};
 public bool IsElementVisible(string id){if(!Elements.TryGetValue(id,out var e))return false;if(e.Locked)return e.Visible;return e.Visible&&(id switch{"cover"=>ShowCover,"artist"=>ShowArtist,"time"=>ShowTime,"progress"=>ShowProgress,"previous" or "play" or "next" or "repeat"=>ShowControls,_=>true});}
 public void LockElement(string id,bool locked){
  Normalize();if(!Elements.TryGetValue(id,out var item))throw new ArgumentException("Неизвестный элемент.",nameof(id));
  if(item.Locked==locked)return;
  if(locked){item.LockedStyle=EffectiveStyle(id).Copy();item.Visible=IsElementVisible(id);item.Locked=true;}
  else{item.Locked=false;item.LockedStyle=null;}
 }
 public bool Resize(double width,double height){
  Normalize();return ResizeCore(width,height,Width,Height);
 }
 bool ResizeCore(double width,double height,double oldWidth,double oldHeight){
  width=Bound(width,240,640,Width);height=Bound(height,140,420,Height);
  double requiredWidth=Elements.Values.Where(x=>x.Locked).Select(x=>x.X+x.Width).DefaultIfEmpty(240).Max();
  double requiredHeight=Elements.Values.Where(x=>x.Locked).Select(x=>x.Y+x.Height).DefaultIfEmpty(140).Max();
  bool exact=width>=requiredWidth&&height>=requiredHeight;
  if(Shape is "circle" or "square"){double side=Math.Clamp(Math.Max(width,Math.Max(requiredWidth,requiredHeight)),240,420);exact&=side==width;width=height=side;}
  else{width=Math.Max(width,requiredWidth);height=Math.Max(height,requiredHeight);}
  Width=width;Height=height;
  // Scale the position within the available travel, leaving each control's size independent of text and canvas size.
  foreach(var item in Elements.Values.Where(x=>!x.Locked)){
   double x=oldWidth>item.Width?item.X/(oldWidth-item.Width):0,y=oldHeight>item.Height?item.Y/(oldHeight-item.Height):0;
   item.Width=Math.Min(item.Width,Width);item.Height=Math.Min(item.Height,Height);item.X=x*Math.Max(0,Width-item.Width);item.Y=y*Math.Max(0,Height-item.Height);
  }
  if(Shape is not ("circle" or "square")){LastFreeWidth=Width;LastFreeHeight=Height;}Normalize();return exact;
 }
 public bool SetShape(string shape){
  if(!Shapes.Contains(shape))throw new ArgumentException("Неизвестная форма.",nameof(shape));Normalize();if(shape==Shape)return true;
  bool wasEqual=Shape is "circle" or "square",equal=shape is "circle" or "square";
  if(equal&&Elements.Values.Any(e=>e.Locked&&(e.X+e.Width>420||e.Y+e.Height>420)))return false;
  if(!wasEqual){LastFreeWidth=Width;LastFreeHeight=Height;}
  double oldWidth=Width,oldHeight=Height;
  Shape=shape;
  if(equal){ResizeCore(Width,Width,oldWidth,oldHeight);return true;}
  double width=wasEqual?LastFreeWidth:Width,height=wasEqual?LastFreeHeight:Height;
  if(shape=="ellipse"&&Math.Abs(width-height)<1)height=Math.Max(140,Math.Round(width*.64));
  ResizeCore(width,height,oldWidth,oldHeight);return true;
 }
 public void Normalize(){
  Id=Guid.TryParseExact(Id,"N",out var parsedId)?parsedId.ToString("N"):"";
  Name=CleanText(Name,80,"Мой пресет");Theme=PresetThemes.Names.Contains(Theme)?Theme:"Светлая";
  Shape=Shapes.Contains(Shape)?Shape:"rounded";MediaFit=Fits.Contains(MediaFit)?MediaFit:"cover";
  Width=Bound(Width,240,640,350);Height=Bound(Height,140,420,180);CornerRadius=Bound(CornerRadius,0,100,20);BorderWidth=Bound(BorderWidth,0,10,1);Opacity=Bound(Opacity,.15,1,1);
  LastFreeWidth=Bound(LastFreeWidth,240,640,350);LastFreeHeight=Bound(LastFreeHeight,140,420,220);
  FontFamily=CleanText(FontFamily,100,"Onest");if(FontFamily.IndexOfAny(new[]{':','/','\\','#',';','{','}'})>=0)FontFamily="Onest";
  FontSize=Bound(FontSize,1,60,16);FontWeight=Math.Clamp((int)Math.Round(FontWeight/100d)*100,100,900);
  BackgroundColor=Color(BackgroundColor,"#F9F7F0");AccentColor=Color(AccentColor,"#7FA6D9");TextColor=Color(TextColor,"#252831");BorderColor=Color(BorderColor,"#7FA6D9");
  CoverSize=Bound(CoverSize,32,140,64);MediaOpacity=Bound(MediaOpacity,0,1,.65);MediaX=Bound(MediaX,0,1,.5);MediaY=Bound(MediaY,0,1,.5);
  MediaPath??="";if(MediaPath.Length>0&&!PresetStore.ValidMediaPath(MediaPath))throw new InvalidDataException("Недопустимый путь изображения в пресете.");
  if(Shape is "square" or "circle"){Height=Math.Clamp(Width,240,420);Width=Height;}
  var defaults=DefaultElements();Elements??=new();
  LayerOrder=(LayerOrder??DefaultLayerOrder).Where(DefaultLayerOrder.Contains).Concat(DefaultLayerOrder).Distinct().ToArray();
  foreach(string id in Elements.Keys.Except(ElementIds).ToArray())Elements.Remove(id);
  bool addMouseButton=!Elements.ContainsKey("clickthrough")&&Elements.Count>0;
  foreach(string id in ElementIds){
   if(!Elements.TryGetValue(id,out var item)||item==null){item=defaults[id];Elements[id]=item;}item.Width=Bound(item.Width,8,Width,defaults[id].Width);item.Height=Bound(item.Height,8,Height,defaults[id].Height);item.X=Bound(item.X,0,Math.Max(0,Width-item.Width),defaults[id].X);item.Y=Bound(item.Y,0,Math.Max(0,Height-item.Height),defaults[id].Y);
   if(item.Locked){item.LockedStyle??=new PresetElementStyle{BackgroundColor=BackgroundColor,AccentColor=AccentColor,TextColor=TextColor,BorderColor=BorderColor,FontFamily=FontFamily,FontSize=FontSize,FontWeight=FontWeight,Opacity=Opacity};var s=item.LockedStyle;s.BackgroundColor=Color(s.BackgroundColor,BackgroundColor);s.AccentColor=Color(s.AccentColor,AccentColor);s.TextColor=Color(s.TextColor,TextColor);s.BorderColor=Color(s.BorderColor,BorderColor);s.FontFamily=CleanText(s.FontFamily,100,"Onest");if(s.FontFamily.IndexOfAny(new[]{':','/','\\','#',';','{','}'})>=0)s.FontFamily="Onest";s.FontSize=Bound(s.FontSize,1,60,FontSize);s.FontWeight=Math.Clamp((int)Math.Round(s.FontWeight/100d)*100,100,900);s.Opacity=Bound(s.Opacity,.15,1,Opacity);}
  }
  if(addMouseButton)PlaceMouseButton();
 }
 void PlaceMouseButton(){
  var button=Elements["clickthrough"];var pin=Elements["pin"];
  double gap=3,w=button.Width,h=button.Height;
  bool Fits(double x,double y)=>x>=0&&y>=0&&x+w<=Width&&y+h<=Height&&
   Elements.Where(pair=>pair.Key!="clickthrough"&&IsElementVisible(pair.Key)).All(pair=>{
    var other=pair.Value;
    return x+w<=other.X||y+h<=other.Y||x>=other.X+other.Width||y>=other.Y+other.Height;
   });
  if(Fits(pin.X-w-gap,pin.Y)){button.X=pin.X-w-gap;button.Y=pin.Y;return;}
  if(Fits(button.X,button.Y))return;
  double best=double.PositiveInfinity;
  for(double y=gap;y+h<=Height;y+=6)for(double x=gap;x+w<=Width;x+=6){
   double cost=(x-pin.X)*(x-pin.X)+(y-pin.Y)*(y-pin.Y);
   if(cost<best&&Fits(x,y)){best=cost;button.X=x;button.Y=y;}
  }
 }
 public Dictionary<string,PresetElement> DefaultElements(){
  var values=new Dictionary<string,PresetElement>();void Put(string id,double x,double y,double w,double h){values[id]=new PresetElement{X=x,Y=y,Width=w,Height=h};}
  double cover=Math.Min(CoverSize,Height*.30),row=Math.Max(22,Math.Min(32,Height*.17));
  Put("cover",16,Height*.24,cover,cover);Put("title",30+cover,Height*.24,Math.Max(80,Width-cover-48),30);Put("artist",30+cover,Height*.24+30,Math.Max(80,Width-cover-48),22);
  Put("previous",Width*.24,Height*.59,row,row);Put("play",Width*.39,Height*.59,row,row);Put("next",Width*.54,Height*.59,row,row);Put("repeat",Width*.70,Height*.59,row,row);
  int index=0;foreach(string id in new[]{"clickthrough","pin","lock","hide","menu"})Put(id,Width-135+index++*25,8,22,22);
  Put("progress",16,Height*.79,Width-110,18);Put("time",Width-88,Height*.79,74,18);Put("volume",16,Height*.91,Width-32,16);
  return values;
 }
 static double Bound(double value,double min,double max,double fallback)=>Math.Clamp(double.IsFinite(value)?value:fallback,min,max);
 static string CleanText(string? text,int limit,string fallback){string value=new((text??"").Where(c=>!char.IsControl(c)).Take(limit).ToArray());return string.IsNullOrWhiteSpace(value)?fallback:value.Trim();}
 public static bool ValidColor(string? value)=>value!=null&&Regex.IsMatch(value,@"\A#[0-9A-Fa-f]{6}\z",RegexOptions.CultureInvariant);
 static string Color(string? value,string fallback)=>ValidColor(value)?value!.ToUpperInvariant():fallback;
}

public static class PresetThemes
{
 public static readonly string[] Names={"Винил","Закат","Карманный LCD","Светлая","Тёмная","Minecraft","Ретро","Лаванда","Ночной океан","Лес","Кофе","Персик","Терминал","Стекло"};
 public static WidgetPreset Create(string theme){
  if(!Names.Contains(theme))throw new ArgumentException("Неизвестная тема.",nameof(theme));
  var values=theme switch{
   "Винил"=>("#C2A9B2","#302632","#FA8095",360d,140d,40d),
   "Закат"=>("#F8E8DF","#63372B","#FF8655",320d,188d,20d),
   "Карманный LCD"=>("#C9C8B6","#243C28","#FF795C",240d,224d,8d),
   "Тёмная"=>("#191A20","#F6EFE9","#7FA6D9",339d,182d,5d),
   "Minecraft"=>("#4C5140","#FFF3D9","#91DE47",280d,200d,0d),
   "Ретро"=>("#D7C4A3","#3E3027","#B44940",280d,200d,8d),
   "Лаванда"=>("#76617F","#FFF5FF","#B38DE3",280d,200d,12d),
   "Ночной океан"=>("#102638","#F2F4F3","#4CD8ED",280d,200d,12d),
   "Лес"=>("#173A30","#F0F4E7","#B2D975",280d,200d,12d),
   "Кофе"=>("#78513B","#FFF2DD","#ECB570",280d,200d,12d),
   "Персик"=>("#FFEBDA","#62332B","#F59B75",318d,212d,16d),
   "Терминал"=>("#0B2018","#D2EBC8","#52ED89",280d,200d,4d),
   "Стекло"=>("#CBE4EF","#193D55","#6FADE8",337d,184d,22d),
   _=>("#F9F7F0","#252831","#7FA6D9",351d,163d,21d)
  };
  return new WidgetPreset{Name=theme,Theme=theme,BackgroundColor=values.Item1,TextColor=values.Item2,AccentColor=values.Item3,BorderColor=values.Item3,Width=values.Item4,Height=values.Item5,CornerRadius=values.Item6,Shape=theme=="Minecraft"?"rectangle":"rounded",BorderWidth=theme=="Minecraft"?3:1,FontFamily=theme=="Терминал"?"Consolas":"Onest",Opacity=theme=="Стекло"?.8:1};
 }
}

public sealed class PresetStore
{
 public const long MaximumMediaBytes=20*1024*1024;
 internal const long MaximumJsonBytes=65536;
 static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
 public string Root {get;}
 public PresetStore(string hubRoot,string widgetId="mini-player"){
  PresetPaths.ValidateId(widgetId);Root=PresetPaths.Child(Path.Combine(Path.GetFullPath(hubRoot),"Data","Customization"),widgetId);PresetPaths.RejectLinks(Root);
 }
 string At(string relative){string path=PresetPaths.Child(Root,relative);PresetPaths.RejectLinks(path);return path;}
 public static bool ValidMediaPath(string? relative)=>relative!=null&&Regex.IsMatch(relative,@"\AMedia/[a-f0-9]{64}\.(png|jpg|gif|bmp)\z",RegexOptions.CultureInvariant);
 public string ResolveMedia(string relative){if(string.IsNullOrEmpty(relative))return "";if(!ValidMediaPath(relative))throw new InvalidDataException("Изображение должно находиться в папке пресетов.");return At(relative);}
 public WidgetPreset? LoadDraft(){string file=At("draft.json");return File.Exists(file)?Read(file):null;}
 public string ActivePath=>At("active.json");
 // Both windows can change the active preset. Keep ownership until rendering succeeds
 // or rollback finishes. Windows also releases the handle if its process exits.
 public IDisposable AcquireApplyLock(){
  string path=At("apply.lock");Directory.CreateDirectory(Root);
  try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
  catch(IOException ex)when((ex.HResult & 0xffff) is 32 or 33){
   throw new IOException("Оформление уже меняется в другом окне. Повтори выбор через несколько секунд.",ex);
  }
 }
 public WidgetPreset? LoadActive()=>File.Exists(ActivePath)?Read(ActivePath):null;
 public void Apply(WidgetPreset preset){var copy=preset.Copy();copy.Normalize();if(copy.MediaPath.Length>0&&!File.Exists(ResolveMedia(copy.MediaPath)))throw new IOException("Фоновое изображение не найдено.");Write(ActivePath,copy);}
 public void ClearActive(){if(File.Exists(ActivePath))File.Delete(ActivePath);}
 public void SaveDraft(WidgetPreset preset){var copy=preset.Copy();copy.Normalize();Write(At("draft.json"),copy);}
 public WidgetPreset? LoadSaved(string id){
  if(!Guid.TryParseExact(id,"N",out var guid))return null;
  string key=guid.ToString("N"),path=At("Presets/"+key+".json");
  if(!File.Exists(path))return null;
  var preset=Read(path);
  if(preset.Id!=key)throw new InvalidDataException("Не удалось проверить идентификатор сохранённого пресета.");
  return preset;
 }
 public IReadOnlyList<WidgetPreset> List()=>ListSaved().Select(entry=>entry.Preset).ToArray();
 public IReadOnlyList<(WidgetPreset Preset,bool Favorite)> ListSaved(){
  var result=new List<(WidgetPreset Preset,bool Favorite)>();
  string folder=At("Presets");if(!Directory.Exists(folder))return result;
  string markers=At("Favorites");
  var favorites=Directory.Exists(markers)
   ?Directory.EnumerateFiles(markers,"*.json").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase)
   :new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
  foreach(string path in Directory.EnumerateFiles(folder,"*.json").Take(1000)){
   if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path),"N",out var id))continue;
   try{var preset=Read(path);if(preset.Id==id.ToString("N"))result.Add((preset,favorites.Contains(preset.Id)));}catch(IOException){}catch(InvalidDataException){}catch(UnauthorizedAccessException){}catch(JsonException){}
  }
  return SortSaved(result);
 }
 internal static (WidgetPreset Preset,bool Favorite)[] SortSaved(IEnumerable<(WidgetPreset Preset,bool Favorite)> entries)=>
  entries.OrderByDescending(entry=>entry.Favorite).ThenBy(entry=>entry.Preset.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(entry=>entry.Preset.Id,StringComparer.Ordinal).ToArray();
 public void SetFavorite(string id,bool favorite){
  if(!Guid.TryParseExact(id,"N",out var parsed))throw new InvalidDataException("Неизвестный пресет.");
  string key=parsed.ToString("N"),marker=At("Favorites/"+key+".json");
  if(!favorite){if(File.Exists(marker))File.Delete(marker);return;}
  if(!File.Exists(At("Presets/"+key+".json")))throw new IOException("Этот пресет удалён.");
  if(File.Exists(marker))return;
  Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
  string temporary=marker+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temporary,"true");File.Move(temporary,marker,true);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public WidgetPreset Save(WidgetPreset preset){
  var copy=preset.Copy();copy.Normalize();if(copy.Id.Length==0)copy.Id=Guid.NewGuid().ToString("N");
  Write(At("Presets/"+copy.Id+".json"),copy);preset.Id=copy.Id;return copy;
 }
 public WidgetPreset SaveCopy(WidgetPreset source){
  var copy=source.Copy();copy.Id="";copy.Normalize();
  var existing=List().Select(item=>item.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
  string original=copy.Name;
  for(int index=1;;index++){
   string suffix=index==1?" (копия)":" (копия "+index+")";
   copy.Name=original[..Math.Min(original.Length,80-suffix.Length)].TrimEnd()+suffix;
   if(!existing.Contains(copy.Name))break;
  }
  return Save(copy);
 }
 public void Delete(string id){if(!Guid.TryParseExact(id,"N",out var parsed))throw new InvalidDataException("Неизвестный пресет.");string path=At("Presets/"+parsed.ToString("N")+".json");if(File.Exists(path))File.Delete(path);SetFavorite(id,false);}
 WidgetPreset Read(string file){
  PresetPaths.RejectLinks(file);if(new FileInfo(file).Length>MaximumJsonBytes)throw new InvalidDataException("Файл пресета слишком большой.");
  var value=JsonSerializer.Deserialize<WidgetPreset>(File.ReadAllText(file),Json)??throw new InvalidDataException("Файл пресета пуст.");value.Normalize();return value;
 }
 static void Write(string path,WidgetPreset value){
  PresetPaths.RejectLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temporary,JsonSerializer.Serialize(value,Json));File.Move(temporary,path,true);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public IReadOnlyList<string> MediaFiles(){
  string folder=At("Media");if(!Directory.Exists(folder))return Array.Empty<string>();
  return Directory.EnumerateFiles(folder).Select(x=>"Media/"+Path.GetFileName(x)).Where(ValidMediaPath).Where(x=>{try{string path=ResolveMedia(x);return new FileInfo(path).Length<=MaximumMediaBytes;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}).Take(1000).ToArray();
 }
 public string ImportMedia(string source){
  string extension=Path.GetExtension(source).ToLowerInvariant();if(extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"))throw new InvalidDataException("Выберите PNG, JPG, BMP или GIF.");
  using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(input.Length==0||input.Length>MaximumMediaBytes)throw new InvalidDataException("Размер изображения должен быть от 1 байта до 20 МБ.");
  byte[] bytes=new byte[input.Length];input.ReadExactly(bytes);
  return StoreMedia(bytes,extension);
 }
 internal string StoreMedia(byte[] bytes,string extension){
  if(bytes.Length==0||bytes.Length>MaximumMediaBytes)throw new InvalidDataException("Размер изображения должен быть от 1 байта до 20 МБ.");
  ValidateMedia(bytes,extension);
  string relative="Media/"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+(extension==".jpeg"?".jpg":extension);string path=ResolveMedia(relative);
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  // Content-addressed imports remain usable when the original image is moved or deleted.
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}return relative;
 }
 internal static void ValidateMedia(byte[] bytes,string extension){
  try{
   using var stream=new MemoryStream(bytes,false);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
   ValidateMediaFrames(decoder,extension);
  }catch(Exception ex)when(ex is NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException or OverflowException){throw new InvalidDataException("Не удалось прочитать изображение. Выберите корректный PNG, JPG, BMP или GIF.",ex);}
 }
 internal static (int Width,int Height) ValidateMediaFrames(BitmapDecoder decoder,string extension){
  bool format=extension switch{".png"=>decoder is PngBitmapDecoder,".jpg" or ".jpeg"=>decoder is JpegBitmapDecoder,".gif"=>decoder is GifBitmapDecoder,".bmp"=>decoder is BmpBitmapDecoder,_=>false};
  if(!format)throw new InvalidDataException("Содержимое изображения не совпадает с расширением файла.");
  if(decoder.Frames.Count is <1 or >180)throw new InvalidDataException("Изображение должно содержать от 1 до 180 кадров.");
  const long maximumPixels=48_000_000;
  long pixels=0;
  foreach(var frame in decoder.Frames){
   if(frame.PixelWidth is <1 or >4096||frame.PixelHeight is <1 or >4096)
    throw new InvalidDataException("Размер изображения — не более 4096 × 4096 пикселей.");
   pixels+=(long)frame.PixelWidth*frame.PixelHeight;
   if(pixels>maximumPixels)throw new InvalidDataException("Слишком большой GIF. Уменьшите разрешение или число кадров.");
  }
  int width=decoder.Frames[0].PixelWidth,height=decoder.Frames[0].PixelHeight;
  if(decoder is GifBitmapDecoder&&decoder.Metadata is BitmapMetadata info){
   width=Convert.ToInt32(info.GetQuery("/logscrdesc/Width")??width);
   height=Convert.ToInt32(info.GetQuery("/logscrdesc/Height")??height);
   if(width is <1 or >4096||height is <1 or >4096||(long)width*height*decoder.Frames.Count>maximumPixels)
    throw new InvalidDataException("GIF слишком большой для предпросмотра. Уменьшите разрешение или число кадров.");
  }
  return(width,height);
 }
}

public static class PresetRandomizer
{
 static readonly (string Background,string Text,string Accent,string Border)[] Palettes={
  ("#172B35","#ECF7F8","#66D6CE","#428680"),("#272236","#F5F0FF","#C0A4EE","#8973AC"),
  ("#F3E8DC","#3A2B25","#CB8053","#A97F69"),("#E6EDF4","#253647","#76ACD9","#91A7BB"),
  ("#20382C","#EDF3E6","#ADCA79","#668D67"),("#F5E4E8","#482B36","#D484A0","#B792A1"),
  ("#151D27","#ECF0F9","#7DA3EB","#526990"),("#EDEBE3","#33362F","#9CA36E","#A3A38B")
 };
 public static WidgetPreset Create(Random random,IEnumerable<string>? fontFamilies=null,IEnumerable<string>? mediaPaths=null,WidgetPreset? basis=null){
  T Pick<T>(T[] values)=>values[random.Next(values.Length)];double Between(double min,double max)=>Math.Round(min+random.NextDouble()*(max-min),2);
  var available=(fontFamilies??new[]{"Onest","Segoe UI","Arial","Consolas"}).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2000).ToArray();
  // Symbol and decorative fonts can make transport information unreadable; random choices use known text families.
  var fonts=new[]{"Onest","Segoe UI","Arial","Calibri","Verdana","Tahoma","Consolas","Trebuchet MS"}.Where(x=>available.Contains(x,StringComparer.OrdinalIgnoreCase)).ToArray();if(fonts.Length==0)fonts=new[]{"Onest"};
  var media=(mediaPaths??Array.Empty<string>()).Where(PresetStore.ValidMediaPath).Distinct().Prepend("").ToArray();
  var previous=basis?.Copy();previous?.Normalize();bool locked=previous?.Elements.Values.Any(e=>e.Locked)==true;
  var result=locked?previous!:PresetThemes.Create(Pick(PresetThemes.Names));result.Id="";result.Name="Случайный пресет";
  if(previous!=null&&!locked)result.LayerOrder=previous.LayerOrder;
  var palettes=locked?Palettes.Where(p=>previous!.Elements.Where(e=>e.Value.Locked&&previous.IsElementVisible(e.Key)&&e.Key is not ("cover" or "volume" or "progress")).All(e=>Contrast(p.Background,previous.EffectiveStyle(e.Key).TextColor)>=(e.Key is "title" or "artist" or "time"?4.5:3))).ToArray():Palettes;
  var palette=Pick(palettes.Length>0?palettes:Palettes);
  if(locked&&palettes.Length==0){palette.Background=previous!.BackgroundColor;palette.Text=Contrast(palette.Background,"#252831")>=4.5?"#252831":"#F9F7F0";}
  result.BackgroundColor=palette.Background;result.TextColor=palette.Text;result.AccentColor=palette.Accent;result.BorderColor=palette.Border;
  result.FontFamily=Pick(fonts);result.FontSize=random.Next(14,19);result.FontWeight=Pick(new[]{500,600,700});result.Opacity=Between(.94,1);result.BorderWidth=Pick(new[]{0d,1d,2d});
  result.MediaPath=random.Next(4)==0?Pick(media):"";result.MediaFit=Pick(WidgetPreset.Fits);result.MediaOpacity=Between(.08,.2);result.MediaX=Between(.25,.75);result.MediaY=Between(.25,.75);
  if(locked){
   // Keep the user's arrangement as a whole when anchors are locked. This avoids moving free controls on top of anchors.
   // If their manual arrangement already has overlaps, a random palette must not silently rewrite it.
   foreach(string id in WidgetPreset.ElementIds)if(previous!.Elements[id].Locked)result.Elements[id]=previous.Elements[id].Copy();
   result.Normalize();return result;
  }
  result.Shape=Pick(new[]{"rounded","rectangle","cut","square","ellipse","circle","pill"});
  bool round=result.Shape is "ellipse" or "circle" or "pill";
  if(result.Shape is "circle" or "square"){result.Width=result.Height=random.Next(390,421);}
  else if(round){result.Width=random.Next(460,581);result.Height=random.Next(330,391);}
  else{result.Width=random.Next(360,521);result.Height=random.Next(240,301);}
  result.CornerRadius=result.Shape=="rectangle"?0:random.Next(12,29);
  result.LastFreeWidth=result.Width;result.LastFreeHeight=result.Shape=="circle"?Math.Round(result.Width*.65):result.Height;
  result.ShowControls=true;result.ShowTime=true;result.ShowProgress=true;result.ShowCover=random.Next(5)!=0;result.ShowArtist=random.Next(5)!=0;
  result.Elements.Clear();
  double x=round?result.Width*.16:20,y=round?result.Height*.16:18,w=round?result.Width*.68:result.Width-40,h=round?result.Height*.68:result.Height-36;
  // Vertical groups always follow the same reading order. Only proportions and cover orientation vary.
  double utility=22,topGap=10,infoHeight=Math.Min(72,Math.Max(58,h-142)),controls=30,groupGap=12,track=18,footer=18;
  double used=utility+topGap+infoHeight+groupGap+controls+groupGap+track+8+footer;
  y+=(h-used)/2;
  void Put(string id,double px,double py,double width,double height,bool visible=true){result.Elements[id]=new PresetElement{X=Math.Round(px,2),Y=Math.Round(py,2),Width=Math.Round(width,2),Height=Math.Round(height,2),Visible=visible};}
  double utilityStart=x+w-(utility*5+7*4);int i=0;foreach(string id in new[]{"clickthrough","pin","lock","hide","menu"})Put(id,utilityStart+i++*(utility+7),y,utility,utility);
  double infoY=y+utility+topGap,cover=Math.Min(infoHeight,64);result.CoverSize=cover;bool artRight=random.Next(2)==0;
  Put("cover",artRight?x+w-cover:x,infoY,cover,cover,result.ShowCover);
  double textX=x+(result.ShowCover&&!artRight?cover+12:0),textWidth=w-(result.ShowCover?cover+12:0);
  Put("title",textX,infoY+5,textWidth,26);Put("artist",textX,infoY+35,textWidth,22,result.ShowArtist);
  double controlsY=infoY+infoHeight+groupGap,controlsX=x+(w-(controls*4+12*3))/2;i=0;
  foreach(string id in new[]{"previous","play","next","repeat"})Put(id,controlsX+i++*(controls+12),controlsY,controls,controls);
  double trackY=controlsY+controls+groupGap;Put("progress",x,trackY,w-92,track);Put("time",x+w-80,trackY,80,track);Put("volume",x,trackY+track+8,w,footer);
  result.Normalize();return result;
 }
 static double Contrast(string a,string b){double L(string color){double C(int offset){double value=Convert.ToInt32(color.Substring(offset,2),16)/255d;return value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4);}return .2126*C(1)+.7152*C(3)+.0722*C(5);}double x=L(a),y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
}
internal static class PresetPaths
{
 public static void ValidateId(string value){if(value==null||!Regex.IsMatch(value,@"\A[a-z0-9][a-z0-9-]{0,63}\z"))throw new InvalidDataException("Недопустимое имя виджета.");}
 public static string Child(string root,string relative){if(Path.IsPathRooted(relative))throw new IOException("Путь должен быть относительным.");string parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar;string result=Path.GetFullPath(Path.Combine(parent,relative));if(!result.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь выходит за пределы папки пресетов.");return result;}
 public static void RejectLinks(string path){for(string? part=Path.GetFullPath(path);part!=null;part=Path.GetDirectoryName(part)){if((File.Exists(part)||Directory.Exists(part))&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки в папке пресетов не поддерживаются.");}}
}
