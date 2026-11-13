using System;
using System.Windows;
using System.Windows.Media;

namespace YandexMiniPlayer;

// Coordinates refer to the compact face, in device-independent pixels.
sealed record StudioFace(int Width,int Height,string Paper,string Ink,string Key,string Font,
 Rect Scene,Point Title,int TextWidth,Point Artwork,Point Controls,Rect Timeline,Point Clock,int KeyWidth,int Radius){
 public static StudioFace For(string theme)=>theme switch{
  "Кофе"=>new(364,162,"#F1DFC1","#422C22","#DAB990","Georgia",new(0,0,144,145),new(208,33),150,new(135,14),new(150,88),new(148,123,201,16),new(152,141),42,12),
  "Лаванда"=>new(296,224,"#F3EDF7","#483653","#E4D2F0","Georgia",new(0,0,296,103),new(87,109),202,new(9,77),new(73,174),new(6,151,282,16),new(9,201),43,18),
  "Лес"=>new(350,178,"#173A35","#EDF5E3","#B8CAB4","Georgia",new(0,0,139,178),new(149,44),197,new(35,64),new(149,113),new(148,86,192,18),new(151,150),46,5),
  "Minecraft"=>new(348,173,"#3A3731","#FFF2D4","#B8AE96","Consolas",new(0,0,348,76),new(86,78),252,new(7,73),new(151,134),new(71,117,265,14),new(8,144),55,0),
  "Ночной океан"=>new(364,169,"#10283D","#E5F1FA","#9ABACD","Georgia",new(193,0,171,169),new(8,38),178,new(199,34),new(7,99),new(7,137,181,14),new(9,151),47,20),
  "Персик"=>new(318,192,"#FFEBDA","#62332B","#FFD0AA","Segoe UI",new(174,32,144,109),new(9,42),162,new(5,77),new(76,145),new(79,103,91,15),new(81,123),45,16),
  "Ретро"=>new(322,205,"#DDCEAA","#413B32","#B8ADA1","Consolas",new(15,14,168,105),new(9,123),296,new(216,37),new(136,177),new(9,163,301,10),new(5,183),53,3),
  "Терминал"=>new(284,205,"#273431","#BFECC1","#B5C2AC","Consolas",new(0,28,284,112),new(18,46),193,new(209,48),new(15,160),new(17,92,180,18),new(19,116),58,3),
  "Стекло"=>new(337,164,"#CBE4EF","#193D55","#EDF8FC","Segoe UI",new(0,28,126,128),new(128,42),202,new(18,54),new(128,101),new(128,138,193,15),new(4,140),47,22),
  "Тёмная"=>new(339,162,"#191A20","#F6EFE9","#BCBAC6","Segoe UI",new(115,74,215,43),new(115,33),215,new(7,18),new(7,128),new(178,128,150,15),new(258,147),49,5),
  _=>new(351,143,"#F9F7F0","#252831","#E6E4DC","Georgia",new(0,0,119,143),new(128,35),211,new(19,34),new(129,83),new(130,120,146,14),new(279,121),44,21)
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
    Rect(0,0,160,100,G("#D0AA78","#8D6146",35),12);
    E(79,57,57,33,B("#926C4E"));E(76,53,58,33,B("#E5C9A7"));E(78,52,51,27,B("#BC9974"));
    E(122,49,15,11,B("#F7E9D2"));E(123,48,9,6,B("#B3926B"));E(75,48,43,25,B("#FCF1D9"));E(75,47,38,20,G("#8F4829","#C38751",20));
    for(int i=0;i<5;i++){double y=36+i*4;d.PushTransform(new TranslateTransform(75,y));d.PushTransform(new ScaleTransform(1-i*.12,.65));P("M 0,8 Q -20,3 -18,-4 Q -9,-4 0,4 Q 8,-4 18,-4 Q 20,3 0,8 Z","#FAE7BA");d.Pop();d.Pop();}
    Line(75,32,75,63,"#FFEFCF",1.1);break;
   case "Лаванда":
    Rect(0,0,160,100,G("#BDAAD9","#F6D8C7"),5);E(111,27,7,12,B("#FFF0CD"));
    P("M0,53 Q40,33 79,49 T160,46 L160,100 H0Z","#AF9CBA");P("M0,64 Q64,43 160,58 V100 H0Z","#9884A4");
    Rect(0,55,160,45,G("#ACA2B7","#746984"));break;
   case "Лес":
    Rect(0,0,160,100,G("#B3CBBD","#4B756B"),7);
    P("M0,44 L36,19 60,32 95,10 160,47 V100 H0Z","#829F94");
    for(int layer=0;layer<3;layer++)for(int i=0;i<16;i++){double x=i*12+rnd.Next(-5,5),y=34+layer*22+rnd.Next(-8,10),h=18+layer*8+rnd.Next(10);d.PushTransform(new TranslateTransform(x,y));d.PushTransform(new ScaleTransform(h/29,h/29));P("M0,0 L-5,10 -2,9 -8,18 -4,17 -11,29 H11 L4,17 8,18 2,9 5,10Z",new[]{"#78988B","#476E60","#214C40"}[layer]);d.Pop();d.Pop();}break;
   case "Minecraft":
    Rect(0,0,160,100,G("#B890AD","#F8C995"));Rect(122,12,12,23,B("#FFE7AE"));
break;
   case "Ночной океан":
    Rect(0,0,160,100,G("#111B34","#475367"),7);E(85,28,12,7.5,B("#FFDFAC"));Rect(0,53,160,47,G("#4F616E","#162E42"));

    for(int i=0;i<34;i++){double x=rnd.Next(160),y=rnd.Next(1,42);E(x,y,.5,.5,B("#D8DDDF"));}break;
   case "Персик":
    E(91,81,52,9,B("#ECC1A2"));E(65,55,36,30,G("#FFCB89","#EA7867",50));E(95,53,36,32,G("#FFDA96","#E98B72",60));
    P("M76,38 Q70,13 111,9 Q104,30 76,38Z","#698A59");P("M76,39 Q71,20 59,15 L62,12 Q76,18 80,36Z","#916647");
    for(int i=0;i<35;i++){double a=rnd.NextDouble()*Math.PI*2,r=rnd.NextDouble()*23;E(97+Math.Cos(a)*r,55+Math.Sin(a)*r,.65,.6,B("#E39170"));}break;
   case "Ретро":
    Rect(0,0,160,100,G("#534B4A","#2E3232"),8);Rect(7,8,146,76,B("#D2AE78"),4);
    for(int i=0;i<3;i++)Rect(7,15+i*5,146,4,B(new[]{"#AF5042","#DB794C","#F2B663"}[i]));
    Rect(20,35,120,39,B("#403C39"),10);
    foreach(int x in new[]{43,117}){E(x,54,15,15,B("#E9D8BB"));E(x,54,8,8,B("#504B46"));for(int i=0;i<6;i++){double a=i*Math.PI/3;E(x+9*Math.Cos(a),54+9*Math.Sin(a),2,2,B("#6A5D51"));}}
    Rect(62,46,36,15,B("#906347"),2);P("M29,100 L35,83 H125 L132,100Z","#71645A");break;
   case "Терминал":
    Rect(0,0,160,100,B("#4D554C"),6);Rect(3,4,154,92,G("#111E19","#263B2D"),5);
    for(int i=0;i<7;i++)Rect(122+i*4,81-rnd.Next(5),2,8,B("#668D6B"));break;
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
 public bool UsesAudio=>theme is "Светлая" or "Тёмная" or "Minecraft";
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
 public void Sample(float level,double sensitivity){
  float strength=(float)Math.Min(1,Math.Sqrt(level)*sensitivity);
  if(theme=="Светлая"){pulse+=(strength-pulse)*(strength>pulse?.85:.24);return;}
  if(theme=="Тёмная"){
   darkPeaks[^1]=Math.Max(darkPeaks[^1],strength);return;
  }
  Array.Copy(history,1,history,0,history.Length-1);history[^1]=strength;
 }
 public void Silence(){previousFrame=-1;columnFraction=0;Array.Clear(darkPeaks);Array.Clear(darkHeights);pulse=0;Array.Clear(history);InvalidateVisual();}
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
 void DrawLavender(DrawingContext d){
  for(int row=0;row<9;row++){
   double depth=row/8d,y=55+48*Math.Pow(depth,1.45);
   double scale=.19+.88*depth,spacing=2.65+4.4*depth;
   for(int col=-1;col*spacing<164;col++){
    double x=(col+(row%2)*.5)*spacing;
    double angle=8*Math.Sin(Phase*.82-x*.039-row*.21)+2*Math.Sin(Phase*1.17+x*.065);
    double radians=angle*Math.PI/180,cs=scale*Math.Cos(radians),sn=scale*Math.Sin(radians);
    d.PushTransform(new MatrixTransform(cs,sn,-sn,cs,x,y+.55*Math.Sin(col*2.3+row)));
    d.DrawDrawing(LavenderSprigs[(col+1+row*2)%3]);d.Pop();
   }
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
   double top=48+(int)(10*Math.Sin(i*.48))-2*Math.Round(HeightSignal(i)*7);
   Block(Ridge,i*8,top,8,100-top);Block(RidgeGrass,i*8,top,8,5);
   if(i==3)treeBase=top;
  }
  Block(Trunk,29,treeBase-18,4,21);Block(Leaves,20,treeBase-32,22,19);Block(Crown,24,treeBase-40,14,13);
  for(int i=0;i<20;i++){
   double top=68+(int)(11*Math.Sin(i*.42+1))-2*Math.Round(HeightSignal(i+3)*13);
   Block(Dirt,i*8,top,8,100-top);Block(Grass,i*8,top,8,7);Block(Stone,i*8+2,top+10,3,4);
  }
 }
 static readonly Brush Signal=new SolidColorBrush(Color.FromRgb(192,165,226));
 public SceneMotion(string theme){this.theme=theme;IsHitTestVisible=false;ClipToBounds=true;}
 static readonly Pen Steam=new(new SolidColorBrush(Color.FromArgb(156,255,242,213)),1.4);
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
    var path=new StreamGeometry();double x=63+i*10,drift=3*Math.Sin(Phase+i);
    using(var ctx=path.Open()){ctx.BeginFigure(new Point(x,33),false,false);ctx.BezierTo(new Point(x-7+drift,25),new Point(x+9+drift,18),new Point(x+drift,10-i*2+2*Math.Sin(Phase*.7+i)),true,true);}d.DrawGeometry(null,Steam,path);
   }
  }else if(theme=="Ретро"){
   foreach(int x in new[]{43,117})for(int i=0;i<3;i++){
    double a=Phase+i*2*Math.PI/3;d.DrawLine(Spoke,new Point(x+3*Math.Cos(a),54+3*Math.Sin(a)),new Point(x+7*Math.Cos(a),54+7*Math.Sin(a)));
   }
  }else if(theme=="Лес"){
   d.PushOpacity(.055);d.DrawEllipse(Brushes.White,null,new Point(68+18*Math.Sin(Phase*.19),42),60,3);d.Pop();
   for(int i=0;i<6;i++){double a=Phase*.5+i*7;d.PushOpacity(.3+.5*(.5+.5*Math.Sin(a)));d.DrawEllipse(Lamp,null,new Point(13+i*26+4*Math.Cos(a),49+i%3*14+3*Math.Sin(a)),1.3,.8);d.Pop();}
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
    double x=85+Math.Sin(i*2.399)*(y-52)*.34+ripple*2;
    double width=2+(1+Math.Cos(i*.7-Phase))*(1+(y-53)*.09);
    d.PushOpacity(.5+.24*ripple);d.DrawLine(Water,new Point(x-width,y),new Point(x+width,y));d.Pop();
   }
  }else if(theme=="Лаванда"){
   DrawLavender(d);
  }else if(theme=="Персик"){
   for(int i=0;i<3;i++){double a=Phase*.8+i*2;d.PushOpacity(.18+.13*Math.Sin(a));d.DrawEllipse(Brushes.White,null,new Point(100+i*5+Math.Cos(a)*1.4,41+i*3),1.7,1);d.Pop();}
  }else if(theme=="Терминал"){
   d.PushOpacity(.25+.75*(.5+.5*Math.Cos(Phase*3)));d.DrawRectangle(Lamp,null,new Rect(62,84,5,2));d.Pop();
  }
  d.Pop();
 }
}