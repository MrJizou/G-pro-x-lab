// Copyright (c) 2026 the Receiver Rescue project owner. All rights reserved. Proprietary; see LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace ProXLab {
 public static class Json {
  static readonly JavaScriptSerializer Ser=new JavaScriptSerializer{MaxJsonLength=100000000};
  public static string Serialize(object value){return Ser.Serialize(value);}
  public static T Deserialize<T>(string text){return Ser.Deserialize<T>(text);}
  // ASCII-only JSON so helper-process output survives any console code page.
  public static string Ascii(object value){
   string text=Ser.Serialize(value);var sb=new StringBuilder();
   foreach(char c in text){if(c>'\u007f')sb.Append("\\u").Append(((int)c).ToString("x4"));else sb.Append(c);}
   return sb.ToString();
  }
 }

 // Native HID access. Nothing here writes firmware or changes device state. The serial number and the interface
 // path are never read into stored data: the path stays inside the process that opens the device.
 public static class HidIo {
  struct InterfaceData{public int Size;public Guid Guid;public int Flags;public IntPtr Reserved;}
  struct Attributes{public int Size;public ushort Vendor,Product,Version;}
  [DllImport("hid.dll")]static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(ref Guid g,string en,IntPtr w,uint flags);
  [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInterfaces(IntPtr h,IntPtr dev,ref Guid g,uint i,ref InterfaceData d);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h,ref InterfaceData d,IntPtr detail,uint n,out uint need,IntPtr dev);
  [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string p,uint access,uint share,IntPtr sa,uint disposition,uint flags,IntPtr tmpl);
  [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetPreparsedData(SafeFileHandle h,out IntPtr p);
  [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_FreePreparsedData(IntPtr p);
  [DllImport("hid.dll")]static extern int HidP_GetCaps(IntPtr p,IntPtr caps);
  [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetAttributes(SafeFileHandle h,ref Attributes a);
  [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetProductString(SafeFileHandle h,IntPtr b,int len);
  [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetManufacturerString(SafeFileHandle h,IntPtr b,int len);
  [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_SetFeature(SafeFileHandle h,byte[] b,int len);
  [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)]static extern bool HidD_GetFeature(SafeFileHandle h,[In][Out] byte[] b,int len);

  public static Guid HidGuid{get{Guid g;HidD_GetHidGuid(out g);return g;}}

  public static List<DeviceInfo> Enumerate(){
   var list=new List<DeviceInfo>();Guid g=HidGuid;
   IntPtr set=SetupDiGetClassDevs(ref g,null,IntPtr.Zero,0x12);
   if(set==new IntPtr(-1))throw new IOException("HID enumeration failed ("+Marshal.GetLastWin32Error()+")");
   try{
    for(uint i=0;i<512;i++){
     var d=new InterfaceData{Size=Marshal.SizeOf(typeof(InterfaceData))};
     if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref g,i,ref d))break;
     uint need;SetupDiGetDeviceInterfaceDetail(set,ref d,IntPtr.Zero,0,out need,IntPtr.Zero);if(need<8)continue;
     IntPtr buf=Marshal.AllocHGlobal((int)need);
     try{
      Marshal.WriteInt32(buf,IntPtr.Size==8?8:6);
      if(!SetupDiGetDeviceInterfaceDetail(set,ref d,buf,need,out need,IntPtr.Zero))continue;
      string path=Marshal.PtrToStringUni(IntPtr.Add(buf,4));string key=Ids.KeyFromPath(path);if(key==null)continue;
      var info=new DeviceInfo{Path=path,Key=key,Role=Ids.RoleFor(key)};
      info.Vendor=Convert.ToInt32(key.Substring(0,4),16);info.ProductId=Convert.ToInt32(key.Substring(5,4),16);
      using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
       if(h.IsInvalid){info.Error="access "+Marshal.GetLastWin32Error();list.Add(info);continue;}
       var a=new Attributes{Size=Marshal.SizeOf(typeof(Attributes))};
       if(HidD_GetAttributes(h,ref a)){info.Vendor=a.Vendor;info.ProductId=a.Product;info.Revision=a.Version;}
       IntPtr p;
       if(HidD_GetPreparsedData(h,out p)){
        IntPtr caps=Marshal.AllocHGlobal(64);
        try{if(HidP_GetCaps(p,caps)==0x110000){info.Usage=Marshal.ReadInt16(caps,0)&0xFFFF;info.UsagePage=Marshal.ReadInt16(caps,2)&0xFFFF;info.InputLength=Marshal.ReadInt16(caps,4)&0xFFFF;info.OutputLength=Marshal.ReadInt16(caps,6)&0xFFFF;info.FeatureLength=Marshal.ReadInt16(caps,8)&0xFFFF;}}
        finally{Marshal.FreeHGlobal(caps);HidD_FreePreparsedData(p);}
       }
       IntPtr s=Marshal.AllocHGlobal(512);
       try{
        // 510 of 512 bytes are offered, so the zeroed buffer always ends with a terminator.
        for(int z=0;z<512;z+=4)Marshal.WriteInt32(s,z,0);
        if(HidD_GetProductString(h,s,510))info.Product=Proto.Clean(Marshal.PtrToStringUni(s),64);
        for(int z=0;z<512;z+=4)Marshal.WriteInt32(s,z,0);
        if(HidD_GetManufacturerString(h,s,510))info.Manufacturer=Proto.Clean(Marshal.PtrToStringUni(s),64);
        // Serial number is intentionally NOT read.
       }finally{Marshal.FreeHGlobal(s);}
      }
      list.Add(info);
     }finally{Marshal.FreeHGlobal(buf);}
    }
   }finally{SetupDiDestroyDeviceInfoList(set);}
   return list;
  }

  public static bool IsBatteryInterface(DeviceInfo d){return d.Key==Ids.ReceiverKey&&d.UsagePage==0xFF43&&d.Usage==0x0202;}
  public static bool IsVersionInterface(DeviceInfo d){return (d.Key==Ids.ReceiverKey||d.Key==Ids.HeadsetUsbKey)&&d.UsagePage==0xFF00&&d.Usage==1&&d.FeatureLength>=18&&d.FeatureLength<=4096;}

  public static async Task<BatteryReading> ReadBattery(DeviceInfo d){
   var r=new BatteryReading{Code="no-reply"};
   if(!IsBatteryInterface(d)||d.OutputLength<20||d.InputLength<7){r.Code="no-interface";return r;}
   byte[] tx=Proto.BatteryRequest(d.OutputLength);r.Request=BitConverter.ToString(tx,0,16);
   using(var h=CreateFile(d.Path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)){
    if(h.IsInvalid){r.Code="access";r.Detail=Marshal.GetLastWin32Error().ToString();return r;}
    try{
     using(var f=new FileStream(h,FileAccess.ReadWrite,4096,true))
     using(var ct=new CancellationTokenSource(900)){
      await f.WriteAsync(tx,0,tx.Length,ct.Token).ConfigureAwait(false);
      for(int i=0;i<12;i++){
       var rx=new byte[d.InputLength];int count=await f.ReadAsync(rx,0,rx.Length,ct.Token).ConfigureAwait(false);
       var parsed=Proto.ParseBattery(rx,count);
       if(parsed!=null){parsed.Request=r.Request;return parsed;}
       if(count>2&&rx[2]==0xFF){r.Code="headset-offline";return r;}
      }
     }
    }catch(OperationCanceledException){r.Code="timeout";}
    catch(Exception e){r.Code="error";r.Detail=e.Message.Trim();}
   }
   return r;
  }

  public static async Task<VersionReading> ReadRadioVersion(DeviceInfo d){
   var r=new VersionReading{Kind=d.Key==Ids.ReceiverKey?"receiver":"headset",Code="no-interface"};
   if(!IsVersionInterface(d))return r;
   using(var h=CreateFile(d.Path,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)){
    if(h.IsInvalid){r.Code="access";r.Detail=Marshal.GetLastWin32Error().ToString();return r;}
    var tx=new byte[d.FeatureLength];Array.Copy(Proto.RadioQuery,tx,Proto.RadioQuery.Length);r.Request=BitConverter.ToString(tx,0,12);
    if(!HidD_SetFeature(h,tx,tx.Length)){r.Code="send-failed";r.Detail=Marshal.GetLastWin32Error().ToString();return r;}
    await Task.Delay(500).ConfigureAwait(false);
    var rx=new byte[d.FeatureLength];rx[0]=255;
    if(!HidD_GetFeature(h,rx,rx.Length)){r.Code="no-reply";r.Detail=Marshal.GetLastWin32Error().ToString();return r;}
    r.Raw=BitConverter.ToString(rx,0,Math.Min(32,rx.Length));
    string v=Proto.ParseRadioVersion(rx);
    if(v!=null){r.Version=v;r.Code="ok";}else r.Code="unrecognized";
   }
   return r;
  }
 }

 // Runs risky device calls in a short-lived helper process (this same EXE) so a blocked driver call can be killed on timeout.
 public static class Helper {
  static string Run(string arguments,int timeoutMs){
   using(var p=new Process()){
    p.StartInfo=new ProcessStartInfo{FileName=Process.GetCurrentProcess().MainModule.FileName,Arguments=arguments,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    if(!p.Start())throw new IOException("helper start failed");
    var outTask=p.StandardOutput.ReadToEndAsync();var errTask=p.StandardError.ReadToEndAsync();
    if(!p.WaitForExit(timeoutMs)){try{p.Kill();}catch{}p.WaitForExit(1000);throw new TimeoutException("helper timeout");}
    if(!outTask.Wait(1000)||string.IsNullOrWhiteSpace(outTask.Result))throw new IOException("helper returned nothing");
    return outTask.Result;
   }
  }
  public static List<DeviceInfo> Enumerate(){return Json.Deserialize<List<DeviceInfo>>(Run("--enumerate-helper",3000));}
  public static BatteryReading Battery(){
   try{return Json.Deserialize<BatteryReading>(Run("--battery-helper",3000));}
   catch(TimeoutException){return new BatteryReading{Code="timeout"};}
   catch(Exception e){return new BatteryReading{Code="error",Detail=e.Message};}
  }
  public static VersionReading Version(string pid){
   try{return Json.Deserialize<VersionReading>(Run("--version-helper "+pid,4000));}
   catch(TimeoutException){return new VersionReading{Kind=pid=="0aba"?"receiver":"headset",Code="timeout"};}
   catch(Exception e){return new VersionReading{Kind=pid=="0aba"?"receiver":"headset",Code="error",Detail=e.Message};}
  }
 }
}
