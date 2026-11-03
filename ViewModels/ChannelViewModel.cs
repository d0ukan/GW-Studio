using System.Collections.ObjectModel;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.ViewModels;

public sealed class ChannelViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    public ChannelCapability Capability { get; }
    public int Channel { get; }
    public string Title => $"CH{Channel}";
    public string QuickOutputLabel => $"CH{Channel} ON / OFF";
    public string CurrentInputLabel => CurrentProgrammable ? "REQUESTED CURRENT LIMIT / A" : "FIXED CURRENT RATING / A";
    public string RatingText => Capability.RatingText;
    public bool UsesDiscreteVoltage => Capability.UsesDiscreteVoltage;
    public bool UsesContinuousVoltage => !UsesDiscreteVoltage;
    public bool CurrentProgrammable => Capability.CurrentProgrammable;
    public bool IsAdvancedMode => owner.IsAdvancedMode;
    public IReadOnlyList<double> AvailableVoltages => Capability.DiscreteVoltages ?? [];

    private string voltageInput = "0.0000", currentInput = "0.1000";
    private GppChannelState? state;
    private bool stale = true;
    private string maxCurrentAutoOffInput = "";
    private double? appliedMaxCurrentAutoOff;
    private bool protectionTripLatched;
    private bool channelError;
    private string protectionMessage = "Software max-current auto-off: disabled";

    private string warningVoltageInput = "", warningCurrentInput = "";
    private double? appliedWarningVoltage, appliedWarningCurrent;
    private string warningStatus = "Soft warning: disabled";
    private bool warningActive, criticalWarning;
    private bool isLocked;
    private string presetName = "";
    private QuickPreset? selectedPreset;
    private double selectedDiscreteVoltage;
    private bool includeInReport = true;

    public string VoltageInput { get => voltageInput; set => Set(ref voltageInput, value); }
    public string CurrentInput { get => currentInput; set => Set(ref currentInput, value); }
    public double SelectedDiscreteVoltage { get => selectedDiscreteVoltage; set { if (Set(ref selectedDiscreteVoltage, value)) VoltageInput = Numbers.F(value, 4); } }
    public bool IncludeInGraph { get => owner.Graph.IsChannelVisible(Channel); set { owner.Graph.SetChannelVisible(Channel, value); OnChanged(); } }
    public bool IncludeInReport { get => includeInReport; set => Set(ref includeInReport, value); }
    public string MaxCurrentAutoOffInput { get => maxCurrentAutoOffInput; set => Set(ref maxCurrentAutoOffInput, value); }
    public string WarningVoltageInput { get => warningVoltageInput; set => Set(ref warningVoltageInput, value); }
    public string WarningCurrentInput { get => warningCurrentInput; set => Set(ref warningCurrentInput, value); }
    public string MaxCurrentProtectionStatus => protectionMessage;
    public string WarningStatus => warningStatus;
    public bool WarningActive => warningActive;
    public bool CriticalWarning => criticalWarning;

    public bool IsLocked
    {
        get => isLocked;
        set
        {
            if (!Set(ref isLocked, value)) return;
            OnChanged(nameof(LockText)); OnChanged(nameof(CanEditSettings)); ApplyCommand.Refresh(); LoadPresetCommand?.Refresh();
        }
    }
    public string LockText => IsLocked ? "🔒 SETTINGS LOCKED" : "🔓 LOCK SETTINGS";
    public bool CanEditSettings => CanControl && !IsLocked;

    public ObservableCollection<QuickPreset> Presets { get; } = [];
    public string PresetName { get => presetName; set => Set(ref presetName, value); }
    public QuickPreset? SelectedPreset { get => selectedPreset; set { Set(ref selectedPreset, value); LoadPresetCommand.Refresh(); DeletePresetCommand.Refresh(); } }

    public string ActualVoltage => state == null || stale ? "—" : Numbers.F(state.Voltage, 4);
    public string ActualCurrent => state == null || stale ? "—" : Numbers.F(state.Current, 4);
    public string ActualPower => state == null || stale ? "—" : Numbers.F(state.Voltage * state.Current, 4);
    public string DeviceSetVoltage => state == null || stale ? "—" : Numbers.F(state.SetVoltage, 4);
    public string DeviceCurrentLimit => state == null || stale ? "—" : Numbers.F(state.CurrentLimit, 4);
    public string SetReadback => state == null || stale ? "Device setpoints: unknown" : $"Device setpoints  {Numbers.F(state.SetVoltage, 4)} V / {Numbers.F(state.CurrentLimit, 4)} A";
    public string OutputText => state == null || stale ? "OUTPUT UNKNOWN" : state.OutputOn ? "OUTPUT ON" : "OUTPUT OFF";
    public bool OutputOn => !stale && state?.OutputOn == true;
    public string OutputAction => OutputOn ? "OUTPUT ON" : "OUTPUT OFF";
    public string OperatingModeText
    {
        get
        {
            if (stale || state == null) return "NO LIVE MODE";
            var configured = owner.GetChannelModeText(Channel);
            if (configured.StartsWith("Load ", StringComparison.OrdinalIgnoreCase)) return configured;
            if (!state.OutputOn) return "SOURCE";
            return $"Source {InferSourceRegulation()}";
        }
    }
    public string ChannelStatus
    {
        get
        {
            if (channelError) return "ERROR";
            if (protectionTripLatched) return "TRIP";
            if (stale || state == null || !owner.Connected) return "DISCONNECTED";
            if (!state.OutputOn) return "OFF";
            var configured = owner.GetChannelModeText(Channel);
            if (configured.Equals("Load CR", StringComparison.OrdinalIgnoreCase)) return "CR";
            if (configured.Equals("Load CC", StringComparison.OrdinalIgnoreCase)) return "CC";
            if (configured.Equals("Load CV", StringComparison.OrdinalIgnoreCase)) return "CV";
            return InferSourceRegulation();
        }
    }
    public bool CanControl => owner.CanControl && owner.IsChannelMonitored(Channel);

    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand OutputCommand { get; }
    public AsyncCommand QuickOutputCommand { get; }
    public RelayCommand ToggleLockCommand { get; }
    public AsyncCommand SavePresetCommand { get; }
    public RelayCommand LoadPresetCommand { get; }
    public AsyncCommand DeletePresetCommand { get; }

    public ChannelViewModel(MainViewModel owner, ChannelCapability capability)
    {
        this.owner = owner; Capability = capability; Channel = capability.Channel;
        if (UsesDiscreteVoltage) { selectedDiscreteVoltage = AvailableVoltages[0]; voltageInput = Numbers.F(selectedDiscreteVoltage, 4); currentInput = Numbers.F(Capability.MaxCurrent, 4); }
        foreach (var p in owner.GetQuickPresets(Channel)) Presets.Add(p);

        ApplyCommand = new(async () =>
        {
            if (IsLocked) throw new InvalidOperationException($"CH{Channel} settings are locked.");
            var v = Numbers.Parse(VoltageInput, "Voltage");
            var i = Numbers.Parse(CurrentInput, "Current limit");
            owner.ActiveProfile.Validate(Channel, v, i);
            var maxCurrentAutoOff = ParseOptional(MaxCurrentAutoOffInput, "Max current auto-off", 0, Capability.MaxCurrent, strictlyPositive: true);
            var warnV = ParseOptional(WarningVoltageInput, "Voltage warning", 0, Capability.MaxVoltage, strictlyPositive: true);
            var warnI = ParseOptional(WarningCurrentInput, "Current warning", 0, Capability.MaxCurrent, strictlyPositive: true);
            await owner.OperateAsync(async ct =>
            {
                await owner.Device.ApplySettingsAsync(Channel, v, i, ct);
                ApplyMaxCurrentAutoOff(maxCurrentAutoOff);
                appliedWarningVoltage = warnV; appliedWarningCurrent = warnI;
                Update(await owner.Device.ReadChannelAsync(Channel, true, ct), false);
                owner.AddGraphEvent($"CH{Channel} SET {Numbers.F(v, 4)} V / {Numbers.F(i, 4)} A", "Setpoint", Channel);
            });
        }, owner.ReportError, () => CanEditSettings);

        OutputCommand = new(async () =>
        {
            if (state == null || stale) throw new InvalidOperationException("Refresh the channel state before changing its output.");
            bool enable = !state.OutputOn;
            await owner.OperateAsync(async ct =>
            {
                await owner.Device.SetOutputStateAsync(Channel, enable, ct);
                Update(await owner.Device.ReadChannelAsync(Channel, false, ct), false);
                owner.AddGraphEvent($"CH{Channel} OUTPUT {(enable ? "ON" : "OFF")}", enable ? "OutputOn" : "OutputOff", Channel);
            });
        }, owner.ReportError, () => CanControl);
        QuickOutputCommand = new(() => owner.QuickToggleOutputAsync(Channel), owner.ReportError, () => owner.CanQuickControl);

        ToggleLockCommand = new(() => IsLocked = !IsLocked);
        SavePresetCommand = new(async () =>
        {
            if (string.IsNullOrWhiteSpace(PresetName)) throw new ArgumentException("Enter a preset name.");
            var v = Numbers.Parse(VoltageInput, "Preset voltage");
            var i = Numbers.Parse(CurrentInput, "Preset current");
            owner.ActiveProfile.Validate(Channel, v, i);
            var preset = new QuickPreset { TargetModel = owner.ActiveProfile.ModelName, Channel = Channel, Name = PresetName.Trim(), Voltage = v, Current = i };
            var existing = Presets.FirstOrDefault(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) Presets.Remove(existing);
            Presets.Add(preset); SelectedPreset = preset;
            await owner.SaveQuickPresetsAsync(Channel, Presets);
            owner.ShowInfo($"CH{Channel} preset '{preset.Name}' saved. It only fills requested values; APPLY SETTINGS is still required.");
        }, owner.ReportError);
        LoadPresetCommand = new(() =>
        {
            if (SelectedPreset == null) return;
            VoltageInput = Numbers.F(SelectedPreset.Voltage, 4);
            CurrentInput = Numbers.F(SelectedPreset.Current, 4);
            if (UsesDiscreteVoltage) SelectedDiscreteVoltage = SelectedPreset.Voltage;
            owner.ShowInfo($"CH{Channel} preset '{SelectedPreset.Name}' loaded into requested values. Hardware was not changed.");
        }, () => SelectedPreset != null && !IsLocked);
        DeletePresetCommand = new(async () =>
        {
            if (SelectedPreset == null) return;
            var name = SelectedPreset.Name; Presets.Remove(SelectedPreset); SelectedPreset = null;
            await owner.SaveQuickPresetsAsync(Channel, Presets);
            owner.ShowInfo($"CH{Channel} preset '{name}' deleted.");
        }, owner.ReportError, () => SelectedPreset != null);
    }

    public void Update(GppChannelState next, bool populateInputs)
    {
        state = next; stale = false; channelError = false;
        if (populateInputs) { VoltageInput = Numbers.F(next.SetVoltage, 4); CurrentInput = Numbers.F(next.CurrentLimit, 4); if (UsesDiscreteVoltage) SelectedDiscreteVoltage = next.SetVoltage; }
        EvaluateWarnings(next);
        Notify();
    }
    public void MarkStale() { stale = true; warningActive = criticalWarning = false; warningStatus = "Soft warning: no live data"; Notify(); }

    private void EvaluateWarnings(GppChannelState sample)
    {
        var hits = new List<(string text, double ratio)>();
        if (appliedWarningVoltage is double v && sample.Voltage > v) hits.Add(($"V {sample.Voltage:0.0000} > {v:0.0000}", sample.Voltage / v));
        if (appliedWarningCurrent is double i && sample.Current > i) hits.Add(($"I {sample.Current:0.0000} > {i:0.0000}", sample.Current / i));
        warningActive = hits.Count > 0;
        criticalWarning = hits.Any(h => h.ratio >= 1.10);
        warningStatus = hits.Count == 0
            ? (appliedWarningVoltage == null && appliedWarningCurrent == null ? "Soft warning: disabled" : "Soft warning: armed / normal")
            : (criticalWarning ? "CRITICAL WARNING — " : "WARNING — ") + string.Join("  •  ", hits.Select(h => h.text));
        OnChanged(nameof(WarningStatus)); OnChanged(nameof(WarningActive)); OnChanged(nameof(CriticalWarning));
    }

    public bool TryStartMaxCurrentTrip(GppChannelState sample, out double limit)
    {
        limit = 0;
        if (!sample.OutputOn)
        {
            protectionTripLatched = false;
            if (!protectionMessage.StartsWith("TRIPPED", StringComparison.OrdinalIgnoreCase))
            { protectionMessage = BuildProtectionStatus(); OnChanged(nameof(MaxCurrentProtectionStatus)); }
            return false;
        }
        if (!TryGetMaxCurrentLimit(out limit)) return false;
        if (sample.Current <= limit || protectionTripLatched) return false;
        protectionTripLatched = true;
        protectionMessage = $"TRIP REQUESTED — {Numbers.F(sample.Current, 4)} A > {Numbers.F(limit, 4)} A";
        OnChanged(nameof(MaxCurrentProtectionStatus)); return true;
    }
    public void MarkMaxCurrentTripCompleted(double actual, double limit) { protectionMessage = $"TRIPPED — output forced OFF at {Numbers.F(actual, 4)} A (limit {Numbers.F(limit, 4)} A)"; OnChanged(nameof(MaxCurrentProtectionStatus)); OnChanged(nameof(ChannelStatus)); }
    public void MarkMaxCurrentTripFailed(Exception ex) { channelError = true; protectionMessage = "AUTO-OFF FAILED — " + ex.Message; OnChanged(nameof(MaxCurrentProtectionStatus)); OnChanged(nameof(ChannelStatus)); }

    private bool TryGetMaxCurrentLimit(out double limit) { if (appliedMaxCurrentAutoOff is double applied) { limit = applied; return true; } limit = 0; return false; }
    private static double? ParseOptional(string input, string label, double min, double max, bool strictlyPositive)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var value = Numbers.Parse(input, label);
        if (!double.IsFinite(value) || value > max || (strictlyPositive ? value <= min : value < min))
            throw new ArgumentException($"{label} must be {(strictlyPositive ? "> " : "≥ ")}{min} and ≤ {max}, or blank to disable.");
        return value;
    }
    private void ApplyMaxCurrentAutoOff(double? limit) { appliedMaxCurrentAutoOff = limit; protectionTripLatched = false; channelError = false; protectionMessage = BuildProtectionStatus(); OnChanged(nameof(MaxCurrentProtectionStatus)); OnChanged(nameof(ChannelStatus)); }
    private string BuildProtectionStatus() => appliedMaxCurrentAutoOff is double limit ? $"ARMED — software auto-off above {Numbers.F(limit, 4)} A" : "Software max-current auto-off: disabled";

    private string InferSourceRegulation()
    {
        if (state == null || !state.OutputOn) return "OFF";
        if (!CurrentProgrammable) return "CV";
        var currentTolerance = Math.Max(0.01, state.CurrentLimit * 0.02);
        var voltageDrop = state.SetVoltage - state.Voltage;
        return state.Current >= Math.Max(0, state.CurrentLimit - currentTolerance) && voltageDrop > 0.03 ? "CC" : "CV";
    }

    private void Notify()
    {
        foreach (var n in new[] { nameof(ActualVoltage), nameof(ActualCurrent), nameof(ActualPower), nameof(DeviceSetVoltage), nameof(DeviceCurrentLimit), nameof(SetReadback), nameof(OutputText), nameof(OutputOn), nameof(OutputAction), nameof(OperatingModeText), nameof(ChannelStatus), nameof(MaxCurrentProtectionStatus), nameof(WarningStatus), nameof(WarningActive), nameof(CriticalWarning), nameof(CanEditSettings) }) OnChanged(n);
        owner.NotifyOutputStates();
    }
    public void NotifyUiModeChanged() => OnChanged(nameof(IsAdvancedMode));
    public void Refresh() { OnChanged(nameof(CanControl)); OnChanged(nameof(CanEditSettings)); ApplyCommand.Refresh(); OutputCommand.Refresh(); QuickOutputCommand.Refresh(); SavePresetCommand.Refresh(); LoadPresetCommand.Refresh(); DeletePresetCommand.Refresh(); }
}
