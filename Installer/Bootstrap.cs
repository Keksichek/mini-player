using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using File=WidgetLauncherSetup.HubFile;

namespace WidgetLauncherSetup
{
 internal static class BootstrapProgram
 {
  [STAThread]static int Main(string[] args){
   InstallEngine.ConfigureRuntime();
   try{
    string root=InstallEngine.Normalize(AppDomain.CurrentDomain.BaseDirectory);
    if(Array.IndexOf(args,"--update-health")<0&&File.Exists(Path.Combine(root,".update-transaction","journal.json"))){
     string helper=Path.Combine(root,".update-transaction","old","Update.exe");if(!File.Exists(helper))helper=Path.Combine(root,"Update.exe");InstallEngine.NoLinks(helper);
     Process.Start(new ProcessStartInfo(HubPath.Disk(helper),"--recover "+InstallEngine.Quote(root)){WorkingDirectory=HubPath.Disk(root),UseShellExecute=false,CreateNoWindow=true});return 0;
    }
    string runtime=Path.Combine(root,"Runtime","dotnet.exe"),assembly=Path.Combine(root,"Launcher.dll");
    if(!File.Exists(runtime)||!File.Exists(assembly))throw new IOException("Файлы лаунчера отсутствуют. Запустите установщик повторно и выберите ту же папку.");
    var command=InstallEngine.Quote(assembly);foreach(string arg in args)command+=" "+InstallEngine.Quote(arg);
    var start=new ProcessStartInfo(HubPath.Disk(runtime),command){WorkingDirectory=HubPath.Disk(root),UseShellExecute=false,CreateNoWindow=true};
    start.EnvironmentVariables["DOTNET_ROOT"]=Path.Combine(root,"Runtime");start.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"]="0";
    Process.Start(start);return 0;
   }catch(Exception ex){MessageBox.Show(ex.Message,"Widget Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
