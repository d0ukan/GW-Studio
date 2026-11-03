namespace Dou_GW_Studio.Models;

public sealed class AppUpdateInfo
{
    public string App { get; set; } = "GW Studio";
    public string Slug { get; set; } = "gw-studio";
    public string Version { get; set; } = "0.0.0";
    public string DownloadUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string PublishedAt { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed record UpdateDownloadProgress(string Stage, long BytesReceived, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0 ? BytesReceived * 100.0 / TotalBytes.Value : null;
}
