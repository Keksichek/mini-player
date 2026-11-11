using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace YandexMiniPlayer;
sealed class ColorWheel : FrameworkElement
{
 const int N=110;static readonly BitmapSource bitmap=Create();double hue,saturation;public double Value=1;
 public event Action<string>? Changed;public event Action<string>? LoadedColor;
 public ColorWheel(){Width=Height=N;Cursor=Cursors.Cross;MouseLeftButtonDown+=(_,e)=>{CaptureMouse();Pick(e.GetPosition(this));};MouseMove+=(_,e)=>{if(IsMouseCaptured)Pick(e.GetPosition(this));};MouseLeftButtonUp+=(_,e)=>{if(IsMouseCaptured){Pick(e.GetPosition(this));ReleaseMouseCapture();}};}
 public static Color Hsv(double h,double s,double v){h=(h%360+360)%360;double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;var (r,g,b)=h switch{<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));}
 static BitmapSource Create(){var pixels=new byte[N*N*4];for(int y=0;y<N;y++)for(int x=0;x<N;x++){double dx=x-55,dy=y-55,s=Math.Sqrt(dx*dx+dy*dy)/53;if(s>1)continue;var c=Hsv(Math.Atan2(dy,dx)*180/Math.PI,s,1);int i=(y*N+x)*4;pixels[i]=c.B;pixels[i+1]=c.G;pixels[i+2]=c.R;pixels[i+3]=255;}var bmp=BitmapSource.Create(N,N,96,96,PixelFormats.Bgra32,null,pixels,N*4);bmp.Freeze();return bmp;}
 public void SetColor(string hex){var c=(Color)ColorConverter.ConvertFromString(hex);double r=c.R/255d,g=c.G/255d,b=c.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;Value=max;saturation=max==0?0:d/max;hue=d==0?0:max==r?60*((g-b)/d%6):max==g?60*((b-r)/d+2):60*((r-g)/d+4);InvalidateVisual();LoadedColor?.Invoke(hex);}
 void Pick(Point p){double x=p.X-55,y=p.Y-55;hue=Math.Atan2(y,x)*180/Math.PI;saturation=Math.Min(1,Math.Sqrt(x*x+y*y)/53);Publish();}
 public void Publish(){InvalidateVisual();var c=Hsv(hue,saturation,Value);Changed?.Invoke($"#{c.R:X2}{c.G:X2}{c.B:X2}");}
 protected override void OnRender(DrawingContext dc){dc.DrawImage(bitmap,new Rect(0,0,N,N));dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)((1-Value)*255),0,0,0)),null,new Point(55,55),53,53);var p=new Point(55+Math.Cos(hue*Math.PI/180)*saturation*53 ,55+Math.Sin(hue*Math.PI/180)*saturation*53);dc.DrawEllipse(null,new Pen(Brushes.Black,3),p,5,5);dc.DrawEllipse(null,new Pen(Brushes.White,1.5),p,5,5);}
}
