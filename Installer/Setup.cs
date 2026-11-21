using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using File=WidgetLauncherSetup.HubFile;
using Directory=WidgetLauncherSetup.HubDirectory;

namespace WidgetLauncherSetup
{
 internal static class SetupProgram
 {
  [STAThread] static int Main(string[] args){
   InstallEngine.ConfigureRuntime();
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   if(args.Length>=2&&args[0]=="--test-install"){
    string result;int code;
    try{string root=InstallEngine.ResolveDestination(args[1]);using(var zip=Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip")){if(zip==null)throw new IOException("Payload.zip missing");result=InstallEngine.Install(zip,root,true,delegate{});}result="PASS "+root+"\n"+result;code=0;}catch(Exception ex){result=ex.ToString();code=1;}
    if(args.Length>=4&&args[2]=="--report")File.WriteAllText(args[3],result);return code;
   }
   using(var window=new SetupWindow()){
    if(args.Length>=2&&args[0]=="--preview"){
     window.PreparePreview(args.Length>2?args[2]:"");window.ShowInTaskbar=false;window.Opacity=0;window.Show();Application.DoEvents();
     using(var bitmap=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(bitmap,new Rectangle(0,0,window.Width,window.Height));bitmap.Save(args[1]);}window.Close();return 0;
    }
    Application.Run(window);return 0;
   }
  }
 }
 internal sealed class SetupWindow:Form
 {
  readonly TextBox destination;readonly Button browse,install,cancel;readonly Label status,pathCaption,target,percent,folderHint;readonly Ui.Progress progress;readonly Ui.Check run;readonly ToolTip tooltips=new ToolTip();
  bool busy,finished;string installedRoot;
  public SetupWindow(){
   Text=InstallEngine.ProductName+" · Установка";ClientSize=new Size(680,620);Ui.Prepare(this);
   Controls.Add(Ui.Label("VIBE WIDGETS HUB  /  ALPHA",36,70,608,26,10,Ui.Accent));
   Controls.Add(Ui.Label("Твой рабочий стол.\nТвои виджеты.",34,112,610,92,26,Ui.Text));
   Controls.Add(Ui.Label("Мини-плеер и будущие виджеты — в одном месте.",36,217,608,30,11,Ui.Muted));
   var card=new Ui.Surface{Location=new Point(36,269),Size=new Size(608,168),Fill=Ui.Panel,BackColor=Ui.Panel};Controls.Add(card);
   pathCaption=Ui.Label("Выберите папку",18,17,570,24,11,Ui.Text);card.Controls.Add(pathCaption);
   var field=new Ui.Surface{Location=new Point(18,54),Size=new Size(438,44),Fill=Ui.Field,Edge=Ui.Border,BackColor=Ui.Field,Radius=9};card.Controls.Add(field);
   destination=new TextBox{Text=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Location=new Point(12,12),Width=414,BackColor=Ui.Field,ForeColor=Ui.Text,BorderStyle=BorderStyle.None,Font=Ui.Font(10)};field.Controls.Add(destination);
   browse=Ui.Button("Обзор…",466,54,124,false);browse.BackColor=Ui.Field;browse.Click+=Browse;card.Controls.Add(browse);
   target=Ui.Label("",18,113,572,38,9,Ui.Muted);target.AutoEllipsis=true;card.Controls.Add(target);destination.TextChanged+=delegate{UpdateTarget();};
   folderHint=Ui.Label("Внутри выбранной папки появится VibeWidgetsHub alpha.\nЗдесь будут лаунчер, виджеты и их настройки.",36,454,608,40,9,Ui.Muted);Controls.Add(folderHint);
   progress=new Ui.Progress{Location=new Point(36,505),Size=new Size(608,7),Visible=false};Controls.Add(progress);
   status=Ui.Label("Всё необходимое для запуска уже входит в установщик.",36,524,534,25,9,Ui.Muted);Controls.Add(status);
   percent=Ui.Label("",575,524,69,25,9,Ui.Accent);percent.TextAlign=ContentAlignment.TopRight;Controls.Add(percent);
   run=new Ui.Check{Text="Открыть лаунчер",Checked=true,Visible=false,Location=new Point(36,520),Size=new Size(510,32),Font=Ui.Font(10),ForeColor=Ui.Text,BackColor=Ui.Background};Controls.Add(run);
   cancel=Ui.Button("Отмена",36,560,122,false);cancel.Click+=delegate{Close();};Controls.Add(cancel);
   install=Ui.Button("Установить",434,560,210,true);install.Click+=Install;Controls.Add(install);AcceptButton=install;CancelButton=cancel;
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};UpdateTarget();
  }
  void UpdateTarget(){try{string root=InstallEngine.ResolveDestination(destination.Text.Trim());target.Text=root;tooltips.SetToolTip(target,root);if(folderHint!=null)folderHint.Text=File.Exists(Path.Combine(root,InstallEngine.MarkerName))?"Обновление установленного лаунчера.\nВиджеты и их настройки сохранятся.":"Внутри выбранной папки появится VibeWidgetsHub alpha.\nЗдесь будут лаунчер, виджеты и их настройки.";}catch{target.Text="Выберите папку для установки лаунчера.";}}
  public void PreparePreview(string state){
   destination.Text=@"D:\Приложения";ActiveControl=install;
   if(state=="progress"){progress.Visible=true;progress.Value=46;percent.Text="46%";status.Text="Распаковываем компоненты…";install.Enabled=false;cancel.Enabled=false;destination.Enabled=false;browse.Enabled=false;Ui.SetCloseEnabled(this,false);}
   if(state=="complete"){progress.Visible=true;progress.Value=100;percent.Text="100%";status.Visible=false;run.Visible=true;install.Text="Готово";cancel.Visible=false;}
  }
  void Browse(object sender,EventArgs e){using(var dialog=new FolderBrowserDialog{Description="Выберите папку для VibeWidgetsHub alpha",SelectedPath=Directory.Exists(destination.Text)?destination.Text:Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),ShowNewFolderButton=true}){if(dialog.ShowDialog(this)==DialogResult.OK)destination.Text=dialog.SelectedPath;}}
  async void Install(object sender,EventArgs e){
   if(finished){if(run.Checked){try{Process.Start(new ProcessStartInfo(Path.Combine(installedRoot,"Launcher.exe")){WorkingDirectory=installedRoot,UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"Установка завершена, но открыть лаунчер не удалось.\n"+ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);}}Close();return;}
   string requested;try{requested=InstallEngine.ResolveDestination(destination.Text.Trim());}catch(Exception ex){MessageBox.Show(this,ex.Message,"Выберите другую папку",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
   busy=true;install.Enabled=false;cancel.Enabled=false;browse.Enabled=false;destination.Enabled=false;progress.Visible=true;Ui.SetCloseEnabled(this,false);
   try{
    string warning=await Task.Run(delegate{using(var zip=Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip")){if(zip==null)throw new IOException("В установщике отсутствуют файлы лаунчера.");return InstallEngine.Install(zip,requested,false,delegate(int value,string label){BeginInvoke((Action)delegate{progress.Value=value;percent.Text=value.ToString()+"%";status.Text=label;});});}});
    installedRoot=requested;finished=true;install.Text="Готово";cancel.Visible=false;pathCaption.Text="Лаунчер установлен";status.Visible=false;run.Visible=true;
    if(!String.IsNullOrEmpty(warning))MessageBox.Show(this,warning,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);
   }catch(Exception ex){status.Text="Установка не завершена. Можно исправить путь и повторить.";MessageBox.Show(this,"Не удалось установить лаунчер.\n\n"+ex.Message+"\n\nЕсли нет доступа к папке, выберите папку в своём профиле или на другом диске.",Text,MessageBoxButtons.OK,MessageBoxIcon.Error);cancel.Enabled=true;browse.Enabled=true;destination.Enabled=true;}
   finally{busy=false;install.Enabled=true;Ui.SetCloseEnabled(this,true);}
  }
  protected override void Dispose(bool disposing){if(disposing)tooltips.Dispose();base.Dispose(disposing);}
 }
}
