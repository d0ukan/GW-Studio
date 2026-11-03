using System.Diagnostics;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
using Dou_GW_Studio.Services;
namespace Dou_GW_Studio.ViewModels;

public sealed class GraphViewModel : ObservableObject
{
    private readonly MeasurementHistoryService history;
    private readonly Stopwatch redrawClock = Stopwatch.StartNew();
    private readonly List<GraphMarker> markers = [];
    private readonly object markerSync = new();
    private bool ch1 = true, ch2 = true, ch3 = true, ch4 = true, voltage = true, current = true, power, referenceV = true, referenceI = true, showMarkers = true, showSetValues = true, paused, autoScale = true;
    private string window = "30 seconds";
    private string markerNote = "";
    private MeasurementPoint[] cached = [];
    private MeasurementPoint[]? frozen;
    private long revision;
    private HashSet<int> availableChannels = [];

    public GraphViewModel(MeasurementHistoryService history)
    {
        this.history = history;
        PauseCommand = new(() =>
        {
            paused = !paused;
            if (paused) { cached = history.Snapshot(); frozen = cached; }
            else { frozen = null; cached = history.Snapshot(); redrawClock.Restart(); }
            OnChanged(nameof(PauseLabel)); Changed();
        });
        ClearCommand = new(() => { history.Clear(); lock (markerSync) markers.Clear(); cached = []; frozen = paused ? [] : null; Changed(); });
        AddMarkerCommand = new(() =>
        {
            var note = MarkerNote.Trim();
            if (note.Length == 0) return;
            var points = GetPoints();
            var time = points.Length == 0 ? DateTimeOffset.Now : points[^1].Timestamp;
            lock (markerSync) markers.Add(new(time, note));
            MarkerNote = ""; Changed();
        }, () => !string.IsNullOrWhiteSpace(MarkerNote));
    }

    public bool Ch1 { get => ch1; set { Set(ref ch1, value); Changed(); } }
    public bool Ch2 { get => ch2; set { Set(ref ch2, value); Changed(); } }
    public bool Ch3 { get => ch3; set { Set(ref ch3, value); Changed(); } }
    public bool Ch4 { get => ch4; set { Set(ref ch4, value); Changed(); } }
    public bool Voltage { get => voltage; set { Set(ref voltage, value); Changed(); } }
    public bool Current { get => current; set { Set(ref current, value); Changed(); } }
    public bool Power { get => power; set { Set(ref power, value); Changed(); } }
    public bool ReferenceV { get => referenceV; set { Set(ref referenceV, value); Changed(); } }
    public bool ReferenceI { get => referenceI; set { Set(ref referenceI, value); Changed(); } }
    public bool ShowMarkers { get => showMarkers; set { Set(ref showMarkers, value); Changed(); } }
    public bool ShowSetValues { get => showSetValues; set { Set(ref showSetValues, value); Changed(); } }
    public bool AutoScale { get => autoScale; set { Set(ref autoScale, value); Changed(); } }
    public string Window { get => window; set { Set(ref window, value); Changed(); } }
    public string MarkerNote { get => markerNote; set { Set(ref markerNote, value); AddMarkerCommand.Refresh(); } }
    public string[] Windows { get; } = ["10 seconds", "30 seconds", "1 minute", "5 minutes", "10 minutes", "All"];
    public int WindowSeconds => Window switch { "10 seconds" => 10, "30 seconds" => 30, "1 minute" => 60, "5 minutes" => 300, "10 minutes" => 600, _ => 0 };
    public string PauseLabel => paused ? "RESUME" : "PAUSE";
    public string VoltageStats => BuildStats("Voltage", "V");
    public string CurrentStats => BuildStats("Current", "A");
    public string PowerStats => BuildStats("Power", "W");
    public long Revision => revision;
    public RelayCommand PauseCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand AddMarkerCommand { get; }
    public MeasurementPoint[] GetPoints() => frozen ?? cached;
    public GraphMarker[] GetMarkers() { lock (markerSync) return markers.ToArray(); }
    public void AddSystemMarker(string note, string kind, int? channel = null)
    {
        var points = GetPoints();
        var time = points.Length == 0 ? DateTimeOffset.Now : points[^1].Timestamp;
        lock (markerSync) markers.Add(new(time, note, kind, channel));
        Changed();
    }
    public bool IsChannelVisible(int channel) => availableChannels.Contains(channel) && channel switch { 1 => Ch1, 2 => Ch2, 3 => Ch3, 4 => Ch4, _ => false };
    public void SetChannelVisible(int channel, bool visible)
    {
        if (!availableChannels.Contains(channel)) return;
        switch (channel) { case 1: Ch1 = visible; break; case 2: Ch2 = visible; break; case 3: Ch3 = visible; break; case 4: Ch4 = visible; break; }
    }
    public void ConfigureChannels(IEnumerable<int> channels)
    {
        availableChannels = channels.ToHashSet();
        if (!availableChannels.Contains(1)) ch1 = false;
        if (!availableChannels.Contains(2)) ch2 = false;
        if (!availableChannels.Contains(3)) ch3 = false;
        if (!availableChannels.Contains(4)) ch4 = false;
        Changed();
    }

    public MeasurementPoint[] GetVisiblePoints()
    {
        var all = GetPoints();
        if (all.Length == 0) return [];
        var end = all[^1].Timestamp;
        IEnumerable<MeasurementPoint> visible = all;
        if (WindowSeconds > 0) visible = visible.Where(p => (end - p.Timestamp).TotalSeconds <= WindowSeconds);
        visible = visible.Where(p => IsChannelVisible(p.Channel));
        return visible.ToArray();
    }

    public void ApplyPreferences(string requestedWindow, bool showCh1, bool showCh2, bool showCh3, bool showCh4, bool showVoltage, bool showCurrent, bool showPower)
    {
        if (Windows.Contains(requestedWindow)) window = requestedWindow;
        ch1 = showCh1; ch2 = showCh2; ch3 = showCh3; ch4 = showCh4; voltage = showVoltage; current = showCurrent; power = showPower;
        Changed();
    }

    public void Tick()
    {
        if (paused) return;
        if (redrawClock.ElapsedMilliseconds < 60) return;
        cached = history.Snapshot(); redrawClock.Restart(); Changed();
    }
    public void ForceRefresh() { cached = paused ? frozen ?? history.Snapshot() : history.Snapshot(); redrawClock.Restart(); Changed(); }

    private string BuildStats(string signal, string unit)
    {
        var visible = GetVisiblePoints();
        if (visible.Length == 0) return "No samples for the selected channels/time window.";
        var parts = new List<string>();
        foreach (var group in visible.GroupBy(p => p.Channel).OrderBy(g => g.Key))
        {
            var values = group.Select(p => signal switch { "Current" => p.Current, "Power" => p.Power, _ => p.Voltage }).ToArray();
            if (values.Length == 0) continue;
            parts.Add($"CH{group.Key}   Min {values.Min():0.0000} {unit}   •   Avg {values.Average():0.0000} {unit}   •   Max {values.Max():0.0000} {unit}   ({values.Length:N0} samples)");
        }
        return parts.Count == 0 ? "No samples for the selected channels/time window." : string.Join("     |     ", parts);
    }

    private void Changed()
    {
        revision++;
        OnChanged(nameof(Revision)); OnChanged(nameof(VoltageStats)); OnChanged(nameof(CurrentStats)); OnChanged(nameof(PowerStats));
    }
}
