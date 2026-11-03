using System.Diagnostics;
using System.IO;

namespace Dou_GW_Studio.Services;

public static class LogStorageService
{
    public static string GetLogDirectory()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var preferred = Path.Combine(AppContext.BaseDirectory, "Logs", date);
        if (TryEnsureWritable(preferred)) return preferred;

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GW Studio", "Logs", date);
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public static string NewCsvPath() => Path.Combine(GetLogDirectory(), $"GW_Studio_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");
    public static string NewPdfPath() => Path.Combine(GetLogDirectory(), $"GW_Studio_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
    public static string GetExportDirectory()
    {
        var preferred = Path.Combine(GetLogDirectory(), "Exports");
        Directory.CreateDirectory(preferred);
        return preferred;
    }

    public static string NewPngPath(string stem)
    {
        var safe = string.IsNullOrWhiteSpace(stem) ? "GW_Studio_Graph" : stem.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        safe = safe.Replace(' ', '_');
        return Path.Combine(GetExportDirectory(), $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
    }

    public static void OpenFolder(string? path)
    {
        var folder = string.IsNullOrWhiteSpace(path) ? GetLogDirectory() : path;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private static bool TryEnsureWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch { return false; }
    }
}
