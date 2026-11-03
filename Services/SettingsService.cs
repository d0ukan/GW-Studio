using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dou_GW_Studio.Models;

namespace Dou_GW_Studio.Services;

public class SettingsService
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dou-GW-Studio");

    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    public string? LoadWarning { get; private set; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            LoadWarning = $"Settings file could not be read; defaults were loaded: {ex.Message}";
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);

            // Persist the complete settings snapshot and close the stream immediately.
            await File.WriteAllTextAsync(FilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Settings could not be saved: {ex.Message}", ex);
        }
    }
}
