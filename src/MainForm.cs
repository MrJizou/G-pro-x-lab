// Copyright (c) 2026 the Receiver Rescue project owner. All rights reserved. Proprietary; see LICENSE.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GProXLab {
 // Battery trend with disconnect markers.
 public sealed class Trend:Control {
  public List<Sample> Samples=new List<Sample>();
  public List<DateTime> Marks=new List<DateTime>();
  public string Caption="",Empty="",MarkLabel="";
  public Trend(){DoubleBuffered=true;BackColor=UI.Surface;SetStyle(ControlStyles.Selectable,false);}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
   var area=new Rectangle(UI.P(46),UI.P(12),Math.Max(10,Width-UI.P(64)),Math.Max(10,Height-UI.P(52)));
   using(var grid=new Pen(UI.Border))
   for(int i=0;i<=4;i++){int y=area.Bottom-area.Height*i/4;g.DrawLine(grid,area.Left,y,area.Right,y);TextRenderer.DrawText(g,(i*25)+"%",Font,new Point(UI.P(4),y-UI.P(9)),UI.Muted,TextFormatFlags.NoPadding);}
   TextRenderer.DrawText(g,Caption,Font,new Point(area.Left,Height-UI.P(22)),UI.Muted,TextFormatFlags.NoPadding);
   if(Samples.Count<2){TextRenderer.DrawText(g,Empty,Font,new Point(area.Left+UI.P(16),area.Top+area.Height/2-UI.P(9)),UI.Muted,TextFormatFlags.NoPadding);return;}
   DateTime t0=Analysis.ParseTime(Samples[0].Time),t1=Analysis.ParseTime(Samples[Samples.Count-1].Time);double span=Math.Max(1.0,(t1-t0).TotalSeconds);
   Func<DateTime,float> X=t=>area.Left+(float)((t-t0).TotalSeconds/span*area.Width);
   foreach(var m in Marks){if(m<t0||m>t1)continue;using(var pen=new Pen(UI.Danger,Math.Max(1.5f,UI.P(1)*1.5f))){pen.DashStyle=DashStyle.Dash;float x=X(m);g.DrawLine(pen,x,area.Top,x,area.Bottom);}}
   var pts=Samples.Select(s=>new PointF(X(Analysis.ParseTime(s.Time)),area.Bottom-area.Height*Math.Max(0,Math.Min(100,s.Percent))/100f)).ToArray();
   using(var pen=new Pen(UI.Accent,Math.Max(2f,UI.P(2)*1.3f))){pen.LineJoin=LineJoin.Round;g.DrawLines(pen,pts);}
   if(pts.Length<=60){float r=Math.Max(2.5f,UI.P(3));using(var b=new SolidBrush(UI.Accent))using(var ring=new Pen(UI.Surface,1.5f))foreach(var p in pts){g.FillEllipse(b,p.X-r,p.Y-r,2*r,2*r);g.DrawEllipse(ring,p.X-r,p.Y-r,2*r,2*r);}}
   TextRenderer.DrawText(g,t0.ToString("HH:mm:ss"),Font,new Point(area.Left,area.Bottom+UI.P(4)),UI.Muted,TextFormatFlags.NoPadding);
   string end=t1.ToString("HH:mm:ss");var sz=TextRenderer.MeasureText(end,Font);TextRenderer.DrawText(g,end,Font,new Point(area.Right-sz.Width,area.Bottom+UI.P(4)),UI.Muted,TextFormatFlags.NoPadding);
   if(Marks.Count>0&&MarkLabel.Length>0){var ms=TextRenderer.MeasureText(MarkLabel,Font);TextRenderer.DrawText(g,MarkLabel,Font,new Point(area.Right-ms.Width,Height-UI.P(22)),UI.Danger,TextFormatFlags.NoPadding);}
  }
 }

 public sealed class MainForm:Form {
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr RegisterDeviceNotification(IntPtr h,IntPtr filter,uint flags);
  [DllImport("user32.dll")]static extern bool UnregisterDeviceNotification(IntPtr h);
  readonly string appVersion=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
  bool tr,demo,closing,busy,needBattery,needVersions,inLayout;int view;string statusMode="wait";DateTime statusTime;
  SessionData data=new SessionData();DateTime started=DateTime.Now;string sessionFolder;
  List<DeviceInfo> current=new List<DeviceInfo>();HashSet<string> lastKeys;BatteryReading latest;DateTime lastGood=DateTime.MinValue,lastBatteryAttempt=DateTime.MinValue;
  readonly Dictionary<string,DateTime> lastEventAt=new Dictionary<string,DateTime>();readonly List<IntPtr> notifications=new List<IntPtr>();
  readonly System.Windows.Forms.Timer pollTimer=new System.Windows.Forms.Timer(),debounce=new System.Windows.Forms.Timer();
  readonly StringBuilder rawLog=new StringBuilder();
  Panel scroller;ContentPanel content;
  Label title,subtitle,status,footer,recvLabel,usbLabel,lastRead,versionsLbl,rateLbl,summaryLbl,limitsLbl,cmpNote;
  StatusIcon recvIcon,usbIcon;CardPanel connCard,viewCard;CardPanel[] mc=new CardPanel[4];Label[] mTitle=new Label[4],mValue=new Label[4],mHint=new Label[4];
  RescueButton language,srcButton,unitButton,btnBattery,btnVersion,btnExport;RescueButton[] vb=new RescueButton[4];RescueCheck autoCheck;
  Trend trend;ListBox eventList;DataGridView cmpGrid,devGrid;TextBox rawBox;

  string T(string a,string b){return tr?a:b;}
  int P(int v){return UI.P(v);}
  public MainForm(bool demoMode,bool turkish){
   demo=demoMode;tr=turkish;
   using(var g=CreateGraphics())UI.K=g.DpiX/96f;
   Text="G PRO X Lab "+appVersion;AutoScaleMode=AutoScaleMode.None;BackColor=UI.Bg;ForeColor=UI.Text;Font=UI.Body;StartPosition=FormStartPosition.CenterScreen;
   var wa=Screen.PrimaryScreen.WorkingArea;Size=new Size(Math.Min(P(1100),wa.Width-P(40)),Math.Min(P(860),wa.Height-P(40)));MinimumSize=new Size(Math.Min(P(780),wa.Width),Math.Min(P(560),wa.Height));
   data.AppVersion=appVersion;data.Started=started.ToString("o");
   if(demo)LoadDemo();
   Build();
   RefreshAll();
   pollTimer.Interval=5000;pollTimer.Tick+=async(s,e)=>await Poll(false);
   debounce.Interval=500;debounce.Tick+=async(s,e)=>{debounce.Stop();await Poll(false);};
   Shown+=async(s,e)=>{DoLayout();if(demo){status.Text=T("ÖNİZLEME · ÖRNEK VERİ","PREVIEW · SAMPLE DATA");return;}OpenSessionFolder();AddNote(T("İzleme başladı. Firmware yazma, sıfırlama veya aygıtı devre dışı bırakma işlevi yok.","Monitoring started. There is no firmware-write, reset or device-disable function."));pollTimer.Start();await Poll(true);};
   FormClosing+=(s,e)=>{closing=true;pollTimer.Stop();debounce.Stop();foreach(var n in notifications)UnregisterDeviceNotification(n);notifications.Clear();SaveSession();};
  }

  // ---------- device-change notifications (precise timestamps for USB arrival/removal) ----------
  protected override void OnHandleCreated(EventArgs e){
   base.OnHandleCreated(e);if(demo)return;
   foreach(Guid guid in new[]{HidIo.HidGuid,new Guid("a5dcbf10-6530-11d2-901f-00c04fb951ed")}){
    IntPtr f=Marshal.AllocHGlobal(32);
    try{
     for(int i=0;i<32;i++)Marshal.WriteByte(f,i,0);
     Marshal.WriteInt32(f,0,32);Marshal.WriteInt32(f,4,5);Marshal.Copy(guid.ToByteArray(),0,IntPtr.Add(f,12),16);
     IntPtr h=RegisterDeviceNotification(Handle,f,0);if(h!=IntPtr.Zero)notifications.Add(h);
    }finally{Marshal.FreeHGlobal(f);}
   }
  }
  protected override void WndProc(ref Message m){
   base.WndProc(ref m);
   if(m.Msg==0x219)HandleDeviceChange(m.WParam.ToInt64(),m.LParam);
  }
  void HandleDeviceChange(long w,IntPtr lParam){
   if(closing||demo)return;if(w!=0x8000&&w!=0x8004)return;
   if(lParam==IntPtr.Zero||Marshal.ReadInt32(lParam,4)!=5)return;
   // dbcc_size bounds the name, so a malformed message cannot make the handler read past the structure.
   int size=Marshal.ReadInt32(lParam,0);if(size<32||size>8192)return;
   string name=Marshal.PtrToStringUni(IntPtr.Add(lParam,28),(size-28)/2);int nul=name.IndexOf('\0');if(nul>=0)name=name.Substring(0,nul);
   string key=Ids.KeyFromPath(name);if(key==null)return;
   RecordUsb(key,w==0x8000,"event");debounce.Stop();debounce.Start();
  }
  // Test hooks: feed the real notification handler a DEV_BROADCAST_DEVICEINTERFACE structure.
  public int NotificationCount{get{return notifications.Count;}}
  public int EventCount{get{return data.Events.Count;}}
  public DisconnectSummary CurrentSummary(){return Summary();}
  public void TestDeviceMessage(string path,bool arrived){
   IntPtr p=Marshal.AllocHGlobal(28+2*(path.Length+1));
   try{
    for(int i=0;i<28+2*(path.Length+1);i++)Marshal.WriteByte(p,i,0);
    Marshal.WriteInt32(p,0,28+2*(path.Length+1));Marshal.WriteInt32(p,4,5);
    for(int i=0;i<path.Length;i++)Marshal.WriteInt16(p,28+2*i,(short)path[i]);
    HandleDeviceChange(arrived?0x8000:0x8004,p);
   }finally{Marshal.FreeHGlobal(p);}
  }

  // ---------- state ----------
  void RecordUsb(string key,bool arrived,string origin){
   string kind=arrived?"arrived":"removed";DateTime now=DateTime.Now;string id=key+kind;DateTime prev;
   if(lastEventAt.TryGetValue(id,out prev)&&(now-prev).TotalSeconds<(origin=="event"?1.5:6.0))return;
   lastEventAt[id]=now;
   var e=new UsbEvent{Key=key,Kind=kind,Origin=origin};
   if(latest!=null&&(now-lastGood).TotalSeconds<120){e.Percent=latest.Percent;e.Charging=latest.Charging;}
   data.Events.Add(e);SaveSession();RefreshAll();
  }
  void AddNote(string text){data.Notes.Add(new NoteEntry{Text=text});RefreshEvents();SaveSession();}
  // Retain the legacy storage directory so existing sessions and comparison records stay available.
  string SessionRoot(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ProXLab");}
  void OpenSessionFolder(){try{sessionFolder=Path.Combine(SessionRoot(),"Sessions",DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(sessionFolder);}catch{sessionFolder=null;}}
  void SaveSession(){
   if(sessionFolder==null||demo)return;
   try{
    data.Ended=DateTime.Now.ToString("o");data.Devices=current;string text=Json.Serialize(data);
    File.WriteAllText(Path.Combine(sessionFolder,"session.json"),text,new UTF8Encoding(false));
    if(data.Unit!="none"){string dir=Path.Combine(SessionRoot(),"Compare");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,data.Unit+".json"),text,new UTF8Encoding(false));}
   }catch{}
  }
  DisconnectSummary Summary(){return Analysis.Summarize(data.Events,Analysis.ParseTime(data.Started),demo?Analysis.ParseTime(data.Ended):DateTime.Now);}

  string BatteryMessage(BatteryReading r){
   switch(r.Code){
    case "ok":return r.Charging==true?T("Şarj oluyor","Charging"):(r.Charging==false?T("Şarj edilmiyor","Not charging"):T("Şarj durumu bilinmiyor","Charge state unknown"));
    case "offline":return T("Geçersiz veya çevrimdışı pil yanıtı","Invalid or offline battery response");
    case "timeout":return T("Pil sorgusu zaman aşımına uğradı; kulaklık kapalı veya arayüz meşgul olabilir.","The battery query timed out; the headset may be off or the interface busy.");
    case "no-interface":return T("Uygun alıcı arayüzü bulunamadı","No suitable receiver interface found");
    case "access":return T("Alıcıya erişilemiyor (hata "+r.Detail+"). Alıcıyı kullanan başka bir program açıksa kapatıp yeniden deneyin.","The receiver cannot be accessed (error "+r.Detail+"). Close any other program that uses the receiver and try again.");
    case "headset-offline":return T("Kulaklık çevrimdışı veya sorgu desteklenmiyor","Headset offline or the query is not supported");
    case "multiple":return T("Birden fazla alıcı var. Yalnızca test edilen kulaklığın alıcısını takın.","More than one receiver is connected. Connect only the receiver of the headset under test.");
    case "no-receiver":return T("Kablosuz alıcı bağlı değil","The wireless receiver is not connected");
    case "no-reply":return T("Yanıt alınamadı","No reply received");
    default:return r.Detail!=null?r.Detail:T("Okunamadı","Could not be read");
   }
  }
  string VersionMessage(VersionReading v){
   switch(v.Code){
    case "ok":return v.Version;
    case "no-interface":return v.Kind=="headset"?T("Kulaklığın USB kablosu bağlı değil veya arayüz okunamadı","The headset USB cable is not connected or the interface could not be read"):T("Alıcı arayüzü okunamadı","The receiver interface could not be read");
    case "timeout":return T("Sorgu süre sınırına ulaştı; yalnızca okuma işlemi sonlandırıldı.","The query timed out; only the read operation was stopped.");
    case "access":return T("Erişim hatası "+v.Detail,"Access error "+v.Detail);
    case "unrecognized":return T("Tanımlanamayan sürüm yanıtı","Unrecognized version reply");
    default:return v.Detail!=null?v.Detail:T("Okunamadı","Could not be read");
   }
  }
  string KeyName(string key){return key==Ids.ReceiverKey?T("Alıcı","Receiver")+" ("+key+")":(key==Ids.HeadsetUsbKey?T("Kulaklık şarj USB","Headset charging USB")+" ("+key+")":key);}
  string EventLine(UsbEvent e){
   var sb=new StringBuilder();sb.Append(Analysis.ParseTime(e.Time).ToString("HH:mm:ss")).Append("  [USB] ").Append(KeyName(e.Key)).Append(' ').Append(e.Kind=="arrived"?T("bağlandı","connected"):T("ayrıldı","disconnected"));
   sb.Append(" · ").Append(e.Origin=="event"?T("olay","event"):T("örnekleme","poll"));
   if(e.Percent.HasValue)sb.Append(" · ≈").Append(e.Percent).Append('%');
   if(e.Charging==true)sb.Append(" · ").Append(T("şarj oluyordu","was charging"));
   return sb.ToString();
  }

  // ---------- polling ----------
  async Task Poll(bool manual){
   if(demo||closing)return;
   if(busy){if(manual)AddNote(T("Önceki sorgu sürüyor; yeni istek sırada.","The previous query is still running; the new request is queued."));return;}
   busy=true;SetBusy(true);statusMode="read";RefreshStatus();
   try{
    var list=await Task.Run(()=>Helper.Enumerate());if(closing)return;
    current=list;var keys=new HashSet<string>(list.Select(d=>d.Key));
    if(lastKeys!=null){foreach(var k in lastKeys.Except(keys).ToList())RecordUsb(k,false,"poll");foreach(var k in keys.Except(lastKeys).ToList())RecordUsb(k,true,"poll");}
    lastKeys=keys;
    bool receiver=keys.Contains(Ids.ReceiverKey);
    bool readBattery=needBattery||(!needVersions&&(manual||(autoCheck.Checked&&(DateTime.Now-lastBatteryAttempt).TotalSeconds>=10)));needBattery=false;
    if(readBattery){
     lastBatteryAttempt=DateTime.Now;var targets=list.Where(d=>HidIo.IsBatteryInterface(d)).ToList();
     if(targets.Count>1)AcceptBattery(new BatteryReading{Code="multiple"});
     else if(targets.Count==1){var r=await Task.Run(()=>Helper.Battery());if(closing)return;AcceptBattery(r);}
     else AcceptBattery(new BatteryReading{Code=receiver?"no-interface":"no-receiver"});
    }
    if(needVersions){await ReadVersions(list);if(closing)return;}
    statusMode="ok";statusTime=DateTime.Now;SaveSession();
   }catch(Exception ex){if(!closing){statusMode="error";AddNote(T("Okuma hatası: ","Read error: ")+ex.Message);if(needVersions){needVersions=false;MessageBox.Show(this,T("Sürüm sorgusu başlayamadı:\n","The version query could not start:\n")+ex.Message,"G PRO X Lab",MessageBoxButtons.OK,MessageBoxIcon.Exclamation);}}}
   finally{busy=false;if(!closing){SetBusy(false);RefreshAll();}}
  }
  void SetBusy(bool b){btnBattery.Enabled=!b;btnVersion.Enabled=!b;btnVersion.Text=b&&needVersions?T("Okunuyor…","Reading…"):T("Sürümleri oku","Read versions");}
  async Task ReadVersions(List<DeviceInfo> list){
   needVersions=false;var results=new List<VersionReading>();AddNote(T("Sürüm sorguları başladı; her sorgu en çok birkaç saniye bekler.","Version queries started; each query waits only a few seconds."));
   foreach(var d in list.Where(x=>HidIo.IsVersionInterface(x)).GroupBy(x=>x.Key).Select(g=>g.First())){
    string pid=d.Key==Ids.ReceiverKey?"0aba":"0abb";var r=await Task.Run(()=>Helper.Version(pid));if(closing)return;results.Add(r);data.Versions.Add(r);
   }
   if(!results.Any(r=>r.Kind=="headset"))results.Insert(0,new VersionReading{Kind="headset",Code="no-interface"});
   var sb=new StringBuilder();foreach(var r in results)sb.AppendLine((r.Kind=="receiver"?T("Alıcı","Receiver"):T("Kulaklık radyo","Headset radio"))+": "+VersionMessage(r)+Environment.NewLine);
   RefreshAll();MessageBox.Show(this,sb.ToString().TrimEnd(),T("Sürüm sonuçları","Version results"),MessageBoxButtons.OK,MessageBoxIcon.Information);
  }
  void AcceptBattery(BatteryReading r){
   latest=r.Code=="ok"||lastGood==DateTime.MinValue?r:latest;data.Batteries.Add(r);if(data.Batteries.Count>2000)data.Batteries.RemoveAt(0);
   rawLog.AppendLine(Analysis.ParseTime(r.Time).ToString("HH:mm:ss")+"  "+T("Pil","Battery")+"  ["+r.Code+"]").AppendLine("  "+T("İstek: ","Request: ")+(r.Request??"—")).AppendLine("  "+T("Yanıt: ","Reply: ")+(r.Raw??"—")).AppendLine();
   if(rawLog.Length>200000)rawLog.Remove(0,rawLog.Length-120000);
   if(r.Code=="ok"&&r.Percent.HasValue&&r.Millivolts.HasValue){
    latest=r;lastGood=DateTime.Now;data.Samples.Add(new Sample{Time=r.Time,Millivolts=r.Millivolts.Value,Percent=r.Percent.Value,Charging=r.Charging,Source=data.Source});
    if(data.Samples.Count>20000)data.Samples.RemoveAt(0);
   }
   lastMessage=BatteryMessage(r);
  }
  string lastMessage="";

  // ---------- UI ----------
  Label L(string text,Font f,Color fore,Color back){return new Label{Text=text,Font=f,ForeColor=fore,BackColor=back,AutoSize=false,UseMnemonic=false,TextAlign=ContentAlignment.TopLeft};}
  RescueButton B(string text,BtnKind k){return new RescueButton{Text=text,Style=k};}
  static int Fit(Control c,int x,int y,int w,int minH){int tw=Math.Max(20,w-(c is CheckBox?UI.P(30):0));var sz=TextRenderer.MeasureText(c.Text,c.Font,new Size(tw,0),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);int h=Math.Max(minH,sz.Height+UI.P(6));c.SetBounds(x,y,w,h);return h;}
  DataGridView Grid(){
   var g=new DataGridView{ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,BackgroundColor=UI.Surface,BorderStyle=BorderStyle.None,EnableHeadersVisualStyles=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,GridColor=UI.Border,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None};
   g.DefaultCellStyle.BackColor=UI.Surface;g.DefaultCellStyle.ForeColor=UI.Text;g.DefaultCellStyle.SelectionBackColor=UI.SurfaceHi;g.DefaultCellStyle.SelectionForeColor=UI.Text;g.DefaultCellStyle.Font=UI.Body;
   g.ColumnHeadersDefaultCellStyle.BackColor=UI.SurfaceHi;g.ColumnHeadersDefaultCellStyle.ForeColor=UI.Text;g.ColumnHeadersDefaultCellStyle.SelectionBackColor=UI.SurfaceHi;g.ColumnHeadersDefaultCellStyle.Font=UI.Semi;
   g.ColumnHeadersHeight=P(36);g.RowTemplate.Height=P(30);g.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;return g;
  }
  void Build(){
   scroller=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=UI.Bg};content=new ContentPanel{BackColor=UI.Bg,Location=Point.Empty};scroller.Controls.Add(content);Controls.Add(scroller);scroller.Resize+=(s,e)=>DoLayout();
   language=B(tr?"English":"Türkçe",BtnKind.Ghost);language.AccessibleName=T("Dili değiştir","Change language");content.Controls.Add(language);
   language.Click+=(s,e)=>{if(busy)return;tr=!tr;var old=Controls[0];Controls.Remove(old);old.Dispose();Build();RefreshAll();DoLayout();};
   title=L("G PRO X Lab",UI.Title,UI.Text,UI.Bg);subtitle=L(T("Kulaklık ve alıcı bağlantısı teşhisi · şarj sırasındaki USB kopmalarını kaydeder","Headset and receiver connection diagnostics · records USB disconnects while charging"),UI.Body,UI.Muted,UI.Bg);status=L("",UI.Body,UI.Muted,UI.Bg);status.TextAlign=ContentAlignment.TopRight;
   content.Controls.Add(title);content.Controls.Add(subtitle);content.Controls.Add(status);
   // connection card
   connCard=new CardPanel();content.Controls.Add(connCard);recvIcon=new StatusIcon();usbIcon=new StatusIcon();recvLabel=L("",UI.Body,UI.Text,UI.Surface);usbLabel=L("",UI.Body,UI.Text,UI.Surface);
   srcButton=B("",BtnKind.Secondary);unitButton=B("",BtnKind.Secondary);
   srcButton.Click+=(s,e)=>ShowMenu(srcButton,new[]{"unknown","pc-usb","adapter","powerbank","not-charging"},v=>Reports.SourceLabel(v,tr),v=>{data.Source=v;AddNote(T("Şarj kaynağı notu: ","Charging source note: ")+Reports.SourceLabel(v,tr)+T(" (elle seçildi)"," (selected manually)"));RefreshAll();});
   unitButton.Click+=(s,e)=>ShowMenu(unitButton,new[]{"problem","healthy"},v=>Reports.UnitLabel(v,tr),async v=>{if(busy){MessageBox.Show(this,T("Mevcut okuma bitsin; sonra test grubunu seçin.","Wait for the current read to finish, then choose the test group."));return;}BeginUnit(v);await Poll(true);});
   recvLabel.TextAlign=ContentAlignment.MiddleLeft;usbLabel.TextAlign=ContentAlignment.MiddleLeft;
   foreach(Control c in new Control[]{recvIcon,recvLabel,usbIcon,usbLabel,srcButton,unitButton})connCard.Controls.Add(c);
   // metric cards
   for(int i=0;i<4;i++){mc[i]=new CardPanel();mTitle[i]=L("",UI.Small,UI.Muted,UI.Surface);mValue[i]=L("—",UI.Section,UI.Text,UI.Surface);mValue[i].Font=new Font("Segoe UI Semibold",18f);mHint[i]=L("",UI.Small,UI.Muted,UI.Surface);mc[i].Controls.Add(mTitle[i]);mc[i].Controls.Add(mValue[i]);mc[i].Controls.Add(mHint[i]);content.Controls.Add(mc[i]);}
   mTitle[0].Text=T("TAHMİNİ PİL","ESTIMATED BATTERY");mTitle[1].Text=T("BİLDİRİLEN PİL GERİLİMİ","REPORTED BATTERY VOLTAGE");mTitle[2].Text=T("ŞARJ DURUMU","CHARGE STATE");mTitle[3].Text=T("ŞARJ USB KOPMALARI","CHARGING USB DISCONNECTS");
   // actions
   autoCheck=new RescueCheck{Text=T("Pili otomatik oku (10 sn)","Read the battery automatically (10 s)"),Checked=true,BackColor=UI.Bg};
   btnBattery=B(T("Pili şimdi oku","Read battery now"),BtnKind.Primary);btnVersion=B(T("Sürümleri oku","Read versions"),BtnKind.Secondary);btnExport=B(T("Dışa aktar ▾","Export ▾"),BtnKind.Secondary);
   btnBattery.Click+=async(s,e)=>{needBattery=true;await Poll(true);};btnVersion.Click+=async(s,e)=>{needVersions=true;await Poll(true);};btnExport.Click+=(s,e)=>ShowExport();
   foreach(Control c in new Control[]{autoCheck,btnBattery,btnVersion,btnExport})content.Controls.Add(c);
   // view switcher
   string[] names={T("Genel bakış","Overview"),T("Olay kaydı","Event log"),T("Karşılaştırma","Comparison"),T("Teknik","Technical")};
   for(int i=0;i<4;i++){int idx=i;vb[i]=B(names[i],BtnKind.Ghost);vb[i].Click+=(s,e)=>{view=idx;ApplyView();DoLayout();};content.Controls.Add(vb[i]);}
   // view card
   viewCard=new CardPanel();content.Controls.Add(viewCard);
   lastRead=L("",UI.Body,UI.Muted,UI.Surface);versionsLbl=L("",UI.Body,UI.Text,UI.Surface);rateLbl=L("",UI.Body,UI.Accent,UI.Surface);summaryLbl=L("",UI.Body,UI.Text,UI.Surface);limitsLbl=L(T("Şarj akımı, USB adaptör gücü, sıcaklık ve pil sağlığı bu araçla ölçülemez. Pil yüzdesi bir tahmindir. Kayıtlar, geri yüklenebilir bir firmware yedeği değildir.","Charge current, USB adapter power, temperature and battery health cannot be measured by this tool. Battery percentage is an estimate. Records are not a restorable firmware backup."),UI.Small,UI.Muted,UI.Surface);
   trend=new Trend{Font=UI.Small,Caption=T("Tahmini pil yüzdesi · oturum boyunca","Estimated battery percentage · during the session"),Empty=T("Grafik için en az iki geçerli pil okuması bekleniyor.","Waiting for at least two valid battery readings for the chart."),MarkLabel=T("┆ kırmızı çizgi = şarj USB kopması","┆ red line = charging USB disconnect")};
   eventList=new ListBox{BackColor=UI.Surface,ForeColor=UI.Text,BorderStyle=BorderStyle.None,HorizontalScrollbar=true,Font=UI.Mono,IntegralHeight=false};
   cmpNote=L(T("Önce yalnızca bir kulaklığın alıcısını takın, sonra test grubunu seçin; grup değişince yeni oturum açılır. Başlangıç pil seviyeleri farklıysa gerilim ve dolum hızı doğrudan kıyaslanamaz. Akım ölçülmez.","First connect only one headset's receiver, then choose the test group; a new session starts when the group changes. If the starting battery levels differ, voltage and charge rate cannot be compared directly. Current is not measured."),UI.Small,UI.Muted,UI.Surface);
   cmpGrid=Grid();cmpGrid.Columns.Add("m",T("Ölçüt","Metric"));cmpGrid.Columns.Add("p",T("Sorunlu kulaklık","Problem headset"));cmpGrid.Columns.Add("h",T("Sağlam kulaklık","Healthy headset"));
   devGrid=Grid();foreach(string c in new[]{T("Aygıt","Device"),"VID:PID",T("HID sayfası","HID page"),T("Kullanım","Usage"),T("Giriş B","In B"),T("Çıkış B","Out B"),T("Özellik B","Feature B"),T("Ürün","Product"),T("Durum","State")})devGrid.Columns.Add(c,c);
   float[] weights={170,90,80,80,70,70,80,150,190};for(int i=0;i<weights.Length&&i<devGrid.Columns.Count;i++)devGrid.Columns[i].FillWeight=weights[i];
   cmpGrid.Columns[0].FillWeight=140;cmpGrid.Columns[1].FillWeight=110;cmpGrid.Columns[2].FillWeight=110;
   rawBox=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BackColor=UI.Bg,ForeColor=UI.Muted,Font=UI.Mono,BorderStyle=BorderStyle.None};
   foreach(Control c in new Control[]{lastRead,versionsLbl,rateLbl,summaryLbl,trend,limitsLbl,eventList,cmpNote,cmpGrid,devGrid,rawBox})viewCard.Controls.Add(c);
   footer=L(T("Yerel kayıt · ağ bağlantısı yok · pil ve sürüm okumaları alıcıya salt okunur bir sorgu gönderir · firmware yazılmaz · seri numarası ve aygıt yolu kaydedilmez","Local records · no network connection · battery and version reads send a read-only query to the receiver · no firmware is written · serial numbers and device paths are not stored"),UI.Small,UI.Muted,UI.Bg);content.Controls.Add(footer);
   ApplyView();
  }
  void ShowMenu(Control anchor,string[] values,Func<string,string> label,Action<string> pick){
   var menu=new ContextMenuStrip{BackColor=UI.Surface,ForeColor=UI.Text,Font=UI.Body};
   foreach(string v in values){string value=v;menu.Items.Add(label(value),null,(s,e)=>pick(value));}
   menu.Show(anchor,0,anchor.Height);
  }
  string names4(int i){return i==0?T("Genel bakış","Overview"):i==1?T("Olay kaydı","Event log"):i==2?T("Karşılaştırma","Comparison"):T("Teknik","Technical");}
  void ApplyView(){
   for(int i=0;i<4;i++){vb[i].Style=i==view?BtnKind.Secondary:BtnKind.Ghost;vb[i].Selected=i==view;vb[i].AccessibleName=names4(i)+(i==view?T(" (seçili)"," (selected)"):"");}
   bool o=view==0,ev=view==1,cm=view==2,te=view==3;
   foreach(Control c in new Control[]{lastRead,versionsLbl,rateLbl,summaryLbl,trend,limitsLbl})c.Visible=o;
   eventList.Visible=ev;cmpNote.Visible=cm;cmpGrid.Visible=cm;devGrid.Visible=te;rawBox.Visible=te;
  }
  void DoLayout(){
   if(content==null||scroller==null||inLayout||IsDisposed)return;inLayout=true;
   try{for(int pass=0;pass<3;pass++){
    int W=Math.Max(P(600),scroller.ClientSize.Width);int m=P(24),pad=P(16),gap=P(12);int cw=Math.Min(W-2*m,P(1040));int x0=Math.Max(m,(W-cw)/2);int y=P(20);
    language.SetBounds(x0+cw-P(110),y+P(2),P(110),P(40));title.SetBounds(x0,y,Math.Max(P(100),cw-P(126)),P(34));
    int sw=Math.Max(P(200),Math.Min(P(420),cw/2));int sub=Math.Max(P(100),cw-P(126));subtitle.SetBounds(x0,y+P(36),sub,P(24));
    status.SetBounds(x0+cw-sw,y+P(44),sw,P(24));y+=P(78);
    // connection card
    int half=(cw-2*pad-pad)/2;bool two=cw>=P(720);int cy=pad;
    recvIcon.SetBounds(pad,cy+P(2),P(26),P(26));int rl=pad+P(34);int rw=two?half-P(34):cw-2*pad-P(34);Fit(recvLabel,rl,cy,rw,P(28));
    int ux=two?pad+half+pad:pad;int uy=two?cy:cy+P(36);usbIcon.SetBounds(ux,uy+P(2),P(26),P(26));Fit(usbLabel,ux+P(34),uy,two?half-P(34):cw-2*pad-P(34),P(28));
    cy=two?cy+P(40):cy+P(72);int bw=two?Math.Min(P(320),half):cw-2*pad;srcButton.SetBounds(pad,cy,bw,P(40));unitButton.SetBounds(two?pad+bw+gap:pad,two?cy:cy+P(48),bw,P(40));
    int ch=two?cy+P(40)+pad:cy+P(88)+pad;connCard.SetBounds(x0,y,cw,ch);y+=ch+gap;
    // metrics
    bool four=cw>=P(860);int cols=four?4:2;int mwid=(cw-(cols-1)*gap)/cols;int mh=P(128);
    for(int i=0;i<4;i++){int col=i%cols,row=i/cols;mc[i].SetBounds(x0+col*(mwid+gap),y+row*(mh+gap),mwid,mh);mTitle[i].SetBounds(pad,P(12),mwid-2*pad,P(20));mValue[i].SetBounds(pad,P(34),mwid-2*pad,P(40));mHint[i].SetBounds(pad,P(76),mwid-2*pad,P(44));}
    y+=(four?1:2)*mh+(four?0:gap)+gap;
    // actions
    int ay=y;autoCheck.SetBounds(x0,ay,cw,P(30));int ah=Fit(autoCheck,x0,ay,cw,P(30));ay+=ah+P(6);
    int abw=Math.Min(P(220),(cw-2*gap)/3);btnBattery.SetBounds(x0,ay,abw,P(44));btnVersion.SetBounds(x0+abw+gap,ay,abw,P(44));btnExport.SetBounds(x0+2*(abw+gap),ay,abw,P(44));y=ay+P(44)+gap+P(4);
    // view buttons
    int vw=(cw-3*P(8))/4;for(int i=0;i<4;i++)vb[i].SetBounds(x0+i*(vw+P(8)),y,vw,P(40));y+=P(40)+P(8);
    // view card
    int vh;int vx=pad,vwid=cw-2*pad;
    if(view==0){int yy=pad;yy+=Fit(lastRead,vx,yy,vwid,P(20))+P(4);yy+=Fit(versionsLbl,vx,yy,vwid,P(20))+P(4);yy+=Fit(rateLbl,vx,yy,vwid,P(20))+P(4);yy+=Fit(summaryLbl,vx,yy,vwid,P(20))+P(8);trend.SetBounds(vx,yy,vwid,P(210));yy+=P(210)+P(6);yy+=Fit(limitsLbl,vx,yy,vwid,P(20));vh=yy+pad;}
    else if(view==1){vh=P(420);eventList.SetBounds(pad,pad,vwid,vh-2*pad);}
    else if(view==2){int yy=pad;yy+=Fit(cmpNote,vx,yy,vwid,P(20))+P(8);vh=Math.Max(P(300),yy+P(170)+pad);cmpGrid.SetBounds(vx,yy,vwid,vh-yy-pad);}
    else{vh=P(460);int gh=P(170);devGrid.SetBounds(pad,pad,vwid,gh);rawBox.SetBounds(pad,pad+gh+P(10),vwid,vh-2*pad-gh-P(10));}
    viewCard.SetBounds(x0,y,cw,vh);y+=vh+gap;
    y+=Fit(footer,x0,y,cw,P(20))+P(22);
    int extra=Math.Min(P(200),scroller.ClientSize.Height-y);
    if(extra>0&&(view==0||view==1)){if(view==0){trend.Height+=extra;limitsLbl.Top+=extra;}else eventList.Height+=extra;viewCard.Height+=extra;footer.Top+=extra;y+=extra;}
    content.Size=new Size(W,y);
    if(W==Math.Max(P(600),scroller.ClientSize.Width))break;
   }}finally{inLayout=false;}
  }

  // ---------- refresh (UI is a projection of state) ----------
  void RefreshStatus(){
   if(status==null)return;
   switch(statusMode){
    case "read":status.Text="● "+T("Aygıtlar okunuyor","Reading devices");status.ForeColor=UI.Muted;break;
    case "ok":status.Text="● "+T("İzleniyor · ","Monitoring · ")+statusTime.ToString("HH:mm:ss");status.ForeColor=UI.Accent;break;
    case "error":status.Text=T("Okuma hatası","Read error");status.ForeColor=UI.Danger;break;
    default:status.Text=demo?T("ÖNİZLEME · ÖRNEK VERİ","PREVIEW · SAMPLE DATA"):T("Bağlantı bekleniyor","Waiting for connection");status.ForeColor=UI.Muted;break;
   }
  }
  void RefreshEvents(){
   if(eventList==null)return;
   var lines=new List<Tuple<DateTime,string>>();
   foreach(var e in data.Events)lines.Add(Tuple.Create(Analysis.ParseTime(e.Time),EventLine(e)));
   foreach(var n in data.Notes){var t=Analysis.ParseTime(n.Time);lines.Add(Tuple.Create(t,t.ToString("HH:mm:ss")+"  ["+T("Not","Note")+"] "+n.Text));}
   eventList.BeginUpdate();eventList.Items.Clear();foreach(var l in lines.OrderByDescending(x=>x.Item1).Take(500))eventList.Items.Add(l.Item2);eventList.EndUpdate();
  }
  void RefreshAll(){
   if(closing||content==null)return;
   RefreshStatus();
   bool receiver=current.Any(d=>d.Key==Ids.ReceiverKey),usb=current.Any(d=>d.Key==Ids.HeadsetUsbKey);
   if(demo){receiver=true;usb=true;}
   recvIcon.Kind=receiver?IconKind.Success:IconKind.Neutral;recvLabel.Text=T("Alıcı: ","Receiver: ")+(receiver?T("bağlı","connected"):T("yok","not found"));
   usbIcon.Kind=usb?IconKind.Success:IconKind.Neutral;usbLabel.Text=T("Kulaklık şarj USB: ","Headset charging USB: ")+(usb?T("bağlı","connected"):T("PC'ye bağlı değil","not connected to the PC"));
   srcButton.Text=T("Şarj kaynağı notu: ","Charging source note: ")+Reports.SourceLabel(data.Source,tr)+" ▾";unitButton.Text=T("Test grubu: ","Test group: ")+Reports.UnitLabel(data.Unit,tr)+" ▾";
   bool good=latest!=null&&latest.Code=="ok"&&latest.Percent.HasValue;int age=lastGood==DateTime.MinValue?0:(int)(DateTime.Now-lastGood).TotalSeconds;bool stale=!demo&&good&&age>30;
   if(demo&&data.Samples.Count>0){var s=data.Samples[data.Samples.Count-1];mValue[0].Text="≈ "+s.Percent+"%";mValue[1].Text=s.Millivolts+" mV";mValue[2].Text=s.Charging==true?T("Şarj oluyor","Charging"):(s.Charging==false?T("Şarjda değil","Not charging"):T("Bilinmiyor","Unknown"));mHint[0].Text=T("Gerilimden hesaplanan yaklaşık değer","Approximate value calculated from voltage");mHint[1].Text=T("Kulaklığın bildirdiği telemetri","Telemetry reported by the headset");mHint[2].Text=T("Pil sorgusundan gelen durum","State from the battery query");}
   else if(good){mValue[0].Text="≈ "+latest.Percent+"%";mValue[1].Text=latest.Millivolts+" mV";mValue[2].Text=latest.Charging==true?T("Şarj oluyor","Charging"):(latest.Charging==false?T("Şarjda değil","Not charging"):T("Bilinmiyor","Unknown"));
    mHint[0].Text=stale?T("Son bilinen tahmin · güncel değil","Last known estimate · not current"):T("Gerilimden hesaplanan yaklaşık değer","Approximate value calculated from voltage");mHint[1].Text=stale?T("Son bilinen gerilim · güncel değil","Last known voltage · not current"):T("Kulaklığın bildirdiği telemetri","Telemetry reported by the headset");mHint[2].Text=lastMessage;}
   else{mValue[0].Text="— %";mValue[1].Text="— mV";mValue[2].Text=T("Okunmadı","Not read");mHint[0].Text=T("Gerilimden hesaplanan yaklaşık değer","Approximate value calculated from voltage");mHint[1].Text=T("Kulaklığın bildirdiği telemetri","Telemetry reported by the headset");mHint[2].Text=lastMessage.Length>0?lastMessage:T("Pil sorgusundan gelen durum","State from the battery query");}
   var sum=Summary();mValue[3].Text=sum.Count.ToString();mValue[3].ForeColor=sum.Count>0?UI.Caution:UI.Text;mc[3].Bar=sum.Count>0?UI.Caution:Color.Empty;mc[3].Invalidate();mHint[3].Text=T("Alıcı ayrılması: ","Receiver disconnects: ")+sum.ReceiverCount+T(" · kullanıcının çıkarması da sayılır"," · includes unplugging by hand");
   lastRead.Text=lastGood==DateTime.MinValue&&!demo?T("Son geçerli pil okuması: henüz yok","Last valid battery reading: none yet"):T("Son geçerli pil okuması: ","Last valid battery reading: ")+(demo&&data.Samples.Count>0?Analysis.ParseTime(data.Samples[data.Samples.Count-1].Time).ToString("HH:mm:ss"):lastGood.ToString("HH:mm:ss")+" · "+age+" "+T("sn önce"," s ago")+(stale?T(" · EKRANDAKİ DEĞERLER ESKİ OLABİLİR"," · VALUES ON SCREEN MAY BE OUTDATED"):""));
   var vs=data.Versions.Where(v=>v.Version!=null).GroupBy(v=>v.Kind).Select(g=>g.Last()).ToDictionary(v=>v.Kind,v=>v.Version);
   versionsLbl.Text=T("Sürümler: ","Versions: ")+T("kulaklık radyo ","headset radio ")+(vs.ContainsKey("headset")?vs["headset"]:T("okunmadı","not read"))+"   |   "+T("alıcı ","receiver ")+(vs.ContainsKey("receiver")?vs["receiver"]:T("okunmadı","not read"));
   if(data.Samples.Count>=2){var a=data.Samples[0];var b=data.Samples[data.Samples.Count-1];double mins=(Analysis.ParseTime(b.Time)-Analysis.ParseTime(a.Time)).TotalMinutes;rateLbl.Text=T("Oturum değişimi: ","Session change: ")+(b.Percent-a.Percent).ToString("+0;-0;0")+T(" yüzde puanı · "," percentage points · ")+(b.Millivolts-a.Millivolts).ToString("+0;-0;0")+" mV · "+mins.ToString("0.0")+" "+T("dk","min");}
   else rateLbl.Text=T("Pil değişimi için zaman içinde en az iki okuma gerekiyor.","At least two readings over time are needed to show a battery change.");
   if(sum.Count==0)summaryLbl.Text=T("Kopma özeti: henüz kayıtlı şarj USB kopması yok.","Disconnect summary: no charging USB disconnect recorded yet.");
   else summaryLbl.Text=T("Kopma özeti: ","Disconnect summary: ")+sum.Count+T(" kopma · saatte "," disconnects · ")+sum.PerHour.ToString("0.0")+(tr?"":" per hour")+(sum.ReturnedCount>0?T(" · ortanca geri gelme "," · median reconnect ")+Reports.FormatDuration(sum.MedianSeconds)+T(" · en uzun "," · longest ")+Reports.FormatDuration(sum.LongestSeconds):"")+(sum.WhileChargingCount>0?T(" · şarj olurken: "," · while charging: ")+sum.WhileChargingCount:"");
   trend.Samples=data.Samples;trend.Marks=sum.Rows.Select(r=>r.At).ToList();trend.Invalidate();
   RefreshEvents();RefreshDevices();RefreshCompare();rawBox.Text=rawLog.ToString();
   DoLayout();
  }
  void RefreshDevices(){
   devGrid.Rows.Clear();
   foreach(var d in current)devGrid.Rows.Add(Role(d.Role),d.Key,d.UsagePage.ToString("X4"),d.Usage.ToString("X4"),d.InputLength,d.OutputLength,d.FeatureLength,d.Product??"—",d.Error??T("Arayüz okunabiliyor","Interface readable"));
  }
  string Role(string r){switch(r){case "receiver":return T("Kablosuz alıcı","Wireless receiver");case "headset-usb":return T("Kulaklık USB / şarj","Headset USB / charging");case "receiver-boot":return T("Alıcı bootloader","Receiver bootloader");default:return T("Kulaklık bootloader","Headset bootloader");}}
  SessionData LoadStored(string unit){
   if(demo)return unit=="problem"?data:null;
   try{string p=Path.Combine(SessionRoot(),"Compare",unit+".json");return File.Exists(p)?Json.Deserialize<SessionData>(File.ReadAllText(p)):null;}catch{return null;}
  }
  string[] CompareValues(SessionData s){
   if(s==null)return new[]{"—","—","—","—","—","—","—","—"};
   var st=Analysis.ParseTime(s.Started);var en=Analysis.ParseTime(s.Ended);var sum=Analysis.Summarize(s.Events,st,en);
   string first=s.Samples.Count>0?"≈"+s.Samples[0].Percent+"% · "+s.Samples[0].Millivolts+" mV":"—",last=s.Samples.Count>0?"≈"+s.Samples[s.Samples.Count-1].Percent+"% · "+s.Samples[s.Samples.Count-1].Millivolts+" mV":"—";
   return new[]{st.ToString("yyyy-MM-dd HH:mm"),sum.ObservedMinutes.ToString("0.0")+" "+T("dk","min"),Reports.SourceLabel(s.Source,tr),s.Samples.Count.ToString(),first,last,sum.Count+" ("+sum.PerHour.ToString("0.0")+(tr?"/saat":"/hour")+")",sum.ReturnedCount>0?Reports.FormatDuration(sum.LongestSeconds):"—"};
  }
  void RefreshCompare(){
   cmpGrid.Rows.Clear();
   string[] names={T("Başlangıç","Started"),T("Süre","Duration"),T("Şarj kaynağı notu","Charging source note"),T("Pil okuması","Battery readings"),T("İlk tahmin","First estimate"),T("Son tahmin","Last estimate"),T("Şarj USB kopması","Charging USB disconnects"),T("En uzun geri gelme","Longest reconnect")};
   string[] p=CompareValues(LoadStored("problem")),h=CompareValues(LoadStored("healthy"));
   for(int i=0;i<names.Length;i++)cmpGrid.Rows.Add(names[i],p[i],h[i]);
  }
  void BeginUnit(string unit){
   SaveSession();data=new SessionData{AppVersion=appVersion,Started=DateTime.Now.ToString("o"),Unit=unit,Source=data.Source};started=DateTime.Now;
   current=new List<DeviceInfo>();lastKeys=null;latest=null;lastGood=lastBatteryAttempt=DateTime.MinValue;lastMessage="";rawLog.Clear();lastEventAt.Clear();
   OpenSessionFolder();AddNote(Reports.UnitLabel(unit,tr)+T(" testi başladı. Yalnızca bu kulaklığın alıcısını kullanın."," test started. Use only this headset's receiver."));RefreshAll();
  }

  // ---------- export ----------
  void ShowExport(){
   var menu=new ContextMenuStrip{BackColor=UI.Surface,ForeColor=UI.Text,Font=UI.Body};
   menu.Items.Add(T("Destek raporu (metin)…","Support report (text)…"),null,(s,e)=>DoExport("txt"));
   menu.Items.Add(T("Tüm oturum (JSON)…","Full session (JSON)…"),null,(s,e)=>DoExport("json"));
   menu.Items.Add(T("Pil okumaları (CSV)…","Battery readings (CSV)…"),null,(s,e)=>DoExport("csv"));
   menu.Show(btnExport,0,btnExport.Height);
  }
  public string BuildContent(string kind){
   data.Ended=DateTime.Now.ToString("o");data.Devices=current;
   if(kind=="json")return Json.Serialize(Privacy.MaskSession(data));
   if(kind=="csv")return Privacy.Mask(Reports.SamplesCsv(data));
   return Reports.Build(data,Summary(),tr,appVersion,Privacy.WindowsInfo());
  }
  void DoExport(string kind){
   using(var d=new SaveFileDialog{FileName="GProXLab-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+"."+kind,Filter=kind=="txt"?"Text (*.txt)|*.txt":(kind=="json"?"JSON (*.json)|*.json":"CSV (*.csv)|*.csv"),Title=T("Dışa aktar","Export")}){
    if(d.ShowDialog(this)!=DialogResult.OK)return;
    try{File.WriteAllText(d.FileName,BuildContent(kind),new UTF8Encoding(kind=="txt"));AddNote(T("Dışa aktarıldı: ","Exported: ")+kind.ToUpperInvariant());}
    catch(Exception ex){MessageBox.Show(this,ex.Message,"G PRO X Lab",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
   }
  }

  // ---------- demo data and layout self-check ----------
  void LoadDemo(){
   var t0=DateTime.Now.AddMinutes(-72);data=new SessionData{AppVersion=appVersion,Started=t0.ToString("o"),Unit="problem",Source="pc-usb"};
   int[] mv=new int[]{3715,3722,3731,3744,3758,3769,3781,3794,3809,3826,3838,3851,3864};
   for(int i=0;i<mv.Length;i++)data.Samples.Add(new Sample{Time=t0.AddMinutes(i*6).ToString("o"),Millivolts=mv[i],Percent=Proto.EstimatePercent(mv[i]).Value,Charging=true,Source="pc-usb"});
   foreach(int m in new[]{9,21,24,38,51,63}){data.Events.Add(new UsbEvent{Time=t0.AddMinutes(m).ToString("o"),Key=Ids.HeadsetUsbKey,Kind="removed",Origin="event",Percent=Proto.EstimatePercent(mv[Math.Min(mv.Length-1,m/6)]),Charging=true});data.Events.Add(new UsbEvent{Time=t0.AddMinutes(m).AddSeconds(4+m%9).ToString("o"),Key=Ids.HeadsetUsbKey,Kind="arrived",Origin="event"});}
   data.Versions.Add(new VersionReading{Kind="headset",Code="ok",Version="V2.12"});data.Versions.Add(new VersionReading{Kind="receiver",Code="ok",Version="V2.12"});
   data.Ended=DateTime.Now.ToString("o");data.Notes.Add(new NoteEntry{Time=t0.ToString("o"),Text=tr?"Örnek veri — gerçek ölçüm değildir.":"Sample data — not a real measurement."});
   current=new List<DeviceInfo>{new DeviceInfo{Key=Ids.ReceiverKey,Role="receiver",UsagePage=0xFF43,Usage=0x0202,InputLength=20,OutputLength=20,FeatureLength=0,Product="PRO X Wireless"},new DeviceInfo{Key=Ids.HeadsetUsbKey,Role="headset-usb",UsagePage=0xFF00,Usage=1,InputLength=64,OutputLength=0,FeatureLength=64,Product="PRO X Wireless"}};
   latest=new BatteryReading{Code="ok",Millivolts=3864,Percent=Proto.EstimatePercent(3864),Charging=true};lastGood=DateTime.Now;lastMessage=tr?"Şarj oluyor":"Charging";
  }
  public void SetView(int v){view=Math.Max(0,Math.Min(3,v));ApplyView();DoLayout();}
  public List<string> CheckLayout(){
   var problems=new List<string>();string lang=tr?"TR":"EN";
   for(int v=0;v<4;v++){SetView(v);Application.DoEvents();CheckAll(problems,lang+" w="+ClientSize.Width+" view="+v);}
   return problems.Distinct().ToList();
  }
  void CheckAll(List<string> problems,string ctx){
   if(content.Width>scroller.ClientSize.Width+1)problems.Add(ctx+": horizontal overflow");
   Control[] flow={connCard,mc[0],mc[2],btnBattery,vb[0],viewCard,footer};Control prev=null;
   foreach(var c in flow){if(prev!=null&&c.Top<prev.Top)problems.Add(ctx+": order problem at "+c.GetType().Name);prev=c;}
   if(viewCard.Top<vb[0].Bottom)problems.Add(ctx+": view card overlaps view buttons");
   if(vb[0].Top<btnBattery.Bottom)problems.Add(ctx+": view buttons overlap actions");
   if(btnBattery.Top<mc[3].Bottom&&mc[3].Top<btnBattery.Bottom)problems.Add(ctx+": actions overlap metric cards");
   if(mc[0].Top<connCard.Bottom)problems.Add(ctx+": metric cards overlap connection card");
   CheckChildren(content,problems,ctx);
  }
  void CheckChildren(Control parent,List<string> problems,string ctx){
   foreach(Control c in parent.Controls){
    if(!c.Visible)continue;
    if(c.Left<0||c.Top<0||c.Right>parent.ClientSize.Width||(parent!=content&&c.Bottom>parent.ClientSize.Height))problems.Add(ctx+": out of bounds: "+c.GetType().Name+" '"+Brief(c)+"'");
    var label=c as Label;var button=c as RescueButton;var check=c as RescueCheck;
    if(label!=null&&label.Text.Length>0){int need=TextRenderer.MeasureText(label.Text,label.Font,new Size(label.Width,0),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding).Height;if(need>label.Height)problems.Add(ctx+": text clipped in label '"+Brief(c)+"'");}
    if(check!=null){int need=TextRenderer.MeasureText(check.Text,check.Font,new Size(Math.Max(20,check.Width-UI.P(30)),0),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding).Height;if(need>check.Height)problems.Add(ctx+": text clipped in checkbox '"+Brief(c)+"'");}
    if(button!=null){int w=TextRenderer.MeasureText(button.Text,button.Font,new Size(int.MaxValue/2,0),TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Width;if(w>button.Width-UI.P(20))problems.Add(ctx+": text clipped in button '"+Brief(c)+"'");}
    if(c is CardPanel)CheckChildren(c,problems,ctx);
   }
  }
  static string Brief(Control c){string t=c.Text??"";return t.Length>40?t.Substring(0,40)+"…":t;}
 }
}
