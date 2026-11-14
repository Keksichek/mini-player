using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WidgetLauncherSetup
{
 internal static class UninstallProgram
 {
  [STAThread]static int Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   if(args.Length>=2&&args[0]=="--test-uninstall"){
    string report;int code;try{InstallEngine.Remove(args[1],true);report="PASS";code=0;}catch(Exception ex){report=ex.ToString();code=1;}
    if(args.Length>=4&&args[2]=="--report")File.WriteAllText(args[3],report);return code;
   }
   if(args.Length>=4&&args[0]=="--remove"){
    try{int parentId=Int32.Parse(args[2]);long ticks=Int64.Parse(args[3]);try{using(var parent=Process.GetProcessById(parentId)){if(parent.StartTime.ToUniversalTime().Ticks==ticks&&!parent.WaitForExit(15000))throw new IOException("Не удалось завершить предыдущий этап удаления.");}}catch(ArgumentException){}
     using(var window=new RemoveWindow(args[1]))Application.Run(window);return 0;
    }catch(Exception ex){MessageBox.Show(ex.Message,"Удаление Widget Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    finally{CleanupHelper();}
   }
   try{
    string root=InstallEngine.Normalize(AppDomain.CurrentDomain.BaseDirectory);var marker=InstallEngine.ReadMarker(root);
    if(marker.TestInstallation)throw new IOException("Для тестовой установки используйте тестовый режим удаления.");
    if(MessageBox.Show("Удалить Widget Launcher и его установленные виджеты?\n\nБудут удалены файлы лаунчера, виджеты, их настройки и ярлыки.\n\nПапка: "+root,"Удаление Widget Launcher",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return 0;
    string temporary=Path.Combine(Path.GetTempPath(),"WidgetLauncher-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);string helper=Path.Combine(temporary,"Uninstall.exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);
    using(var process=Process.GetCurrentProcess()){string arguments="--remove "+InstallEngine.Quote(root)+" "+process.Id+" "+process.StartTime.ToUniversalTime().Ticks;Process.Start(new ProcessStartInfo(helper,arguments){WorkingDirectory=temporary,UseShellExecute=false,CreateNoWindow=true});}return 0;
   }catch(Exception ex){MessageBox.Show(ex.Message,"Удаление Widget Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
  static void CleanupHelper(){try{
   string own=Assembly.GetExecutingAssembly().Location,folder=Path.GetDirectoryName(own);string parent=Path.GetDirectoryName(folder);
   if(!String.Equals(InstallEngine.Normalize(parent),InstallEngine.Normalize(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("WidgetLauncher-Uninstall-",StringComparison.Ordinal))return;
   int id=Process.GetCurrentProcess().Id;string script="Wait-Process -Id "+id+" -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+own.Replace("'","''")+"' -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+folder.Replace("'","''")+"' -ErrorAction SilentlyContinue";
   string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");Process.Start(new ProcessStartInfo(powershell,"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
  }catch{}}
 }
 internal sealed class RemoveWindow:Form
 {
  readonly string root;readonly Label status;bool busy=true;
  public RemoveWindow(string root){this.root=root;Text="Widget Launcher · Удаление";ClientSize=new Size(550,260);Ui.Prepare(this);Controls.Add(Ui.Label("Удаляем лаунчер",30,30,490,48,22,Ui.Text));status=Ui.Label("Закрываем виджеты и удаляем файлы…",32,102,480,85,10,Ui.Muted);Controls.Add(status);Shown+=Run;FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};}
  async void Run(object sender,EventArgs e){try{await Task.Run(delegate{InstallEngine.Remove(root,false);});status.Text=Directory.Exists(root)?"Лаунчер удалён. Посторонние файлы в папке сохранены.":"Лаунчер и его виджеты удалены.";}catch(Exception ex){status.Text="Удаление не завершено. "+ex.Message+" Закройте приложения и повторите удаление.";}finally{busy=false;var done=Ui.Button("Закрыть",350,192,166,true);done.Click+=delegate{Close();};Controls.Add(done);AcceptButton=done;}}
 }
}
