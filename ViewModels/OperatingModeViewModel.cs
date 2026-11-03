using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.ViewModels;

public sealed class OperatingModeViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    private SourceConfiguration configuration = SourceConfiguration.Independent;
    private string ch1Mode = "Source", ch2Mode = "Source";
    private string ch1LoadSetpoint = "1.0000", ch2LoadSetpoint = "1.0000";
    private string status = "Mode state not read yet.";

    public SourceConfiguration Configuration { get => configuration; set => Set(ref configuration, value); }
    public SourceConfiguration[] Configurations { get; } = Enum.GetValues<SourceConfiguration>();
    public string[] ChannelModes { get; } = ["Source", "Load CV", "Load CC", "Load CR"];
    public string Ch1Mode { get => ch1Mode; set { Set(ref ch1Mode, value); OnChanged(nameof(Ch1LoadUnit)); } }
    public string Ch2Mode { get => ch2Mode; set { Set(ref ch2Mode, value); OnChanged(nameof(Ch2LoadUnit)); } }
    public string Ch1LoadSetpoint { get => ch1LoadSetpoint; set => Set(ref ch1LoadSetpoint, value); }
    public string Ch2LoadSetpoint { get => ch2LoadSetpoint; set => Set(ref ch2LoadSetpoint, value); }
    public string Ch1LoadUnit => Unit(Ch1Mode);
    public string Ch2LoadUnit => Unit(Ch2Mode);
    public string Status { get => status; private set => Set(ref status, value); }
    public bool CanApply => owner.CanControl;

    public AsyncCommand ApplyConfigurationCommand { get; }
    public AsyncCommand ApplyCh1ModeCommand { get; }
    public AsyncCommand ApplyCh2ModeCommand { get; }
    public AsyncCommand RefreshCommand { get; }

    public OperatingModeViewModel(MainViewModel owner)
    {
        this.owner = owner;
        ApplyConfigurationCommand = new(() => ApplyConfigurationAsync(), owner.ReportError, () => CanApply);
        ApplyCh1ModeCommand = new(() => ApplyChannelModeAsync(1), owner.ReportError, () => CanApply);
        ApplyCh2ModeCommand = new(() => ApplyChannelModeAsync(2), owner.ReportError, () => CanApply && owner.ActiveMonitorCh2);
        RefreshCommand = new(RefreshFromDeviceAsync, owner.ReportError, () => owner.Connected && !owner.IsBusy && !owner.IsRunning);
    }

    private static string Unit(string mode) => mode switch { "Load CV" => "V", "Load CC" => "A", "Load CR" => "Ω", _ => "—" };
    private static LoadRegulation? ParseLoad(string mode) => mode switch { "Load CV" => LoadRegulation.CV, "Load CC" => LoadRegulation.CC, "Load CR" => LoadRegulation.CR, _ => null };

    private async Task ApplyConfigurationAsync()
    {
        await owner.OperateAsync(async ct =>
        {
            var ch1On = await owner.Device.GetOutputStateAsync(1, ct);
            var ch2On = await owner.Device.GetOutputStateAsync(2, ct);
            if (ch1On || ch2On) throw new InvalidOperationException("Turn CH1 and CH2 outputs OFF before changing Independent / Series / Parallel configuration.");
            await owner.Device.SetLoadModeAsync(1, null, ct);
            await owner.Device.SetLoadModeAsync(2, null, ct);
            await owner.Device.SetSourceConfigurationAsync(Configuration, ct);
        });
        await RefreshFromDeviceAsync();
        owner.ShowInfo($"Operating configuration changed to {Configuration}. Outputs were not enabled.");
    }

    private async Task ApplyChannelModeAsync(int ch)
    {
        var modeText = ch == 1 ? Ch1Mode : Ch2Mode;
        var load = ParseLoad(modeText);
        await owner.OperateAsync(async ct =>
        {
            if (Configuration != SourceConfiguration.Independent && load != null)
                throw new InvalidOperationException("Load mode is only available in Independent configuration. Switch to Independent first.");
            if (await owner.Device.GetOutputStateAsync(ch, ct))
                throw new InvalidOperationException($"Turn CH{ch} output OFF before changing Source / Load mode.");
            if (load != null)
            {
                var text = ch == 1 ? Ch1LoadSetpoint : Ch2LoadSetpoint;
                var value = Numbers.Parse(text, $"CH{ch} load setpoint");
                await owner.Device.SetLoadSetpointAsync(ch, load.Value, value, ct);
            }
            await owner.Device.SetLoadModeAsync(ch, load, ct);
        });
        await RefreshFromDeviceAsync();
        owner.ShowInfo($"CH{ch} mode changed to {modeText}. Output remains OFF until explicitly enabled.");
    }

    public async Task RefreshFromDeviceAsync()
    {
        if (!owner.Connected) return;
        Configuration = await owner.Device.GetSourceConfigurationAsync();
        Ch1Mode = NormalizeMode(await owner.Device.GetModeTextAsync(1));
        if (owner.ActiveMonitorCh2) Ch2Mode = NormalizeMode(await owner.Device.GetModeTextAsync(2));
        Status = $"Device mode: {Configuration}  •  CH1 {Ch1Mode}  •  CH2 {Ch2Mode}";
        foreach (var channel in owner.Channels) channel.Refresh();
        Refresh();
    }

    private static string NormalizeMode(string text)
    {
        var t = text.Trim().ToUpperInvariant();
        if (t.Contains("CV")) return "Load CV";
        if (t.Contains("CC")) return "Load CC";
        if (t.Contains("CR")) return "Load CR";
        return "Source";
    }

    public void Refresh()
    {
        OnChanged(nameof(CanApply));
        ApplyConfigurationCommand.Refresh(); ApplyCh1ModeCommand.Refresh(); ApplyCh2ModeCommand.Refresh(); RefreshCommand.Refresh();
    }
}
