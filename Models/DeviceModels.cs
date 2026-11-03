using System.Globalization;
namespace Dou_GW_Studio.Models;
public enum SourceConfiguration { Independent, TrackingSeries, TrackingParallel }
public enum ChannelFunction { Source, Load }
public enum LoadRegulation { CV, CC, CR }
public enum ExecutionBackend { PcTimed, GppNativeSequence }
public enum NativeSequenceEndState { Off, Last }
public enum GppModel { Gpp3060, Gpp4323 }
public sealed record ChannelCapability(
    int Channel,
    double MaxVoltage,
    double MaxCurrent,
    bool VoltageProgrammable = true,
    bool CurrentProgrammable = true,
    double[]? DiscreteVoltages = null)
{
    public bool UsesDiscreteVoltage => DiscreteVoltages is { Length: > 0 };
    public string RatingText => DiscreteVoltages is { Length: > 0 } values
        ? $"SELECTABLE V  •  {string.Join(" / ", values.Select(v => v.ToString("0.0##", CultureInfo.InvariantCulture)))} V  •  {MaxCurrent:0.####} A fixed"
        : $"PROGRAMMABLE V/I  •  0–{MaxVoltage:0.###} V  /  0–{MaxCurrent:0.####} A";
}
public sealed class DeviceProfile
{
    public required GppModel Model { get; init; }
    public required string ModelName { get; init; }
    public required IReadOnlyList<ChannelCapability> Channels { get; init; }
    public required IReadOnlySet<int> TrackingChannels { get; init; }
    public required IReadOnlySet<int> LoadChannels { get; init; }
    public required IReadOnlySet<int> NativeSequenceChannels { get; init; }

    public ChannelCapability Get(int channel) => Channels.FirstOrDefault(c => c.Channel == channel)
        ?? throw new ArgumentOutOfRangeException(nameof(channel), $"{ModelName} has no CH{channel}.");

    public void Validate(int channel, double voltage, double current)
    {
        var c = Get(channel);
        if (!double.IsFinite(voltage) || voltage < 0 || voltage > c.MaxVoltage) throw new ArgumentException($"CH{channel}: voltage must be 0–{c.MaxVoltage} V.");
        if (c.DiscreteVoltages is { Length: > 0 } discreteVoltages && !discreteVoltages.Any(v => Math.Abs(v - voltage) <= .0001))
            throw new ArgumentException($"CH{channel}: select one of {string.Join(", ", discreteVoltages.Select(v => v.ToString("0.0##", CultureInfo.InvariantCulture)))} V.");
        if (!double.IsFinite(current) || current < 0 || current > c.MaxCurrent) throw new ArgumentException($"CH{channel}: current must be 0–{c.MaxCurrent} A.");
        if (!c.CurrentProgrammable && Math.Abs(current - c.MaxCurrent) > .0001)
            throw new ArgumentException($"CH{channel}: current is fixed at {c.MaxCurrent:0.####} A on {ModelName}.");
    }
}
public static class DeviceProfiles
{
    public static readonly DeviceProfile Gpp3060 = new()
    {
        Model = GppModel.Gpp3060,
        ModelName = "GPP-3060",
        Channels =
        [
            new(1, 32, 6),
            new(2, 32, 6),
            new(3, 5, 5, VoltageProgrammable: true, CurrentProgrammable: false, DiscreteVoltages: [1.8, 2.5, 3.3, 5.0])
        ],
        TrackingChannels = new HashSet<int> { 1, 2 },
        LoadChannels = new HashSet<int> { 1, 2 },
        NativeSequenceChannels = new HashSet<int> { 1, 2 }
    };

    public static readonly DeviceProfile Gpp4323 = new()
    {
        Model = GppModel.Gpp4323,
        ModelName = "GPP-4323",
        Channels =
        [
            new(1, 32, 3),
            new(2, 32, 3),
            new(3, 5, 1),
            new(4, 15, 1)
        ],
        TrackingChannels = new HashSet<int> { 1, 2 },
        LoadChannels = new HashSet<int> { 1, 2 },
        NativeSequenceChannels = new HashSet<int> { 1, 2 }
    };

    public static IReadOnlyList<DeviceProfile> Supported { get; } = [Gpp3060, Gpp4323];
    public static DeviceProfile Resolve(string model)
    {
        var normalized = model.Trim();
        return Supported.FirstOrDefault(p => p.ModelName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unsupported instrument model '{model}'. Supported models: GPP-3060 and GPP-4323.");
    }
}
public sealed record DeviceInfo(string Manufacturer, string Model, string SerialNumber, string Firmware)
{
    public static DeviceInfo Parse(string reply)
    {
        var p = reply.Split(',').Select(s => s.Trim()).ToArray();
        if (p.Length < 2 || !p[0].Replace(" ", "").Equals("GWInstek", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Connected device is not a supported GW Instek GPP instrument. Reply: " + reply);
        DeviceProfiles.Resolve(p[1]);
        return new(p[0], p[1], p.ElementAtOrDefault(2) ?? "Unknown SN", p.ElementAtOrDefault(3) ?? "Unknown FW");
    }
}
public sealed record GppChannelState(int Channel, double SetVoltage, double CurrentLimit, double Voltage, double Current, bool OutputOn);
public sealed record PollingMetrics(int RequestedDelayMs, double SweepDurationMs, double EffectivePeriodMs, double EffectiveRateHz, int MonitoredChannels);
public sealed class QuickPreset
{
    public string TargetModel { get; set; } = "";
    public int Channel { get; set; }
    public string Name { get; set; } = "Preset";
    public double Voltage { get; set; }
    public double Current { get; set; }
    public override string ToString() => $"{Name}  ({Voltage:0.###} V / {Current:0.####} A)";
}
public sealed record GraphMarker(DateTimeOffset Timestamp, string Note, string Kind = "Manual", int? Channel = null);
public sealed record MeasurementPoint(DateTimeOffset Timestamp, int Channel, double Voltage, double Current, double SetVoltage, double CurrentLimit, bool OutputOn)
{
    public double Power => Voltage * Current;
    public string Regulation => "Unknown";
}
public sealed record TrafficEntry(DateTimeOffset Timestamp, string Direction, string Text, bool IsPolling)
{
    public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
public sealed class AppSettings
{
    public int PollingIntervalMs { get; set; } = 500;
    public int SerialTimeoutMs { get; set; } = 1500;
    public bool OutputsOffOnDisconnect { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool AdvancedMode { get; set; }
    public bool MonitorCh2 { get; set; } = true;
    public bool MonitorCh3 { get; set; } = true;
    public bool MonitorCh4 { get; set; } = true;
    public string LastPort { get; set; } = "";
    public string Theme { get; set; } = "Dark";
    public List<QuickPreset> QuickPresets { get; set; } = [];
    public string GraphWindow { get; set; } = "30 seconds";
    public bool GraphCh1 { get; set; } = true;
    public bool GraphCh2 { get; set; } = true;
    public bool GraphCh3 { get; set; } = true;
    public bool GraphCh4 { get; set; } = true;
    public bool GraphVoltage { get; set; } = true;
    public bool GraphCurrent { get; set; } = true;
    public bool GraphPower { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; } = true;
}
public sealed record NativeSequenceStep(double Voltage, double Current, int DurationSeconds);
public sealed class NativeSequenceProfile
{
    public int Channel { get; set; } = 1;
    public int StartGroup { get; set; }
    public int Cycles { get; set; } = 1;
    public bool Infinite { get; set; }
    public NativeSequenceEndState EndState { get; set; } = NativeSequenceEndState.Off;
    public List<NativeSequenceStep> Steps { get; set; } = [];
}
public sealed record OperatingModeState(SourceConfiguration Configuration, ChannelFunction Ch1Function, LoadRegulation? Ch1LoadMode, ChannelFunction Ch2Function, LoadRegulation? Ch2LoadMode);
