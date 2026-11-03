using System.Globalization;
using System.IO;

namespace Dou_GW_Studio.Services;

public static class AppLogService
{
    private static readonly object Sync = new();

    public static string CurrentLogPath => Path.Combine(LogStorageService.GetLogDirectory(), "GW_Studio_App.log");

    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)}  {message}{Environment.NewLine}";
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CurrentLogPath)!);
                File.AppendAllText(CurrentLogPath, line);
            }
        }
        catch
        {
            // Application logging must never interfere with instrument control or startup.
        }
    }
}
