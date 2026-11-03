using System.Diagnostics;
using Dou_GW_Studio.Models;

namespace Dou_GW_Studio.Services;

public sealed class DevicePollingService(GppDeviceService device)
{
    public int IntervalMs { get; set; } = 500;
    public Func<int, bool> ShouldMonitor { get; set; } = _ => true;
    public event Action<GppChannelState>? Sample;
    public event Action<PollingMetrics>? Metrics;
    public event Action<Exception>? Faulted;

    private CancellationTokenSource? cancellation;
    private Task? loop;

    public void Start()
    {
        if (loop is { IsCompleted: false }) return;
        cancellation?.Dispose();
        cancellation = new();
        loop = Task.Run(() => Run(cancellation.Token));
    }

    private async Task Run(CancellationToken ct)
    {
        try
        {
            int cycle = 0;
            long previousSweepStart = 0;

            while (!ct.IsCancellationRequested)
            {
                var sweepStart = Stopwatch.GetTimestamp();
                var effectivePeriodMs = previousSweepStart == 0
                    ? 0
                    : Stopwatch.GetElapsedTime(previousSweepStart, sweepStart).TotalMilliseconds;
                previousSweepStart = sweepStart;

                var channels = device.RequireProfile().Channels.Select(c => c.Channel).Where(ShouldMonitor).ToArray();
                foreach (var ch in channels)
                {
                    // Stop only between complete transactions so serial response boundaries remain intact.
                    if (ct.IsCancellationRequested) return;
                    var state = await device.ReadChannelAsync(ch, cycle % 10 == 0, polling: true).ConfigureAwait(false);
                    Sample?.Invoke(state);
                }

                var sweepDurationMs = Stopwatch.GetElapsedTime(sweepStart).TotalMilliseconds;
                var rateHz = effectivePeriodMs > 0 ? 1000.0 / effectivePeriodMs : 0;
                Metrics?.Invoke(new PollingMetrics(
                    Math.Clamp(IntervalMs, 1, 10000),
                    sweepDurationMs,
                    effectivePeriodMs,
                    rateHz,
                    channels.Length));

                cycle++;
                // IntervalMs is a post-sweep delay, not a guaranteed sample period.
                await Task.Delay(Math.Clamp(IntervalMs, 1, 10000), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Faulted?.Invoke(ex); }
    }

    public async Task StopAsync()
    {
        cancellation?.Cancel();
        if (loop != null) await loop.ConfigureAwait(false);
        loop = null;
    }
}
