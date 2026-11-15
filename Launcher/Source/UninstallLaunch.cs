using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 Process? uninstallProcess;
 [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(int processId);
 string UninstallAttentionName=>"Local\\WidgetLauncher.Uninstall."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(manager.Root)).ToUpperInvariant())))[..16]+".Attention";
 bool FocusExistingUninstaller(){
  if(!EventWaitHandle.TryOpenExisting(UninstallAttentionName,out var attention))return false;
  using(attention){AllowSetForegroundWindow(-1);attention.Set();}return true;
 }
 async Task RequestLauncherUninstall(){
  if(FocusExistingUninstaller())return;
  if(uninstallProcess!=null&&!uninstallProcess.HasExited){
   for(int attempt=0;attempt<30;attempt++){
    if(FocusExistingUninstaller())return;
    if(uninstallProcess.HasExited)break;
    await Task.Delay(50);
   }
   if(!uninstallProcess.HasExited){SystemSounds.Exclamation.Play();return;}
  }
  uninstallProcess?.Dispose();uninstallProcess=null;
  string executable=Path.Combine(manager.Root,"Uninstall.exe");
  if(!File.Exists(executable))throw new FileNotFoundException("Программа удаления отсутствует. Переустановите лаунчер в эту же папку.");
  uninstallProcess=Process.Start(new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=manager.Root})??throw new IOException("Не удалось открыть удаление лаунчера.");
 }
}
