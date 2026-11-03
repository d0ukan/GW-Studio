using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Dou_GW_Studio.ViewModels;

public sealed class Mil704SegmentDefinition
{
    public string Type { get; set; } = "Hold";
    public double StartVoltage { get; set; } = 28;
    public double EndVoltage { get; set; } = 28;
    public int DurationMs { get; set; } = 1000;
}

public sealed class Mil704SavedProfile
{
    public string Name { get; set; } = "704 waveform";
    public int Channel { get; set; } = 1;
    public string Mode { get; set; } = "Curve";
    public string Curve { get; set; } = "Linear";
    public double StartVoltage { get; set; } = 28;
    public double EndVoltage { get; set; } = 20;
    public double DurationSeconds { get; set; } = 7;
    public double PreHoldSeconds { get; set; } = 0.5;
    public double PostHoldSeconds { get; set; } = 1;
    public double CurrentLimit { get; set; } = 1;
    public int StepMs { get; set; } = 100;
    public string CustomPoints { get; set; } = "0=28; 0.05=28; 0.10=20; 0.20=20";
    public int InterruptMs { get; set; } = 50;
    public int RepeatCount { get; set; } = 1;
    public int RepeatDelayMs { get; set; }
    public List<Mil704SegmentDefinition> Segments { get; set; } = [];
}

public sealed class Mil704LibraryItem
{
    public required string Category { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Kind { get; init; }
    public string Curve { get; init; } = "Linear";
    public double StartVoltage { get; init; } = 28;
    public double EndVoltage { get; init; } = 28;
    public double DurationSeconds { get; init; } = 1;
    public double PreHoldSeconds { get; init; } = 0.25;
    public double PostHoldSeconds { get; init; } = 0.5;
    public int InterruptMs { get; init; } = 50;
    public string CustomPoints { get; init; } = "";
    public override string ToString() => $"{Category}  •  {Name}";
}

public sealed class Mil704SegmentRow : ObservableObject
{
    private string type = "Hold";
    private double startVoltage = 28;
    private double endVoltage = 28;
    private int durationMs = 1000;

    public string Type { get => type; set => Set(ref type, value); }
    public double StartVoltage { get => startVoltage; set => Set(ref startVoltage, value); }
    public double EndVoltage { get => endVoltage; set => Set(ref endVoltage, value); }
    public int DurationMs { get => durationMs; set => Set(ref durationMs, value); }

    public Mil704SegmentDefinition Snapshot() => new() { Type = Type, StartVoltage = StartVoltage, EndVoltage = EndVoltage, DurationMs = DurationMs };
}

public sealed class Mil704DragPoint : ObservableObject
{
    private double time;
    private double voltage;
    public Mil704DragPoint(double time, double voltage) { this.time = time; this.voltage = voltage; }
    public double Time { get => time; set => Set(ref time, value); }
    public double Voltage { get => voltage; set => Set(ref voltage, value); }
}

public sealed class Mil704ViewModel : ObservableObject
{
    // This is the per-channel setpoint, NOT an automatic two-channel series controller.
    // Original 704 reference is 31.38 + 0.931/t V; one channel is exactly half.
    private const string ImageEquationCurve = "Image equation (CH): 15.69 + 0.4655/t";
    private const string LegacyImageEquationCurve = "Image equation: 31.38 + 0.931/t";
    private const string SeriesTemplate = "Reference image (per CH): 25 V → 15.75 V inverse-time";
    private readonly MainViewModel owner;
    private readonly object measuredSync = new();
    private readonly Stopwatch plotRefreshThrottle = Stopwatch.StartNew();
    private DeviceProfile? activeProfile;
    private CustomTestProfile? armed;
    private string status = "Build a waveform preview. ARM/RUN becomes available after a compatible instrument is connected.";
    private string selectedTemplate = "Custom";
    private Mil704LibraryItem? selectedLibraryItem;
    private string mode = "Curve";
    private string curve = "Linear";
    private int channel = 1;
    private string startVoltage = "28.0000";
    private string endVoltage = "20.0000";
    private string durationSeconds = "7.000";
    private string preHoldSeconds = "0.500";
    private string postHoldSeconds = "1.000";
    private string currentLimit = "1.0000";
    private string stepMs = "100";
    private string customPoints = "0=28; 0.05=28; 0.10=20; 0.20=20";
    private string interruptMs = "50";
    private string repeatCount = "1";
    private string repeatDelayMs = "0";
    private string profileName = "My 704 waveform";
    private string summary = "No waveform generated yet.";
    private SavedProfileInfo? selectedSavedProfile;
    private Mil704SegmentRow? selectedSegment;
    private PlotModel previewModel = new();
    private PlotModel dragEditModel = new();
    private List<(double Time, double Voltage)> previewPoints = [];
    private readonly List<(double Time, double Voltage)> measuredPoints = [];
    private DateTimeOffset? runStart;
    private bool showMeasured = true;

    public Mil704ViewModel(MainViewModel owner)
    {
        this.owner = owner;
        BuildCommand = new(() => Safe(() => { Mode = "Curve"; BuildPreview(); }), () => !owner.IsRunning);
        BuildCustomPointsCommand = new(() => Safe(() => { Mode = "Custom points"; BuildPreview(); }), () => !owner.IsRunning);
        BuildInterruptCommand = new(() => Safe(() => { Mode = "Power interrupt"; BuildPreview(); }), () => !owner.IsRunning);
        ApplyTemplateCommand = new(() => Safe(ApplyTemplate), () => !owner.IsRunning);
        ApplyLibraryCommand = new(() => Safe(ApplyLibraryItem), () => !owner.IsRunning && SelectedLibraryItem != null);

        AddHoldSegmentCommand = new(() => AddSegment(new() { Type = "Hold", StartVoltage = 28, EndVoltage = 28, DurationMs = 1000 }));
        AddLinearSegmentCommand = new(() => AddSegment(new() { Type = "Linear Ramp", StartVoltage = 28, EndVoltage = 20, DurationMs = 100 }));
        AddExponentialSegmentCommand = new(() => AddSegment(new() { Type = "Exponential Ramp", StartVoltage = 20, EndVoltage = 28, DurationMs = 100 }));
        AddInterruptSegmentCommand = new(() => AddSegment(new() { Type = "Output Off", StartVoltage = 28, EndVoltage = 28, DurationMs = 50 }));
        DeleteSegmentCommand = new(DeleteSelectedSegment, () => SelectedSegment != null && !owner.IsRunning);
        MoveSegmentUpCommand = new(() => MoveSelectedSegment(-1), () => CanMoveSelectedSegment(-1));
        MoveSegmentDownCommand = new(() => MoveSelectedSegment(1), () => CanMoveSelectedSegment(1));
        BuildSegmentsCommand = new(() => Safe(() => { Mode = "Segment editor"; BuildPreview(); }), () => !owner.IsRunning && Segments.Count > 0);

        LoadCurrentIntoDragCommand = new(() => Safe(LoadCurrentIntoDrag), () => !owner.IsRunning && previewPoints.Count >= 2);
        UseDragWaveformCommand = new(() => Safe(() => { Mode = "Drag points"; CustomPoints = SerializeDragPoints(); BuildPreview(); }), () => !owner.IsRunning && DragPoints.Count >= 2);
        ResetDragCommand = new(() => Safe(ResetDragPoints), () => !owner.IsRunning);

        ArmCommand = new(() => Safe(() =>
        {
            armed = BuildExecutionProfile();
            Status = $"ARMED — {armed.Steps.Count:N0} discrete steps including repeats. RUN will change CH{Channel}.";
            Refresh();
        }), () => owner.CanControl && !owner.IsRunning);
        RunCommand = new(async () =>
        {
            var profile = armed ?? throw new InvalidOperationException("ARM the 704 waveform first.");
            lock (measuredSync) measuredPoints.Clear();
            runStart = DateTimeOffset.Now;
            RebuildPlot();
            Disarm();
            try { await owner.RunProfileAsync(profile); }
            finally { RebuildPlot(); }
        }, owner.ReportError, () => owner.CanControl && IsArmed);
        StopCommand = new(owner.StopTestAsync, owner.ReportError, () => owner.IsRunning);
        SaveCommand = new(async () =>
        {
            var saved = SnapshotDefinition();
            saved.Name = SanitizeName(ProfileName);
            Directory.CreateDirectory(ProfileFolder);
            var file = UniqueProfilePath(saved.Name);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
            RefreshProfiles();
            SelectedSavedProfile = SavedProfiles.FirstOrDefault(x => x.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase));
            Status = $"704 profile saved: {saved.Name}";
        }, owner.ReportError, () => !owner.IsRunning);
        LoadCommand = new(async () =>
        {
            var selected = SelectedSavedProfile ?? throw new InvalidOperationException("Select a saved 704 profile first.");
            var info = new FileInfo(selected.FilePath);
            if (!info.Exists) throw new FileNotFoundException("The selected 704 profile no longer exists.", info.FullName);
            if (info.Length > 1000000) throw new ArgumentException("704 profile file is unexpectedly large.");
            var saved = JsonSerializer.Deserialize<Mil704SavedProfile>(await File.ReadAllTextAsync(info.FullName)) ?? throw new ArgumentException("Invalid 704 profile JSON.");
            LoadDefinition(saved);
            ProfileName = saved.Name;
            BuildPreview();
            Status = $"Loaded 704 profile: {saved.Name}";
        }, owner.ReportError, () => !owner.IsRunning && SelectedSavedProfile != null);
        DeleteProfileCommand = new(async () =>
        {
            var selected = SelectedSavedProfile ?? throw new InvalidOperationException("Select a saved 704 profile first.");
            if (File.Exists(selected.FilePath)) File.Delete(selected.FilePath);
            await Task.CompletedTask;
            RefreshProfiles();
            Status = "Saved 704 profile deleted.";
        }, owner.ReportError, () => !owner.IsRunning && SelectedSavedProfile != null);
        ClearMeasuredCommand = new(() => { lock (measuredSync) measuredPoints.Clear(); runStart = null; RebuildPlot(); });
        RefreshProfilesCommand = new(RefreshProfiles);

        SeedSegments();
        BuildLibrary();
        ResetDragPoints();
        RefreshProfiles();
        BuildPreview();
    }

    public string[] Templates { get; } = [
        "Custom",
        "28 V → 20 V linear drop",
        "28 V → 20 V exponential drop",
        "20 V → 28 V linear recovery",
        "20 V → 28 V exponential recovery",
        "Image Fig.13 upper: 50 V → 29 V transient",
        "Image Fig.13 lower: 18 V → 22 V transient",
        SeriesTemplate
    ];
    public string[] Curves { get; } = ["Linear", "Exponential", "Logarithmic", "Inverse-time", ImageEquationCurve];
    public string[] SegmentTypes { get; } = ["Hold", "Linear Ramp", "Exponential Ramp", "Output Off"];
    public ObservableCollection<int> Channels { get; } = [1];
    public ObservableCollection<SavedProfileInfo> SavedProfiles { get; } = [];
    public ObservableCollection<Mil704LibraryItem> LibraryItems { get; } = [];
    public ObservableCollection<Mil704SegmentRow> Segments { get; } = [];
    public ObservableCollection<Mil704DragPoint> DragPoints { get; } = [];

    public string SelectedTemplate { get => selectedTemplate; set => Set(ref selectedTemplate, value); }
    public Mil704LibraryItem? SelectedLibraryItem { get => selectedLibraryItem; set { if (Set(ref selectedLibraryItem, value)) ApplyLibraryCommand?.Refresh(); } }
    public string Mode { get => mode; private set { if (Set(ref mode, value)) OnChanged(nameof(ModeLabel)); } }
    public string ModeLabel => Mode.ToUpperInvariant();
    public string Curve
    {
        get => curve;
        set
        {
            if (!Set(ref curve, value)) return;
            OnChanged(nameof(IsImageEquation));
            OnChanged(nameof(EquationHint));
            if (value == ImageEquationCurve)
            {
                // Fixed equation has fixed reference endpoints and timing; don't let the
                // editable generic curve fields silently disagree with the generated waveform.
                StartVoltage = "25.0000";
                EndVoltage = "15.7500";
                PreHoldSeconds = "0.050";
                DurationSeconds = "7.708";
            }
            Disarm();
        }
    }
    public bool IsImageEquation => Curve == ImageEquationCurve;
    public string EquationHint => IsImageEquation
        ? "Per-channel setpoints: 25.0000 V → 15.7500 V; V(t) = 15.69 + 0.4655/t (0.05–7.758 s). Original reference is 2× these values. The app controls only the selected CH; it does not command or verify a second series-connected channel."
        : "";
    public int Channel { get => channel; set { if (Set(ref channel, value)) Disarm(); } }
    public string StartVoltage { get => startVoltage; set { if (Set(ref startVoltage, value)) Disarm(); } }
    public string EndVoltage { get => endVoltage; set { if (Set(ref endVoltage, value)) Disarm(); } }
    public string DurationSeconds { get => durationSeconds; set { if (Set(ref durationSeconds, value)) Disarm(); } }
    public string PreHoldSeconds { get => preHoldSeconds; set { if (Set(ref preHoldSeconds, value)) Disarm(); } }
    public string PostHoldSeconds { get => postHoldSeconds; set { if (Set(ref postHoldSeconds, value)) Disarm(); } }
    public string CurrentLimit { get => currentLimit; set { if (Set(ref currentLimit, value)) Disarm(); } }
    public string StepMs { get => stepMs; set { if (Set(ref stepMs, value)) Disarm(); } }
    public string CustomPoints { get => customPoints; set { if (Set(ref customPoints, value)) Disarm(); } }
    public string InterruptMs { get => interruptMs; set { if (Set(ref interruptMs, value)) Disarm(); } }
    public string RepeatCount { get => repeatCount; set { if (Set(ref repeatCount, value)) Disarm(); } }
    public string RepeatDelayMs { get => repeatDelayMs; set { if (Set(ref repeatDelayMs, value)) Disarm(); } }
    public string ProfileName { get => profileName; set => Set(ref profileName, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public PlotModel PreviewModel { get => previewModel; private set => Set(ref previewModel, value); }
    public PlotModel DragEditModel { get => dragEditModel; private set => Set(ref dragEditModel, value); }
    public bool ShowMeasured { get => showMeasured; set { if (Set(ref showMeasured, value)) RebuildPlot(); } }
    public SavedProfileInfo? SelectedSavedProfile { get => selectedSavedProfile; set { Set(ref selectedSavedProfile, value); LoadCommand?.Refresh(); DeleteProfileCommand?.Refresh(); } }
    public Mil704SegmentRow? SelectedSegment { get => selectedSegment; set { if (Set(ref selectedSegment, value)) RefreshSegmentCommands(); } }
    public bool IsArmed => armed != null;
    public string ArmLabel => IsArmed ? "ARMED — READY" : "ARM WAVEFORM";
    public bool CanRun => owner.CanControl && IsArmed;

    public RelayCommand BuildCommand { get; }
    public RelayCommand BuildCustomPointsCommand { get; }
    public RelayCommand BuildInterruptCommand { get; }
    public RelayCommand ApplyTemplateCommand { get; }
    public RelayCommand ApplyLibraryCommand { get; }
    public RelayCommand AddHoldSegmentCommand { get; }
    public RelayCommand AddLinearSegmentCommand { get; }
    public RelayCommand AddExponentialSegmentCommand { get; }
    public RelayCommand AddInterruptSegmentCommand { get; }
    public RelayCommand DeleteSegmentCommand { get; }
    public RelayCommand MoveSegmentUpCommand { get; }
    public RelayCommand MoveSegmentDownCommand { get; }
    public RelayCommand BuildSegmentsCommand { get; }
    public RelayCommand LoadCurrentIntoDragCommand { get; }
    public RelayCommand UseDragWaveformCommand { get; }
    public RelayCommand ResetDragCommand { get; }
    public RelayCommand ArmCommand { get; }
    public AsyncCommand RunCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand LoadCommand { get; }
    public AsyncCommand DeleteProfileCommand { get; }
    public RelayCommand ClearMeasuredCommand { get; }
    public RelayCommand RefreshProfilesCommand { get; }

    private static string ProfileFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dou-GW-Studio", "704Profiles");

    public void ConfigureProfile(DeviceProfile profile)
    {
        activeProfile = profile;
        var oldChannel = Channel;
        Channels.Clear();
        foreach (var capability in profile.Channels.Where(c => c.CurrentProgrammable)) Channels.Add(capability.Channel);
        Channel = Channels.Contains(oldChannel) ? oldChannel : Channels.FirstOrDefault();
        if (Channel > 0)
        {
            var capability = profile.Get(Channel);
            CurrentLimit = Numbers.F(Math.Min(1, capability.MaxCurrent), 4);
        }
        Disarm();
        Status = $"{profile.ModelName} connected. Preview is unrestricted; ARM validates every generated point against the selected channel limits.";
        OnChanged(nameof(Channels));
    }

    public void RecordMeasurement(MeasurementPoint point)
    {
        var start = runStart;
        if (start == null || point.Channel != Channel) return;
        var t = (point.Timestamp - start.Value).TotalSeconds;
        if (t < -0.2 || t > 604800) return;
        lock (measuredSync)
        {
            measuredPoints.Add((Math.Max(0, t), point.Voltage));
            if (measuredPoints.Count > 10000) measuredPoints.RemoveRange(0, measuredPoints.Count - 10000);
        }
    }

    public void UpdateProgress(TestProgress p)
    {
        if (!owner.IsRunning) return;
        Status = $"{p.State}  •  Step {p.Step}/{p.TotalSteps}  •  Elapsed {p.Elapsed.TotalSeconds:0.000} s" +
                 (p.Requested == null ? "" : $"  •  CH{p.Requested.Channel} {p.Requested.Voltage:0.0000} V {(p.Requested.OutputOn ? "ON" : "OFF")}");
        if (plotRefreshThrottle.ElapsedMilliseconds >= 250 || p.State.StartsWith("Completed", StringComparison.Ordinal))
        {
            plotRefreshThrottle.Restart();
            RebuildPlot();
        }
    }

    public void Disarm()
    {
        armed = null;
        OnChanged(nameof(IsArmed));
        OnChanged(nameof(ArmLabel));
        OnChanged(nameof(CanRun));
        ArmCommand?.Refresh();
        RunCommand?.Refresh();
    }

    public void Refresh()
    {
        OnChanged(nameof(CanRun)); OnChanged(nameof(IsArmed)); OnChanged(nameof(ArmLabel));
        BuildCommand?.Refresh(); BuildCustomPointsCommand?.Refresh(); BuildInterruptCommand?.Refresh(); ApplyTemplateCommand?.Refresh(); ApplyLibraryCommand?.Refresh(); BuildSegmentsCommand?.Refresh(); LoadCurrentIntoDragCommand?.Refresh(); UseDragWaveformCommand?.Refresh(); ResetDragCommand?.Refresh(); ArmCommand?.Refresh(); RunCommand?.Refresh(); StopCommand?.Refresh(); SaveCommand?.Refresh(); LoadCommand?.Refresh(); DeleteProfileCommand?.Refresh();
        RefreshSegmentCommands();
    }

    public void UpdateDragPoint(int index, double time, double voltage, bool finalize = false)
    {
        if (owner.IsRunning || index < 0 || index >= DragPoints.Count) return;
        var minTime = index == 0 ? 0 : DragPoints[index - 1].Time + 0.001;
        var maxTime = index == DragPoints.Count - 1 ? Math.Max(minTime, DragPoints[index].Time + 86400) : DragPoints[index + 1].Time - 0.001;
        if (index == 0) time = 0;
        else if (index == DragPoints.Count - 1) time = Math.Max(minTime, time);
        else time = Math.Clamp(time, minTime, Math.Max(minTime, maxTime));
        voltage = Math.Clamp(voltage, 0, 1000);
        DragPoints[index].Time = time;
        DragPoints[index].Voltage = voltage;

        // Fast drag path: mutate the existing OxyPlot series instead of allocating a
        // complete PlotModel for every mouse movement. Full rebuild happens on mouse-up.
        if (!finalize && DragEditModel.Series.Count >= 2 &&
            DragEditModel.Series[0] is LineSeries line && DragEditModel.Series[1] is ScatterSeries scatter &&
            index < line.Points.Count && index < scatter.Points.Count)
        {
            line.Points[index] = new DataPoint(time, voltage);
            scatter.Points[index] = new ScatterPoint(time, voltage);
            DragEditModel.InvalidatePlot(false);
            return;
        }

        RebuildDragPlot();
        Disarm();
    }

    public int AddDragPoint(int preferredIndex = -1)
    {
        if (owner.IsRunning) return -1;
        if (DragPoints.Count >= 64) throw new InvalidOperationException("Drag Editor supports up to 64 control points to keep editing responsive.");
        if (DragPoints.Count < 2) { ResetDragPoints(); return Math.Min(1, DragPoints.Count - 1); }

        int left;
        if (preferredIndex >= 0 && preferredIndex < DragPoints.Count - 1) left = preferredIndex;
        else if (preferredIndex == DragPoints.Count - 1) left = Math.Max(0, preferredIndex - 1);
        else
        {
            left = 0; double widest = -1;
            for (var i = 0; i < DragPoints.Count - 1; i++)
            {
                var gap = DragPoints[i + 1].Time - DragPoints[i].Time;
                if (gap > widest) { widest = gap; left = i; }
            }
        }

        var a = DragPoints[left]; var b = DragPoints[left + 1];
        var time = (a.Time + b.Time) * 0.5;
        var voltage = (a.Voltage + b.Voltage) * 0.5;
        var index = left + 1;
        DragPoints.Insert(index, new Mil704DragPoint(time, voltage));
        RebuildDragPlot();
        Disarm();
        Status = $"Added drag control point {index + 1}/{DragPoints.Count}. Maximum is 64.";
        return index;
    }

    public int RemoveDragPoint(int index)
    {
        if (owner.IsRunning) return -1;
        if (DragPoints.Count <= 2) throw new InvalidOperationException("At least two drag control points are required.");
        if (index <= 0 || index >= DragPoints.Count - 1) throw new InvalidOperationException("Keep the first and last control points; select an interior point to remove.");
        DragPoints.RemoveAt(index);
        RebuildDragPlot();
        Disarm();
        var next = Math.Min(index, DragPoints.Count - 1);
        Status = $"Removed drag control point. {DragPoints.Count} points remain.";
        return next;
    }

    public void FinalizeDragEdit()
    {
        if (owner.IsRunning) return;
        RebuildDragPlot();
        Disarm();
    }

    private void BuildLibrary()
    {
        LibraryItems.Clear();
        LibraryItems.Add(new() { Category = "Normal transient", Name = "28 V short overvoltage example", Description = "Simple bench example: hold 28 V, rise to 32 V briefly, then recover to 28 V. Verify limits against the applicable MIL-STD-704 revision/platform.", Kind = "Segments" });
        LibraryItems.Add(new() { Category = "Power interrupt", Name = "50 ms interruption", Description = "Hold nominal voltage, turn the selected channel output OFF for 50 ms, then restore nominal voltage.", Kind = "Interrupt", StartVoltage = 28, InterruptMs = 50, PreHoldSeconds = 0.5, PostHoldSeconds = 1 });
        LibraryItems.Add(new() { Category = "Undervoltage", Name = "28 V → 20 V linear", Description = "Linear bench ramp from 28 V to 20 V with pre/post hold.", Kind = "Curve", Curve = "Linear", StartVoltage = 28, EndVoltage = 20, DurationSeconds = 1, PreHoldSeconds = 0.5, PostHoldSeconds = 1 });
        LibraryItems.Add(new() { Category = "Overvoltage", Name = "28 V → 32 V linear", Description = "Linear bench ramp from 28 V to 32 V. ARM still enforces the connected channel hardware range.", Kind = "Curve", Curve = "Linear", StartVoltage = 28, EndVoltage = 32, DurationSeconds = 0.25, PreHoldSeconds = 0.5, PostHoldSeconds = 0.5 });
        LibraryItems.Add(new() { Category = "Recovery", Name = "20 V → 28 V exponential", Description = "Exponential recovery example from 20 V back to nominal 28 V.", Kind = "Curve", Curve = "Exponential", StartVoltage = 20, EndVoltage = 28, DurationSeconds = 1, PreHoldSeconds = 0.25, PostHoldSeconds = 1 });
        LibraryItems.Add(new() { Category = "Ramp", Name = "28 V → 24 V → 28 V", Description = "Two-segment down/up ramp example built with the Segment Editor.", Kind = "RampSegments" });
        SelectedLibraryItem = LibraryItems.FirstOrDefault();
    }

    private void ApplyLibraryItem()
    {
        var item = SelectedLibraryItem ?? throw new InvalidOperationException("Select a library profile first.");
        switch (item.Kind)
        {
            case "Interrupt":
                Mode = "Power interrupt";
                StartVoltage = Numbers.F(item.StartVoltage);
                PreHoldSeconds = item.PreHoldSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                PostHoldSeconds = item.PostHoldSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                InterruptMs = item.InterruptMs.ToString(CultureInfo.InvariantCulture);
                break;
            case "Curve":
                Mode = "Curve"; Curve = item.Curve; StartVoltage = Numbers.F(item.StartVoltage); EndVoltage = Numbers.F(item.EndVoltage);
                DurationSeconds = item.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture); PreHoldSeconds = item.PreHoldSeconds.ToString("0.###", CultureInfo.InvariantCulture); PostHoldSeconds = item.PostHoldSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                break;
            case "Segments":
                ReplaceSegments([
                    new() { Type = "Hold", StartVoltage = 28, EndVoltage = 28, DurationMs = 500 },
                    new() { Type = "Hold", StartVoltage = 32, EndVoltage = 32, DurationMs = 50 },
                    new() { Type = "Linear Ramp", StartVoltage = 32, EndVoltage = 28, DurationMs = 100 },
                    new() { Type = "Hold", StartVoltage = 28, EndVoltage = 28, DurationMs = 1000 }
                ]);
                Mode = "Segment editor";
                break;
            case "RampSegments":
                ReplaceSegments([
                    new() { Type = "Hold", StartVoltage = 28, EndVoltage = 28, DurationMs = 500 },
                    new() { Type = "Linear Ramp", StartVoltage = 28, EndVoltage = 24, DurationMs = 250 },
                    new() { Type = "Hold", StartVoltage = 24, EndVoltage = 24, DurationMs = 250 },
                    new() { Type = "Linear Ramp", StartVoltage = 24, EndVoltage = 28, DurationMs = 250 },
                    new() { Type = "Hold", StartVoltage = 28, EndVoltage = 28, DurationMs = 500 }
                ]);
                Mode = "Segment editor";
                break;
            default:
                throw new ArgumentException("Unsupported library item.");
        }
        BuildPreview();
        Status = $"Library profile loaded: {item.Category} / {item.Name}. Verify the applicable 704 revision/platform limits before use.";
    }

    private void ApplyTemplate()
    {
        switch (SelectedTemplate)
        {
            case "28 V → 20 V linear drop": Mode = "Curve"; Curve = "Linear"; StartVoltage = "28.0000"; EndVoltage = "20.0000"; DurationSeconds = "7.000"; PreHoldSeconds = "0.500"; PostHoldSeconds = "1.000"; StepMs = "100"; break;
            case "28 V → 20 V exponential drop": Mode = "Curve"; Curve = "Exponential"; StartVoltage = "28.0000"; EndVoltage = "20.0000"; DurationSeconds = "7.000"; PreHoldSeconds = "0.500"; PostHoldSeconds = "1.000"; StepMs = "100"; break;
            case "20 V → 28 V linear recovery": Mode = "Curve"; Curve = "Linear"; StartVoltage = "20.0000"; EndVoltage = "28.0000"; DurationSeconds = "7.000"; PreHoldSeconds = "0.500"; PostHoldSeconds = "1.000"; StepMs = "100"; break;
            case "20 V → 28 V exponential recovery": Mode = "Curve"; Curve = "Exponential"; StartVoltage = "20.0000"; EndVoltage = "28.0000"; DurationSeconds = "7.000"; PreHoldSeconds = "0.500"; PostHoldSeconds = "1.000"; StepMs = "100"; break;
            case "Image Fig.13 upper: 50 V → 29 V transient": Mode = "Custom points"; CustomPoints = "0=50; 0.0125=50; 0.0825=29; 0.160=29"; StepMs = "20"; break;
            case "Image Fig.13 lower: 18 V → 22 V transient": Mode = "Custom points"; CustomPoints = "0=18; 0.015=18; 0.100=22; 0.160=22"; StepMs = "20"; break;
            case SeriesTemplate: Mode = "Curve"; Curve = ImageEquationCurve; StartVoltage = "25.0000"; EndVoltage = "15.7500"; DurationSeconds = "7.708"; PreHoldSeconds = "0.050"; PostHoldSeconds = "1.000"; StepMs = "100"; break;
            default: Mode = "Curve"; break;
        }
        BuildPreview();
        Status = SelectedTemplate == SeriesTemplate
            ? "Half-scale image equation ready: selected CH outputs 25.0000 → 15.7500 V. The original figure depicts twice these values; a second series-connected channel is NOT controlled by this profile."
            : SelectedTemplate.Contains("50 V", StringComparison.Ordinal)
                ? "Reference waveform loaded. A 32 V GPP channel cannot reproduce portions above its hardware limit; ARM will reject those points."
                : "Template loaded. Review Requested vs Measured, then ARM when a compatible instrument is connected.";
    }

    private void SeedSegments()
    {
        ReplaceSegments([
            new() { Type = "Hold", StartVoltage = 28, EndVoltage = 28, DurationMs = 2000 },
            new() { Type = "Hold", StartVoltage = 32, EndVoltage = 32, DurationMs = 50 },
            new() { Type = "Linear Ramp", StartVoltage = 32, EndVoltage = 24, DurationMs = 100 },
            new() { Type = "Hold", StartVoltage = 20, EndVoltage = 20, DurationMs = 1000 },
            new() { Type = "Linear Ramp", StartVoltage = 20, EndVoltage = 28, DurationMs = 250 }
        ]);
    }

    private void ReplaceSegments(IEnumerable<Mil704SegmentDefinition> definitions)
    {
        foreach (var row in Segments) row.PropertyChanged -= SegmentChanged;
        Segments.Clear();
        foreach (var d in definitions)
        {
            var row = new Mil704SegmentRow { Type = d.Type, StartVoltage = d.StartVoltage, EndVoltage = d.EndVoltage, DurationMs = d.DurationMs };
            row.PropertyChanged += SegmentChanged;
            Segments.Add(row);
        }
        SelectedSegment = Segments.FirstOrDefault();
        Disarm();
    }

    private void AddSegment(Mil704SegmentDefinition d)
    {
        if (owner.IsRunning) return;
        var row = new Mil704SegmentRow { Type = d.Type, StartVoltage = d.StartVoltage, EndVoltage = d.EndVoltage, DurationMs = d.DurationMs };
        row.PropertyChanged += SegmentChanged;
        var index = SelectedSegment == null ? Segments.Count : Segments.IndexOf(SelectedSegment) + 1;
        Segments.Insert(index, row);
        SelectedSegment = row;
        Disarm();
        BuildSegmentsCommand.Refresh();
    }

    private void DeleteSelectedSegment()
    {
        var row = SelectedSegment;
        if (row == null || owner.IsRunning) return;
        var index = Segments.IndexOf(row);
        row.PropertyChanged -= SegmentChanged;
        Segments.Remove(row);
        SelectedSegment = Segments.Count == 0 ? null : Segments[Math.Clamp(index, 0, Segments.Count - 1)];
        Disarm();
        BuildSegmentsCommand.Refresh();
    }

    private void MoveSelectedSegment(int delta)
    {
        var row = SelectedSegment;
        if (row == null || owner.IsRunning) return;
        var oldIndex = Segments.IndexOf(row);
        var newIndex = oldIndex + delta;
        if (newIndex < 0 || newIndex >= Segments.Count) return;
        Segments.Move(oldIndex, newIndex);
        SelectedSegment = row;
        Disarm();
        RefreshSegmentCommands();
    }

    private bool CanMoveSelectedSegment(int delta)
    {
        if (owner.IsRunning || SelectedSegment == null) return false;
        var index = Segments.IndexOf(SelectedSegment) + delta;
        return index >= 0 && index < Segments.Count;
    }

    private void RefreshSegmentCommands()
    {
        DeleteSegmentCommand?.Refresh(); MoveSegmentUpCommand?.Refresh(); MoveSegmentDownCommand?.Refresh(); BuildSegmentsCommand?.Refresh();
    }

    private void SegmentChanged(object? sender, PropertyChangedEventArgs e) => Disarm();

    private void LoadCurrentIntoDrag()
    {
        if (previewPoints.Count < 2) throw new InvalidOperationException("Build a waveform first.");
        var source = previewPoints.OrderBy(p => p.Time).ToList();
        var unique = new List<(double Time, double Voltage)>();
        foreach (var p in source)
        {
            if (unique.Count == 0 || p.Time > unique[^1].Time + 0.000001) unique.Add(p);
            else unique[^1] = p;
        }
        var reduced = Thin(unique, 32);
        DragPoints.Clear();
        foreach (var p in reduced) DragPoints.Add(new Mil704DragPoint(p.Time, p.Voltage));
        RebuildDragPlot();
        Status = $"Loaded {DragPoints.Count} editable control points. Drag points, then click USE DRAG WAVEFORM.";
    }

    private void ResetDragPoints()
    {
        DragPoints.Clear();
        DragPoints.Add(new(0, 28)); DragPoints.Add(new(0.5, 28)); DragPoints.Add(new(1.0, 24)); DragPoints.Add(new(1.5, 20)); DragPoints.Add(new(2.0, 28));
        RebuildDragPlot();
        Disarm();
    }

    private string SerializeDragPoints() => string.Join("; ", DragPoints.OrderBy(p => p.Time).Select(p => $"{p.Time.ToString("0.####", CultureInfo.InvariantCulture)}={p.Voltage.ToString("0.####", CultureInfo.InvariantCulture)}"));

    private void RebuildDragPlot()
    {
        var background = OxyColor.Parse("#171E28"); var border = OxyColor.Parse("#263140"); var secondary = OxyColor.Parse("#A6B4C2"); var accent = OxyColor.Parse("#4ED4C4");
        var model = new PlotModel { Background = background, PlotAreaBorderColor = border, TextColor = secondary, Title = "DRAG CONTROL POINTS", TitleColor = OxyColor.Parse("#E8EDF2"), DefaultFont = "Segoe UI", DefaultFontSize = 11, Padding = new OxyThickness(12) };
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Time / s", Minimum = 0, MinimumPadding = 0, MaximumPadding = .08, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = border, MinorGridlineStyle = LineStyle.Dot, MinorGridlineColor = border });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Voltage / VDC", Minimum = 0, MinimumPadding = .05, MaximumPadding = .15, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = border, MinorGridlineStyle = LineStyle.Dot, MinorGridlineColor = border });
        var line = new LineSeries { Color = accent, StrokeThickness = 2.2, Title = "Editable waveform", TrackerFormatString = string.Empty };
        var scatter = new ScatterSeries { MarkerType = MarkerType.Circle, MarkerSize = 6, MarkerFill = accent, MarkerStroke = OxyColors.White, MarkerStrokeThickness = 1.2, Title = "Drag points", TrackerFormatString = string.Empty };
        foreach (var p in DragPoints.OrderBy(p => p.Time)) { line.Points.Add(new DataPoint(p.Time, p.Voltage)); scatter.Points.Add(new ScatterPoint(p.Time, p.Voltage)); }
        model.Series.Add(line); model.Series.Add(scatter); DragEditModel = model;
    }

    private void BuildPreview()
    {
        var definition = SnapshotDefinition();
        previewPoints = BuildPoints(definition);
        if (previewPoints.Count > 2500) previewPoints = Thin(previewPoints, 2500);
        var min = previewPoints.Min(p => p.Voltage); var max = previewPoints.Max(p => p.Voltage); var total = previewPoints.Max(p => p.Time);
        Summary = $"{definition.Mode} / {(definition.Mode == "Curve" ? definition.Curve : definition.Mode)}  •  {previewPoints.Count:N0} preview points  •  {min:0.0000}–{max:0.0000} V  •  one loop {total:0.###} s  •  repeat {definition.RepeatCount}× / delay {definition.RepeatDelayMs} ms  •  execution step {definition.StepMs} ms";
        RebuildPlot(definition); Disarm();
    }

    private void RebuildPlot(Mil704SavedProfile? definition = null)
    {
        try
        {
            definition ??= SnapshotDefinition();
            List<(double Time, double Voltage)> measured;
            lock (measuredSync) measured = Thin(measuredPoints, 1500);
            PreviewModel = BuildPlot(previewPoints, measured, definition, ShowMeasured);
        }
        catch { }
    }

    private Mil704SavedProfile SnapshotDefinition()
    {
        var current = Positive(CurrentLimit, "Current limit", 0.0001, 1000);
        var stepRaw = Numbers.Parse(StepMs, "Step interval");
        if (stepRaw != Math.Truncate(stepRaw) || stepRaw < 20 || stepRaw > 60000) throw new ArgumentException("Step interval must be a whole number from 20 to 60000 ms.");
        var start = Positive(StartVoltage, "Start voltage", 0, 1000); var end = Positive(EndVoltage, "End voltage", 0, 1000);
        var duration = Positive(DurationSeconds, "Ramp duration", 0.001, 86400); var pre = Positive(PreHoldSeconds, "Pre-hold", 0, 86400); var post = Positive(PostHoldSeconds, "Post-hold", 0, 86400);
        if (!Curves.Contains(Curve)) throw new ArgumentException("Select a valid curve type.");
        if (Mode == "Curve" && Curve == ImageEquationCurve &&
            (Math.Abs(start - 25) > .00001 || Math.Abs(end - 15.75) > .00001 ||
             Math.Abs(pre - .05) > .00001 || Math.Abs(duration - 7.708) > .00001))
            throw new ArgumentException("Image equation has fixed per-channel values: start 25 V, end 15.75 V, pre-hold 0.05 s and ramp 7.708 s. Use Custom Points for other voltages/timing.");
        var interruptRaw = Numbers.Parse(InterruptMs, "Power interrupt duration");
        if (interruptRaw != Math.Truncate(interruptRaw) || interruptRaw < 1 || interruptRaw > 60000) throw new ArgumentException("Power interrupt duration must be a whole number from 1 to 60000 ms.");
        var repeatRaw = Numbers.Parse(RepeatCount, "Repeat count");
        if (repeatRaw != Math.Truncate(repeatRaw) || repeatRaw < 1 || repeatRaw > 100) throw new ArgumentException("Repeat count must be a whole number from 1 to 100.");
        var delayRaw = Numbers.Parse(RepeatDelayMs, "Repeat delay");
        if (delayRaw != Math.Truncate(delayRaw) || delayRaw < 0 || delayRaw > 600000) throw new ArgumentException("Repeat delay must be a whole number from 0 to 600000 ms.");
        if (Mode is "Custom points" or "Drag points") ParseCustomPoints(CustomPoints);
        if (Mode == "Segment editor") ValidateSegments(Segments.Select(x => x.Snapshot()));
        return new Mil704SavedProfile
        {
            Name = ProfileName, Channel = Channel, Mode = Mode, Curve = Curve, StartVoltage = start, EndVoltage = end,
            DurationSeconds = duration, PreHoldSeconds = pre, PostHoldSeconds = post, CurrentLimit = current,
            StepMs = (int)stepRaw, CustomPoints = CustomPoints, InterruptMs = (int)interruptRaw,
            RepeatCount = (int)repeatRaw, RepeatDelayMs = (int)delayRaw, Segments = Segments.Select(x => x.Snapshot()).ToList()
        };
    }

    private CustomTestProfile BuildExecutionProfile()
    {
        var profile = activeProfile ?? throw new InvalidOperationException("Connect a supported GPP instrument before ARM.");
        var definition = SnapshotDefinition();
        if (!Channels.Contains(definition.Channel)) throw new ArgumentException("Select a programmable source channel.");
        var baseSteps = definition.Mode switch
        {
            "Power interrupt" => BuildInterruptSteps(definition),
            "Custom points" or "Drag points" => BuildCustomPointSteps(definition),
            "Segment editor" => BuildSegmentSteps(definition),
            _ => BuildCurveSteps(definition)
        };
        var steps = ExpandRepeats(baseSteps, definition.RepeatCount, definition.RepeatDelayMs);
        if (steps.Count > 10000) throw new ArgumentException("Waveform plus repeats generates more than 10,000 execution steps. Increase the step interval or reduce repeat count.");
        var custom = new CustomTestProfile { Name = definition.Name, TargetModel = profile.ModelName, Backend = ExecutionBackend.PcTimed, Steps = steps };
        custom.Validate(profile);
        if (!owner.IsChannelMonitored(definition.Channel)) throw new ArgumentException($"Enable monitoring for CH{definition.Channel} before running a 704 waveform.");
        return custom;
    }

    private static List<CustomTestStep> ExpandRepeats(List<CustomTestStep> baseSteps, int repeatCount, int repeatDelayMs)
    {
        var estimated = (long)baseSteps.Count * repeatCount + Math.Max(0, repeatCount - 1);
        if (estimated > 10000) throw new ArgumentException("Repeat expansion exceeds 10,000 execution steps.");
        var result = new List<CustomTestStep>((int)estimated);
        for (var r = 0; r < repeatCount; r++)
        {
            result.AddRange(baseSteps);
            if (r < repeatCount - 1 && repeatDelayMs > 0)
            {
                var last = baseSteps[^1];
                result.Add(new(last.Channel, last.Voltage, last.CurrentLimit, last.OutputOn, repeatDelayMs));
            }
        }
        return result;
    }

    private static List<CustomTestStep> BuildCurveSteps(Mil704SavedProfile d)
    {
        var steps = new List<CustomTestStep>(); var preMs = checked((int)Math.Round(d.PreHoldSeconds * 1000)); var postMs = checked((int)Math.Round(d.PostHoldSeconds * 1000));
        if (preMs > 0) steps.Add(new(d.Channel, d.StartVoltage, d.CurrentLimit, true, preMs));
        var rampSamples = Math.Max(1, (int)Math.Ceiling(d.DurationSeconds * 1000.0 / d.StepMs));
        for (var i = 1; i <= rampSamples; i++)
        {
            var x = i / (double)rampSamples; var rampTime = d.DurationSeconds * x; var v = VoltageAt(d, x, rampTime);
            var remainingMs = d.DurationSeconds * 1000.0 - (i - 1) * d.StepMs; var dwell = (int)Math.Max(1, Math.Min(d.StepMs, Math.Round(remainingMs)));
            steps.Add(new(d.Channel, v, d.CurrentLimit, true, dwell));
        }
        if (postMs > 0) steps.Add(new(d.Channel, d.EndVoltage, d.CurrentLimit, true, postMs));
        return steps;
    }

    private static List<CustomTestStep> BuildCustomPointSteps(Mil704SavedProfile d)
    {
        var points = ParseCustomPoints(d.CustomPoints); var steps = new List<CustomTestStep>();
        for (var segment = 0; segment < points.Count - 1; segment++)
        {
            var a = points[segment]; var b = points[segment + 1]; var durationMs = Math.Max(1, (int)Math.Round((b.Time - a.Time) * 1000));
            var samples = Math.Max(1, (int)Math.Ceiling(durationMs / (double)d.StepMs));
            for (var i = 0; i < samples; i++)
            {
                var x = i / (double)samples; var v = a.Voltage + (b.Voltage - a.Voltage) * x; var dwell = Math.Max(1, (int)Math.Round(durationMs / (double)samples));
                steps.Add(new(d.Channel, v, d.CurrentLimit, true, dwell));
            }
        }
        steps.Add(new(d.Channel, points[^1].Voltage, d.CurrentLimit, true, 1)); return steps;
    }

    private static List<CustomTestStep> BuildInterruptSteps(Mil704SavedProfile d)
    {
        var preMs = checked((int)Math.Round(d.PreHoldSeconds * 1000)); var postMs = checked((int)Math.Round(d.PostHoldSeconds * 1000)); var steps = new List<CustomTestStep>();
        if (preMs > 0) steps.Add(new(d.Channel, d.StartVoltage, d.CurrentLimit, true, preMs));
        steps.Add(new(d.Channel, d.StartVoltage, d.CurrentLimit, false, d.InterruptMs)); steps.Add(new(d.Channel, d.StartVoltage, d.CurrentLimit, true, Math.Max(1, postMs))); return steps;
    }

    private static List<CustomTestStep> BuildSegmentSteps(Mil704SavedProfile d)
    {
        ValidateSegments(d.Segments); var steps = new List<CustomTestStep>();
        foreach (var s in d.Segments)
        {
            if (s.Type == "Output Off") { steps.Add(new(d.Channel, s.StartVoltage, d.CurrentLimit, false, s.DurationMs)); continue; }
            if (s.Type == "Hold") { steps.Add(new(d.Channel, s.StartVoltage, d.CurrentLimit, true, s.DurationMs)); continue; }
            var samples = Math.Max(1, (int)Math.Ceiling(s.DurationMs / (double)d.StepMs));
            for (var i = 1; i <= samples; i++)
            {
                var x = i / (double)samples; var shaped = s.Type == "Exponential Ramp" ? (1 - Math.Exp(-4 * x)) / (1 - Math.Exp(-4)) : x;
                var v = s.StartVoltage + (s.EndVoltage - s.StartVoltage) * shaped; var dwell = Math.Max(1, (int)Math.Round(s.DurationMs / (double)samples));
                steps.Add(new(d.Channel, v, d.CurrentLimit, true, dwell));
            }
        }
        return steps;
    }

    private static void ValidateSegments(IEnumerable<Mil704SegmentDefinition> segments)
    {
        var list = segments.ToList(); if (list.Count is < 1 or > 500) throw new ArgumentException("Segment Editor requires 1–500 rows.");
        foreach (var s in list)
        {
            if (!new[] { "Hold", "Linear Ramp", "Exponential Ramp", "Output Off" }.Contains(s.Type)) throw new ArgumentException($"Unsupported segment type: {s.Type}");
            if (!double.IsFinite(s.StartVoltage) || !double.IsFinite(s.EndVoltage) || s.StartVoltage < 0 || s.StartVoltage > 1000 || s.EndVoltage < 0 || s.EndVoltage > 1000) throw new ArgumentException("Segment voltages must be 0–1000 V in the editor preview.");
            if (s.DurationMs < 1 || s.DurationMs > 86400000) throw new ArgumentException("Segment duration must be 1–86400000 ms.");
        }
    }

    private static List<(double Time, double Voltage)> BuildPoints(Mil704SavedProfile d)
    {
        if (d.Mode is "Custom points" or "Drag points") return ParseCustomPoints(d.CustomPoints).Select(p => (p.Time, p.Voltage)).ToList();
        if (d.Mode == "Segment editor") return BuildSegmentPoints(d);
        if (d.Mode == "Power interrupt")
        {
            var t0 = 0.0; var t1 = d.PreHoldSeconds; var t2 = t1 + d.InterruptMs / 1000.0; var t3 = t2 + Math.Max(0.001, d.PostHoldSeconds);
            return [(t0, d.StartVoltage), (t1, d.StartVoltage), (t1, 0), (t2, 0), (t2, d.StartVoltage), (t3, d.StartVoltage)];
        }
        var points = new List<(double Time, double Voltage)> { (0, d.StartVoltage) };
        if (d.PreHoldSeconds > 0) points.Add((d.PreHoldSeconds, d.StartVoltage));
        var samples = Math.Clamp((int)Math.Ceiling(d.DurationSeconds * 1000.0 / Math.Max(20, d.StepMs)), 2, 2000);
        for (var i = 1; i <= samples; i++) { var x = i / (double)samples; var rampTime = d.DurationSeconds * x; points.Add((d.PreHoldSeconds + rampTime, VoltageAt(d, x, rampTime))); }
        if (d.PostHoldSeconds > 0) points.Add((d.PreHoldSeconds + d.DurationSeconds + d.PostHoldSeconds, d.EndVoltage)); return points;
    }

    private static List<(double Time, double Voltage)> BuildSegmentPoints(Mil704SavedProfile d)
    {
        ValidateSegments(d.Segments); var points = new List<(double Time, double Voltage)>(); var time = 0.0;
        foreach (var s in d.Segments)
        {
            var duration = s.DurationMs / 1000.0;
            if (s.Type == "Output Off")
            {
                if (points.Count == 0) points.Add((time, s.StartVoltage)); else points.Add((time, points[^1].Voltage));
                points.Add((time, 0)); time += duration; points.Add((time, 0)); continue;
            }
            if (points.Count == 0) points.Add((time, s.StartVoltage)); else if (Math.Abs(points[^1].Voltage - s.StartVoltage) > 1e-9) points.Add((time, s.StartVoltage));
            if (s.Type == "Hold") { time += duration; points.Add((time, s.StartVoltage)); continue; }
            var samples = Math.Clamp((int)Math.Ceiling(s.DurationMs / (double)Math.Max(20, d.StepMs)), 2, 250);
            for (var i = 1; i <= samples; i++)
            {
                var x = i / (double)samples; var shaped = s.Type == "Exponential Ramp" ? (1 - Math.Exp(-4 * x)) / (1 - Math.Exp(-4)) : x;
                points.Add((time + duration * x, s.StartVoltage + (s.EndVoltage - s.StartVoltage) * shaped));
            }
            time += duration;
        }
        return points;
    }

    private static List<(double Time, double Voltage)> ParseCustomPoints(string text)
    {
        var result = new List<(double Time, double Voltage)>();
        foreach (var token in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) throw new ArgumentException("Custom points must use time=voltage pairs separated by semicolons. Example: 0=28; 0.05=28; 0.10=20");
            var t = Numbers.Parse(parts[0], "Custom point time"); var v = Numbers.Parse(parts[1], "Custom point voltage");
            if (t < 0 || t > 86400 || v < 0 || v > 1000) throw new ArgumentException("Custom point time/voltage is outside the supported preview range."); result.Add((t, v));
        }
        if (result.Count < 2) throw new ArgumentException("Enter at least two custom points."); result = result.OrderBy(p => p.Time).ToList();
        for (var i = 1; i < result.Count; i++) if (result[i].Time <= result[i - 1].Time) throw new ArgumentException("Custom point times must be unique and increasing."); return result;
    }

    private static double VoltageAt(Mil704SavedProfile d, double x, double rampTime)
    {
        x = Math.Clamp(x, 0, 1);
        if (d.Curve == ImageEquationCurve)
        {
            var absoluteTime = d.PreHoldSeconds + rampTime;
            return absoluteTime < 7.758 ? 15.69 + 0.4655 / Math.Max(0.05, absoluteTime) : 15.75;
        }
        var f = d.Curve switch { "Exponential" => (1 - Math.Exp(-4 * x)) / (1 - Math.Exp(-4)), "Logarithmic" => Math.Log10(1 + 9 * x), "Inverse-time" => (1 - 1 / (1 + 12 * x)) / (1 - 1 / 13.0), _ => x };
        return d.StartVoltage + (d.EndVoltage - d.StartVoltage) * f;
    }

    private static PlotModel BuildPlot(IEnumerable<(double Time, double Voltage)> requested, IEnumerable<(double Time, double Voltage)> measured, Mil704SavedProfile d, bool showMeasured)
    {
        var background = OxyColor.Parse("#171E28"); var border = OxyColor.Parse("#263140"); var text = OxyColor.Parse("#E8EDF2"); var secondary = OxyColor.Parse("#A6B4C2"); var accent = OxyColor.Parse("#4ED4C4"); var measuredColor = OxyColor.Parse("#FFB84D");
        var model = new PlotModel { Background = background, PlotAreaBorderColor = border, TextColor = secondary, TitleColor = text, Title = $"CH{d.Channel}  •  {d.Mode.ToUpperInvariant()}  •  REQUESTED vs MEASURED", TitleFontSize = 14, DefaultFont = "Segoe UI", DefaultFontSize = 11, Padding = new OxyThickness(12) };
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Time from start / s", Minimum = 0, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = border, MinorGridlineStyle = LineStyle.Dot, MinorGridlineColor = border, TicklineColor = border, AxislineColor = border });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Voltage / VDC", MinimumPadding = .08, MaximumPadding = .12, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = border, MinorGridlineStyle = LineStyle.Dot, MinorGridlineColor = border, TicklineColor = border, AxislineColor = border });
        var requestedLine = new LineSeries { Title = "Requested / profile", Color = accent, StrokeThickness = 2.5, TrackerFormatString = "{0}\nTime: {2:0.0000} s\nRequested: {4:0.0000} V" };
        foreach (var p in requested) requestedLine.Points.Add(new DataPoint(p.Time, p.Voltage)); model.Series.Add(requestedLine);
        if (showMeasured)
        {
            var measuredLine = new LineSeries { Title = "Measured PSU output", Color = measuredColor, StrokeThickness = 2.0, TrackerFormatString = "{0}\nTime: {2:0.0000} s\nMeasured: {4:0.0000} V" };
            foreach (var p in measured) measuredLine.Points.Add(new DataPoint(p.Time, p.Voltage)); model.Series.Add(measuredLine);
        }
        return model;
    }

    public PlotModel CreatePreviewPlotSnapshot()
    {
        var definition = SnapshotDefinition();
        List<(double Time, double Voltage)> measured;
        lock (measuredSync) measured = Thin(measuredPoints, 1500);
        return BuildPlot(previewPoints.ToList(), measured, definition, ShowMeasured);
    }

    public string BuildExportBaseName() => $"GW_Studio_704_CH{Channel}_{ModeLabel.Replace(' ', '_')}";

    public void NotifyExternalStatus(string text) => Status = text;

    public string CurrentPdfWaveformTitle => $"704 waveform snapshot - CH{Channel} - {ModeLabel}";

    public IReadOnlyList<(double Time, double Voltage)> GetRequestedWaveformSnapshot() => previewPoints.ToList();

    public IReadOnlyList<(double Time, double Voltage)> GetMeasuredWaveformSnapshot()
    {
        lock (measuredSync) return measuredPoints.ToList();
    }

    private void LoadDefinition(Mil704SavedProfile p)
    {
        Channel = p.Channel; Mode = string.IsNullOrWhiteSpace(p.Mode) ? "Curve" : p.Mode;
        // Saved old 50 V equation profiles are deliberately migrated to fixed half-scale
        // setpoints, rather than silently applying a 50 V legacy voltage to hardware.
        var isEquation = p.Curve == ImageEquationCurve || p.Curve == LegacyImageEquationCurve;
        Curve = isEquation ? ImageEquationCurve : Curves.Contains(p.Curve) ? p.Curve : "Linear";
        StartVoltage = Numbers.F(isEquation ? 25.0 : p.StartVoltage);
        EndVoltage = Numbers.F(isEquation ? 15.75 : p.EndVoltage);
        DurationSeconds = (isEquation ? 7.708 : p.DurationSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        PreHoldSeconds = (isEquation ? .05 : p.PreHoldSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        PostHoldSeconds = p.PostHoldSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        CurrentLimit = Numbers.F(p.CurrentLimit, 4); StepMs = p.StepMs.ToString(CultureInfo.InvariantCulture); CustomPoints = p.CustomPoints ?? "0=28; 0.1=20"; InterruptMs = Math.Max(1, p.InterruptMs).ToString(CultureInfo.InvariantCulture); RepeatCount = Math.Clamp(p.RepeatCount <= 0 ? 1 : p.RepeatCount, 1, 100).ToString(CultureInfo.InvariantCulture); RepeatDelayMs = Math.Max(0, p.RepeatDelayMs).ToString(CultureInfo.InvariantCulture); SelectedTemplate = "Custom";
        if (p.Segments is { Count: > 0 }) ReplaceSegments(p.Segments);
        if (Mode == "Drag points") { try { var pts = ParseCustomPoints(CustomPoints); DragPoints.Clear(); foreach (var pt in Thin(pts, 32)) DragPoints.Add(new(pt.Time, pt.Voltage)); RebuildDragPlot(); } catch { ResetDragPoints(); } }
    }

    private void RefreshProfiles()
    {
        Directory.CreateDirectory(ProfileFolder); var old = SelectedSavedProfile?.FilePath; SavedProfiles.Clear();
        foreach (var file in Directory.EnumerateFiles(ProfileFolder, "*.json").Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc))
        {
            var name = Path.GetFileNameWithoutExtension(file.Name); try { name = JsonSerializer.Deserialize<Mil704SavedProfile>(File.ReadAllText(file.FullName))?.Name ?? name; } catch { }
            SavedProfiles.Add(new SavedProfileInfo(name, file.FullName, file.LastWriteTime));
        }
        SelectedSavedProfile = SavedProfiles.FirstOrDefault(x => string.Equals(x.FilePath, old, StringComparison.OrdinalIgnoreCase)) ?? SavedProfiles.FirstOrDefault();
    }

    private static List<(double Time, double Voltage)> Thin(IReadOnlyList<(double Time, double Voltage)> source, int max)
    {
        if (source.Count <= max) return source.ToList();
        var result = new List<(double Time, double Voltage)>(max) { source[0] };
        var stride = (source.Count - 1.0) / (max - 1.0);
        for (var i = 1; i < max - 1; i++) result.Add(source[(int)Math.Round(i * stride)]);
        result.Add(source[^1]); return result;
    }

    private static string SanitizeName(string input)
    {
        var text = string.IsNullOrWhiteSpace(input) ? "704 waveform" : input.Trim(); foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_'); return text.Length > 80 ? text[..80] : text;
    }

    private static string UniqueProfilePath(string name)
    {
        var path = Path.Combine(ProfileFolder, name + ".json"); if (!File.Exists(path)) return path;
        for (var i = 2; i < 10000; i++) { path = Path.Combine(ProfileFolder, $"{name} ({i}).json"); if (!File.Exists(path)) return path; }
        throw new IOException("Too many 704 profiles with the same name.");
    }

    private static double Positive(string text, string name, double min, double max)
    {
        var value = Numbers.Parse(text, name); if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"{name}: enter a value from {min} to {max}."); return value;
    }

    private void Safe(Action action) { try { action(); } catch (Exception ex) { owner.ReportError(ex); Status = "ERROR — " + ex.Message; } }
}
