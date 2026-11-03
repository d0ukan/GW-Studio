using System.Globalization;
using Dou_GW_Studio.Models;
using Dou_GW_Studio.Helpers;
namespace Dou_GW_Studio.Services;
public sealed class GppDeviceService(ISerialTransport transport, DeviceProfile? initialProfile = null)
{
    // Keeps multi-command settings/snapshots coherent with polling and the test engine.
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly Dictionary<int, (double V, double I)> settings = [];
    public ISerialTransport Transport => transport;
    public DeviceProfile? Profile { get; private set; } = initialProfile;
    public DeviceProfile RequireProfile() => Profile ?? throw new InvalidOperationException("Identify a supported instrument before sending channel commands.");
    private async Task<T> Exclusive<T>(Func<Task<T>> action, CancellationToken ct)
    { await operations.WaitAsync(ct).ConfigureAwait(false); try { ct.ThrowIfCancellationRequested(); return await action().ConfigureAwait(false); } finally { operations.Release(); } }
    private Task Exclusive(Func<Task> action, CancellationToken ct) => Exclusive(async () => { await action().ConfigureAwait(false); return true; }, ct);
    private Task W(string cmd, CancellationToken ct) => transport.WriteAsync(cmd, ct);
    private Task<string> Q(string cmd, CancellationToken ct, bool poll = false)
    {
        // Higher-level cancellation is honored between complete replies, preserving the link for OFF.
        // An already-started read may take up to the configured serial timeout to drain.
        ct.ThrowIfCancellationRequested();
        return transport.QueryAsync(cmd, CancellationToken.None, poll);
    }
    public static double ParseNumber(string text)
    {
        var s = text.Trim().TrimEnd('V', 'A', 'W', 'v', 'a', 'w').Trim();
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new FormatException("Invalid numeric device response: " + text);
        return value;
    }
    public static bool ParseOutput(string s) => s.Trim().ToUpperInvariant() switch
    { "ON" or "1" => true, "OFF" or "0" => false, _ => throw new FormatException("Unknown output state: " + s) };
    public Task<DeviceInfo> GetIdentityAsync(CancellationToken ct = default) => Exclusive(async () =>
    {
        Profile = null;
        settings.Clear();
        var info = DeviceInfo.Parse(await Q("*IDN?", ct));
        Profile = DeviceProfiles.Resolve(info.Model);
        return info;
    }, ct);
    private ChannelCapability Channel(int ch) => RequireProfile().Get(ch);
    private string VoltageQuery(int ch) => Channel(ch).UsesDiscreteVoltage ? $":SOURce{ch}:VOLTage?" : $"VSET{ch}?";
    private string VoltageCommand(int ch, double value) => Channel(ch).UsesDiscreteVoltage ? $":SOURce{ch}:VOLTage {Numbers.F(value)}" : $"VSET{ch}:{Numbers.F(value)}";
    public Task<double> GetVoltageSetAsync(int ch, CancellationToken ct = default) { Channel(ch); return Exclusive(async () => ParseNumber(await Q(VoltageQuery(ch), ct)), ct); }
    public Task<double> GetCurrentSetAsync(int ch, CancellationToken ct = default)
    { var capability = Channel(ch); return capability.CurrentProgrammable ? Exclusive(async () => ParseNumber(await Q($"ISET{ch}?", ct)), ct) : Task.FromResult(capability.MaxCurrent); }
    public Task<double> MeasureVoltageAsync(int ch, CancellationToken ct = default) { Channel(ch); return Exclusive(async () => ParseNumber(await Q($"VOUT{ch}?", ct)), ct); }
    public Task<double> MeasureCurrentAsync(int ch, CancellationToken ct = default) { Channel(ch); return Exclusive(async () => ParseNumber(await Q($"IOUT{ch}?", ct)), ct); }
    public Task<bool> GetOutputStateAsync(int ch, CancellationToken ct = default) { Channel(ch); return Exclusive(async () => ParseOutput(await Q($":OUTPut{ch}:STATe?", ct)), ct); }
    public Task SetVoltageAsync(int ch, double voltage, CancellationToken ct = default)
    { var c = Channel(ch); RequireProfile().Validate(ch, voltage, c.CurrentProgrammable ? 0 : c.MaxCurrent); return Exclusive(async () => { await W(VoltageCommand(ch, voltage), ct); settings.Remove(ch); }, ct); }
    public Task SetCurrentAsync(int ch, double current, CancellationToken ct = default)
    { var c = Channel(ch); if (!c.CurrentProgrammable) throw new NotSupportedException($"CH{ch} current is fixed on {RequireProfile().ModelName}."); RequireProfile().Validate(ch, 0, current); return Exclusive(async () => { await W($"ISET{ch}:{Numbers.F(current, 4)}", ct); settings.Remove(ch); }, ct); }
    public Task SetOutputStateAsync(int ch, bool on, CancellationToken ct = default)
    {
        Channel(ch);
        return Exclusive(() => W($":OUTPut{ch}:STATe {(on ? "ON" : "OFF")}", ct), ct);
    }
    public Task ApplySettingsAsync(int ch, double voltage, double current, CancellationToken ct = default)
    {
        var capability = Channel(ch);
        RequireProfile().Validate(ch, voltage, current);
        return Exclusive(async () =>
        {
            settings.Remove(ch);
            if (capability.CurrentProgrammable) await W($"ISET{ch}:{Numbers.F(current, 4)}", ct);
            await W(VoltageCommand(ch, voltage), ct);
            var v = ParseNumber(await Q(VoltageQuery(ch), ct));
            var i = capability.CurrentProgrammable ? ParseNumber(await Q($"ISET{ch}?", ct)) : capability.MaxCurrent;
            settings[ch] = (v, i);
            if (Math.Abs(v - voltage) > .0011 || Math.Abs(i - current) > .00011)
                throw new InvalidOperationException($"CH{ch}: device readback differs from requested settings ({v} V / {i} A). Check the front panel mode and limits.");
        }, ct);
    }
    public Task<GppChannelState> ReadChannelAsync(int ch, bool refreshSetpoints, CancellationToken ct = default, bool polling = false)
    {
        Channel(ch);
        return Exclusive(async () =>
        {
            var capability = Channel(ch);
            if (refreshSetpoints || !settings.ContainsKey(ch))
                settings[ch] = (ParseNumber(await Q(VoltageQuery(ch), ct, polling)), capability.CurrentProgrammable ? ParseNumber(await Q($"ISET{ch}?", ct, polling)) : capability.MaxCurrent);
            var v = ParseNumber(await Q($"VOUT{ch}?", ct, polling)); var i = ParseNumber(await Q($"IOUT{ch}?", ct, polling));
            var on = ParseOutput(await Q($":OUTPut{ch}:STATe?", ct, polling)); var set = settings[ch];
            return new GppChannelState(ch, set.V, set.I, v, i, on);
        }, ct);
    }
    // Test transitions omit readback; applied setpoints are queried by subsequent telemetry.
    public Task ApplyStepAsync(CustomTestStep s, CancellationToken ct)
    {
        var capability = Channel(s.Channel);
        RequireProfile().Validate(s.Channel, s.Voltage, s.CurrentLimit);
        return Exclusive(async () =>
        {
            settings.Remove(s.Channel);
            if (!s.OutputOn) await W($":OUTPut{s.Channel}:STATe OFF", ct);
            if (capability.CurrentProgrammable) await W($"ISET{s.Channel}:{Numbers.F(s.CurrentLimit, 4)}", ct);
            await W(VoltageCommand(s.Channel, s.Voltage), ct);
            if (s.OutputOn) await W($":OUTPut{s.Channel}:STATe ON", ct);
        }, ct);
    }
    public Task SetAllOutputsOnAsync(CancellationToken ct = default) => Exclusive(async () =>
    {
        var errors = new List<string>();
        var channels = RequireProfile().Channels.Select(c => c.Channel).ToArray();
        foreach (var ch in channels)
        {
            try { await W($":OUTPut{ch}:STATe ON", ct); }
            catch (Exception ex) { errors.Add($"CH{ch}: {ex.Message}"); }
        }
        foreach (var ch in channels)
        {
            try { if (!ParseOutput(await Q($":OUTPut{ch}:STATe?", ct))) errors.Add($"CH{ch} still reports OFF"); }
            catch (Exception ex) { errors.Add($"CH{ch} ON unconfirmed: {ex.Message}"); }
        }
        if (errors.Count > 0) throw new InvalidOperationException("ON could not be confirmed. Check the instrument. " + string.Join(" | ", errors));
    }, ct);

    public Task SetAllOutputsOffAsync(CancellationToken ct = default) => SetOutputsOffAsync(RequireProfile().Channels.Select(c => c.Channel), ct);
    public Task SetOutputsOffAsync(IEnumerable<int> channels, CancellationToken ct = default) => Exclusive(async () =>
    {
        var selected = channels.Distinct().ToArray(); var errors = new List<string>();
        // Send all OFF commands before verification; do not let one failed channel hide others.
        foreach (var ch in selected) { Channel(ch); try { await W($":OUTPut{ch}:STATe OFF", ct); } catch (Exception ex) { errors.Add($"CH{ch}: {ex.Message}"); } }
        foreach (var ch in selected) { try { if (ParseOutput(await Q($":OUTPut{ch}:STATe?", ct))) errors.Add($"CH{ch} still reports ON"); } catch (Exception ex) { errors.Add($"CH{ch} OFF unconfirmed: {ex.Message}"); } }
        if (errors.Count > 0) throw new InvalidOperationException("OFF could not be confirmed. Check the instrument. " + string.Join(" | ", errors));
    }, ct);
    public Task<string> TerminalAsync(string command, CancellationToken ct = default) => Exclusive(async () =>
    {
        var cmd = SerialDeviceService.NormalizeCommand(command); settings.Clear();
        if (cmd.Contains('?')) return await Q(cmd, ct);
        await W(cmd, ct); return "Sent (no response requested).";
    }, ct);


    public Task<SourceConfiguration> GetSourceConfigurationAsync(CancellationToken ct = default) => Exclusive(async () =>
    {
        var mode = (await Q(":MODE1?", ct)).Trim().ToUpperInvariant();
        return mode switch
        {
            "SERIES" => SourceConfiguration.TrackingSeries,
            "PARALLEL" => SourceConfiguration.TrackingParallel,
            _ => SourceConfiguration.Independent
        };
    }, ct);

    public Task SetSourceConfigurationAsync(SourceConfiguration configuration, CancellationToken ct = default) => Exclusive(async () =>
    {
        var value = configuration switch { SourceConfiguration.TrackingSeries => 1, SourceConfiguration.TrackingParallel => 2, _ => 0 };
        await W($"TRACK{value}", ct);
        settings.Clear();
    }, ct);

    public Task<string> GetModeTextAsync(int ch, CancellationToken ct = default)
    { if (!RequireProfile().LoadChannels.Contains(ch)) throw new ArgumentOutOfRangeException(nameof(ch)); return Exclusive(() => Q($":MODE{ch}?", ct), ct); }

    public Task SetLoadModeAsync(int ch, LoadRegulation? mode, CancellationToken ct = default) => Exclusive(async () =>
    {
        if (!RequireProfile().LoadChannels.Contains(ch)) throw new ArgumentOutOfRangeException(nameof(ch));
        var current = (await Q($":MODE{ch}?", ct)).Trim().ToUpperInvariant();
        if (current.Contains("CV")) await W($":LOAD{ch}:CV OFF", ct);
        else if (current.Contains("CC")) await W($":LOAD{ch}:CC OFF", ct);
        else if (current.Contains("CR")) await W($":LOAD{ch}:CR OFF", ct);
        if (mode != null) await W($":LOAD{ch}:{mode.Value} ON", ct);
        settings.Remove(ch);
    }, ct);

    public Task SetLoadSetpointAsync(int ch, LoadRegulation mode, double value, CancellationToken ct = default) => Exclusive(async () =>
    {
        if (!RequireProfile().LoadChannels.Contains(ch)) throw new ArgumentOutOfRangeException(nameof(ch));
        if (!double.IsFinite(value)) throw new ArgumentException("Load setpoint must be a finite number.");
        switch (mode)
        {
            case LoadRegulation.CV:
                if (value < 0 || value > 32) throw new ArgumentException("Load CV setpoint must be 0–32 V.");
                await W($":SOURce{ch}:VOLTage {Numbers.F(value)}", ct); break;
            case LoadRegulation.CC:
                var maximum = RequireProfile().Get(ch).MaxCurrent;
                if (value < 0 || value > maximum) throw new ArgumentException($"Load CC setpoint must be 0–{maximum:0.####} A.");
                await W($":SOURce{ch}:CURRent {Numbers.F(value, 4)}", ct); break;
            case LoadRegulation.CR:
                if (value < 1 || value > 1000) throw new ArgumentException("Load CR setpoint must be 1–1000 Ω.");
                await W($":LOAD{ch}:RESistor {Numbers.F(value)}", ct); break;
        }
        settings.Remove(ch);
    }, ct);

    public Task UploadNativeSequenceAsync(NativeSequenceProfile profile, CancellationToken ct = default) => Exclusive(async () =>
    {
        if (!RequireProfile().NativeSequenceChannels.Contains(profile.Channel)) throw new ArgumentException("Native sequence supports CH1 or CH2.");
        if (profile.Steps.Count is < 1 or > 2048) throw new ArgumentException("Native sequence must contain 1–2048 steps.");
        if (profile.StartGroup < 0 || profile.StartGroup > 2047) throw new ArgumentException("Sequence start group must be 0–2047.");
        if (!profile.Infinite && (profile.Cycles < 1 || profile.Cycles > 9999)) throw new ArgumentException("Sequence cycles must be 1–9999, or Infinite.");
        foreach (var step in profile.Steps)
        {
            RequireProfile().Validate(profile.Channel, step.Voltage, step.Current);
            if (step.DurationSeconds is < 1 or > 300) throw new ArgumentException("Each native sequence step must be 1–300 seconds.");
        }
        var ch = profile.Channel;
        await W($":SEQUence{ch}:STATe OFF", ct);
        await W($":SEQUence{ch}:CYCLEs {(profile.Infinite ? "I" : $"N,{profile.Cycles}")}", ct);
        await W($":SEQUence{ch}:ENDState {(profile.EndState == NativeSequenceEndState.Last ? "LAST" : "OFF")}", ct);
        await W($":SEQUence{ch}:STARt {profile.StartGroup}", ct);
        await W($":SEQUence{ch}:GROUPs {profile.Steps.Count}", ct);
        for (var i = 0; i < profile.Steps.Count; i++)
        {
            var step = profile.Steps[i];
            await W($":SEQUence{ch}:PARAmeter {i},{Numbers.F(step.Voltage)},{Numbers.F(step.Current, 4)},{step.DurationSeconds}", ct);
        }
    }, ct);

    public Task SetNativeSequenceStateAsync(int ch, bool enabled, CancellationToken ct = default)
    {
        if (!RequireProfile().NativeSequenceChannels.Contains(ch)) throw new ArgumentOutOfRangeException(nameof(ch));
        return Exclusive(() => W($":SEQUence{ch}:STATe {(enabled ? "ON" : "OFF")}", ct), ct);
    }

    public Task RestartNativeSequenceAsync(int ch, CancellationToken ct = default)
    {
        if (!RequireProfile().NativeSequenceChannels.Contains(ch)) throw new ArgumentOutOfRangeException(nameof(ch));
        return Exclusive(() => W($":SEQUence{ch}:RESTart", ct), ct);
    }



}
