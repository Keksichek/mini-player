using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WidgetLauncherSetup
{
 internal static class SetupProgram
 {
  [STAThread] static int Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   if(args.Length>=2&&args[0]=="--test-install"){
    string result;int code;try{using(var zip=Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip")){if(zip==null)throw new IOException("Payload.zip missing");result=InstallEngine.Install(zip,args[1],true,delegate{});}result="PASS "+result;code=0;}catch(Exception ex){result=ex.ToString();code=1;}
    if(args.Length>=4&&args[2]=="--report")File.WriteAllText(args[3],result);return code;
   }
   using(var window=new SetupWindow()){
    if(args.Length>=2&&args[0]=="--preview"){window.PreparePreview();window.ShowInTaskbar=false;window.Opacity=0;window.Show();Application.DoEvents();using(var bitmap=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(bitmap,new Rectangle(0,0,window.Width,window.Height));bitmap.Save(args[1]);}window.Close();return 0;}
    Application.Run(window);return 0;
   }
  }
 }
 internal sealed class SetupWindow:Form
 {
  readonly TextBox destination;readonly Button browse,install,cancel;readonly Label status,pathCaption;readonly ProgressBar progress;readonly CheckBox run;
  bool busy,finished;string installedRoot;
  public SetupWindow(){
   Text="Widget Launcher · Установка";ClientSize=new Size(620,548);Ui.Prepare(this);
   Controls.Add(Ui.Label("WIDGET LAUNCHER",32,29,540,24,10,Ui.Accent));
   Controls.Add(Ui.Label("Твой рабочий стол.\nТвои виджеты.",30,70,550,96,25,Ui.Text));
   Controls.Add(Ui.Label("Мини-плеер и будущие виджеты — в одном месте.",32,181,550,27,11,Ui.Muted));
   pathCaption=Ui.Label("Куда установить лаунчер",32,234,550,26,11,Ui.Text);Controls.Add(pathCaption);
   destination=new TextBox{Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WidgetLauncher"),Location=new Point(32,271),Size=new Size(432,34),BackColor=Ui.Panel,ForeColor=Ui.Text,BorderStyle=BorderStyle.FixedSingle,Font=Ui.Font(10)};Controls.Add(destination);
   browse=Ui.Button("Обзор…",474,266,114,false);browse.Click+=Browse;Controls.Add(browse);
   Controls.Add(Ui.Label("Виджеты будут устанавливаться в подпапку Widgets.\nВсё необходимое для запуска уже входит в установщик.",32,327,552,52,10,Ui.Muted));
   progress=new ProgressBar{Location=new Point(32,398),Size=new Size(556,7),Style=ProgressBarStyle.Continuous,Visible=false};Controls.Add(progress);
   status=Ui.Label("Выберите папку и нажмите «Установить».",32,420,556,48,9,Ui.Muted);Controls.Add(status);
   run=new CheckBox{Text="Открыть лаунчер",Checked=true,Visible=false,Location=new Point(32,420),Size=new Size(540,30),Font=Ui.Font(10),ForeColor=Ui.Text};Controls.Add(run);
   cancel=Ui.Button("Отмена",32,480,122,false);cancel.Click+=delegate{Close();};Controls.Add(cancel);
   install=Ui.Button("Установить",396,480,192,true);install.Click+=Install;Controls.Add(install);AcceptButton=install;CancelButton=cancel;
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
  }
  public void PreparePreview(){destination.Text=@"D:\WidgetLauncher";ActiveControl=install;}
  void Browse(object sender,EventArgs e){using(var dialog=new FolderBrowserDialog{Description="Выберите отдельную папку для Widget Launcher",SelectedPath=destination.Text,ShowNewFolderButton=true}){if(dialog.ShowDialog(this)==DialogResult.OK)destination.Text=dialog.SelectedPath;}}
  async void Install(object sender,EventArgs e){
   if(finished){if(run.Checked){try{Process.Start(new ProcessStartInfo(Path.Combine(installedRoot,"Launcher.exe")){WorkingDirectory=installedRoot,UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"Установка завершена, но открыть лаунчер не удалось.\n"+ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);}}Close();return;}
   string requested=destination.Text.Trim();try{InstallEngine.ValidateDestination(requested);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Выберите другую папку",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
   busy=true;install.Enabled=false;cancel.Enabled=false;browse.Enabled=false;destination.Enabled=false;progress.Visible=true;
   try{
    string warning=await Task.Run(delegate{using(var zip=Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip")){if(zip==null)throw new IOException("В установщике отсутствуют файлы лаунчера.");return InstallEngine.Install(zip,requested,false,delegate(int percent,string label){BeginInvoke((Action)delegate{progress.Value=percent;status.Text=label;});});}});
    installedRoot=InstallEngine.Normalize(requested);finished=true;install.Text="Готово";cancel.Visible=false;pathCaption.Text="Лаунчер установлен";status.Visible=false;run.Visible=true;
    if(!String.IsNullOrEmpty(warning))MessageBox.Show(this,warning,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);
   }catch(Exception ex){status.Text="Установка не завершена. Можно исправить путь и повторить.";MessageBox.Show(this,"Не удалось установить лаунчер.\n\n"+ex.Message+"\n\nЕсли нет доступа к папке, выберите папку в своём профиле или на другом диске.","Установка Widget Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);cancel.Enabled=true;browse.Enabled=true;destination.Enabled=true;}
   finally{busy=false;install.Enabled=true;}
  }
 }
}
