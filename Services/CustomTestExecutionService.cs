using System.Diagnostics;
using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.Services;
public sealed class CustomTestExecutionService(GppDeviceService device)
{
    public event Action<TestProgress>? Progress;
    public async Task RunAsync(CustomTestProfile profile, CancellationToken ct)
    {
        profile.Validate(device.RequireProfile()); var steps = profile.Steps.ToArray(); var clock = Stopwatch.StartNew();
        long remaining = steps.Sum(s => (long)s.DurationMs);
        CustomTestStep? previous = null;
        try
        {
            for (int i = 0; i < steps.Length; i++)
            {
                ct.ThrowIfCancellationRequested(); var s = steps[i];
                Progress?.Invoke(new(i + 1, steps.Length, clock.Elapsed, TimeSpan.FromMilliseconds(remaining), s, "Applying"));
                if (previous is not null && previous.Channel == s.Channel &&
                    Math.Abs(previous.Voltage - s.Voltage) < 0.0000001 &&
                    Math.Abs(previous.CurrentLimit - s.CurrentLimit) < 0.0000001 &&
                    previous.OutputOn == s.OutputOn)
                {
                    // Dwell-only step (for repeat delays / adjacent holds). Keep the instrument
                    // state exactly as-is and avoid a redundant SCPI round trip.
                }
                else if (previous is not null && previous.Channel == s.Channel &&
                    Math.Abs(previous.Voltage - s.Voltage) < 0.0000001 &&
                    Math.Abs(previous.CurrentLimit - s.CurrentLimit) < 0.0000001 &&
                    previous.OutputOn != s.OutputOn)
                {
                    // Power-interrupt transitions should only toggle the output. Avoid adding
                    // redundant ISET/VSET serial transactions to the requested OFF interval.
                    await device.SetOutputStateAsync(s.Channel, s.OutputOn, ct).ConfigureAwait(false);
                }
                else if (previous is not null && previous.Channel == s.Channel && previous.OutputOn && s.OutputOn && Math.Abs(previous.CurrentLimit - s.CurrentLimit) < 0.0000001)
                    await device.SetVoltageAsync(s.Channel, s.Voltage, ct).ConfigureAwait(false);
                else
                    await device.ApplyStepAsync(s, ct).ConfigureAwait(false);
                previous = s;
                var dwell = Stopwatch.StartNew();
                do
                {
                    ct.ThrowIfCancellationRequested();
                    var left = Math.Max(0, s.DurationMs - dwell.ElapsedMilliseconds);
                    Progress?.Invoke(new(i + 1, steps.Length, clock.Elapsed, TimeSpan.FromMilliseconds(Math.Max(0, remaining - dwell.ElapsedMilliseconds)), s, "Running"));
                    if (left == 0) break;
                    await Task.Delay((int)Math.Min(100, left), ct).ConfigureAwait(false);
                } while (true);
                remaining -= s.DurationMs;
            }
            Progress?.Invoke(new(steps.Length, steps.Length, clock.Elapsed, TimeSpan.Zero, steps[^1], "Completed — final step state retained"));
        }
        catch (Exception cause)
        {
            try { await device.SetOutputsOffAsync(steps.Select(s => s.Channel)).ConfigureAwait(false); }
            catch (Exception off) { throw new InvalidOperationException("Test stopped; output OFF is unconfirmed. Check the instrument. " + off.Message, cause); }
            Progress?.Invoke(new(0, steps.Length, clock.Elapsed, TimeSpan.Zero, null, "Stopped — involved outputs OFF confirmed"));
            throw;
        }
    }
}
