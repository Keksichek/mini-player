using System;
using System.Windows;
using System.Windows.Media;

namespace YandexMiniPlayer;

// Coordinates refer to the compact face, in device-independent pixels.
sealed record StudioFace(int Width,int Height,string Paper,string Ink,string Key,string Font,
 Rect Scene,Point Title,int TextWidth,Point Artwork,Point Controls,Rect Timeline,Point Clock,int KeyWidth,int Radius){
 public static StudioFace For(string theme)=>theme switch{
  "Кофе"=>new(364,162,"#F1DFC1","#422C22","#DAB990","Georgia",new(0,0,144,145),new(151,37),202,new(8,99),new(150,88),new(148,123,201,16),new(152,141),42,12),
  "Лаванда"=>new(296,224,"#F3EDF7","#483653","#E4D2F0","Georgia",new(0,0,296,103),new(66,108),221,new(5,104),new(73,174),new(6,151,282,16),new(9,201),43,18),
  "Лес"=>new(350,178,"#173A35","#EDF5E3","#B8CAB4","Georgia",new(0,0,139,178),new(149,44),197,new(76,111),new(149,113),new(148,86,192,18),new(151,150),46,5),
  "Minecraft"=>new(348,173,"#3A3731","#FFF2D4","#B8AE96","Consolas",new(0,0,348,76),new(71,78),265,new(6,78),new(151,134),new(71,117,265,14),new(8,144),55,0),
  "Ночной океан"=>new(364,169,"#10283D","#E5F1FA","#9ABACD","Georgia",new(193,0,171,169),new(8,40),181,new(295,97),new(7,99),new(7,137,181,14),new(9,151),47,20),
  "Персик"=>new(318,192,"#FFEBDA","#62332B","#FFD0AA","Segoe UI",new(174,32,144,109),new(9,42),162,new(12,86),new(76,145),new(79,103,91,15),new(81,123),45,16),
  "Ретро"=>new(322,205,"#DDCEAA","#413B32","#B8ADA1","Consolas",new(0,31,322,94),new(67,123),246,new(4,128),new(136,177),new(67,164,241,10),new(5,183),53,3),
  "Терминал"=>new(284,205,"#273431","#BFECC1","#B5C2AC","Consolas",new(0,28,284,112),new(18,46),193,new(212,45),new(15,160),new(17,92,180,18),new(19,116),58,3),
  "Стекло"=>new(337,164,"#CBE4EF","#193D55","#EDF8FC","Segoe UI",new(0,28,126,128),new(128,42),202,new(30,64),new(128,101),new(128,138,193,15),new(4,140),47,22),
  "Тёмная"=>new(339,162,"#191A20","#F6EFE9","#BCBAC6","Segoe UI",new(226,34,113,116),new(8,41),211,new(252,62),new(6,94),new(6,132,210,17),new(236,147),49,5),
  _=>new(351,143,"#F9F7F0","#252831","#E6E4DC","Georgia",new(0,0,119,143),new(128,35),211,new(30,42),new(129,83),new(130,120,146,14),new(279,121),44,21)
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
    for(int i=-5;i<10;i++){double x=i*31;var geo=new StreamGeometry();using(var g=geo.Open()){g.BeginFigure(new Point(84+(x-84)*.13,54),true,true);g.LineTo(new Point(x+16,100),true,false);g.LineTo(new Point(x-8,100),true,false);}d.DrawGeometry(G("#B8A2C6","#77578A"),null,geo);}
    for(int i=0;i<33;i++){double x=rnd.Next(160),y=rnd.Next(76,110);Line(x,y,x+2,y-9,"#767A66",.65);E(x+2,y-9,1.2,3,B("#D7B5EE"));}break;
   case "Лес":
    Rect(0,0,160,100,G("#B3CBBD","#4B756B"),7);
    P("M0,44 L36,19 60,32 95,10 160,47 V100 H0Z","#829F94");
    for(int layer=0;layer<3;layer++)for(int i=0;i<16;i++){double x=i*12+rnd.Next(-5,5),y=34+layer*22+rnd.Next(-8,10),h=18+layer*8+rnd.Next(10);d.PushTransform(new TranslateTransform(x,y));d.PushTransform(new ScaleTransform(h/29,h/29));P("M0,0 L-5,10 -2,9 -8,18 -4,17 -11,29 H11 L4,17 8,18 2,9 5,10Z",new[]{"#78988B","#476E60","#214C40"}[layer]);d.Pop();d.Pop();}break;
   case "Minecraft":
    Rect(0,0,160,100,G("#B890AD","#F8C995"));Rect(122,12,12,23,B("#FFE7AE"));
    for(int i=0;i<20;i++){int y=48+(int)(10*Math.Sin(i*.48));Rect(i*8,y,8,100-y,B("#526653"));Rect(i*8,y,8,5,B("#8C9B64"));}
    for(int i=0;i<20;i++){int y=68+(int)(11*Math.Sin(i*.42+1));Rect(i*8,y,8,100-y,B("#68503A"));Rect(i*8,y,8,7,B("#7D9A43"));Rect(i*8+2,y+10,3,4,B("#826346"));}
    Rect(29,42,4,24,B("#705036"));Rect(20,29,22,19,B("#536A36"));Rect(24,21,14,13,B("#829B48"));break;
   case "Ночной океан":
    Rect(0,0,160,100,G("#111B34","#475367"),7);E(85,28,12,7.5,B("#FFDFAC"));Rect(0,53,160,47,G("#4F616E","#162E42"));
    for(int i=0;i<85;i++){double y=rnd.Next(54,100),x=85+(rnd.NextDouble()-.5)*(y-49)*1.15;Line(x,y,x+rnd.Next(2,12),y,"#B8A58A",.7);}
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
    for(int i=0;i<24;i++){double h=12+Math.Abs(Math.Sin(i*.42)*Math.Cos(i*.11))*64;Rect(i*7,95-h,4,h,G("#CBB3E8","#575563"));}break;
   default:
    Rect(0,0,160,100,B("#ECE8DF"),18);
    for(int i=0;i<7;i++)d.DrawEllipse(null,new Pen(B("#BCB9B1"),1.1),new Point(77,51),62-i*5,46-i*3.6);
    E(77,51,23,17,B("#E3A69C"));break;
  }
  d.Pop();
 }
 static Brush PlayerBrush(string c)=>(Brush)new BrushConverter().ConvertFromString(c)!;
}

sealed class SceneMotion:FrameworkElement{
 readonly string theme;
 public double Phase;
 public SceneMotion(string theme){this.theme=theme;IsHitTestVisible=false;ClipToBounds=true;}
 static readonly Pen Steam=new(new SolidColorBrush(Color.FromArgb(156,255,242,213)),1.4);
 static readonly Brush Lamp=new SolidColorBrush(Color.FromRgb(244,229,147));
 static readonly Pen Spoke=new(new SolidColorBrush(Color.FromRgb(220,201,169)),1.15);
 protected override void OnRender(DrawingContext d){
  d.PushTransform(new ScaleTransform(ActualWidth/160,ActualHeight/100));
  if(theme=="Кофе"){
   for(int i=0;i<3;i++){
    var path=new StreamGeometry();double x=63+i*10,drift=3*Math.Sin(Phase+i);
    using(var ctx=path.Open()){ctx.BeginFigure(new Point(x,33),false,false);ctx.BezierTo(new Point(x-7+drift,25),new Point(x+9+drift,18),new Point(x+drift,11-i*2),true,true);}d.DrawGeometry(null,Steam,path);
   }
  }else if(theme=="Ретро"){
   foreach(int x in new[]{43,117})for(int i=0;i<3;i++){
    double a=Phase+i*2*Math.PI/3;d.DrawLine(Spoke,new Point(x+3*Math.Cos(a),54+3*Math.Sin(a)),new Point(x+7*Math.Cos(a),54+7*Math.Sin(a)));
   }
  }else if(theme=="Лес"){
   for(int i=0;i<6;i++){double a=Phase*.5+i*7;d.PushOpacity(.3+.5*(.5+.5*Math.Sin(a)));d.DrawEllipse(Lamp,null,new Point(13+i*26+4*Math.Cos(a),49+i%3*14+3*Math.Sin(a)),1.3,.8);d.Pop();}
  }else if(theme=="Терминал"){
   d.PushOpacity(.25+.75*(.5+.5*Math.Cos(Phase*3)));d.DrawRectangle(Lamp,null,new Rect(62,84,5,2));d.Pop();
  }
  d.Pop();
 }
}