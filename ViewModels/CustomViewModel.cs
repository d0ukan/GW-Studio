using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.ViewModels;
public sealed class StepViewModel : ObservableObject
{
    private int number;
    public int Number { get => number; set => Set(ref number, value); }
    private int channel = 1; private bool outputOn; private string voltage = "28.000", current = "2.0000", duration = "1000";
    public int Channel { get => channel; set => Set(ref channel, value); }
    public string Voltage { get => voltage; set => Set(ref voltage, value); }
    public string Current { get => current; set => Set(ref current, value); }
    public bool OutputOn { get => outputOn; set => Set(ref outputOn, value); }
    public string Duration { get => duration; set => Set(ref duration, value); }
    public CustomTestStep ToStep()
    {
        var ms = Numbers.Parse(Duration, "Duration"); if (ms != Math.Truncate(ms) || ms < 0 || ms > 86400000) throw new ArgumentException("Duration must be a whole number of milliseconds, 0–86400000.");
        return new(Channel, Numbers.Parse(Voltage, "Voltage"), Numbers.Parse(Current, "Current limit"), OutputOn, (int)ms);
    }
    public static StepViewModel From(CustomTestStep s) => new() { Channel = s.Channel, Voltage = Numbers.F(s.Voltage), Current = Numbers.F(s.CurrentLimit, 4), OutputOn = s.OutputOn, Duration = s.DurationMs.ToString() };
}
public sealed class CustomViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    private DeviceProfile? activeProfile;
    private CustomTestProfile? armed;
    private string status = "IDLE — choose a preset or edit the timeline";
    private string profileName = "My profile";
    private StepViewModel? selected;
    private SavedProfileInfo? selectedProfile;
    public ObservableCollection<StepViewModel> Steps { get; } = [];
    public ObservableCollection<SavedProfileInfo> SavedProfiles { get; } = [];
    public StepViewModel? Selected { get => selected; set { Set(ref selected, value); Refresh(); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public string ProfileName { get => profileName; set => Set(ref profileName, value); }
    public SavedProfileInfo? SelectedProfile { get => selectedProfile; set { Set(ref selectedProfile, value); LoadCommand?.Refresh(); DeleteProfileCommand?.Refresh(); } }
    public bool CanEdit => !owner.IsRunning;
    public bool IsArmed => armed != null;
    public string ArmLabel => IsArmed ? "ARMED — READY" : "ARM PROFILE";
    public string Preset { get; set; } = "Pulse";
    public string[] Presets { get; } = ["Pulse", "Voltage Step", "Power Cycle", "Custom Timeline"];
    public ObservableCollection<int> Channels { get; } = [];
    public int Channel { get; set; } = 1;
    public string Voltage { get; set; } = "28.000";
    public string StepVoltage { get; set; } = "31.000";
    public string Current { get; set; } = "2.0000";
    public string OnMs { get; set; } = "50";
    public string OffMs { get; set; } = "500";
    public string DelayMs { get; set; } = "1000";
    public string Repeat { get; set; } = "1";
    public bool InitialOn { get; set; }
    public bool FinalOn { get; set; }
    public bool ReturnToBase { get; set; } = true;
    public RelayCommand GenerateCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand DownCommand { get; }
    public RelayCommand ArmCommand { get; }
    public AsyncCommand RunCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand LoadCommand { get; }
    public AsyncCommand DeleteProfileCommand { get; }
    public RelayCommand RefreshProfilesCommand { get; }
    public CustomViewModel(MainViewModel owner)
    {
        this.owner = owner;
        Steps.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (StepViewModel s in e.NewItems) s.PropertyChanged += RowChanged;
            if (e.OldItems != null) foreach (StepViewModel s in e.OldItems) s.PropertyChanged -= RowChanged;
            for (int i = 0; i < Steps.Count; i++) Steps[i].Number = i + 1;
            Disarm();
        };
        GenerateCommand = new(() => Safe(Generate), () => CanEdit);
        AddCommand = new(() => Steps.Add(new()), () => CanEdit);
        DuplicateCommand = new(() => Safe(() => { if (Selected != null) Steps.Insert(Steps.IndexOf(Selected) + 1, StepViewModel.From(Selected.ToStep())); }), () => CanEdit && Selected != null);
        DeleteCommand = new(() => { if (Selected != null) Steps.Remove(Selected); }, () => CanEdit && Selected != null);
        UpCommand = new(() => Move(-1), () => CanEdit && Selected != null && Steps.IndexOf(Selected) > 0);
        DownCommand = new(() => Move(1), () => CanEdit && Selected != null && Steps.IndexOf(Selected) < Steps.Count - 1);
        ArmCommand = new(() => Safe(() => { armed = Snapshot(); Status = $"ARMED — {armed.Steps.Count} steps. RUN will change outputs."; Refresh(); }), () => CanEdit && owner.CanControl && Steps.Count > 0);
        RunCommand = new(async () => { var profile = armed ?? throw new InvalidOperationException("ARM the profile first."); Disarm(); await owner.RunProfileAsync(profile); }, owner.ReportError, () => owner.CanControl && IsArmed);
        StopCommand = new(owner.StopTestAsync, owner.ReportError, () => owner.IsRunning);
        SaveCommand = new(async () =>
        {
            var profile = Snapshot();
            var name = SanitizeName(ProfileName);
            profile.Name = name;
            Directory.CreateDirectory(ProfileFolder);
            var file = UniqueProfilePath(name);
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
            await JsonSerializer.SerializeAsync(stream, profile, new JsonSerializerOptions { WriteIndented = true });
            RefreshProfiles();
            SelectedProfile = SavedProfiles.FirstOrDefault(x => x.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase));
            Status = "Profile saved to the in-app library: " + name;
        }, owner.ReportError, () => CanEdit);
        LoadCommand = new(async () =>
        {
            var selectedProfile = SelectedProfile ?? throw new InvalidOperationException("Select a saved profile in the library first.");
            var file = new FileInfo(selectedProfile.FilePath);
            if (!file.Exists) throw new FileNotFoundException("The selected profile file no longer exists.", file.FullName);
            if (file.Length > 2000000) throw new ArgumentException("Profile file must be smaller than 2 MB.");
            var profile = JsonSerializer.Deserialize<CustomTestProfile>(await File.ReadAllTextAsync(file.FullName)) ?? throw new ArgumentException("Invalid profile JSON.");
            profile.Validate(activeProfile ?? throw new InvalidOperationException("Connect the intended instrument before loading a profile.")); Replace(profile.Steps); ProfileName = profile.Name;
            Status = $"Profile loaded: {profile.Name} — review and ARM before running.";
        }, owner.ReportError, () => CanEdit && SelectedProfile != null);
        DeleteProfileCommand = new(async () =>
        {
            var selectedProfile = SelectedProfile ?? throw new InvalidOperationException("Select a saved profile first.");
            if (File.Exists(selectedProfile.FilePath)) File.Delete(selectedProfile.FilePath);
            await Task.CompletedTask; RefreshProfiles(); Status = "Saved profile deleted.";
        }, owner.ReportError, () => CanEdit && SelectedProfile != null);
        RefreshProfilesCommand = new(RefreshProfiles);
        RefreshProfiles();
    }
    private static string ProfileFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dou-GW-Studio", "Profiles");

    public void ConfigureProfile(DeviceProfile profile)
    {
        activeProfile = profile;
        Channels.Clear();
        foreach (var capability in profile.Channels.Where(c => c.CurrentProgrammable)) Channels.Add(capability.Channel);
        Channel = Channels.FirstOrDefault();
        var first = profile.Get(Channel);
        Voltage = Numbers.F(Math.Min(28, first.MaxVoltage));
        StepVoltage = Numbers.F(Math.Min(31, first.MaxVoltage));
        Current = Numbers.F(Math.Min(2, first.MaxCurrent), 4);
        Steps.Clear();
        Generate();
        Status = $"{profile.ModelName} profile active — timeline channels and limits were adjusted automatically.";
        OnChanged(nameof(Channels)); OnChanged(nameof(Channel)); OnChanged(nameof(Voltage)); OnChanged(nameof(StepVoltage)); OnChanged(nameof(Current));
    }
    private static string SanitizeName(string input)
    {
        var text = string.IsNullOrWhiteSpace(input) ? "Profile" : input.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
        return text.Length > 80 ? text[..80] : text;
    }
    private static string UniqueProfilePath(string name)
    {
        var path = Path.Combine(ProfileFolder, name + ".json");
        if (!File.Exists(path)) return path;
        for (var i = 2; i < 10000; i++)
        {
            path = Path.Combine(ProfileFolder, $"{name} ({i}).json");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Too many profiles with the same name.");
    }
    private void RefreshProfiles()
    {
        Directory.CreateDirectory(ProfileFolder);
        var oldPath = SelectedProfile?.FilePath;
        SavedProfiles.Clear();
        foreach (var file in Directory.EnumerateFiles(ProfileFolder, "*.json").Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc))
        {
            var name = Path.GetFileNameWithoutExtension(file.Name);
            try
            {
                var profile = JsonSerializer.Deserialize<CustomTestProfile>(File.ReadAllText(file.FullName));
                if (!string.IsNullOrWhiteSpace(profile?.Name)) name = profile.Name;
            }
            catch { }
            SavedProfiles.Add(new SavedProfileInfo(name, file.FullName, file.LastWriteTime));
        }
        SelectedProfile = SavedProfiles.FirstOrDefault(x => string.Equals(x.FilePath, oldPath, StringComparison.OrdinalIgnoreCase)) ?? SavedProfiles.FirstOrDefault();
    }

    public void DeleteSelectedSteps(IEnumerable<StepViewModel> rows)
    {
        if (!CanEdit) return;
        var selectedRows = rows.Distinct().OrderByDescending(Steps.IndexOf).ToArray();
        foreach (var row in selectedRows) if (Steps.Contains(row)) Steps.Remove(row);
        Selected = Steps.FirstOrDefault(); Disarm(); Refresh();
    }

    private void RowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Disarm();
    private void Safe(Action action) { try { action(); } catch (Exception ex) { owner.ReportError(ex); } }
    private int Whole(string text, string name, int min, int max)
    { var n = Numbers.Parse(text, name); if (n != Math.Truncate(n) || n < min || n > max) throw new ArgumentException($"{name}: enter a whole number {min}–{max}."); return (int)n; }
    private void Generate()
    {
        var profileCapabilities = activeProfile ?? throw new InvalidOperationException("Connect a supported GPP instrument before building a timeline.");
        if (Preset == "Custom Timeline") { if (Steps.Count == 0) Steps.Add(new()); return; }
        var v = Numbers.Parse(Voltage, "Voltage"); var current = Numbers.Parse(Current, "Current limit");
        profileCapabilities.Validate(Channel, v, current);
        var on = Whole(OnMs, "ON / step duration", 1, 86400000); var off = Whole(OffMs, "OFF duration", 1, 86400000);
        var repeat = Whole(Repeat, "Repeat", 1, 1000); var delay = Whole(DelayMs, "Delay", 0, 86400000);
        var steps = new List<CustomTestStep>();
        if (Preset == "Voltage Step")
        {
            var step = Numbers.Parse(StepVoltage, "Step voltage"); profileCapabilities.Validate(Channel, step, current);
            steps.Add(new(Channel, v, current, true, delay)); steps.Add(new(Channel, step, current, true, on));
            if (ReturnToBase) steps.Add(new(Channel, v, current, true, 0));
        }
        else
        {
            steps.Add(new(Channel, v, current, InitialOn, delay));
            for (int i = 0; i < repeat; i++)
            { steps.Add(new(Channel, v, current, true, on)); steps.Add(new(Channel, v, current, false, i < repeat - 1 ? off : 0)); }
            steps.Add(new(Channel, v, current, FinalOn, 0));
        }
        var profile = new CustomTestProfile { TargetModel = profileCapabilities.ModelName, Steps = steps }; profile.Validate(profileCapabilities); Replace(steps);
        Status = "Timeline generated — review exact step order, then ARM. Preset edits apply with BUILD TIMELINE.";
    }
    private void Replace(IEnumerable<CustomTestStep> steps) { foreach (var row in Steps) row.PropertyChanged -= RowChanged; Steps.Clear(); foreach (var s in steps) Steps.Add(StepViewModel.From(s)); Refresh(); }
    private CustomTestProfile Snapshot()
    {
        var capabilities = activeProfile ?? throw new InvalidOperationException("Connect a supported GPP instrument first.");
        var p = new CustomTestProfile { TargetModel = capabilities.ModelName, Steps = Steps.Select(s => s.ToStep()).ToList() };
        p.Validate(capabilities);
        var unmonitored = p.Steps.Select(s => s.Channel).Distinct().Where(ch => !owner.IsChannelMonitored(ch)).ToArray();
        if (unmonitored.Length > 0)
            throw new ArgumentException("Enable monitoring before using these channels in a profile: " + string.Join(", ", unmonitored.Select(ch => $"CH{ch}")));
        return p;
    }
    private void Move(int delta) { if (Selected == null) return; var i = Steps.IndexOf(Selected); if (i + delta >= 0 && i + delta < Steps.Count) Steps.Move(i, i + delta); Refresh(); }
    public void Disarm() { armed = null; OnChanged(nameof(IsArmed)); OnChanged(nameof(ArmLabel)); RunCommand?.Refresh(); ArmCommand?.Refresh(); }
    public void UpdateProgress(TestProgress p)
    {
        Status = $"{p.State}  |  Step {p.Step}/{p.TotalSteps}  |  Elapsed {p.Elapsed.TotalSeconds:F1} s  |  Nominal remaining {p.NominalRemaining.TotalSeconds:F1} s" +
            (p.Requested == null ? "" : $"  |  CH{p.Requested.Channel}: {Numbers.F(p.Requested.Voltage)} V / {Numbers.F(p.Requested.CurrentLimit, 4)} A");
    }
    public void SetStatus(string text) => Status = text;
    public void Refresh()
    {
        OnChanged(nameof(CanEdit)); OnChanged(nameof(IsArmed)); OnChanged(nameof(ArmLabel));
        foreach (var c in new[] { GenerateCommand, AddCommand, DuplicateCommand, DeleteCommand, UpCommand, DownCommand, ArmCommand }) c?.Refresh();
        RunCommand?.Refresh(); StopCommand?.Refresh(); SaveCommand?.Refresh(); LoadCommand?.Refresh(); DeleteProfileCommand?.Refresh();
    }
}
