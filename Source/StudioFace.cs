using System;
using System.Windows;
using System.Windows.Media;

namespace MiniPlayer;

// Coordinates refer to the compact face, in device-independent pixels.
sealed record StudioFace(int Width,int Height,string Paper,string Ink,string Key,
 Rect Scene,Point Title,int TextWidth,Point Artwork,Point Controls,Rect Timeline,Point Clock,int KeyWidth,int Radius){
 public bool SceneIsBackground=>Scene==new Rect(0,0,Width,Height);
 static StudioFace BackgroundFace(string paper,string ink,string key,int radius)=>
  new(280,180,paper,ink,key,new(0,0,280,180),new(16,44),248,new(),new(16,100),new(16,142,248,12),new(16,159),32,radius);
 public static StudioFace For(string theme)=>theme switch{
  "Кофе"=>BackgroundFace("#78513B","#FFF2DD","#AA8267",12),
  "Лаванда"=>BackgroundFace("#76617F","#FFF5FF","#B9A1C7",12),
  "Лес"=>BackgroundFace("#173A30","#F0F4E7","#6B917A",12),
  "Minecraft"=>BackgroundFace("#4C5140","#FFF3D9","#8C9D70",2),
  "Ночной океан"=>BackgroundFace("#102638","#F2F4F3","#4E7689",12),
  "Персик"=>new(318,192,"#FFEBDA","#62332B","#FFD0AA",new(174,32,144,109),new(9,42),162,new(5,77),new(76,145),new(79,103,91,15),new(81,123),45,16),
  "Ретро"=>BackgroundFace("#D7C4A3","#3E3027","#B7A181",8),
  "Терминал"=>BackgroundFace("#0B2018","#D2EBC8","#345E45",4),
  "Стекло"=>new(337,164,"#CBE4EF","#193D55","#EDF8FC",new(0,28,126,128),new(128,42),202,new(18,54),new(128,101),new(128,138,193,15),new(4,140),47,22),
  "Тёмная"=>new(339,162,"#191A20","#F6EFE9","#BCBAC6",new(115,74,215,43),new(115,33),215,new(7,18),new(7,128),new(178,128,150,15),new(258,147),49,5),
  _=>new(351,143,"#F9F7F0","#252831","#E6E4DC",new(0,0,119,143),new(128,35),211,new(19,34),new(129,83),new(130,120,146,14),new(279,121),44,21)
 };
}

// Original vector artwork. The illustration is independent of the media thumbnail.
sealed class StudioArt:FrameworkElement{
 readonly string theme;
 public StudioArt(string theme){this.theme=theme;IsHitTestVisible=false;ClipToBounds=true;}
 protected override void OnRender(DrawingContext d){
  d.PushTransform(new ScaleTransform(ActualWidth/160,ActualHeight/100));
  Brush B(string c)=>PlayerBrush(c);
  Brush G(string a,string b,double angle=90)=>new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a),(Color)ColorConverter.ConvertFromString(b),angle);
  void Rect(double x,double y,double w,double h,Brush b,double r=0)=>d.DrawRoundedRectangle(b,null,new Rect(x,y,w,h),r,r);
  void E(double x,double y,double rx,double ry,Brush b)=>d.DrawEllipse(b,null,new Point(x,y),rx,ry);
  void P(string path,string c)=>d.DrawGeometry(B(c),null,Geometry.Parse(path));
  void Line(double x,double y,double u,double v,string c,double width)=>d.DrawLine(new Pen(B(c),width),new Point(x,y),new Point(u,v));
  var rnd=new Random(31);
  switch(theme){
   case "Кофе":
    Rect(0,0,160,100,G("#8E674E","#C6A47F",15));
    E(129,72,61,34,B("#75513E"));E(126,68,59,34,G("#D7BA98","#B59472",30));
    E(127,67,51,26,B("#A28162"));
    E(165,57,15,12,B("#E8D5B7"));E(165,57,9,7,B("#967050"));
    E(123,58,43,27,B("#F7E7CD"));E(123,56,37,21,G("#90522F","#BC8451",15));
    for(int i=0;i<5;i++){
     d.PushTransform(new TranslateTransform(123,44+i*4));d.PushTransform(new ScaleTransform(1-i*.13,.65));
     P("M 0,8 Q -20,3 -18,-4 Q -9,-4 0,4 Q 8,-4 18,-4 Q 20,3 0,8 Z","#F8E4B9");d.Pop();d.Pop();
    }
    Line(123,40,123,72,"#F8E5C0",1);break;
   case "Лаванда":
    Rect(0,0,160,100,G("#B69CCB","#F0CBB5"));E(125,16,7,7,B("#FFF0CD"));
    P("M0,53 Q40,33 79,49 T160,46 L160,100 H0Z","#AF9CBA");P("M0,64 Q64,43 160,58 V100 H0Z","#9884A4");
    Rect(0,55,160,45,G("#ACA2B7","#746984"));break;
   case "Лес":
    Rect(0,0,160,100,G("#71968A","#163C30"));
    P("M0,44 L36,19 60,32 95,10 160,47 V100 H0Z","#607F70");
break;
   case "Minecraft":
    Rect(0,0,160,100,G("#B298A8","#EDC49B"));Rect(128,7,13,13,B("#FFE4A6"));
    Rect(87,18,19,3,B("#30FFF3DB"));Rect(83,21,29,3,B("#30FFF3DB"));
break;
   case "Ночной океан":
    Rect(0,0,160,100,G("#101C30","#415365"));E(128,15,8,8,B("#F5DFB6"));Rect(0,53,160,47,G("#3C5667","#0C2434"));

    for(int i=0;i<34;i++){double x=rnd.Next(160),y=rnd.Next(1,42);E(x,y,.5,.5,B("#D8DDDF"));}break;
   case "Персик":break; // The fruit and its shadow are drawn together in SceneMotion.
   case "Ретро":
    Rect(0,0,160,100,G("#D9C5A0","#B7A184",30));Rect(4,4,152,92,B("#E8D8B8"),7);
    for(int i=0;i<3;i++)Rect(75,17+i*4,85,3,B(new[]{"#A95041","#CD7B4E","#E9B66A"}[i]));
    Rect(73,39,91,37,B("#625B4E"),17);
    foreach(int x in new[]{90,145}){
     E(x,57,13,13,B("#E5D8BE"));E(x,57,7,7,B("#6A6050"));
     for(int i=0;i<6;i++){double a=i*Math.PI/3;E(x+9*Math.Cos(a),57+9*Math.Sin(a),1.5,1.5,B("#8C7B63"));}
    }
    Rect(105,51,27,12,B("#A58B65"),2);
    P("M75,100 L83,87 H153 L160,100Z","#B09B7B");
    foreach(var point in new[]{new Point(8,8),new Point(151,8),new Point(8,91),new Point(151,91)})E(point.X,point.Y,1.5,1.5,B("#9C896C"));
    break;
   case "Терминал":
    Rect(0,0,160,100,G("#0D261A","#07150F",30));
    d.DrawRoundedRectangle(new RadialGradientBrush(Color.FromArgb(45,133,197,125),Colors.Transparent),new Pen(B("#356546"),.6),new Rect(2,2,156,96),8,8);
    break;
   case "Стекло":
    Rect(0,0,160,100,G("#8FAFCB","#EEF8F7",40),14);
    foreach(var (x,y,r) in new[]{(37,29,26),(108,68,39),(137,18,17)}){E(x,y,r,r,G("#ECFEFD","#91B8D7",55));E(x-r*.29,y-r*.4,r*.19,r*.09,B("#E6F7FF"));}break;
   case "Тёмная":
    Rect(0,0,160,100,B("#24252C"),4);
    break;
   default:
    Rect(0,0,160,100,B("#ECE8DF"),18);
break;
  }
  d.Pop();
 }
 static Brush PlayerBrush(string c)=>(Brush)new BrushConverter().ConvertFromString(c)!;
}

sealed class SceneMotion:FrameworkElement{
 readonly string theme;
 public double Phase;
 public bool UsesAudio=>theme is "Терминал" or "Светлая" or "Тёмная" or "Minecraft";
 public double CoverRadius {get;set;}
 double pulse;
 static readonly Pen Ring=new(new SolidColorBrush(Color.FromRgb(173,169,158)),1);
 readonly float[] history=new float[28];
 const double DarkColumnSeconds=.20;
 readonly float[] darkPeaks=new float[29];
 readonly double[] darkHeights=new double[29];
 double previousFrame=-1, columnFraction;

 public void AdvanceDarkTrail(double seconds){
  if(previousFrame<0){previousFrame=seconds;return;}
  double elapsed=Math.Max(0,seconds-previousFrame);
  previousFrame=seconds;
  // Carry the fractional distance through each shift so a column stays in motion.
  double distance=columnFraction+elapsed/DarkColumnSeconds;
  int steps=(int)Math.Min(Math.Floor(distance),darkPeaks.Length);
  columnFraction=distance-Math.Floor(distance);
  if(steps>0){
   int keep=darkPeaks.Length-steps;
   Array.Copy(darkPeaks,steps,darkPeaks,0,keep);Array.Clear(darkPeaks,keep,steps);
   Array.Copy(darkHeights,steps,darkHeights,0,keep);Array.Clear(darkHeights,keep,steps);
  }
  double blend=1-Math.Exp(-elapsed/.055);
  for(int i=0;i<darkHeights.Length;i++)darkHeights[i]+=(darkPeaks[i]-darkHeights[i])*blend;
 }
 readonly double[] terminalBars=new double[7];
 public void AdvanceTerminal(double dt){
  double blend=1-Math.Exp(-Math.Max(0,dt)/.095);
  for(int i=0;i<7;i++)terminalBars[i]+=(history[history.Length-7+i]-terminalBars[i])*blend;
 }
 void DrawTerminal(DrawingContext d){
  var scanline=new Pen(new SolidColorBrush(Color.FromArgb(28,2,8,4)),.55);
  for(int row=0;row<34;row++)d.DrawLine(scanline,new Point(0,row*3),new Point(160,row*3));
  for(int i=0;i<7;i++){
   double h=1+7*terminalBars[i];
   d.PushOpacity(.35);d.DrawRectangle(TerminalPhosphor,null,new Rect(127+i*3,86-h,1.5,h));d.Pop();
  }
 }
 static readonly Brush TerminalPhosphor=TerrainBrush(155,203,150);
 public void Sample(float level,double sensitivity){
  float strength=(float)Math.Min(1,Math.Sqrt(level)*sensitivity);
  if(theme=="Светлая"){pulse+=(strength-pulse)*(strength>pulse?.85:.24);return;}
  if(theme=="Тёмная"){
   darkPeaks[^1]=Math.Max(darkPeaks[^1],strength);return;
  }
  Array.Copy(history,1,history,0,history.Length-1);history[^1]=strength;
 }
 public void Silence(){Array.Clear(terminalBars);previousFrame=-1;columnFraction=0;Array.Clear(darkPeaks);Array.Clear(darkHeights);pulse=0;Array.Clear(history);InvalidateVisual();}
 static readonly Pen Water=new(new SolidColorBrush(Color.FromArgb(196,227,201,156)),.85);
 static readonly Pen Stem=new(new SolidColorBrush(Color.FromRgb(104,113,83)),.8);
 static readonly DrawingGroup[] LavenderSprigs=CreateSprigs();
 static DrawingGroup[] CreateSprigs(){
  var sprites=new DrawingGroup[3];
  var petals=new[]{Color.FromRgb(161,131,183),Color.FromRgb(182,149,203),Color.FromRgb(197,167,214)};
  for(int i=0;i<sprites.Length;i++){
   var sprig=new DrawingGroup();using(var pen=sprig.Open()){
    var flower=new SolidColorBrush(petals[i]);
    pen.DrawLine(Stem,new Point(0,0),new Point(0,-12));
    pen.DrawLine(Stem,new Point(0,-3),new Point(-1.2,-5.5));
    for(int j=0;j<5;j++)pen.DrawEllipse(flower,null,new Point((j%2==0?-.42:.42),-8.2-j*.93),.65,.97);
   }
   sprig.Freeze();sprites[i]=sprig;
  }
  return sprites;
 }
 readonly record struct Sprig(double X,double Y,double Scale,double Lean,int Tint);
 static readonly Sprig[] Field=PlantLavender();
 static Sprig[] PlantLavender(){
  var random=new Random(7103);
  var plants=new System.Collections.Generic.List<Sprig>();
  // Guarantee coverage locally, then scatter small clumps around each anchor.
  for(int band=0;band<11;band++){
   double near=band/11d,far=(band+1)/11d;
   double top=55+52*Math.Pow(near,1.5),bottom=55+52*Math.Pow(far,1.5);
   double cell=4+near*5;
   double start=-cell*random.NextDouble()-2;
   for(double left=start;left<163;left+=cell){
    double x=left+cell*(.12+.76*random.NextDouble());
    double y=top+(bottom-top)*random.NextDouble();
    double size=(.20+.88*near)*(.84+.34*random.NextDouble());
    int stems=random.NextDouble()<.45?1:random.Next(2,4);
    for(int i=0;i<stems;i++){
     double spread=(i-(stems-1)*.5)*size;
     plants.Add(new Sprig(x+spread,y+size*random.NextDouble(),size*(.85+.30*random.NextDouble()),
      spread*8+(random.NextDouble()-.5)*11,random.Next(3)));
    }
   }
  }
  plants.Sort((a,b)=>a.Y.CompareTo(b.Y));return plants.ToArray();
 }
 readonly record struct Fir(double X,double Base,double Scale,int Layer);
 static readonly Fir[] Grove=PlantForest();
 static Fir[] PlantForest(){
  var trees=new Fir[48];var random=new Random(31);
  for(int layer=0;layer<3;layer++)for(int i=0;i<16;i++){
   double x=i*12+random.Next(-5,5),tip=34+layer*22+random.Next(-8,10),height=18+layer*8+random.Next(10);
   trees[layer*16+i]=new Fir(x,tip+height,height/29,layer);
  }
  return trees;
 }
 static readonly Geometry FirOutline=Geometry.Parse("M0,-29 L-5,-19 -2,-20 -8,-11 -4,-12 -11,0 H11 L4,-12 8,-11 2,-20 5,-19 Z");
 static readonly Brush[] PineInks={TerrainBrush(120,152,139),TerrainBrush(71,110,96),TerrainBrush(33,76,64)};
 static readonly Brush Cloud=new RadialGradientBrush(Color.FromArgb(116,236,246,237),Color.FromArgb(0,236,246,237));
 static readonly Brush Halo=new RadialGradientBrush(Color.FromArgb(165,255,232,146),Color.FromArgb(0,255,232,146));
 static readonly DrawingGroup PeachSkin=CreatePeachSkin();
 static readonly Geometry PeachLeaf=Geometry.Parse("M76,38 Q70,13 111,9 Q104,30 76,38Z");
 static readonly Brush LeafInk=TerrainBrush(105,138,89), PeachShadow=TerrainBrush(236,193,162);
 static readonly Pen LeafVein=new(TerrainBrush(144,164,105),.75);
 static DrawingGroup CreatePeachSkin(){
  var skin=new DrawingGroup();
  using(var dc=skin.Open()){
   dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(255,203,137),Color.FromRgb(234,120,103),50),null,new Point(65,55),36,30);
   dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(255,218,150),Color.FromRgb(233,139,114),60),null,new Point(95,53),36,32);
   var rng=new Random(31);var freckle=TerrainBrush(227,145,112);
   for(int i=0;i<35;i++){double a=rng.NextDouble()*Math.PI*2,r=rng.NextDouble()*23;dc.DrawEllipse(freckle,null,new Point(97+Math.Cos(a)*r,55+Math.Sin(a)*r),.65,.6);}
   dc.DrawGeometry(TerrainBrush(145,102,71),null,Geometry.Parse("M76,39 Q71,20 59,15 L62,12 Q76,18 80,36Z"));
  }
  skin.Freeze();return skin;
 }
 void DrawPeach(DrawingContext d){
  double phase=Phase*1.12,roll=Math.Sin(phase);
  d.DrawEllipse(PeachShadow,null,new Point(88+2.4*roll,83),49+1.8*roll,7.5-.5*roll);
  d.PushTransform(new TranslateTransform(2.6*roll,0));
  d.PushTransform(new RotateTransform(5.2*roll,80,83));
  d.DrawDrawing(PeachSkin);
  for(int i=0;i<3;i++){d.PushOpacity(.18+.10*Math.Sin(phase-.5+i));d.DrawEllipse(Brushes.White,null,new Point(101+i*4,41+i*2.6),1.5,.9);d.Pop();}
  d.PushTransform(new RotateTransform(6*Math.Sin(phase-.65)+2*Math.Sin(Phase*1.8),76,38));
  d.DrawGeometry(LeafInk,null,PeachLeaf);d.DrawLine(LeafVein,new Point(79,34),new Point(103,15));
  d.Pop();d.Pop();d.Pop();
 }
 void DrawForest(DrawingContext d){
  for(int i=0;i<3;i++){
   double x=(Phase*(.95+i*.18)+i*83+70)%300-70,y=17+i*7;
   d.DrawEllipse(Cloud,null,new Point(x,y),32+i*4,3.6);
   d.DrawEllipse(Cloud,null,new Point(x+10,y-2),16,3.2);
  }
  foreach(var tree in Grove){
   double wind=.02*Math.Sin(Phase*.55-tree.X*.045)+.008*Math.Sin(Phase*.9+tree.Base*.09);
   double scale=tree.Scale;
   d.PushTransform(new MatrixTransform(scale,0,wind*scale*(.65+tree.Layer*.2),scale,tree.X,tree.Base));
   d.DrawGeometry(PineInks[tree.Layer],null,FirOutline);d.Pop();
  }
  for(int i=0;i<4;i++){
   double a=Phase*(.49+i*.019)+i*2.4;
   var pos=new Point(94+i*18+2*Math.Cos(a),53+(i*17)%39+2*Math.Sin(a*.83));
   d.PushOpacity(.10+.45*Math.Pow(.5+.5*Math.Sin(a*1.57),2));
   d.DrawEllipse(Halo,null,pos,4,2.4);d.DrawEllipse(Lamp,null,pos,.96,.57);d.Pop();
  }
 }
 void DrawLavender(DrawingContext d){
  foreach(var plant in Field){
   double angle=plant.Lean+3*Math.Sin(Phase*.55-plant.X*.039-plant.Y*.035)+Math.Sin(Phase*.8+plant.X*.065);
   double radians=angle*Math.PI/180,cs=plant.Scale*Math.Cos(radians),sn=plant.Scale*Math.Sin(radians);
   d.PushTransform(new MatrixTransform(cs,sn,-sn,cs,plant.X,plant.Y));
   d.DrawDrawing(LavenderSprigs[plant.Tint]);d.Pop();
  }
 }
 static SolidColorBrush TerrainBrush(byte r,byte g,byte b){var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush;}
 static readonly Brush Ridge=TerrainBrush(82,102,83), RidgeGrass=TerrainBrush(140,155,100),
  Dirt=TerrainBrush(104,80,58), Grass=TerrainBrush(125,154,67), Stone=TerrainBrush(130,99,70),
  Trunk=TerrainBrush(112,80,54), Leaves=TerrainBrush(83,106,54), Crown=TerrainBrush(130,155,72);
 void DrawHills(DrawingContext d){
  void Block(Brush fill,double x,double y,double w,double h)=>d.DrawRectangle(fill,null,new Rect(x,y,w,h));
  // Average adjacent samples so a beat lifts a hillside across several blocks.
  double HeightSignal(int i)=>(history[i]+2*history[i+1]+3*history[i+2]+2*history[i+3]+history[i+4])/9;
  double treeBase=0;
  for(int i=0;i<20;i++){
   double top=48+(int)(10*Math.Sin(i*.48))-Math.Round(HeightSignal(i)*2);
   Block(Ridge,i*8,top,8,100-top);Block(RidgeGrass,i*8,top,8,5);
   if(i==15)treeBase=top;
  }
  Block(Trunk,125,treeBase-18,4,21);Block(Leaves,116,treeBase-32,22,19);Block(Crown,120,treeBase-40,14,13);
  for(int i=0;i<20;i++){
   double top=68+(int)(11*Math.Sin(i*.42+1))-Math.Round(HeightSignal(i+3)*3);
   Block(Dirt,i*8,top,8,100-top);Block(Grass,i*8,top,8,7);Block(Stone,i*8+2,top+10,3,4);
  }
 }
 static readonly Brush Signal=new SolidColorBrush(Color.FromRgb(192,165,226));
 public SceneMotion(string theme){this.theme=theme;IsHitTestVisible=false;ClipToBounds=true;}
 static readonly Pen Steam=new(new SolidColorBrush(Color.FromArgb(95,255,242,213)),.9);
 static readonly Brush Lamp=new SolidColorBrush(Color.FromRgb(244,229,147));
 static readonly Pen Spoke=new(new SolidColorBrush(Color.FromRgb(220,201,169)),1.15);
 protected override void OnRender(DrawingContext d){
  if(theme=="Светлая"){
   var center=new Point(ActualWidth/2,ActualHeight/2);
   double outer=Math.Min(center.X,center.Y)-9;
   for(int i=1;i<=5;i++){
    double fraction=i/5d;
    double radius=CoverRadius+1+(outer-CoverRadius-1)*fraction+pulse*(2+5*fraction);
    d.DrawEllipse(null,Ring,center,radius,radius);
   }
   return;
  }
  d.PushTransform(new ScaleTransform(ActualWidth/160,ActualHeight/100));
  if(theme=="Кофе"){
   for(int i=0;i<3;i++){
    var path=new StreamGeometry();double x=111+i*9,drift=1.5*Math.Sin(Phase*.6+i);
    using(var ctx=path.Open()){ctx.BeginFigure(new Point(x,38),false,false);ctx.BezierTo(new Point(x-4+drift,31),new Point(x+5+drift,25),new Point(x+drift,19-i*2+Math.Sin(Phase*.4+i)),true,true);}d.DrawGeometry(null,Steam,path);
   }
  }else if(theme=="Ретро"){
   foreach(int x in new[]{90,145})for(int i=0;i<3;i++){
    double a=Phase*.55+i*2*Math.PI/3;d.DrawLine(Spoke,new Point(x+2*Math.Cos(a),57+2*Math.Sin(a)),new Point(x+6*Math.Cos(a),57+6*Math.Sin(a)));
   }
  }else if(theme=="Лес"){
   DrawForest(d);
  }else if(theme=="Тёмная"){
   const double pitch=160d/28;
   for(int i=0;i<darkHeights.Length;i++){
    double h=Math.Max(2,darkHeights[i]*89);
    d.DrawRoundedRectangle(Signal,null,new Rect((i-columnFraction)*pitch,98-h,3.9,h),1,1);
   }
  }else if(theme=="Minecraft"){
   DrawHills(d);
  }else if(theme=="Ночной океан"){
   for(int i=0;i<60;i++){
    double y=55+i*.7;double ripple=Math.Sin(i*.89+Phase*1.15);
    double x=128+Math.Sin(i*2.399)*(y-52)*.34+ripple*1.1;
    double width=2+(1+Math.Cos(i*.7-Phase))*(1+(y-53)*.09);
    d.PushOpacity(.5+.24*ripple);d.DrawLine(Water,new Point(x-width,y),new Point(x+width,y));d.Pop();
   }
  }else if(theme=="Лаванда"){
   DrawLavender(d);
  }else if(theme=="Персик"){
   DrawPeach(d);
  }else if(theme=="Терминал"){
   DrawTerminal(d);
  }
  d.Pop();
 }
}
