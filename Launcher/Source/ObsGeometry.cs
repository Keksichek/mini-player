using System;
using System.Collections.Generic;
using System.Linq;
namespace WidgetLauncher;
// Every gesture is calculated from its starting snapshot, so repeated mouse moves cannot accumulate drift.
public static class ObsGeometry
{
 public static ObsPart Clone(ObsPart p)=>new(p.X,p.Y,p.Width,p.Height,p.FontSize){Visible=p.Visible};
 static double Clamp(double value,double min,double max)=>Math.Clamp(value,min,max);
 static double Step(double value,double min,double max,int step){double low=Math.Ceiling(min/step)*step,high=Math.Floor(max/step)*step;return low<=high?Clamp(Math.Round(value/step,MidpointRounding.AwayFromZero)*step,low,high):Clamp(value,min,max);}
 public static void Move(ObsAppearance a,IReadOnlyDictionary<string,ObsPart> original,double dx,double dy){
  if(original.Count==0)return;double left=original.Values.Min(p=>p.X),top=original.Values.Min(p=>p.Y),right=original.Values.Max(p=>p.X+p.Width),bottom=original.Values.Max(p=>p.Y+p.Height);
  dx=a.GridEnabled?Step(dx,-left,a.Width-right,a.GridSize):Clamp(dx,-left,a.Width-right);dy=a.GridEnabled?Step(dy,-top,a.Height-bottom,a.GridSize):Clamp(dy,-top,a.Height-bottom);
  foreach(var pair in original){var p=a.Parts[pair.Key];p.X=pair.Value.X+dx;p.Y=pair.Value.Y+dy;}
 }
 public static ObsPart Resize(ObsAppearance a,string key,ObsPart p,string handle,double dx,double dy,bool proportional){
  bool west=handle.Contains('w'),east=handle.Contains('e'),north=handle.Contains('n'),south=handle.Contains('s');double minW=key=="bar"?16:12,minH=key=="bar"?2:12;
  double maxW=west?p.X+p.Width:a.Width-p.X,maxH=north?p.Y+p.Height:a.Height-p.Y;
  double w=p.Width+(east?dx:west?-dx:0),h=p.Height+(south?dy:north?-dy:0);
  if(proportional){bool horizontal=(east||west)&&(!(north||south)||Math.Abs((w-p.Width)/p.Width)>=Math.Abs((h-p.Height)/p.Height));
   double scale=horizontal?w/p.Width:h/p.Height,low=Math.Max(minW/p.Width,minH/p.Height),high=Math.Min(maxW/p.Width,maxH/p.Height);
   if(a.GridEnabled){double basis=horizontal?p.Width:p.Height;scale=Step(scale*basis,low*basis,high*basis,a.GridSize)/basis;}else scale=Clamp(scale,low,high);
   w=p.Width*scale;h=p.Height*scale;
  }else{
   if(east||west){double edge=west?p.X+p.Width-w:p.X+w;edge=a.GridEnabled?Step(edge,west?0:p.X+minW,west?p.X+p.Width-minW:a.Width,a.GridSize):Clamp(edge,west?0:p.X+minW,west?p.X+p.Width-minW:a.Width);w=west?p.X+p.Width-edge:edge-p.X;}
   if(north||south){double edge=north?p.Y+p.Height-h:p.Y+h;edge=a.GridEnabled?Step(edge,north?0:p.Y+minH,north?p.Y+p.Height-minH:a.Height,a.GridSize):Clamp(edge,north?0:p.Y+minH,north?p.Y+p.Height-minH:a.Height);h=north?p.Y+p.Height-edge:edge-p.Y;}
  }
  var result=Clone(p);result.Width=w;result.Height=h;result.X=west?p.X+p.Width-w:p.X;result.Y=north?p.Y+p.Height-h:p.Y;
  if(proportional&&key is not ("cover" or "bar"))result.FontSize=Clamp(p.FontSize*w/p.Width,8,48);return result;
 }
 public static void ResizeFrame(ObsAppearance a,ObsAppearance original,string handle,double dx,double dy,bool proportional){
  double w=original.Width+(handle.Contains('e')?dx:handle.Contains('w')?-dx:0),h=original.Height+(handle.Contains('s')?dy:handle.Contains('n')?-dy:0);
  if(proportional){bool horizontal=(handle.Contains('e')||handle.Contains('w'))&&(!(handle.Contains('n')||handle.Contains('s'))||Math.Abs((w-original.Width)/original.Width)>=Math.Abs((h-original.Height)/original.Height));double basis=horizontal?original.Width:original.Height;
   double low=Math.Max(180/original.Width,90/original.Height),high=Math.Min(900/original.Width,800/original.Height),scale=horizontal?w/original.Width:h/original.Height;
   scale=a.GridEnabled?Step(scale*basis,low*basis,high*basis,a.GridSize)/basis:Clamp(scale,low,high);w=original.Width*scale;h=original.Height*scale;
  }else{if(handle.Contains('e')||handle.Contains('w'))w=a.GridEnabled?Step(w,180,900,a.GridSize):Clamp(w,180,900);if(handle.Contains('n')||handle.Contains('s'))h=a.GridEnabled?Step(h,90,800,a.GridSize):Clamp(h,90,800);}
  a.Width=w;a.Height=h;foreach(var pair in original.Parts)a.Parts[pair.Key]=Clone(pair.Value);a.Normalize();
 }
}
