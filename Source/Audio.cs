using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
namespace YandexMiniPlayer;
static class Audio {
 public record Entry(string Name,double Volume);
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator {}
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IEnumerator {void EnumAudioEndpoints(int flow,uint state,out IDevices devices);void GetDefaultAudioEndpoint(int flow,int role,out IDevice device);}
 [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevices {void GetCount(out uint count);void Item(uint index,out IDevice device);}
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice {void Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)] out object result);}
 [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IManager {void GetAudioSessionControl(IntPtr guid,uint flags,out IntPtr control);void GetSimpleAudioVolume(IntPtr guid,uint flags,out IntPtr volume);void GetSessionEnumerator(out ISessions sessions);}
 [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessions {void GetCount(out int count);void GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)] out object session);}
 [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IControl {
 void GetState(out int state);void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name,ref Guid context);
 void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path,ref Guid context);
 void GetGroupingParam(out Guid group);void SetGroupingParam(ref Guid group,ref Guid context);void RegisterAudioSessionNotification(IntPtr callback);void UnregisterAudioSessionNotification(IntPtr callback);
 void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);void GetProcessId(out uint pid);
 }
 [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IVolume {void SetMasterVolume(float value,ref Guid context);void GetMasterVolume(out float value);}
 [ComImport,Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMeter{void GetPeakValue(out float peak);}
 // Returning true transfers ownership of the session reference to the caller.
 static void VisitSessions(Func<string,object,bool> action){
  var enumerator=(IEnumerator)new Enumerator();
  try{
   enumerator.EnumAudioEndpoints(0,1,out var devices);
   try{
    devices.GetCount(out uint total);
    for(uint index=0;index<total;index++){
     IDevice? device=null;object? manager=null;ISessions? sessions=null;
     try{
      devices.Item(index,out device);var id=typeof(IManager).GUID;
      device.Activate(ref id,23,IntPtr.Zero,out manager);
      ((IManager)manager).GetSessionEnumerator(out sessions);sessions.GetCount(out int count);
      for(int i=0;i<count;i++){
       object? item=null;
       try{
        sessions.GetSession(i,out item);var control=(IControl)item;control.GetState(out int state);
        if(state==2)continue;control.GetProcessId(out uint pid);if(pid==0)continue;
        using var process=Process.GetProcessById((int)pid);
        if(action(process.ProcessName,item))item=null;
       }catch(ArgumentException){}catch(COMException){}catch(System.ComponentModel.Win32Exception){}
       finally{if(item!=null)Marshal.ReleaseComObject(item);}
      }
     }catch(COMException){}
     finally{if(sessions!=null)Marshal.ReleaseComObject(sessions);if(manager!=null)Marshal.ReleaseComObject(manager);if(device!=null)Marshal.ReleaseComObject(device);}
    }
   }finally{Marshal.ReleaseComObject(devices);}
  }finally{Marshal.ReleaseComObject(enumerator);}
 }
 static void Visit(Action<string,IVolume> action)=>VisitSessions((name,item)=>{action(name,(IVolume)item);return false;});
 internal sealed class PeakReader:IDisposable{
  readonly List<IMeter> meters=new();string current="";long refreshAt;
  public float Read(string selected,string source){
   string key=selected+"|"+source;
   if(current!=key||Environment.TickCount64>=refreshAt){
    Dispose();current=key;refreshAt=Environment.TickCount64+4000;
    try{VisitSessions((name,item)=>{
     bool match=selected.Length>0?string.Equals(selected,name,StringComparison.OrdinalIgnoreCase):Match(source,new[]{new Entry(name,0)})!=null;
     if(!match||item is not IMeter meter)return false;
     meters.Add(meter);return true;
    });}catch(COMException){}
   }
   float peak=0;foreach(var meter in meters){try{meter.GetPeakValue(out float value);peak=Math.Max(peak,value);}catch(COMException){}}
   return Math.Clamp(peak,0,1);
  }
  public void Dispose(){foreach(var m in meters)Marshal.ReleaseComObject(m);meters.Clear();current="";refreshAt=0;}
 }
 public static List<Entry> List(){var result=new List<Entry>();Visit((name,v)=>{v.GetMasterVolume(out float value);result.Add(new Entry(name,value*100));});return result.GroupBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).Select(g=>new Entry(g.Key,g.Average(e=>e.Volume))).ToList();}
 public static void Set(string name,double value){if(string.IsNullOrEmpty(name))throw new InvalidOperationException("Выберите приложение для громкости");int matched=0;Visit((n,v)=>{if(!string.Equals(n,name,StringComparison.OrdinalIgnoreCase))return;var context=Guid.Empty;v.SetMasterVolume((float)Math.Clamp(value/100,0,1),ref context);matched++;});if(matched==0)throw new InvalidOperationException("Аудиосессия не найдена");}
 public static string? Match(string source,IEnumerable<Entry> entries){var normalized=new string(source.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());return entries.Select(e=>e.Name).FirstOrDefault(n=>{var key=new string(n.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());if(normalized.Contains("yandex")&&normalized.Contains("music")&&(key.Contains("yandex")&&key.Contains("music")||key.Contains("яндекс")&&key.Contains("музык")))return true;return key.Length>=5&&normalized.Contains(key);});}
}
