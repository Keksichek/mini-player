using System;
using System.IO;
static class LauncherRequestRecorder
{
 [STAThread] static void Main(string[] args){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-request.txt"),String.Join("|",args));}
}
