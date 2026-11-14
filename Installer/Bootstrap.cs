using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace WidgetLauncherSetup
{
 internal static class BootstrapProgram
 {
  [STAThread]static int Main(string[] args){
   try{
    string root=AppDomain.CurrentDomain.BaseDirectory;
    string runtime=Path.Combine(root,"Runtime","dotnet.exe"),assembly=Path.Combine(root,"Launcher.dll");
    if(!File.Exists(runtime)||!File.Exists(assembly))throw new IOException("Файлы лаунчера отсутствуют. Запустите установщик повторно и выберите ту же папку.");
    var command=InstallEngine.Quote(assembly);foreach(string arg in args)command+=" "+InstallEngine.Quote(arg);
    var start=new ProcessStartInfo(runtime,command){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true};
    start.EnvironmentVariables["DOTNET_ROOT"]=Path.Combine(root,"Runtime");start.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"]="0";
    Process.Start(start);return 0;
   }catch(Exception ex){MessageBox.Show(ex.Message,"Widget Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
