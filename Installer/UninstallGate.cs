using System;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WidgetLauncherSetup
{
 internal sealed class UninstallGate:IDisposable
 {
  readonly EventWaitHandle attention;RegisteredWaitHandle listener;bool pending;int generation;
  public readonly bool IsOwner;
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
  [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(int processId);
  public static string Name(string root){return "Local\\WidgetLauncher.Uninstall."+InstallEngine.Hash(root)+".Attention";}
  public UninstallGate(string root){bool created;attention=new EventWaitHandle(false,EventResetMode.AutoReset,Name(root),out created);IsOwner=created;}
  UninstallGate(EventWaitHandle existing){attention=existing;IsOwner=true;}
  public static UninstallGate Attach(string root){return new UninstallGate(EventWaitHandle.OpenExisting(Name(root)));}
  public void Signal(){AllowSetForegroundWindow(-1);attention.Set();}
  public void Bind(Form window,Action onAttention){
   Unbind();int binding=generation;var handle=window.Handle;
   window.Shown+=delegate{if(binding==generation&&pending&&!window.IsDisposed){pending=false;onAttention();}};
   listener=ThreadPool.RegisterWaitForSingleObject(attention,delegate{
    try{if(!window.IsDisposed&&window.IsHandleCreated)window.BeginInvoke((Action)delegate{if(binding!=generation||window.IsDisposed)return;if(!window.Visible){pending=true;return;}onAttention();});}catch(InvalidOperationException){}
   },null,Timeout.Infinite,false);
  }
  public static void BringForward(Form window,bool sound){
   if(sound)SystemSounds.Exclamation.Play();
   if(window.IsDisposed)return;if(window.WindowState==FormWindowState.Minimized)window.WindowState=FormWindowState.Normal;
   window.BringToFront();window.Activate();SetForegroundWindow(window.Handle);
  }
  public void Unbind(){generation++;if(listener!=null){listener.Unregister(null);listener=null;}pending=false;}
  public void Dispose(){Unbind();attention.Dispose();}
 }
}
