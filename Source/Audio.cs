using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
namespace YandexMiniPlayer;
static class Audio {
 public record Entry(string Name,double Volume);
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator {}
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IEnumerator {void EnumAudioEndpoints(int flow,uint state,out IntPtr devices);void GetDefaultAudioEndpoint(int flow,int role,out IDevice device);}
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
 static void Visit(Action<string,IVolume> action){object? enumerator=null,device=null,manager=null,sessions=null;try{
 enumerator=new Enumerator();((IEnumerator)enumerator).GetDefaultAudioEndpoint(0,1,out var d);device=d;var id=typeof(IManager).GUID;d.Activate(ref id,23,IntPtr.Zero,out var m);manager=m;((IManager)m).GetSessionEnumerator(out var list);sessions=list;list.GetCount(out int count);
 for(int i=0;i<count;i++){object? item=null;try{list.GetSession(i,out item);var c=(IControl)item;c.GetState(out int state);if(state==2)continue;c.GetProcessId(out uint pid);if(pid==0)continue;using var process=Process.GetProcessById((int)pid);action(process.ProcessName,(IVolume)item);}catch(ArgumentException){}finally{if(item!=null)Marshal.ReleaseComObject(item);}}
 }finally{if(sessions!=null)Marshal.ReleaseComObject(sessions);if(manager!=null)Marshal.ReleaseComObject(manager);if(device!=null)Marshal.ReleaseComObject(device);if(enumerator!=null)Marshal.ReleaseComObject(enumerator);}}
 public static List<Entry> List(){var result=new List<Entry>();Visit((name,v)=>{v.GetMasterVolume(out float value);result.Add(new Entry(name,value*100));});return result.GroupBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).Select(g=>new Entry(g.Key,g.Average(e=>e.Volume))).ToList();}
 public static void Set(string name,double value){if(string.IsNullOrEmpty(name))throw new InvalidOperationException("Выберите приложение для громкости");int matched=0;Visit((n,v)=>{if(!string.Equals(n,name,StringComparison.OrdinalIgnoreCase))return;var context=Guid.Empty;v.SetMasterVolume((float)Math.Clamp(value/100,0,1),ref context);matched++;});if(matched==0)throw new InvalidOperationException("Аудиосессия не найдена");}
 public static string? Match(string source,IEnumerable<Entry> entries){var normalized=new string(source.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());return entries.Select(e=>e.Name).FirstOrDefault(n=>{var key=new string(n.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());return key.Length>=5&&normalized.Contains(key);});}
}
