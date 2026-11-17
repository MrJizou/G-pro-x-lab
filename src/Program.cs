// Copyright (c) 2026 the Receiver Rescue project owner. All rights reserved. Proprietary; see LICENSE.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

// All native imports in this assembly are Windows system libraries.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
[assembly: AssemblyTitle("G PRO X Lab")]
[assembly: AssemblyDescription("Read-only connection diagnostics for Logitech PRO X Wireless headsets and receivers")]
[assembly: AssemblyVersion("2.0.3.0")]
[assembly: AssemblyFileVersion("2.0.3.0")]

namespace GProXLab {
 static class Program {
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  static void Assert(bool value,string message){if(!value)throw new Exception(message);}

  static void SelfTest(){
   // battery frame, reply decoding and estimate
   byte[] req=Proto.BatteryRequest(20);Assert(req.Length==20&&req[0]==0x11&&req[1]==0xFF&&req[2]==0x06&&req[3]==0x0D,"Battery request");
   var ok=Proto.ParseBattery(new byte[]{16,255,6,13,14,246,3},7);
   Assert(ok!=null&&ok.Code=="ok"&&ok.Millivolts==3830&&ok.Percent==50&&ok.Charging==true,"Battery decode");
   Assert(Proto.ParseBattery(new byte[]{16,255,5,13,14,246,3},7)==null,"Unrelated response accepted");
   Assert(Proto.ParseBattery(new byte[]{16,255,6,13,0,0,1},7).Percent==null,"Offline voltage accepted");
   Assert(Proto.ParseBattery(new byte[]{16,255,6,13,14,246,1},7).Charging==false,"Not-charging state");
   Assert(Proto.ParseBattery(new byte[]{16,255,6,13,14,246,7},7).Charging==null,"Unknown charge state mislabelled");
   Assert(Proto.ParseBattery(new byte[]{16,255,6},3)==null,"Short frame accepted");
   Assert(Proto.EstimatePercent(500)==null&&Proto.EstimatePercent(5001)==null&&Proto.EstimatePercent(4150)==100&&Proto.EstimatePercent(3320)==0&&Proto.EstimatePercent(3830)==50,"Estimate bounds");
   int prev=-1;for(int mv=3320;mv<=4150;mv+=10){int p=Proto.EstimatePercent(mv).Value;Assert(p>=prev,"Estimate must not decrease with voltage");prev=p;}
   // radio version reply
   var rx=new byte[20];rx[0]=255;rx[3]=254;rx[4]=7;byte[] ver=Encoding.ASCII.GetBytes("V2.03\r\nX");Array.Copy(ver,0,rx,9,ver.Length);
   Assert(Proto.ParseRadioVersion(rx)=="V2.03","Radio version decode");rx[3]=0;Assert(Proto.ParseRadioVersion(rx)==null,"Wrong radio reply accepted");
   // disconnect analysis
   DateTime t0=new DateTime(2026,11,16,12,0,0,DateTimeKind.Local);
   var ev=new List<UsbEvent>{
    new UsbEvent{Time=t0.AddMinutes(5).ToString("o"),Key=Ids.HeadsetUsbKey,Kind="removed",Origin="event",Percent=40,Charging=true},
    new UsbEvent{Time=t0.AddMinutes(5).AddSeconds(10).ToString("o"),Key=Ids.HeadsetUsbKey,Kind="arrived",Origin="event"},
    new UsbEvent{Time=t0.AddMinutes(20).ToString("o"),Key=Ids.HeadsetUsbKey,Kind="removed",Origin="poll",Percent=45,Charging=false},
    new UsbEvent{Time=t0.AddMinutes(21).ToString("o"),Key=Ids.ReceiverKey,Kind="removed",Origin="event"},
    new UsbEvent{Time=t0.AddMinutes(30).ToString("o"),Key=Ids.HeadsetUsbKey,Kind="removed",Origin="event",Percent=50,Charging=true},
    new UsbEvent{Time=t0.AddMinutes(30).AddSeconds(30).ToString("o"),Key=Ids.HeadsetUsbKey,Kind="arrived",Origin="event"}};
   var sum=Analysis.Summarize(ev,t0,t0.AddMinutes(60));
   Assert(sum.Count==3&&sum.ReceiverCount==1&&sum.ReturnedCount==2&&sum.WhileChargingCount==2,"Disconnect counts");
   Assert(Math.Abs(sum.LongestSeconds-30)<0.5&&Math.Abs(sum.MedianSeconds-20)<0.5&&Math.Abs(sum.PerHour-3)<0.01,"Disconnect durations");
   Assert(Analysis.Summarize(new List<UsbEvent>(),t0,t0.AddMinutes(10)).Count==0,"Empty analysis");
   Assert(Reports.FormatDuration(0)=="00:00"&&Reports.FormatDuration(61)=="01:01"&&Reports.FormatDuration(3661)=="1:01:01"&&Reports.FormatDuration(-3)=="00:00","Duration formatting");
   // privacy: nothing identifying is stored or exported
   Assert(!typeof(DeviceInfo).GetFields().Any(f=>f.Name.IndexOf("serial",StringComparison.OrdinalIgnoreCase)>=0)&&!typeof(DeviceInfo).GetProperties().Any(p=>p.Name.IndexOf("serial",StringComparison.OrdinalIgnoreCase)>=0),"Device model must not carry a serial number");
   string devJson=Json.Serialize(new DeviceInfo{Key=Ids.ReceiverKey,Role="receiver",Path=@"\\?\hid#vid_046d&pid_0aba&mi_03#7&1234&0&0000#{guid}"});
   Assert(!devJson.Contains("hid#")&&!devJson.Contains("Path")&&!devJson.Contains("1234"),"Interface path must never be serialized");
   string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),masked=Privacy.Mask("a "+profile+"\\x b C:\\Users\\SomeoneElse\\y c "+Environment.UserName+" d "+Environment.MachineName);
   Assert(!masked.Contains(profile)&&!masked.Contains("SomeoneElse")&&masked.Contains("%USERPROFILE%")&&masked.Contains("<user>"),"Personal data masking");
   string rest=masked.Replace("%USERPROFILE%","").Replace("<user>","").Replace("<machine>","");
   if(Environment.UserName.Length>2)Assert(rest.IndexOf(Environment.UserName,StringComparison.OrdinalIgnoreCase)<0,"User name must be masked");
   // session round trip and report
   var s=new SessionData{AppVersion="9.9.9",Started=t0.ToString("o"),Ended=t0.AddMinutes(60).ToString("o"),Unit="problem",Source="pc-usb",Events=ev};
   s.Samples.Add(new Sample{Time=t0.ToString("o"),Millivolts=3715,Percent=Proto.EstimatePercent(3715).Value,Charging=true,Source="pc-usb"});s.Samples.Add(new Sample{Time=t0.AddMinutes(60).ToString("o"),Millivolts=3864,Percent=Proto.EstimatePercent(3864).Value,Charging=true,Source="pc-usb"});
   s.Notes.Add(new NoteEntry{Text="note "+profile});
   var back=Json.Deserialize<SessionData>(Json.Serialize(s));Assert(back.Events.Count==6&&back.Samples.Count==2&&back.Unit=="problem"&&back.Events[0].Percent==40,"Session round trip");
   foreach(bool tr in new[]{true,false}){
    string rep=Reports.Build(s,sum,tr,"9.9.9","Windows test");
    Assert(rep.Contains("G PRO X Lab 9.9.9")&&rep.Contains("046D:0ABB")&&rep.Contains("(3")&&!rep.Contains(profile),"Report content and masking ("+(tr?"TR":"EN")+")");
   }
   Assert(Reports.SamplesCsv(s).Split('\n').Length>=3,"CSV export");
   // exported session is masked; the in-memory session is left untouched
   s.Batteries.Add(new BatteryReading{Code="error",Detail="failed at "+profile+"\\x"});
   string exported=Json.Serialize(Privacy.MaskSession(s));
   Assert(!exported.Contains(profile.Replace("\\","\\\\"))&&!exported.Contains(profile)&&exported.Contains("%USERPROFILE%")&&s.Notes[0].Text.Contains(profile),"Exported session must be masked");
   // text that comes from a device is cleaned and limited
   string cleaned=Proto.Clean("V1\u0007é\u001b[31m"+new string('A',100),32);
   Assert(cleaned.Length==32&&cleaned.StartsWith("V1[31m")&&cleaned.IndexOf('\u0007')<0,"Device text cleaning");
   var long_=new byte[200];long_[0]=255;long_[3]=254;long_[4]=7;for(int i=9;i<200;i++)long_[i]=86;long_[12]=7;
   string lv=Proto.ParseRadioVersion(long_);Assert(lv!=null&&lv.Length<=32&&lv.IndexOf('\u0007')<0,"Radio version must be cleaned and limited");
   Console.WriteLine("PASS: battery decode and estimate, radio-version decode, disconnect analysis, duration formatting, no serial number or device path stored, personal-data masking, session round trip, TR/EN report, CSV, masked session export, device-text cleaning.");
  }

  static int UiTest(){
   int bad=0,runs=0;
   foreach(bool tr in new[]{true,false})foreach(int width in new[]{780,1100,1500}){
    using(var f=new MainForm(true,tr)){f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-30000,-30000);f.ShowInTaskbar=false;f.Show();f.Width=Math.Max(f.MinimumSize.Width,width);f.Height=900;Application.DoEvents();Application.DoEvents();
     foreach(string problem in f.CheckLayout()){Console.WriteLine(problem);bad++;}runs++;f.Close();}
   }
   // device-change handling: the real notification handler is fed synthetic USB arrival/removal messages
   using(var f=new MainForm(false,true)){
    string path0abb=@"\\?\hid#vid_046d&pid_0abb&mi_03#7&1a2b3c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    if(f.NotificationCount<2){Console.WriteLine("Device notifications were not registered");bad++;}
    f.TestDeviceMessage(path0abb,false);f.TestDeviceMessage(path0abb,false);f.TestDeviceMessage(path0abb,true);f.TestDeviceMessage(@"\\?\hid#vid_1234&pid_5678#x",false);f.TestDeviceMessage(@"\\?\hid#vid_046d&pid_0aba&mi_03#x",false);
    var sum=f.CurrentSummary();
    if(f.EventCount!=3||sum.Count!=1||sum.ReceiverCount!=1||!sum.Rows[0].Seconds.HasValue){Console.WriteLine("Device-change handling: unexpected result (events="+f.EventCount+", disconnects="+sum.Count+", receiver="+sum.ReceiverCount+")");bad++;}
    runs++;f.Close();
   }
   Console.WriteLine(bad==0?"PASS: layout self-check ("+runs+" windows, TR/EN, 3 widths, 4 views): no clipped text, overflow or overlapping blocks; USB arrival/removal handling verified with synthetic device-change messages.":"FAIL: "+bad+" problem(s).");
   return bad==0?0:1;
  }

  [STAThread]static int Main(string[] args){
   try{Console.OutputEncoding=new UTF8Encoding(false);}catch{}
   try{
    string a0=args.Length>0?args[0]:"";
    if(a0=="--self-test"){SelfTest();return 0;}
    if(a0=="--enumerate-helper"){Console.WriteLine(Json.Ascii(HidIo.Enumerate()));return 0;}
    if(a0=="--battery-helper"){
     BatteryReading r;var t=HidIo.Enumerate().Where(d=>HidIo.IsBatteryInterface(d)).ToList();
     if(t.Count==0)r=new BatteryReading{Code="no-interface"};else if(t.Count>1)r=new BatteryReading{Code="multiple"};else r=HidIo.ReadBattery(t[0]).GetAwaiter().GetResult();
     Console.WriteLine(Json.Ascii(r));return 0;
    }
    if(a0=="--version-helper"&&args.Length==2){
     string key=args[1].ToLowerInvariant()=="0aba"?Ids.ReceiverKey:Ids.HeadsetUsbKey;string kind=key==Ids.ReceiverKey?"receiver":"headset";VersionReading r;
     var t=HidIo.Enumerate().Where(d=>d.Key==key&&HidIo.IsVersionInterface(d)).ToList();
     if(t.Count!=1)r=new VersionReading{Kind=kind,Code="no-interface"};else r=HidIo.ReadRadioVersion(t[0]).GetAwaiter().GetResult();
     Console.WriteLine(Json.Ascii(r));return 0;
    }
    if(a0=="--scan"){var l=HidIo.Enumerate();Console.WriteLine("Receiver (046D:0ABA) interfaces: "+l.Count(d=>d.Key==Ids.ReceiverKey)+"; headset USB (046D:0ABB) interfaces: "+l.Count(d=>d.Key==Ids.HeadsetUsbKey)+"; bootloader interfaces: "+l.Count(d=>d.Key==Ids.ReceiverBootKey||d.Key==Ids.HeadsetBootKey)+"; battery interfaces: "+l.Count(d=>HidIo.IsBatteryInterface(d))+"; version interfaces: "+l.Count(d=>HidIo.IsVersionInterface(d)));return 0;}
    try{SetProcessDPIAware();}catch{}
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    if(a0=="--ui-test")return UiTest();
    if(a0=="--render-preview"&&args.Length>=2){
     bool en=args.Contains("--en");int view=0;int vi=Array.IndexOf(args,"--view");if(vi>=0&&vi+1<args.Length)int.TryParse(args[vi+1],out view);
     using(var f=new MainForm(true,!en)){f.Show();f.Width=1180;f.Height=1300;f.SetView(view);Application.DoEvents();Application.DoEvents();using(var image=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new Rectangle(Point.Empty,f.Size));image.Save(Path.GetFullPath(args[1]));}f.Close();}
     return 0;
    }
    bool tr2=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="tr";
    Application.Run(new MainForm(a0=="--demo",tr2));return 0;
   }catch(Exception e){if(args.Length>0)Console.Error.WriteLine(e.Message);else MessageBox.Show(e.Message,"G PRO X Lab",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
