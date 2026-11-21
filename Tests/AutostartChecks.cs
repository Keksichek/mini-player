using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WidgetLauncher;

static class AutostartChecks
{
 static int checks;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Throws(Action action,string name){try{action();}catch{Check(true,name);return;}throw new Exception(name);}
 static Widget Widget(string id)=>new(){Id=id,Name=id,Version="0.1",Executable="App/MiniPlayer.exe",Package=id+".zip",Available=true};
 static string MakeRoot(string scratch,string name,bool test=true){
  string root=Path.Combine(scratch,name);Directory.CreateDirectory(Path.Combine(root,"Catalog"));
  File.WriteAllText(Path.Combine(root,"launcher-install.json"),JsonSerializer.Serialize(new{ProductId="MiniPlayer.WidgetLauncher",InstallPath=root,InstallId=Autostart.RootHash(root),TestInstallation=test}));return root;
 }
 static void InstallFixture(WidgetManager manager,Widget widget,bool executable=true){string directory=manager.WidgetDirectory(widget);Directory.CreateDirectory(Path.Combine(directory,"App"));File.WriteAllText(Path.Combine(directory,".widget-install.json"),JsonSerializer.Serialize(widget));if(executable)File.WriteAllText(manager.Executable(widget),"fixture only");}
 sealed class RunningFixture:IDisposable{
  readonly Thread owner;readonly ManualResetEventSlim held=new(),release=new();public readonly EventWaitHandle Show;
  public RunningFixture(string name){Show=new(false,EventResetMode.AutoReset,name+".Show");owner=new Thread(()=>{using var mutex=new Mutex(true,name);held.Set();release.Wait();mutex.ReleaseMutex();});owner.Start();held.Wait();}
  public void Dispose(){release.Set();owner.Join();Show.Dispose();held.Dispose();release.Dispose();}
 }
 static async Task<int> Main(string[] args){
  try{
   string scratch=Path.GetFullPath(args[0]);Directory.CreateDirectory(scratch);
   var defaults=HubPreferences.Load(scratch);Check(defaults.AutoStartMode==0&&!defaults.LightMode&&defaults.AllowOffscreen&&defaults.SelectedWidgets.Length==0,"First launch keeps autostart off and prior offscreen behavior");
   string path=HubPreferences.FilePath(scratch);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"{\"AutoStartMode\":4,\"SelectedWidgets\":[\" Mini-Player \",\"mini-player\",\"../escape\",null],\"LightMode\":true,\"AllowOffscreen\":false,\"FutureSetting\":{\"Keep\":7}}");
   var preferences=HubPreferences.Load(scratch);Check(preferences.AutoStartMode==4&&preferences.SelectedWidgets.SequenceEqual(new[]{"mini-player"})&&preferences.LightMode&&!preferences.AllowOffscreen,"Preferences normalize widget IDs while preserving all four settings");
   preferences.AutoStartMode=3;preferences.Save(scratch);using(var json=JsonDocument.Parse(File.ReadAllText(path)))Check(json.RootElement.GetProperty("FutureSetting").GetProperty("Keep").GetInt32()==7,"Saving preserves unknown future settings");
   Check(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp").Length==0,"Atomic preferences save leaves no temporary files");
   File.WriteAllText(path,"{\"AutoStartMode\":98,\"SelectedWidgets\":null}");Check(HubPreferences.Load(scratch).AutoStartMode==0&&HubPreferences.Load(scratch).AllowOffscreen,"Unsupported modes load disabled without changing screen defaults");
   File.WriteAllText(path,"incomplete");Check(HubPreferences.Load(scratch).AutoStartMode==0,"Damaged settings do not enable startup accidentally");

   string root=MakeRoot(scratch,"Установка с пробелами");var manager=new WidgetManager(root);var a=Widget("mini-player");var b=Widget("second-widget");var broken=Widget("broken");var missing=Widget("not-installed");var upcoming=Widget("upcoming") with{Available=false};
   File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),JsonSerializer.Serialize(new[]{a,b,broken,missing,upcoming}));InstallFixture(manager,a);InstallFixture(manager,b);InstallFixture(manager,broken,false);InstallFixture(manager,upcoming);
   preferences=new(){SelectedWidgets=new[]{"second-widget","broken","not-installed","upcoming"}};
   for(int mode=0;mode<=4;mode++){
    preferences.AutoStartMode=mode;var ids=Autostart.Plan(manager,preferences).Select(widget=>widget.Id).ToArray();
    Check(mode==0?ids.Length==0:mode is 1 or 2?ids.SequenceEqual(new[]{"mini-player","second-widget"}):ids.SequenceEqual(new[]{"second-widget"}),"Mode "+mode+" launches exactly its installed widget set");
    Check(Autostart.ShowLauncher(preferences)==(mode is 1 or 4),"Mode "+mode+" has the requested launcher visibility");
   }
   Check(Autostart.ValueName(root)==Autostart.ValueName(root.ToUpperInvariant()+Path.DirectorySeparatorChar),"Startup registry identity is stable for path case and trailing slash");
   Check(Autostart.Command(root)=="\""+Path.Combine(root,"Launcher.exe")+"\" --autostart","Startup command quotes the complete launcher path");
   bool written=false;Autostart.Apply(root,preferences,(_,_)=>written=true);Check(!written,"Test installation cannot modify personal startup registration");
   string realFixture=MakeRoot(scratch,"Registry adapter fixture",false);string? valueName=null,command="pending";
   Autostart.Apply(realFixture,preferences,(name,value)=>{valueName=name;command=value;});Check(valueName==Autostart.ValueName(realFixture)&&command==Autostart.Command(realFixture),"Enabled real-install registration sends only its own Run entry to the adapter");
   preferences.AutoStartMode=0;Autostart.Apply(realFixture,preferences,(_,value)=>command=value);Check(command==null,"Disabling startup removes that installation's Run value");
   string invalid=MakeRoot(scratch,"Wrong marker");File.WriteAllText(Path.Combine(invalid,"launcher-install.json"),"{\"ProductId\":\"other\"}");Throws(()=>Autostart.Apply(invalid,preferences,(_,_)=>throw new Exception("Unexpected registry call")),"Unverified installation cannot register startup");

   string empty=MakeRoot(scratch,"No widgets");File.WriteAllText(Path.Combine(empty,"Catalog","widgets.json"),"[]");
   for(int mode=0;mode<=4;mode++){preferences.AutoStartMode=mode;preferences.Save(empty);Check(await Autostart.RunAsync(empty)==(mode is 1 or 4),"Empty catalog startup honors mode "+mode);}
   preferences.AutoStartMode=4;preferences.Save(empty);using(var overlapping=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\WidgetLauncher.Autostart."+Autostart.RootHash(empty))){Check(!await Autostart.RunAsync(empty),"Concurrent startup handoff does not create a second launcher");}
   preferences.AutoStartMode=3;preferences.SelectedWidgets=new[]{"mini-player"};preferences.Save(root);
   using(var running=new RunningFixture(manager.InstanceName(a))){Check(!await Autostart.RunAsync(root)&&!running.Show.WaitOne(0),"Widgets-only startup does not foreground an already-running widget");}
   preferences.AutoStartMode=2;preferences.Save(root);
   using(var running=new RunningFixture(manager.InstanceName(b))){Check(!await Autostart.RunAsync(root)&&!running.Show.WaitOne(0)&&File.Exists(Path.Combine(root,"Data","autostart-error.txt")),"One launch failure is logged while the remaining running widgets stay untouched");}
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
