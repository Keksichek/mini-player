using System;
using WidgetLauncherSetup;

static class UninstallGateChecks
{
 static int count;
 static void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
 static int Main(string[] args){try{
  InstallEngine.ConfigureRuntime();string root=args[0];
  var owner=new UninstallGate(root);Check(owner.IsOwner,"First confirmation owns the installation gate");
  using(var duplicate=new UninstallGate(root))Check(!duplicate.IsOwner,"Second confirmation cannot own the same installation");
  using(var helper=UninstallGate.Attach(root)){
   owner.Dispose();
   using(var duringHandoff=new UninstallGate(root))Check(!duringHandoff.IsOwner,"Helper retains singleton ownership after installed stub exits");
  }
  using(var reopened=new UninstallGate(root))Check(reopened.IsOwner,"Closing helper releases confirmation gate");
  using(var first=new UninstallGate(root))using(var other=new UninstallGate(root+"-other"))Check(other.IsOwner,"Another installation has independent confirmation state");
  Console.WriteLine("Gate checks: "+count+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
