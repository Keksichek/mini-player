using System.Reflection;

[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
[assembly: AssemblyInformationalVersion(WidgetLauncher.BuildInfo.Version)]

namespace WidgetLauncher;

internal static class BuildInfo
{
 internal const string Version="0.3";
}
