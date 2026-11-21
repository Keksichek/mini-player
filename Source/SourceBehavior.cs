using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniPlayer;

public sealed partial class Player
{
 readonly SourceWindowTracker sourceWindowTracker=new(Environment.ProcessId);
 string behaviorSource="";
 bool? previousSourceVisibility;

 void UpdateSourceBehavior(){
  try{
  if(closing||auto.IsChecked!=true)return;
  string source=session?.SourceAppUserModelId??"";
  if(!String.Equals(source,behaviorSource,StringComparison.OrdinalIgnoreCase)){
   behaviorSource=source;previousSourceVisibility=null;shownByUser=false;
  }
  bool? shouldShow=sourceWindowTracker.ShouldShow(source);
  if(!shouldShow.HasValue)return;
  if(previousSourceVisibility.HasValue&&previousSourceVisibility!=shouldShow)shownByUser=false;
  previousSourceVisibility=shouldShow;
  if(shownByUser || (shouldShow.Value && SuppressPauseDisplay))return;
  if(shouldShow.Value&&!IsVisible){bool activate=ShowActivated;ShowActivated=false;Show();ShowActivated=activate;}
  else if(!shouldShow.Value&&IsVisible)Hide();
  }catch{ /* Source windows and media sessions may disappear between polls. */ }
 }
}

// Source IDs identify applications, not individual browser tabs. A restored
// window of that application takes precedence over its minimized windows.
internal sealed class SourceWindowTracker
{
 readonly int ignoredProcessId;
 readonly HashSet<IntPtr> knownWindows=new();
 readonly HashSet<int> knownProcesses=new();
 string currentSource="";
 sealed record ProcessIdentity(int Id,string Name);
 sealed record ProcessCache(long Expires,Dictionary<int,string> Processes);
 static readonly Dictionary<string,ProcessCache> cache=new(StringComparer.OrdinalIgnoreCase);
 static readonly object cacheGate=new();
 static readonly Dictionary<IntPtr,(int Pid,long Expires,string Id)> windowIds=new();
 static readonly Dictionary<string,(long Expires,HashSet<string> Names)> windowProcessNames=new(StringComparer.OrdinalIgnoreCase);

 internal SourceWindowTracker(int ignoredProcessId=0){this.ignoredProcessId=ignoredProcessId;}

 internal bool? ShouldShow(string source){
  if(!String.Equals(source,currentSource,StringComparison.OrdinalIgnoreCase)){knownWindows.Clear();knownProcesses.Clear();currentSource=source;}
  if(string.IsNullOrWhiteSpace(source))return null;
  var processes=ResolveProcesses(source);bool visibleNormal=false,visibleMinimized=false,cloaked=false;
  EnumWindows((window,_)=>{
   try{
   GetWindowThreadProcessId(window,out uint pid);
   if(pid==ignoredProcessId||!CandidateWindow(window))return true;
   bool matches=processes.ContainsKey((int)pid);
   if(!matches&&String.Equals(WindowSourceId(window,(int)pid),source,StringComparison.OrdinalIgnoreCase)){
    matches=true;string name="";try{using var process=Process.GetProcessById((int)pid);name=process.ProcessName;}catch{}
    if(name.Length>0)lock(cacheGate){if(!windowProcessNames.TryGetValue(source,out var values)||values.Expires<Environment.TickCount64)values=(Environment.TickCount64+2000,new(StringComparer.OrdinalIgnoreCase));values.Names.Add(name);windowProcessNames[source]=values;}
   }
   if(!matches&&ClassName(window)=="ApplicationFrameWindow"){
    EnumChildWindows(window,(child,unused)=>{GetWindowThreadProcessId(child,out uint childPid);if(processes.ContainsKey((int)childPid)){matches=true;return false;}return true;},IntPtr.Zero);
   }
   if(!matches)return true;
   knownProcesses.Add((int)pid);
   if(IsCloaked(window)){cloaked=true;return true;}
   if(IsWindowVisible(window)){
    knownWindows.Add(window);
    if(IsIconic(window))visibleMinimized=true;else visibleNormal=true;
   }
   return true;
   }catch{return true;}
  },IntPtr.Zero);
  if(visibleNormal)return false;
  if(visibleMinimized)return true;
  if(cloaked)return null;
  // A previously seen app may hide/destroy its window when sent to the tray.
  // Never infer a minimized state from an unrecognized source at first launch.
  return knownWindows.Count>0&&(processes.Count>0||knownProcesses.Any(ProcessAlive))?true:null;
 }

 internal static string[] ResolveProcessNames(string source){var names=ResolveProcesses(source).Values;lock(cacheGate)return names.Concat(windowProcessNames.TryGetValue(source,out var value)&&value.Expires>=Environment.TickCount64?value.Names:Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
 static bool ProcessAlive(int id){try{using var process=Process.GetProcessById(id);return !process.HasExited;}catch{return false;}}

 static Dictionary<int,string> ResolveProcesses(string source){
  if(string.IsNullOrWhiteSpace(source))return new();
  lock(cacheGate){
   if(cache.TryGetValue(source,out var previous)&&Environment.TickCount64<previous.Expires)return new(previous.Processes);
   var identities=new List<ProcessIdentity>();
   foreach(var process in Process.GetProcesses())using(process){try{identities.Add(new(process.Id,process.ProcessName));}catch{}}
   var matches=identities.Where(p=>ExecutableMatches(source,p.Name,false)).ToDictionary(p=>p.Id,p=>p.Name);
   if(matches.Count==0){
    foreach(var process in identities){
     IntPtr handle=OpenProcess(0x1000,false,(uint)process.Id);if(handle==IntPtr.Zero)continue;
     try{
      string app=ReadIdentity(handle,GetApplicationUserModelId),family=ReadIdentity(handle,GetPackageFamilyName);
      if(ApplicationIdentityMatches(source,app,family))matches[process.Id]=process.Name;
     }finally{CloseHandle(handle);}
    }
   }
   if(matches.Count==0&&!source.Contains('!'))foreach(var process in identities)if(ExecutableMatches(source,process.Name,true))matches[process.Id]=process.Name;
   cache[source]=new(Environment.TickCount64+2000,matches);
   if(cache.Count>32)foreach(var old in cache.Where(x=>x.Value.Expires<Environment.TickCount64).Select(x=>x.Key).ToArray())cache.Remove(old);
   return new(matches);
  }
 }

 internal static bool ExecutableMatches(string source,string processName,bool allowSegments){
  if(string.IsNullOrWhiteSpace(source)||string.IsNullOrWhiteSpace(processName)||source.Contains('!'))return false;
  string name=processName.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?processName[..^4]:processName;
  string id=source.Trim();if(id.Contains('\\')||id.Contains('/'))id=Path.GetFileName(id);
  if(id.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))id=id[..^4];
  string Normalize(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
  if(Normalize(id)==Normalize(name))return true;
  return allowSegments&&(id.StartsWith(name+".",StringComparison.OrdinalIgnoreCase)||id.EndsWith("."+name,StringComparison.OrdinalIgnoreCase));
 }
 internal static bool ApplicationIdentityMatches(string source,string app,string family){
  if(string.IsNullOrEmpty(source))return false;
  if(String.Equals(source,app,StringComparison.OrdinalIgnoreCase)||String.Equals(source,family,StringComparison.OrdinalIgnoreCase))return true;
  if(!string.IsNullOrEmpty(app)||string.IsNullOrEmpty(family))return false;
  int separator=source.IndexOf('!');
  return separator>0&&separator<source.Length-1&&source.IndexOf('!',separator+1)<0&&String.Equals(source[..separator],family,StringComparison.OrdinalIgnoreCase);
 }

 static bool CandidateWindow(IntPtr window){
  if(GetWindowTextLength(window)==0)return false;
  long style=GetWindowLongPtr(window,-20).ToInt64();
  if((style&0x80)!=0||(style&0x08000000)!=0)return false;
  if(GetWindow(window,4)!=IntPtr.Zero&&(style&0x40000)==0)return false;
  if(!GetWindowRect(window,out var bounds))return false;
  return IsIconic(window)||(bounds.Right-bounds.Left>=64&&bounds.Bottom-bounds.Top>=32);
 }
 static string ClassName(IntPtr window){var value=new StringBuilder(256);GetClassName(window,value,value.Capacity);return value.ToString();}
 static bool IsCloaked(IntPtr window)=>DwmGetWindowAttribute(window,14,out int value,4)==0&&value!=0;
 delegate int IdentityReader(IntPtr handle,ref uint length,StringBuilder? value);
 static string ReadIdentity(IntPtr handle,IdentityReader read){uint length=0;if(read(handle,ref length,null)!=122||length==0||length>4096)return "";var value=new StringBuilder((int)length);return read(handle,ref length,value)==0?value.ToString():"";}

 static string WindowSourceId(IntPtr window,int pid){
  if(windowIds.TryGetValue(window,out var old)&&old.Pid==pid&&Environment.TickCount64<old.Expires)return old.Id;
  string value="";
  var iid=typeof(IPropertyStore).GUID;
  if(SHGetPropertyStoreForWindow(window,ref iid,out var store)==0){
   try{var key=new PropertyKey{FormatId=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};if(store.GetValue(ref key,out var property)==0)try{if(property.Type==31&&property.Pointer!=IntPtr.Zero)value=Marshal.PtrToStringUni(property.Pointer)??"";}finally{PropVariantClear(ref property);}}
   finally{Marshal.ReleaseComObject(store);}
  }
  windowIds[window]=(pid,Environment.TickCount64+2000,value);
  if(windowIds.Count>512)foreach(var expired in windowIds.Where(x=>x.Value.Expires<Environment.TickCount64).Select(x=>x.Key).ToArray())windowIds.Remove(expired);
  return value;
 }
 [StructLayout(LayoutKind.Sequential)]struct WindowRect{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct PropertyKey{public Guid FormatId;public uint Id;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct PropertyValue{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;}
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore{
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropertyValue value);[PreserveSig]int SetValue(ref PropertyKey key,ref PropertyValue value);[PreserveSig]int Commit();
 }
 delegate bool WindowCallback(IntPtr window,IntPtr argument);
 [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr argument);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr window,WindowCallback callback,IntPtr argument);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowTextLength(IntPtr window);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int size);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out WindowRect rectangle);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetApplicationUserModelId(IntPtr handle,ref uint length,StringBuilder? value);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackageFamilyName(IntPtr handle,ref uint length,StringBuilder? value);
 [DllImport("shell32.dll")]static extern int SHGetPropertyStoreForWindow(IntPtr window,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IPropertyStore store);
 [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropertyValue value);
}
