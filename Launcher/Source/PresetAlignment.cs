using System;
using System.Collections.Generic;
using System.Windows;

namespace WidgetLauncher;

internal static class PresetAlignment
{
 internal readonly record struct Result(Point Position,double? VerticalGuide,double? HorizontalGuide);

 // Input coordinates are in widget units. The attraction radius stays constant on screen at every zoom.
 internal static Result Move(WidgetPreset preset,string id,Point requested,double zoom,bool freeMove){
  var item=preset.Elements[id];
  return MoveSelection(preset,new[]{id},new Size(item.Width,item.Height),requested,zoom,freeMove);
 }
 internal static Result MoveSelection(WidgetPreset preset,ICollection<string> selected,Size size,Point requested,double zoom,bool freeMove){
  var x=new Axis(requested.X,size.Width,preset.Width,5/zoom);
  var y=new Axis(requested.Y,size.Height,preset.Height,5/zoom);
  if(freeMove)return new(new Point(x.Position,y.Position),null,null);
  x.MatchCanvas();y.MatchCanvas();
  foreach(var pair in preset.Elements){
   if(selected.Contains(pair.Key)||!preset.IsElementVisible(pair.Key))continue;
   var other=pair.Value;
   x.MatchElement(other.X,other.Width);
   y.MatchElement(other.Y,other.Height);
   if(other.Y+other.Height>y.Origin && other.Y<y.End)x.MeasureGap(other.X,other.Width);
   if(other.X+other.Width>x.Origin && other.X<x.End)y.MeasureGap(other.Y,other.Height);
  }
  x.MatchGap();y.MatchGap();
  return new(new Point(x.Position,y.Position),x.Guide,y.Guide);
 }

 struct Axis
 {
  readonly double size,canvas,radius;
  internal readonly double Origin;
  internal double End=>Origin+size;
  internal double Position {get;private set;}
  internal double? Guide {get;private set;}
  double bestDistance,precedingEnd,followingStart;

  internal Axis(double requested,double size,double canvas,double radius){
   this.size=size;this.canvas=canvas;this.radius=radius;
   Origin=Math.Clamp(requested,0,Math.Max(0,canvas-size));
   Position=Origin;Guide=null;
   bestDistance=double.PositiveInfinity;precedingEnd=double.NegativeInfinity;followingStart=double.PositiveInfinity;
  }
  void Match(double target,double offset){
   double position=target-offset,distance=Math.Abs(position-Origin);
   if(position<0||position+size>canvas||distance>radius||distance>=bestDistance)return;
   Position=position;Guide=target;bestDistance=distance;
  }
  internal void MatchCanvas(){
   Match(canvas/2,size/2);Match(0,0);Match(canvas,size);
  }
  internal void MatchElement(double start,double length){
   double end=start+length;
   Match(start+length/2,size/2);
   Match(start,0);Match(end,size);Match(start,size);Match(end,0);
  }
  internal void MeasureGap(double start,double length){
   if(start+length<=Origin)precedingEnd=Math.Max(precedingEnd,start+length);
   if(start>=End)followingStart=Math.Min(followingStart,start);
  }
  internal void MatchGap(){
   if(double.IsFinite(precedingEnd)&&double.IsFinite(followingStart))Match((precedingEnd+followingStart)/2,size/2);
  }
 }
}
