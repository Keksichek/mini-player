using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Controls;
using MiniPlayer;
using Forms=System.Windows.Forms;

// The production behavior module is compiled unchanged against this UI/session
// seam. Window state and application identity are still probed through Windows.
namespace MiniPlayer
{
 public sealed partial class Player
 {
  internal sealed record Session(string SourceAppUserModelId);
  internal bool closing,shownByUser;
  internal readonly CheckBox auto=new(){IsChecked=true};
  internal Session? session;
  internal bool IsVisible {get;private set;}=true;
  internal bool ShowActivated {get;set;}=true;
  internal bool? ActivationOnLastShow;
  public Player(){sourceWindowTracker=new SourceWindowTracker();}
  internal void Show(){ActivationOnLastShow=ShowActivated;IsVisible=true;}
  internal void Hide(){IsVisible=false;}
  internal void Tick()=>UpdateSourceBehavior();
 }
}

internal static class SourceBehaviorChecks
{
 static int count;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Pump(){Forms.Application.DoEvents();Thread.Sleep(20);Forms.Application.DoEvents();}
 sealed class SourceForm:Forms.Form
 {
  public SourceForm(string title){Text=title;Width=320;Height=180;StartPosition=Forms.FormStartPosition.Manual;Left=220;Top=180;Opacity=0;ShowInTaskbar=true;}
  protected override bool ShowWithoutActivation=>true;
 }
 [STAThread] static int Main(){
  try{
   Forms.Application.EnableVisualStyles();
   Check(SourceWindowTracker.ExecutableMatches("spotify.exe","Spotify",false),"Classic executable source matches exact process");
   Check(SourceWindowTracker.ExecutableMatches(@"C:\Apps\VLC.exe","vlc",false),"Executable path and short name are supported");
   Check(!SourceWindowTracker.ExecutableMatches("chromedriver.exe","chrome",true),"Similar process substring does not match");
   Check(SourceWindowTracker.ExecutableMatches("Chrome.123ABC","chrome",true),"Browser application IDs can use a bounded process prefix");
   Check(!SourceWindowTracker.ExecutableMatches("Package_family!App","App",true),"Packaged identity is never guessed from executable token");
   Check(SourceWindowTracker.ApplicationIdentityMatches("Package_family!App","package_family!APP","Package_family"),"Packaged app identity matches ignoring case");
   Check(SourceWindowTracker.ApplicationIdentityMatches("Package_family","Package_family!App","Package_family"),"Package family source is supported");
   Check(SourceWindowTracker.ApplicationIdentityMatches("Package_family!App","","PACKAGE_FAMILY"),"Missing app identity falls back to exact known package family");
   Check(!SourceWindowTracker.ApplicationIdentityMatches("Package_family!App","Package_family!Other","Package_family"),"Known different app in the same family does not match");
   Check(!SourceWindowTracker.ApplicationIdentityMatches("Other_family!App","","Package_family"),"Missing app identity never falls back to an unrelated family");
   Check(!SourceWindowTracker.ApplicationIdentityMatches("Package_family!","","Package_family"),"Empty packaged application suffix does not match");
   Check(!SourceWindowTracker.ApplicationIdentityMatches("Package_family!App!Other","","Package_family"),"Malformed packaged application identity does not match");
   Check(!SourceWindowTracker.ApplicationIdentityMatches("Other_family!App","Package_family!App","Package_family"),"Unrelated package cannot affect player behavior");

   string source="Vibe.SourceBehavior."+Guid.NewGuid().ToString("N"),other="Vibe.OtherSource."+Guid.NewGuid().ToString("N");
   using var first=new SourceForm("Source behavior first");using var second=new SourceForm("Source behavior second");using var unrelated=new SourceForm("Unrelated source");
   SetSource(first.Handle,source);SetSource(second.Handle,source);SetSource(unrelated.Handle,other);
   first.Show();second.Show();unrelated.Show();Pump();
   var tracker=new SourceWindowTracker();
   Check(tracker.ShouldShow(source)==false,"Restored selected source hides the player");
   first.WindowState=Forms.FormWindowState.Minimized;Pump();
   Check(tracker.ShouldShow(source)==false,"Another restored source window keeps the player hidden");
   second.WindowState=Forms.FormWindowState.Minimized;Pump();
   Check(tracker.ShouldShow(source)==true,"All selected source windows minimized shows the player");
   Check(SourceWindowTracker.ResolveProcessNames(source).Contains(Process.GetCurrentProcess().ProcessName,StringComparer.OrdinalIgnoreCase),"Custom window application identity resolves its audio process name");
   first.WindowState=Forms.FormWindowState.Normal;Pump();
   Check(tracker.ShouldShow(source)==false,"Restoring selected source hides the player again");
   first.Hide();second.Hide();Pump();
   Check(tracker.ShouldShow(source)==true,"Previously identified source hidden to tray shows the player");
   Check(new SourceWindowTracker().ShouldShow(source)==null,"Hidden windows at first launch do not imply a minimized source");
   Check(new SourceWindowTracker(Environment.ProcessId).ShouldShow(source)==null,"The mini-player process can be excluded from source tracking");
   Check(tracker.ShouldShow(other)==false,"Changing selected source reads the new source windows");
   Check(tracker.ShouldShow("Vibe.Unknown."+Guid.NewGuid().ToString("N"))==null,"Unknown source preserves visibility");

   first.Show();second.Show();first.WindowState=second.WindowState=Forms.FormWindowState.Normal;Pump();
   var player=new Player{session=new(source)};player.Tick();Check(!player.IsVisible,"Auto behavior applies to the selected session");
   player.shownByUser=true;player.Show();player.Tick();Check(player.IsVisible,"Manual show overrides unchanged source state");
   first.WindowState=second.WindowState=Forms.FormWindowState.Minimized;Pump();player.Tick();
   Check(player.IsVisible&&!player.shownByUser,"Source transition resumes auto behavior after manual show");
   player.shownByUser=true;player.Hide();player.Tick();Check(!player.IsVisible,"Manual hide does not immediately pop the player back");
   first.WindowState=Forms.FormWindowState.Normal;Pump();player.Tick();Check(!player.IsVisible&&!player.shownByUser,"Restoring source clears manual hide while keeping player hidden");
   first.WindowState=Forms.FormWindowState.Minimized;Pump();player.Tick();Check(player.IsVisible,"Next minimize shows the player after manual hide");
   Check(player.ActivationOnLastShow==false,"Auto show does not steal keyboard focus");
   player.auto.IsChecked=false;first.WindowState=Forms.FormWindowState.Normal;Pump();player.Tick();Check(player.IsVisible,"Disabled auto behavior leaves player visibility alone");
   player.auto.IsChecked=true;player.shownByUser=true;player.session=new(other);player.Tick();Check(!player.IsVisible&&!player.shownByUser,"Selecting a different app resets the manual override");
   player.Show();player.session=null;player.Tick();Check(player.IsVisible,"Missing source preserves current player visibility");
   Console.WriteLine("TOTAL "+count+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
 [StructLayout(LayoutKind.Sequential)]struct PropertyKey{public Guid FormatId;public uint Id;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct PropertyValue{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;}
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore{
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropertyValue value);[PreserveSig]int SetValue(ref PropertyKey key,ref PropertyValue value);[PreserveSig]int Commit();
 }
 [DllImport("shell32.dll")]static extern int SHGetPropertyStoreForWindow(IntPtr window,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IPropertyStore store);
 static void SetSource(IntPtr window,string id){var iid=typeof(IPropertyStore).GUID;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window,ref iid,out var store));try{var key=new PropertyKey{FormatId=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};var value=new PropertyValue{Type=31,Pointer=Marshal.StringToCoTaskMemUni(id)};try{Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));Marshal.ThrowExceptionForHR(store.Commit());}finally{Marshal.FreeCoTaskMem(value.Pointer);}}finally{Marshal.ReleaseComObject(store);}}
}
