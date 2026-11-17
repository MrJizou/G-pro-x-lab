// Copyright (c) 2026 the Receiver Rescue project owner. All rights reserved. Proprietary; see LICENSE.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GProXLab {
 // ---------- Design tokens (dark, navy + teal; text contrast >= 4.5:1 on every surface) ----------
 static class UI {
  public static float K=1f;
  public static int P(int v){return (int)Math.Round(v*K);}
  public static readonly Color Bg=Color.FromArgb(11,18,32),Surface=Color.FromArgb(18,27,46),SurfaceHi=Color.FromArgb(30,44,72),Border=Color.FromArgb(52,70,102),
   Text=Color.FromArgb(232,238,246),Muted=Color.FromArgb(169,184,204),Dim=Color.FromArgb(112,128,153),
   Accent=Color.FromArgb(45,212,191),OnAccent=Color.FromArgb(4,33,29),Caution=Color.FromArgb(251,191,36),OnCaution=Color.FromArgb(31,21,0),CautionTint=Color.FromArgb(40,34,20),
   Success=Color.FromArgb(52,211,153),Danger=Color.FromArgb(248,113,113),Info=Color.FromArgb(96,165,250);
  public static readonly Font Body=new Font("Segoe UI",10f),Small=new Font("Segoe UI",9f),Semi=new Font("Segoe UI Semibold",10f),Section=new Font("Segoe UI Semibold",11.5f),Title=new Font("Segoe UI Semibold",18f),Mono=new Font("Consolas",9.5f);
  public static GraphicsPath Round(Rectangle r,int rad){var p=new GraphicsPath();int d=Math.Max(1,rad*2);p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  public static void Check(Graphics g,Color c,float cx,float cy,float s){using(var pen=new Pen(c,Math.Max(1.6f,s*0.16f)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})g.DrawLines(pen,new[]{new PointF(cx-s*0.32f,cy+s*0.02f),new PointF(cx-s*0.08f,cy+s*0.27f),new PointF(cx+s*0.34f,cy-s*0.22f)});}
 }
 public enum BtnKind{Primary,Secondary,Caution,Ghost}
 public enum IconKind{Neutral,Success,Warning,Danger,Info}

 public sealed class RescueButton:Button {
  public BtnKind Style=BtnKind.Secondary;bool hover,down,selected;
  public bool Selected{get{return selected;}set{if(selected!=value){selected=value;Invalidate();}}}
  public RescueButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;Font=UI.Semi;}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;down=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
  protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
  protected override void OnEnabledChanged(EventArgs e){Cursor=Enabled?Cursors.Hand:Cursors.Default;Invalidate();base.OnEnabledChanged(e);}
  protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
  protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
  static Color Shift(Color c,int d){return Color.FromArgb(Math.Max(0,Math.Min(255,c.R+d)),Math.Max(0,Math.Min(255,c.G+d)),Math.Max(0,Math.Min(255,c.B+d)));}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent!=null?Parent.BackColor:UI.Bg);
   Color fill,border,text;
   if(!Enabled){fill=Color.FromArgb(22,32,53);border=Color.FromArgb(36,50,76);text=UI.Dim;}
   else switch(Style){
    case BtnKind.Primary:fill=UI.Accent;border=fill;text=UI.OnAccent;break;
    case BtnKind.Caution:fill=UI.Caution;border=fill;text=UI.OnCaution;break;
    case BtnKind.Ghost:fill=Parent!=null?Parent.BackColor:UI.Bg;border=UI.Border;text=hover?UI.Text:UI.Muted;break;
    default:fill=UI.SurfaceHi;border=UI.Border;text=UI.Text;break;
   }
   if(Enabled&&hover&&Style!=BtnKind.Ghost)fill=Shift(fill,Style==BtnKind.Secondary?10:18);
   if(Enabled&&down)fill=Shift(fill,-14);
   var r=new Rectangle(0,0,Width-1,Height-1);int rad=UI.P(7);
   using(var path=UI.Round(r,rad))using(var b=new SolidBrush(fill))using(var pen=new Pen(border)){g.FillPath(b,path);g.DrawPath(pen,path);}
   if(Enabled&&selected){text=UI.Accent;using(var b=new SolidBrush(UI.Accent))g.FillRectangle(b,UI.P(14),Height-UI.P(5),Width-UI.P(28),UI.P(3));}
   TextRenderer.DrawText(g,Text,Font,new Rectangle(UI.P(10),0,Width-UI.P(20),Height),text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
   if(Focused&&ShowFocusCues){var f=new Rectangle(UI.P(2),UI.P(2),Width-UI.P(5),Height-UI.P(5));using(var path=UI.Round(f,Math.Max(2,rad-2)))using(var pen=new Pen(UI.Text,Math.Max(2f,UI.P(2))))g.DrawPath(pen,path);}
  }
 }
 public sealed class RescueCheck:CheckBox {
  bool hover;
  public RescueCheck(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AutoSize=false;Cursor=Cursors.Hand;Font=UI.Body;}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
  protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
  protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
  protected override void OnCheckedChanged(EventArgs e){Invalidate();base.OnCheckedChanged(e);}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);int bs=UI.P(20),by=UI.P(4);var box=new Rectangle(UI.P(1),by,bs,bs);
   using(var path=UI.Round(box,UI.P(4))){
    if(Checked&&Enabled){using(var b=new SolidBrush(UI.Accent))g.FillPath(b,path);UI.Check(g,UI.OnAccent,box.X+bs/2f,box.Y+bs/2f,bs);}
    else{using(var b=new SolidBrush(Enabled?UI.Bg:Color.FromArgb(22,32,53)))g.FillPath(b,path);if(Checked)UI.Check(g,UI.Dim,box.X+bs/2f,box.Y+bs/2f,bs);}
    using(var pen=new Pen(Enabled?(hover?UI.Accent:UI.Muted):UI.Dim,Math.Max(1.5f,UI.P(1)*1.5f)))g.DrawPath(pen,path);
   }
   var tr=new Rectangle(UI.P(30),0,Width-UI.P(30),Height);
   TextRenderer.DrawText(g,Text,Font,tr,Enabled?UI.Text:UI.Dim,TextFormatFlags.Left|TextFormatFlags.Top|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
   if(Focused&&ShowFocusCues){using(var path=UI.Round(new Rectangle(0,by-UI.P(2),bs+UI.P(4),bs+UI.P(4)),UI.P(5)))using(var pen=new Pen(UI.Text,2f))g.DrawPath(pen,path);}
  }
 }
 public sealed class CardPanel:Panel {
  public Color Fill=UI.Surface,Stroke=UI.Border,Bar=Color.Empty;
  public CardPanel(){DoubleBuffered=true;ResizeRedraw=true;BackColor=UI.Surface;}
  protected override void OnPaintBackground(PaintEventArgs e){}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent!=null?Parent.BackColor:UI.Bg);BackColor=Fill;
   var r=new Rectangle(0,0,Width-1,Height-1);using(var path=UI.Round(r,UI.P(10)))using(var b=new SolidBrush(Fill))using(var pen=new Pen(Stroke)){g.FillPath(b,path);g.DrawPath(pen,path);
    if(Bar!=Color.Empty){var old=g.Clip;g.SetClip(path);using(var bb=new SolidBrush(Bar))g.FillRectangle(bb,0,0,UI.P(5),Height);g.Clip=old;}}
  }
 }
 public sealed class StatusIcon:Control {
  IconKind kind=IconKind.Neutral;public Color Back=UI.Surface;
  public IconKind Kind{get{return kind;}set{if(kind!=value){kind=value;Invalidate();}}}
  public StatusIcon(){DoubleBuffered=true;SetStyle(ControlStyles.Selectable,false);}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Back);int s=Math.Min(Width,Height)-2;var rc=new Rectangle((Width-s)/2,(Height-s)/2,s,s);float cx=rc.X+s/2f,cy=rc.Y+s/2f;
   switch(kind){
    case IconKind.Success:using(var b=new SolidBrush(UI.Success))g.FillEllipse(b,rc);UI.Check(g,UI.OnAccent,cx,cy,s);break;
    case IconKind.Info:using(var b=new SolidBrush(UI.Info))g.FillEllipse(b,rc);using(var b2=new SolidBrush(UI.Bg)){g.FillEllipse(b2,cx-s*0.06f,cy-s*0.27f,s*0.12f,s*0.12f);g.FillRectangle(b2,cx-s*0.055f,cy-s*0.08f,s*0.11f,s*0.34f);}break;
    case IconKind.Danger:using(var b=new SolidBrush(UI.Danger))g.FillEllipse(b,rc);using(var pen=new Pen(UI.Bg,Math.Max(2f,s*0.12f)){StartCap=LineCap.Round,EndCap=LineCap.Round}){g.DrawLine(pen,cx-s*0.2f,cy-s*0.2f,cx+s*0.2f,cy+s*0.2f);g.DrawLine(pen,cx+s*0.2f,cy-s*0.2f,cx-s*0.2f,cy+s*0.2f);}break;
    case IconKind.Warning:using(var b=new SolidBrush(UI.Caution))g.FillPolygon(b,new[]{new PointF(cx,rc.Y),new PointF(rc.Right,rc.Bottom-s*0.06f),new PointF(rc.X,rc.Bottom-s*0.06f)});using(var b2=new SolidBrush(UI.OnCaution)){g.FillRectangle(b2,cx-s*0.05f,cy-s*0.12f,s*0.10f,s*0.30f);g.FillEllipse(b2,cx-s*0.065f,cy+s*0.23f,s*0.13f,s*0.13f);}break;
    default:using(var pen=new Pen(UI.Dim,Math.Max(2f,s*0.09f)))g.DrawEllipse(pen,rc.X+1,rc.Y+1,rc.Width-2,rc.Height-2);using(var b=new SolidBrush(UI.Dim))g.FillEllipse(b,cx-s*0.08f,cy-s*0.08f,s*0.16f,s*0.16f);break;
   }
  }
 }
 public sealed class ThinProgress:Control {
  int v;public int Minimum=0,Maximum=100;public Color Back=UI.Surface;
  public int Value{get{return v;}set{int n=Math.Max(Minimum,Math.Min(Maximum,value));if(n!=v){v=n;Invalidate();}}}
  public ThinProgress(){DoubleBuffered=true;SetStyle(ControlStyles.Selectable,false);}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Back);var tr=new Rectangle(0,0,Width-1,Height-1);
   using(var path=UI.Round(tr,Height/2))using(var b=new SolidBrush(UI.SurfaceHi))g.FillPath(b,path);
   int w=Maximum>Minimum?(int)((long)(Width-1)*(v-Minimum)/(Maximum-Minimum)):0;if(w>=Height/2){using(var path=UI.Round(new Rectangle(0,0,w,Height-1),Height/2))using(var b=new SolidBrush(UI.Accent))g.FillPath(b,path);}
  }
 }
 public sealed class ContentPanel:Panel { public ContentPanel(){DoubleBuffered=true;} }
}
