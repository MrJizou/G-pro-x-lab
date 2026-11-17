// Copyright (c) 2026 the Receiver Rescue project owner. All rights reserved. Proprietary; see LICENSE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace GProXLab {
 // ---------- Identifiers ----------
 public static class Ids {
  public const string ReceiverKey="046D:0ABA",HeadsetUsbKey="046D:0ABB",ReceiverBootKey="170D:0100",HeadsetBootKey="170D:0101";
  public static string KeyFromPath(string path){
   if(string.IsNullOrEmpty(path))return null;string s=path.ToLowerInvariant();
   if(s.Contains("vid_046d&pid_0aba"))return ReceiverKey;
   if(s.Contains("vid_046d&pid_0abb"))return HeadsetUsbKey;
   if(s.Contains("vid_170d&pid_0100"))return ReceiverBootKey;
   if(s.Contains("vid_170d&pid_0101"))return HeadsetBootKey;
   return null;
  }
  public static string RoleFor(string key){
   switch(key){case ReceiverKey:return "receiver";case HeadsetUsbKey:return "headset-usb";case ReceiverBootKey:return "receiver-boot";default:return "headset-boot";}
  }
 }

 // ---------- Data model (what is stored and exported; contains no serial numbers or device paths) ----------
 public sealed class DeviceInfo {
  public string Key,Role,Product,Manufacturer,Error;
  public int Vendor,ProductId,UsagePage,Usage,InputLength,OutputLength,FeatureLength,Revision;
  // The interface path is needed only inside the helper process that opens the device. It is never serialized.
  [ScriptIgnore] public string Path;
 }
 public sealed class BatteryReading {
  public string Time=DateTimeOffset.Now.ToString("o");
  public string Code="error",Detail,Request,Raw;
  public int? Millivolts,Percent;public bool? Charging;
 }
 public sealed class VersionReading {
  public string Time=DateTimeOffset.Now.ToString("o");
  public string Kind,Code="error",Detail,Version,Request,Raw;
 }
 public sealed class UsbEvent {
  public string Time=DateTimeOffset.Now.ToString("o");
  public string Key,Kind,Origin;public int? Percent;public bool? Charging;
 }
 public sealed class Sample { public string Time;public int Millivolts,Percent;public bool? Charging;public string Source; }
 public sealed class NoteEntry { public string Time=DateTimeOffset.Now.ToString("o");public string Text; }
 public sealed class SessionData {
  public string AppVersion,Started,Ended,Unit="none",Source="unknown";
  public List<DeviceInfo> Devices=new List<DeviceInfo>();
  public List<BatteryReading> Batteries=new List<BatteryReading>();
  public List<VersionReading> Versions=new List<VersionReading>();
  public List<UsbEvent> Events=new List<UsbEvent>();
  public List<Sample> Samples=new List<Sample>();
  public List<NoteEntry> Notes=new List<NoteEntry>();
 }

 // ---------- Protocol (read-only queries) ----------
 public static class Proto {
  public static byte[] BatteryRequest(int length){
   if(length<20||length>4096)throw new ArgumentException("Report length");
   var b=new byte[length];b[0]=0x11;b[1]=0xFF;b[2]=0x06;b[3]=0x0D;return b;
  }
  static readonly int[] Mv=new[]{4150,3830,3780,3740,3670,3320};
  static readonly int[] Pc=new[]{100,50,30,20,5,0};
  // Percentage is an estimate interpolated from the reported battery voltage. It is not a capacity measurement.
  public static int? EstimatePercent(int mv){
   if(mv<2500||mv>5000)return null;
   if(mv>=Mv[0])return 100;if(mv<=Mv[5])return 0;
   for(int i=0;i<5;i++)if(mv<=Mv[i]&&mv>=Mv[i+1])return (int)Math.Round(Pc[i+1]+(mv-Mv[i+1])*(double)(Pc[i]-Pc[i+1])/(Mv[i]-Mv[i+1]));
   return null;
  }
  public static BatteryReading ParseBattery(byte[] b,int length){
   if(b==null||length<7||length>b.Length||(b[0]!=0x10&&b[0]!=0x11)||b[1]!=0xFF||b[2]!=0x06||b[3]!=0x0D)return null;
   int mv=(b[4]<<8)|b[5];var r=new BatteryReading{Millivolts=mv,Raw=BitConverter.ToString(b,0,Math.Min(length,32))};
   if(mv<2500||mv>5000){r.Code="offline";return r;}
   r.Code="ok";r.Percent=EstimatePercent(mv);
   r.Charging=b[6]==3?(bool?)true:(b[6]==1?(bool?)false:null);
   if(r.Charging==null)r.Detail="charge-state-code "+b[6];
   return r;
  }
  public static readonly byte[] RadioQuery=new byte[]{255,10,0,253,4,7,0,5,3,5,0,0};
  public static string ParseRadioVersion(byte[] rx){
   if(rx==null||rx.Length<12||rx[0]!=255||rx[3]!=254||rx[4]!=7||(rx[9]!=86&&rx[9]!=84))return null;
   string s=Encoding.ASCII.GetString(rx,9,rx.Length-9).TrimEnd('\0');
   string v=Clean(s.Split('\r',',','\n')[0],32);
   return string.IsNullOrEmpty(v)?null:v;
  }
  // Text that comes from a USB device is untrusted: keep printable ASCII only and limit its length.
  public static string Clean(string text,int max){
   if(text==null)return null;var sb=new StringBuilder();
   foreach(char c in text){if(c>=' '&&c<='~')sb.Append(c);if(sb.Length>=max)break;}
   return sb.ToString().Trim();
  }
 }

 // ---------- Disconnect analysis ----------
 public sealed class DisconnectRow { public DateTime At;public string Key;public int? Percent;public bool? Charging;public double? Seconds;public string Origin; }
 public sealed class DisconnectSummary {
  public int Count,ReceiverCount,ReturnedCount,WhileChargingCount;
  public double LongestSeconds,MedianSeconds,TotalSeconds,ObservedMinutes,PerHour;
  public List<DisconnectRow> Rows=new List<DisconnectRow>();
 }
 public static class Analysis {
  public static DateTime ParseTime(string iso){DateTimeOffset t;return DateTimeOffset.TryParse(iso,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out t)?t.LocalDateTime:DateTime.MinValue;}
  public static DisconnectSummary Summarize(IList<UsbEvent> events,DateTime start,DateTime end){
   var s=new DisconnectSummary();
   var ordered=events.Where(e=>e!=null&&e.Key!=null).OrderBy(e=>ParseTime(e.Time)).ToList();
   for(int i=0;i<ordered.Count;i++){
    var e=ordered[i];if(e.Kind!="removed")continue;
    if(e.Key==Ids.ReceiverKey){s.ReceiverCount++;continue;}
    if(e.Key!=Ids.HeadsetUsbKey)continue;
    var row=new DisconnectRow{At=ParseTime(e.Time),Key=e.Key,Percent=e.Percent,Charging=e.Charging,Origin=e.Origin};
    for(int j=i+1;j<ordered.Count;j++){
     if(ordered[j].Key==e.Key&&ordered[j].Kind=="arrived"){row.Seconds=Math.Max(0,(ParseTime(ordered[j].Time)-row.At).TotalSeconds);break;}
     if(ordered[j].Key==e.Key&&ordered[j].Kind=="removed")break;
    }
    s.Rows.Add(row);
   }
   s.Count=s.Rows.Count;s.WhileChargingCount=s.Rows.Count(r=>r.Charging==true);
   var times=s.Rows.Where(r=>r.Seconds.HasValue).Select(r=>r.Seconds.Value).OrderBy(x=>x).ToList();
   s.ReturnedCount=times.Count;
   if(times.Count>0){s.LongestSeconds=times[times.Count-1];s.TotalSeconds=times.Sum();int m=times.Count/2;s.MedianSeconds=times.Count%2==1?times[m]:(times[m-1]+times[m])/2.0;}
   s.ObservedMinutes=Math.Max(0,(end-start).TotalMinutes);
   s.PerHour=s.ObservedMinutes>=1?s.Count*60.0/s.ObservedMinutes:0;
   return s;
  }
 }

 // ---------- Privacy ----------
 public static class Privacy {
  // Masks the Windows user name, machine name and user-profile paths. Placeholders are inserted as control-character
  // tokens first so a short or generic name (for example "user") cannot corrupt an already inserted placeholder.
  public static string Mask(string text){
   if(string.IsNullOrEmpty(text))return text;
   const string P="\u0001P\u0001",U="\u0001U\u0001",M="\u0001M\u0001";
   string r=text;string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
   if(!string.IsNullOrEmpty(profile))r=Regex.Replace(r,Regex.Escape(profile),P,RegexOptions.IgnoreCase);
   r=Regex.Replace(r,@"(?i)([A-Za-z]:\\Users\\)[^\\\s""'<>|\u0001]+","${1}"+U);
   if(!string.IsNullOrEmpty(Environment.UserName)&&Environment.UserName.Length>2)r=Regex.Replace(r,Regex.Escape(Environment.UserName),U,RegexOptions.IgnoreCase);
   if(!string.IsNullOrEmpty(Environment.MachineName)&&Environment.MachineName.Length>2)r=Regex.Replace(r,Regex.Escape(Environment.MachineName),M,RegexOptions.IgnoreCase);
   return r.Replace(P,"%USERPROFILE%").Replace(U,"<user>").Replace(M,"<machine>");
  }
  // A copy of the session whose free-text fields are masked, for anything that leaves the PC.
  public static SessionData MaskSession(SessionData s){
   var ser=new JavaScriptSerializer{MaxJsonLength=100000000};
   var c=ser.Deserialize<SessionData>(ser.Serialize(s));
   foreach(var n in c.Notes)n.Text=Mask(n.Text);
   foreach(var b in c.Batteries)b.Detail=Mask(b.Detail);
   foreach(var v in c.Versions){v.Detail=Mask(v.Detail);v.Version=Mask(v.Version);}
   foreach(var d in c.Devices){d.Product=Mask(d.Product);d.Manufacturer=Mask(d.Manufacturer);d.Error=Mask(d.Error);}
   return c;
  }
  static string Reg(string path,string name){try{using(var k=Registry.LocalMachine.OpenSubKey(path)){if(k==null)return "";object v=k.GetValue(name);return v==null?"":Convert.ToString(v);}}catch{return "";}}
  public static string WindowsInfo(){
   const string p=@"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
   string name=Reg(p,"ProductName"),ver=Reg(p,"DisplayVersion"),build=Reg(p,"CurrentBuildNumber"),ubr=Reg(p,"UBR");
   var sb=new StringBuilder(string.IsNullOrEmpty(name)?"Windows":name);
   if(!string.IsNullOrEmpty(ver))sb.Append(" ").Append(ver);
   if(!string.IsNullOrEmpty(build))sb.Append(" (build ").Append(build).Append(string.IsNullOrEmpty(ubr)?"":"."+ubr).Append(")");
   return sb.ToString();
  }
 }

 // ---------- Report ----------
 public static class Reports {
  public static string FormatDuration(double seconds){
   if(seconds<0)seconds=0;var t=TimeSpan.FromSeconds(Math.Round(seconds));
   return t.TotalHours>=1?string.Format("{0}:{1:00}:{2:00}",(int)t.TotalHours,t.Minutes,t.Seconds):string.Format("{0:00}:{1:00}",t.Minutes,t.Seconds);
  }
  public static string UnitLabel(string unit,bool tr){switch(unit){case "problem":return tr?"Sorunlu kulaklık":"Problem headset";case "healthy":return tr?"Sağlam kulaklık":"Healthy headset";default:return tr?"Seçilmedi":"Not selected";}}
  public static string SourceLabel(string source,bool tr){
   switch(source){case "pc-usb":return tr?"PC USB":"PC USB";case "adapter":return tr?"Priz / USB adaptörü":"Wall / USB adapter";case "powerbank":return tr?"Powerbank":"Power bank";case "not-charging":return tr?"Şarjda değil":"Not charging";default:return tr?"Bilinmiyor":"Unknown";}
  }
  public static string Charge(bool? c,bool tr){return c==true?(tr?"şarj oluyor":"charging"):(c==false?(tr?"şarjda değil":"not charging"):(tr?"bilinmiyor":"unknown"));}
  // A plain-text report meant for a support request. It contains no serial numbers, device paths, user or machine names.
  public static string Build(SessionData s,DisconnectSummary d,bool tr,string appVersion,string windowsInfo){
   var sb=new StringBuilder();
   Func<string,string,string> T=(a,b)=>tr?a:b;
   sb.AppendLine("G PRO X Lab "+appVersion+" — "+T("Bağlantı teşhis raporu","Connection diagnostic report"));
   sb.AppendLine("Windows: "+windowsInfo);
   sb.AppendLine(T("Test grubu: ","Test group: ")+UnitLabel(s.Unit,tr)+"   "+T("Şarj kaynağı notu: ","Charging source note: ")+SourceLabel(s.Source,tr));
   var st=Analysis.ParseTime(s.Started);var en=Analysis.ParseTime(s.Ended);
   sb.AppendLine(T("Oturum: ","Session: ")+st.ToString("yyyy-MM-dd HH:mm:ss")+" → "+en.ToString("HH:mm:ss")+"  ("+d.ObservedMinutes.ToString("0.0",CultureInfo.InvariantCulture)+" "+T("dk","min")+")");
   sb.AppendLine();
   sb.AppendLine(T("ÖZET","SUMMARY"));
   sb.AppendLine("  "+T("Şarj USB (046D:0ABB) kopmaları: ","Charging USB (046D:0ABB) disconnects: ")+d.Count+"  ("+d.PerHour.ToString("0.0",CultureInfo.InvariantCulture)+" "+T("/saat","/hour")+")");
   sb.AppendLine("  "+T("Şarj olurken görülen kopma: ","Disconnects seen while charging: ")+d.WhileChargingCount);
   if(d.ReturnedCount>0)sb.AppendLine("  "+T("Geri gelme süresi — ortanca / en uzun: ","Time to reconnect — median / longest: ")+FormatDuration(d.MedianSeconds)+" / "+FormatDuration(d.LongestSeconds));
   sb.AppendLine("  "+T("Alıcı (046D:0ABA) ayrılmaları: ","Receiver (046D:0ABA) disconnects: ")+d.ReceiverCount);
   if(s.Samples.Count>0){
    var first=s.Samples[0];var last=s.Samples[s.Samples.Count-1];
    sb.AppendLine("  "+T("Pil (tahmini): ","Battery (estimated): ")+"≈"+first.Percent+"% ("+first.Millivolts+" mV) → ≈"+last.Percent+"% ("+last.Millivolts+" mV), "+s.Samples.Count+" "+T("okuma","readings"));
   } else sb.AppendLine("  "+T("Geçerli pil okuması yok.","No valid battery reading."));
   sb.AppendLine();
   sb.AppendLine(T("KOPMALAR","DISCONNECTS"));
   if(d.Rows.Count==0)sb.AppendLine("  "+T("Kayıtlı kopma yok.","No disconnect recorded."));
   foreach(var r in d.Rows){
    sb.AppendLine("  "+r.At.ToString("HH:mm:ss")+"  "+(r.Percent.HasValue?"≈"+r.Percent+"%":"—")+"  "+Charge(r.Charging,tr)+"  "+(r.Seconds.HasValue?T("geri geldi: ","back after: ")+FormatDuration(r.Seconds.Value):T("geri gelmedi / bilinmiyor","not back / unknown"))+"  ["+(r.Origin=="event"?T("olay","event"):T("örnekleme","poll"))+"]");
   }
   sb.AppendLine();
   sb.AppendLine(T("SÜRÜMLER","VERSIONS"));
   var vs=s.Versions.Where(v=>v.Version!=null).GroupBy(v=>v.Kind).Select(g=>g.Last()).ToList();
   if(vs.Count==0)sb.AppendLine("  "+T("Okunmadı.","Not read."));
   foreach(var v in vs)sb.AppendLine("  "+(v.Kind=="receiver"?T("Alıcı","Receiver"):T("Kulaklık radyo","Headset radio"))+": "+v.Version);
   sb.AppendLine();
   sb.AppendLine(T("SINIRLAR","LIMITS"));
   sb.AppendLine("  "+T("Pil yüzdesi bildirilen gerilimden hesaplanan bir tahmindir; gerçek kapasite, şarj akımı, sıcaklık veya pil sağlığı ölçülmez.","Battery percentage is an estimate calculated from the reported voltage; real capacity, charge current, temperature and battery health are not measured."));
   sb.AppendLine("  "+T("USB kopmaları, kullanıcının kabloyu çekmesini veya kabloya çarpmayı da içerir.","USB disconnects also include unplugging the cable or bumping it."));
   sb.AppendLine("  "+T("Bu rapor seri numarası, aygıt yolu, kullanıcı adı veya makine adı içermez.","This report contains no serial number, device path, user name or machine name."));
   return Privacy.Mask(sb.ToString());
  }
  public static string SamplesCsv(SessionData s){
   var sb=new StringBuilder("time,millivolts,percent_estimated,charging,source_note\r\n");
   foreach(var x in s.Samples)sb.Append(x.Time).Append(',').Append(x.Millivolts).Append(',').Append(x.Percent).Append(',').Append(x.Charging==true?"yes":(x.Charging==false?"no":"unknown")).Append(',').Append(x.Source).Append("\r\n");
   return sb.ToString();
  }
 }
}
