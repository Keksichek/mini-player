using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WidgetLauncherSetup
{
 internal static partial class Ui
 {
  public static readonly Color Border=Color.FromArgb(51,55,72),Field=Color.FromArgb(35,39,51);
  [DllImport("user32.dll")]static extern bool ReleaseCapture();
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
  public static GraphicsPath Rounded(RectangleF bounds,float radius){
   var path=new GraphicsPath();float diameter=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
   path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);
   path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
  }
  static void AddChrome(Form form){
   var bar=new Panel{Location=new Point(1,1),Size=new Size(form.ClientSize.Width-2,46),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,BackColor=Background};
   var title=Label(InstallEngine.ProductName,20,14,form.ClientSize.Width-132,24,9,Muted);bar.Controls.Add(title);
   MouseEventHandler drag=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(form.Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};bar.MouseDown+=drag;title.MouseDown+=drag;
   var minimize=Button("—",form.ClientSize.Width-94,7,36,false);minimize.Size=new Size(36,30);minimize.BackColor=Background;minimize.ForeColor=Muted;minimize.Anchor=AnchorStyles.Top|AnchorStyles.Right;minimize.AccessibleName="Свернуть";minimize.Click+=delegate{form.WindowState=FormWindowState.Minimized;};bar.Controls.Add(minimize);
   var close=Button("×",form.ClientSize.Width-53,7,36,false);close.Name="HubClose";close.Size=new Size(36,30);close.BackColor=Background;close.ForeColor=Text;close.Font=Font(14);close.Anchor=AnchorStyles.Top|AnchorStyles.Right;close.AccessibleName="Закрыть";close.Click+=delegate{form.Close();};bar.Controls.Add(close);form.Controls.Add(bar);
   form.Paint+=delegate(object sender,PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Rounded(new RectangleF(.5f,.5f,form.ClientSize.Width-1,form.ClientSize.Height-1),12))using(var pen=new Pen(Border))e.Graphics.DrawPath(pen,path);};
   form.SizeChanged+=delegate{using(var path=Rounded(new RectangleF(0,0,form.Width,form.Height),12)){var old=form.Region;form.Region=new Region(path);if(old!=null)old.Dispose();}};
   using(var path=Rounded(new RectangleF(0,0,form.Width,form.Height),12))form.Region=new Region(path);
  }
  public static void SetCloseEnabled(Form form,bool enabled){foreach(Control control in form.Controls.Find("HubClose",true))control.Enabled=enabled;}
  public sealed class RoundedButton:Button
  {
   bool hover,pressed;
   public RoundedButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;}
   protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
   protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
   protected override void OnPaint(PaintEventArgs e){
    e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Background:Parent.BackColor);
    Color fill=BackColor;if(hover&&Enabled)fill=ControlPaint.Light(fill,.10f);if(pressed&&Enabled)fill=ControlPaint.Dark(fill,.12f);
    using(var path=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),9))using(var brush=new SolidBrush(fill)){e.Graphics.FillPath(brush,path);if(Focused){using(var pen=new Pen(Accent))e.Graphics.DrawPath(pen,path);}}
    TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
   }
  }
  public sealed class Surface:Panel
  {
   public Color Fill=Panel,Edge=Border;public int Radius=12;
   public Surface(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Panel;}
   protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?Background:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),Radius))using(var brush=new SolidBrush(Fill))using(var pen=new Pen(Edge)){e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}}
  }
  public sealed class Progress:Control
  {
   int value;public int Value{get{return value;}set{this.value=Math.Max(0,Math.Min(100,value));Invalidate();}}
   public Progress(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Background;AccessibleRole=AccessibleRole.ProgressBar;AccessibleName="Ход установки";}
   protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var track=Rounded(new RectangleF(0,0,Width,Height),Height/2f))using(var brush=new SolidBrush(Field))e.Graphics.FillPath(brush,track);if(value>0){float width=Math.Max(Height,Width*value/100f);using(var fill=Rounded(new RectangleF(0,0,width,Height),Height/2f))using(var brush=new LinearGradientBrush(new Rectangle(0,0,Width,Height),Accent,Color.FromArgb(211,195,255),0f))e.Graphics.FillPath(brush,fill);}}
  }
  public sealed class Check:CheckBox
  {
   public Check(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
   protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float y=(Height-18)/2f;using(var box=Rounded(new RectangleF(.5f,y,18,18),4))using(var brush=new SolidBrush(Checked?Accent:Field))using(var pen=new Pen(Border)){e.Graphics.FillPath(brush,box);e.Graphics.DrawPath(pen,box);}if(Checked)using(var pen=new Pen(Background,2)){e.Graphics.DrawLines(pen,new[]{new PointF(4,y+9),new PointF(8,y+13),new PointF(14,y+5)});}TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(29,0,Width-29,Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);}
  }
 }
}
