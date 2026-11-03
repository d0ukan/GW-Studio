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
    public const string Owner = "d0ukan";
    public const string Repo = "GW-Studio";
    public const string LatestApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{Owner}/{Repo}/releases/latest";

    /// <summary>
    /// True when running from the Portable package (it ships an empty portable.txt next to the exe).
    /// Portable copies are never updated through the installer; the user is sent to the releases page instead.
    /// </summary>
    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public void OpenReleasesPage()
    {
        Process.Start(new ProcessStartInfo(ReleasesPageUrl) { UseShellExecute = true });
        AppLogService.Write($"portable build: opened releases page {ReleasesPageUrl}");
    }

    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient http;

    public AppUpdateService(HttpClient? httpClient = null) => http = httpClient ?? SharedClient;

    public string InstalledVersion => AppVersionInfo.DisplayVersion;

    /// <summary>Queries the latest published (non-draft, non-prerelease) GitHub Release.</summary>
    public async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        AppLogService.Write("update check started (GitHub Releases)");
        AppLogService.Write($"installed version: {InstalledVersion}");
        try
        {
            using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            checkCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await http.GetAsync(LatestApiUrl, HttpCompletionOption.ResponseHeadersRead, checkCts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(checkCts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: checkCts.Token).ConfigureAwait(false);
            var info = ParseRelease(doc.RootElement);

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

    /// <summary>Maps a GitHub "latest release" JSON object to <see cref="AppUpdateInfo"/>.</summary>
    public static AppUpdateInfo ParseRelease(JsonElement root)
    {
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) throw new InvalidDataException("GitHub release response does not contain a tag_name.");
        ParseVersion(tag); // validates the tag looks like a version

        var info = new AppUpdateInfo
        {
            Version = tag!,
            Notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
            PublishedAt = root.TryGetProperty("published_at", out var pub) ? pub.GetString() ?? string.Empty : string.Empty
        };

        // Prefer the Setup .exe; accept a ZIP that contains it.
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            JsonElement? best = null;
            var bestScore = 0;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) continue;
                var score = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? 2
                          : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                if (score > bestScore) { best = asset; bestScore = score; }
            }
            if (best is { } chosen)
            {
                info.FileName = chosen.GetProperty("name").GetString() ?? string.Empty;
                info.DownloadUrl = chosen.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                info.SizeBytes = chosen.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size) ? size : 0;
            }
        }

        if (string.IsNullOrWhiteSpace(info.DownloadUrl) && !IsPortable)
            throw new InvalidDataException("The latest GitHub release has no Setup installer asset.");
        return info;
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

        var fileName = SanitizeFileName(string.IsNullOrWhiteSpace(info.FileName) ? $"GW-Studio-Setup-v{NormalizeVersionText(info.Version)}.exe" : info.FileName);
        var isExe = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (!isExe && !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) fileName += ".zip";
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

            if (isExe)
            {
                AppLogService.Write($"setup found: {zipPath}");
                progress?.Report(new("Ready", total ?? 0, total));
                return zipPath;
            }

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
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string SanitizeFileName(string name)
    {
        var value = Path.GetFileName(name);
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "GW-Studio-Update.zip" : value;
    }
}
