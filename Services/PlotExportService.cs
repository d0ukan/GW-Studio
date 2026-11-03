using System.IO;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Wpf;

namespace Dou_GW_Studio.Services;

public static class PlotExportService
{
    public static string ExportPngWithDialog(PlotModel model, string baseName, int width = 1800, int height = 900)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        var directory = LogStorageService.GetExportDirectory();
        Directory.CreateDirectory(directory);
        var suggested = Path.Combine(directory, BuildFileName(baseName));

        var dialog = new SaveFileDialog
        {
            Title = "Export graph as PNG",
            Filter = "PNG Image (*.png)|*.png",
            FileName = Path.GetFileName(suggested),
            InitialDirectory = Path.GetDirectoryName(suggested)
        };

        if (dialog.ShowDialog() != true) return string.Empty;

        var path = dialog.FileName;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        var exporter = new PngExporter
        {
            Width = Math.Max(640, width),
            Height = Math.Max(360, height)
        };
        exporter.Export(model, stream);
        return path;
    }

    private static string BuildFileName(string baseName) => $"{Sanitize(baseName)}_{DateTime.Now:yyyyMMdd_HHmmss}.png";

    private static string Sanitize(string text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "GW_Studio_Graph" : text.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Replace(' ', '_');
    }
}
