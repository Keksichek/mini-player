using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using File=WidgetLauncherSetup.HubFile;
using Directory=WidgetLauncherSetup.HubDirectory;

namespace WidgetLauncherSetup
{
 internal static class UninstallProgram
 {
  [STAThread]static int Main(string[] args){
   InstallEngine.ConfigureRuntime();
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   if(args.Length>=2&&args[0]=="--test-uninstall"){
    string report;int code;try{InstallEngine.Remove(args[1],true);report="PASS";code=0;}catch(Exception ex){report=ex.ToString();code=1;}
    if(args.Length>=4&&args[2]=="--report")File.WriteAllText(args[3],report);return code;
   }
   if(args.Length>=5&&args[0]=="--remove"){
    try{
     string root=InstallEngine.Normalize(args[1]);var marker=InstallEngine.ReadMarker(root);if(marker.TestInstallation)throw new IOException("Тестовую установку нельзя удалить в обычном режиме.");
     Guid transfer;if(!Guid.TryParseExact(args[4],"N",out transfer))throw new IOException("Не удалось проверить передачу удаления.");
     using(var gate=UninstallGate.Attach(root))using(var window=new RemoveWindow(root)){
      gate.Bind(window,delegate{UninstallGate.BringForward(window,true);});
      using(var accepted=EventWaitHandle.OpenExisting(TransferName(root,args[4])))accepted.Set();
      int parentId=Int32.Parse(args[2]);long ticks=Int64.Parse(args[3]);
      try{using(var parent=Process.GetProcessById(parentId)){if(parent.StartTime.ToUniversalTime().Ticks==ticks&&!parent.WaitForExit(15000))throw new IOException("Не удалось завершить предыдущий этап удаления.");}}catch(ArgumentException){}
      Application.Run(window);
     }
     return 0;
    }catch(Exception ex){MessageBox.Show(ex.Message,"Удаление Widget Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    finally{CleanupHelper();}
   }
   if(args.Length>=2&&args[0]=="--preview-confirm"){
    using(var preview=new ConfirmWindow(@"D:\Приложения\VibeWidgetsHub alpha")){preview.SuppressActivation=true;preview.ShowInTaskbar=false;preview.Opacity=0;preview.Show();Application.DoEvents();using(var bitmap=new Bitmap(preview.Width,preview.Height)){preview.DrawToBitmap(bitmap,new Rectangle(0,0,preview.Width,preview.Height));bitmap.Save(args[1]);}preview.Close();}return 0;
   }
   try{
    bool test=args.Length>=2&&args[0]=="--test-confirm";
    string root=InstallEngine.Normalize(test?args[1]:AppDomain.CurrentDomain.BaseDirectory);var marker=InstallEngine.ReadMarker(root);
    if(marker.TestInstallation!=test)throw new IOException("Для тестовой установки используйте тестовый режим удаления.");
    using(var gate=new UninstallGate(root)){
     if(!gate.IsOwner){gate.Signal();if(test)Report(args,"DUPLICATE");return 0;}
     using(var window=new ConfirmWindow(root)){
      int attentionCount=0;gate.Bind(window,delegate{attentionCount++;if(!test)UninstallGate.BringForward(window,true);});
      if(test){
       window.SuppressActivation=true;window.ShowInTaskbar=false;window.Opacity=0;int expected=0,index=Array.IndexOf(args,"--expect-attention");if(index>=0&&index+1<args.Length)expected=Int32.Parse(args[index+1]);
       using(var timer=new System.Windows.Forms.Timer{Interval=100}){
        var time=Stopwatch.StartNew();timer.Tick+=delegate{if((attentionCount>=expected&&time.ElapsedMilliseconds>=500)||time.ElapsedMilliseconds>20000)window.Close();};timer.Start();Application.Run(window);timer.Stop();
       }
       Report(args,"OWNED "+attentionCount);return 0;
      }
      Application.Run(window);gate.Unbind();if(!window.Confirmed)return 0;
     }
     StartRemoval(root);return 0;
    }
   }catch(Exception ex){MessageBox.Show(ex.Message,"Удаление Widget Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
  static string TransferName(string root,string token){return "Local\\WidgetLauncher.Uninstall."+InstallEngine.Hash(root)+".Transfer."+token;}
  static void Report(string[] args,string message){int index=Array.IndexOf(args,"--report");if(index>=0&&index+1<args.Length)File.WriteAllText(args[index+1],message);}
  static void StartRemoval(string root){
   string temporary=Path.Combine(Path.GetTempPath(),"WidgetLauncher-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);string helper=Path.Combine(temporary,"Uninstall.exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);
   string token=Guid.NewGuid().ToString("N");
   using(var accepted=new EventWaitHandle(false,EventResetMode.ManualReset,TransferName(root,token)))using(var current=Process.GetCurrentProcess()){
    string arguments="--remove "+InstallEngine.Quote(root)+" "+current.Id+" "+current.StartTime.ToUniversalTime().Ticks+" "+token;
    using(var helperProcess=Process.Start(new ProcessStartInfo(helper,arguments){WorkingDirectory=temporary,UseShellExecute=false,CreateNoWindow=true})){
     var timeout=Stopwatch.StartNew();while(!accepted.WaitOne(100)){if(helperProcess==null||helperProcess.HasExited||timeout.ElapsedMilliseconds>10000)throw new IOException("Не удалось подготовить удаление. Повторите попытку.");}
    }
   }
  }
  static void CleanupHelper(){try{
   string own=Assembly.GetExecutingAssembly().Location,folder=Path.GetDirectoryName(own);string parent=Path.GetDirectoryName(folder);
   if(!String.Equals(InstallEngine.Normalize(parent),InstallEngine.Normalize(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("WidgetLauncher-Uninstall-",StringComparison.Ordinal))return;
   int id=Process.GetCurrentProcess().Id;string script="Wait-Process -Id "+id+" -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+own.Replace("'","''")+"' -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+folder.Replace("'","''")+"' -ErrorAction SilentlyContinue";
   string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");Process.Start(new ProcessStartInfo(powershell,"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
  }catch{}}
 }
 internal sealed class ConfirmWindow:Form
 {
  public bool Confirmed {get;private set;}
  public bool SuppressActivation {get;set;}
  protected override bool ShowWithoutActivation{get{return SuppressActivation;}}
  public ConfirmWindow(string root){
   Text=InstallEngine.ProductName+" · Подтверждение удаления";AccessibleName="Подтверждение удаления лаунчера";ClientSize=new Size(620,390);Ui.Prepare(this);
   Controls.Add(Ui.Label("Удалить лаунчер?",30,75,560,46,22,Ui.Text));
   Controls.Add(Ui.Label("Будут удалены лаунчер, установленные виджеты,\nих настройки и ярлыки.",32,137,556,52,11,Ui.Muted));
   var path=new Ui.Surface{Location=new Point(32,206),Size=new Size(556,66),Fill=Ui.Panel,BackColor=Ui.Panel};var label=Ui.Label(root,14,15,528,38,9,Ui.Muted);label.AutoEllipsis=true;path.Controls.Add(label);Controls.Add(path);
   Controls.Add(Ui.Label("Это действие нельзя отменить.",32,288,556,24,10,Color.FromArgb(240,162,172)));
   var cancel=Ui.Button("Отмена",32,330,132,true);cancel.TabIndex=0;cancel.Click+=delegate{Close();};Controls.Add(cancel);AcceptButton=cancel;CancelButton=cancel;
   var remove=Ui.Button("Удалить",402,330,186,false);remove.BackColor=Color.FromArgb(82,37,51);remove.ForeColor=Color.FromArgb(255,190,202);remove.TabIndex=1;remove.Click+=delegate{Confirmed=true;Close();};Controls.Add(remove);
  }
 }
 internal sealed class RemoveWindow:Form
 {
  readonly string root;readonly Label status;bool busy=true;
  public RemoveWindow(string root){this.root=root;Text=InstallEngine.ProductName+" · Удаление";ClientSize=new Size(600,320);Ui.Prepare(this);Controls.Add(Ui.Label("Удаляем лаунчер",30,74,540,48,22,Ui.Text));status=Ui.Label("Закрываем виджеты и удаляем файлы…",32,140,536,90,10,Ui.Muted);Controls.Add(status);Shown+=Run;FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};}
  async void Run(object sender,EventArgs e){try{await Task.Run(delegate{InstallEngine.Remove(root,false);});status.Text=Directory.Exists(root)?"Лаунчер удалён. Посторонние файлы в папке сохранены.":"Лаунчер и его виджеты удалены.";}catch(Exception ex){status.Text="Удаление не завершено. "+ex.Message+" Закройте приложения и повторите удаление.";}finally{busy=false;var done=Ui.Button("Закрыть",402,256,166,true);done.Click+=delegate{Close();};Controls.Add(done);AcceptButton=done;}}
 }
}
