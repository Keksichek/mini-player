using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace WidgetLauncher;
public sealed class ObsPart
{
 public double X{get;set;} public double Y{get;set;} public double Width{get;set;} public double Height{get;set;} public double FontSize{get;set;}=16;public bool Visible{get;set;}=true;
 public ObsPart(){} public ObsPart(double x,double y,double w,double h,double size=16){X=x;Y=y;Width=w;Height=h;FontSize=size;}
}
public sealed class ObsAppearance
{
 public double Width{get;set;}=360;public double Height{get;set;}=106;public double Radius{get;set;}=18;public double Opacity{get;set;}=.97;public double BorderWidth{get;set;}=1;
 public string Shape{get;set;}="rounded";public string Background{get;set;}="#191821";public string Border{get;set;}="#494051";public string Text{get;set;}="#F5F3FA";public string Secondary{get;set;}="#AAA5B9";public string Accent{get;set;}="#C4A9F4";public string Font{get;set;}="Segoe UI";
 public bool GridEnabled{get;set;} public int GridSize{get;set;}=8;
 public string FontId{get;set;}="";
 public static bool ValidFontId(string? id)=>id!=null&&Regex.IsMatch(id,"^[a-f0-9]{64}\\.(ttf|otf)$");
 public double Snap(double value,double min,double max){double step=GridSize;return Math.Clamp(Math.Round(value/step,MidpointRounding.AwayFromZero)*step,Math.Ceiling(min/step)*step,Math.Floor(max/step)*step);}
 public Dictionary<string,ObsPart> Parts{get;set;}=DefaultParts();
 public static readonly string[] Keys={"cover","title","artist","elapsed","duration","bar"};
 public static Dictionary<string,ObsPart> DefaultParts()=>new(){["cover"]=new(12,12,80,80),["title"]=new(104,12,244,22,16),["artist"]=new(104,35,244,20,12),["elapsed"]=new(104,67,90,14,10),["duration"]=new(258,67,90,14,10),["bar"]=new(104,86,244,4)};
 static double Clamp(double n,double min,double max,double fallback)=>double.IsFinite(n)?Math.Clamp(n,min,max):fallback;
 static string Hex(string? s,string fallback)=>s!=null&&Regex.IsMatch(s,"^#[0-9a-fA-F]{6}$")?s:fallback;
 public ObsAppearance Normalize(){
  Width=Clamp(Width,180,900,360);Height=Clamp(Height,90,800,106);
  if(Shape is not ("rounded" or "rectangle" or "circle"))Shape="rounded";
  Radius=Clamp(Radius,0,100,18);Opacity=Clamp(Opacity,0,1,.97);BorderWidth=Clamp(BorderWidth,0,8,1);
  Background=Hex(Background,"#191821");Border=Hex(Border,"#494051");Text=Hex(Text,"#F5F3FA");Secondary=Hex(Secondary,"#AAA5B9");Accent=Hex(Accent,"#C4A9F4");
  if(!new[]{"Segoe UI","Arial","Verdana","Georgia","Consolas"}.Contains(Font))Font="Segoe UI";
  GridSize=Math.Clamp(GridSize,2,32);if(!ValidFontId(FontId))FontId="";
  Parts??=DefaultParts();foreach(var key in Keys){if(!Parts.TryGetValue(key,out var p)||p==null)Parts[key]=p=DefaultParts()[key];p.Width=Clamp(p.Width,key=="bar"?16:12,Width,40);p.Height=Clamp(p.Height,key=="bar"?2:12,Height,20);p.X=Clamp(p.X,0,Width-p.Width,0);p.Y=Clamp(p.Y,0,Height-p.Height,0);p.FontSize=Clamp(p.FontSize,8,48,16);
}return this;
 }
 // Changing grid settings aligns positions only: thin timelines and text boxes keep their size.
 public void AlignToGrid(){Normalize();if(GridEnabled)foreach(var p in Parts.Values){p.X=Snap(p.X,0,Width-p.Width);p.Y=Snap(p.Y,0,Height-p.Height);}}
 public ObsAppearance Copy()=>JsonSerializer.Deserialize<ObsAppearance>(JsonSerializer.Serialize(this))!.Normalize();
 public static ObsAppearance Preset(string kind){
  var a=new ObsAppearance();if(kind=="wide")return a;
  a.Width=kind is "circle" or "square"?300:260;a.Height=kind=="vertical"?320:300;a.Shape=kind=="circle"?"circle":"rounded";
  double w=a.Width;a.Parts=new(){["cover"]=new((w-100)/2,24,100,100),["title"]=new(32,140,w-64,26,18),["artist"]=new(32,173,w-64,22,13),["elapsed"]=new(40,220,80,16,11),["duration"]=new(w-120,220,80,16,11),["bar"]=new(40,244,w-80,5)};return a.Normalize();
 }
}
