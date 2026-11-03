using System.Collections.ObjectModel;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.ViewModels;

public sealed class NativeSequenceStepViewModel : ObservableObject
{
    private int number;
    private string voltage = "28.000", current = "1.0000", duration = "1";
    public int Number { get => number; set => Set(ref number, value); }
    public string Voltage { get => voltage; set => Set(ref voltage, value); }
    public string Current { get => current; set => Set(ref current, value); }
    public string Duration { get => duration; set => Set(ref duration, value); }
    public NativeSequenceStep ToStep()
    {
        var v = Numbers.Parse(Voltage, "Voltage");
        var i = Numbers.Parse(Current, "Current");
        var d = Numbers.Parse(Duration, "Duration");
        if (d != Math.Truncate(d) || d < 1 || d > 300) throw new ArgumentException("Native sequence duration must be a whole number from 1 to 300 seconds.");
        return new(v, i, (int)d);
    }
}

public sealed class SequenceViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    private NativeSequenceStepViewModel? selected;
    private int channel = 1;
    private string cycles = "1", startGroup = "0";
    private bool infinite;
    private NativeSequenceEndState endState = NativeSequenceEndState.Off;
    private string status = "Build steps, upload them to the GPP, then explicitly enable the native sequence.";

    public ObservableCollection<NativeSequenceStepViewModel> Steps { get; } = [];
    public NativeSequenceStepViewModel? Selected { get => selected; set { Set(ref selected, value); Refresh(); } }
    public int[] Channels { get; } = [1, 2];
    public int Channel { get => channel; set => Set(ref channel, value); }
    public string Cycles { get => cycles; set => Set(ref cycles, value); }
    public string StartGroup { get => startGroup; set => Set(ref startGroup, value); }
    public bool Infinite { get => infinite; set => Set(ref infinite, value); }
    public NativeSequenceEndState EndState { get => endState; set => Set(ref endState, value); }
    public NativeSequenceEndState[] EndStates { get; } = Enum.GetValues<NativeSequenceEndState>();
    public string Status { get => status; private set => Set(ref status, value); }
    public bool CanEdit => owner.CanControl;

    public RelayCommand AddCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public AsyncCommand UploadCommand { get; }
    public AsyncCommand EnableCommand { get; }
    public AsyncCommand DisableCommand { get; }
    public AsyncCommand RestartCommand { get; }

    public SequenceViewModel(MainViewModel owner)
    {
        this.owner = owner;
        Steps.CollectionChanged += (_, _) => Renumber();
        AddCommand = new(() => Steps.Add(new NativeSequenceStepViewModel()), () => CanEdit);
        DeleteCommand = new(() => { if (Selected != null) Steps.Remove(Selected); }, () => CanEdit && Selected != null);
        DuplicateCommand = new(() =>
        {
            if (Selected == null) return;
            var copy = new NativeSequenceStepViewModel { Voltage = Selected.Voltage, Current = Selected.Current, Duration = Selected.Duration };
            Steps.Insert(Steps.IndexOf(Selected) + 1, copy);
        }, () => CanEdit && Selected != null);
        UploadCommand = new(UploadAsync, owner.ReportError, () => CanEdit && Steps.Count > 0);
        EnableCommand = new(() => owner.OperateAsync(async ct =>
        {
            if (await owner.Device.GetOutputStateAsync(Channel, ct)) throw new InvalidOperationException($"Turn CH{Channel} output OFF before enabling native sequence mode. Enable the output explicitly afterwards when ready.");
            await owner.Device.SetNativeSequenceStateAsync(Channel, true, ct);
        }), owner.ReportError, () => CanEdit && Steps.Count > 0);
        DisableCommand = new(() => owner.OperateAsync(ct => owner.Device.SetNativeSequenceStateAsync(Channel, false, ct)), owner.ReportError, () => owner.Connected && !owner.IsBusy);
        RestartCommand = new(() => owner.OperateAsync(ct => owner.Device.RestartNativeSequenceAsync(Channel, ct)), owner.ReportError, () => CanEdit && Steps.Count > 0);
        Steps.Add(new NativeSequenceStepViewModel());
    }

    private void Renumber() { for (var i = 0; i < Steps.Count; i++) Steps[i].Number = i + 1; Refresh(); }
    private int Whole(string text, string name, int min, int max)
    {
        var value = Numbers.Parse(text, name);
        if (value != Math.Truncate(value) || value < min || value > max) throw new ArgumentException($"{name} must be a whole number {min}–{max}.");
        return (int)value;
    }
    private async Task UploadAsync()
    {
        var profile = new NativeSequenceProfile
        {
            Channel = Channel,
            Cycles = Whole(Cycles, "Cycles", 1, 9999),
            StartGroup = Whole(StartGroup, "Start group", 0, 2047),
            Infinite = Infinite,
            EndState = EndState,
            Steps = Steps.Select(s => s.ToStep()).ToList()
        };
        foreach (var step in profile.Steps) owner.ActiveProfile.Validate(Channel, step.Voltage, step.Current);
        await owner.OperateAsync(async ct =>
        {
            if (await owner.Device.GetOutputStateAsync(Channel, ct))
                throw new InvalidOperationException($"Turn CH{Channel} output OFF before uploading a native sequence.");
            await owner.Device.UploadNativeSequenceAsync(profile, ct);
        });
        Status = $"Uploaded {profile.Steps.Count} steps to CH{Channel}. ENABLE SEQUENCE does not automatically turn the channel output ON.";
        owner.ShowInfo(Status);
    }
    public void Refresh()
    {
        OnChanged(nameof(CanEdit)); AddCommand.Refresh(); DeleteCommand.Refresh(); DuplicateCommand.Refresh(); UploadCommand.Refresh(); EnableCommand.Refresh(); DisableCommand.Refresh(); RestartCommand.Refresh();
    }
}
