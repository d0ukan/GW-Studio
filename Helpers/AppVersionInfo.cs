using System.Reflection;

namespace Dou_GW_Studio.Helpers;

public static class AppVersionInfo
{
    public static string DisplayVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v is null) return "0.0.0";
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    public static string DisplayVersionWithPrefix => "v" + DisplayVersion;
}
