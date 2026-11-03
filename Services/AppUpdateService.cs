using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;

namespace Dou_GW_Studio.Services;

public sealed class AppUpdateService
{
    public const string UpdateSlug = "gw-studio";
    public const string LatestApiUrl = "https://portal.doukan.io/api/updates/gw-studio/latest";

    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient http;

    public AppUpdateService(HttpClient? httpClient = null) => http = httpClient ?? SharedClient;

    public string InstalledVersion => AppVersionInfo.DisplayVersion;

    public async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        AppLogService.Write("update check started");
        AppLogService.Write($"installed version: {InstalledVersion}");
        try
        {
            using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            checkCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await http.GetAsync(LatestApiUrl, HttpCompletionOption.ResponseHeadersRead, checkCts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(checkCts.Token).ConfigureAwait(false);
            var info = await JsonSerializer.DeserializeAsync<AppUpdateInfo>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, checkCts.Token).ConfigureAwait(false) ?? throw new InvalidDataException("Portal update response was empty or invalid.");

            if (string.IsNullOrWhiteSpace(info.Version)) throw new InvalidDataException("Portal update response does not contain a version.");
            if (string.IsNullOrWhiteSpace(info.DownloadUrl)) throw new InvalidDataException("Portal update response does not contain a download URL.");
            if (!string.IsNullOrWhiteSpace(info.Slug) && !string.Equals(info.Slug, UpdateSlug, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Portal returned an unexpected update slug: {info.Slug}");

            AppLogService.Write($"remote version: {NormalizeVersionText(info.Version)}");
            AppLogService.Write(IsUpdateAvailable(info) ? "update available" : "no update available");
            return info;
        }
        catch (Exception ex)
        {
            AppLogService.Write($"update check/download error: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    public bool IsUpdateAvailable(AppUpdateInfo info) => CompareVersions(info.Version, InstalledVersion) > 0;

    public static int CompareVersions(string left, string right) => ParseVersion(left).CompareTo(ParseVersion(right));

    public static Version ParseVersion(string value)
    {
        var text = NormalizeVersionText(value);
        var core = text.Split('-', 2)[0].Split('+', 2)[0];
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4) throw new FormatException($"Invalid version: {value}");
        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) throw new FormatException($"Invalid version: {value}");
        }
        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    public static string NormalizeVersionText(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        return text;
    }

    public async Task<string> DownloadExtractAndFindSetupAsync(AppUpdateInfo info, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var updateRoot = Path.Combine(Path.GetTempPath(), "GW Studio", "Updates", Guid.NewGuid().ToString("N"));
        var extractDir = Path.Combine(updateRoot, "extracted");
        Directory.CreateDirectory(updateRoot);
        Directory.CreateDirectory(extractDir);

        var fileName = SanitizeFileName(string.IsNullOrWhiteSpace(info.FileName) ? $"GW-Studio-Setup-v{NormalizeVersionText(info.Version)}.zip" : info.FileName);
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) fileName += ".zip";
        var zipPath = Path.Combine(updateRoot, fileName);

        try
        {
            var downloadUri = ValidateHttpsUri(info.DownloadUrl);
            AppLogService.Write($"download started: {downloadUri}");
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            downloadCts.CancelAfter(TimeSpan.FromMinutes(30));
            using var response = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? (info.SizeBytes > 0 ? info.SizeBytes : null);
            await using (var source = await response.Content.ReadAsStreamAsync(downloadCts.Token).ConfigureAwait(false))
            await using (var destination = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), downloadCts.Token).ConfigureAwait(false);
                    if (read <= 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), downloadCts.Token).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new("Downloading", received, total));
                }
            }
            AppLogService.Write($"download completed: {zipPath}");

            progress?.Report(new("Extracting", 0, null));
            ZipFile.ExtractToDirectory(zipPath, extractDir, false);
            AppLogService.Write($"extract completed: {extractDir}");

            var setup = Directory.EnumerateFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                .OrderByDescending(path => FileNameContains(path, "GW-Studio"))
                .ThenByDescending(path => FileNameContains(path, "Setup"))
                .FirstOrDefault(path => FileNameContains(path, "Setup"));

            if (setup is null) throw new FileNotFoundException("Setup.exe was not found in the downloaded update package.");
            AppLogService.Write($"setup found: {setup}");
            progress?.Report(new("Ready", total ?? 0, total));
            return setup;
        }
        catch (Exception ex)
        {
            AppLogService.Write($"update check/download error: {ex.GetType().Name}: {ex.Message}");
            BestEffortDelete(updateRoot);
            throw;
        }
    }

    public void StartSetup(string setupPath)
    {
        if (string.IsNullOrWhiteSpace(setupPath) || !File.Exists(setupPath)) throw new FileNotFoundException("The update installer could not be found.", setupPath);
        var process = Process.Start(new ProcessStartInfo(setupPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(setupPath) ?? Path.GetTempPath()
        });
        if (process is null) throw new InvalidOperationException("Windows could not start the update installer.");
        AppLogService.Write($"setup started: {setupPath}");
    }

    private static bool FileNameContains(string path, string value)
        => (Path.GetFileName(path) ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);

    private static void BestEffortDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
    }

    private static Uri ValidateHttpsUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Update download URL must be an absolute HTTPS URL.");
        return uri;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GW-Studio-Updater/1.0");
        return client;
    }

    private static string SanitizeFileName(string name)
    {
        var value = Path.GetFileName(name);
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "GW-Studio-Update.zip" : value;
    }
}
