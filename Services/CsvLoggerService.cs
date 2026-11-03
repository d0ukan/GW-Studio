using System.Globalization;
using System.IO;
using System.Threading.Channels;
using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.Services;
public sealed class CsvLoggerService
{
    private Channel<MeasurementPoint>? queue;
    private Task? writer;
    public long Rows { get; private set; }
    public bool IsRecording => queue != null;
    public event Action<Exception>? Faulted;
    public async Task StartAsync(string path)
    {
        if (IsRecording) throw new InvalidOperationException("Logger is already recording.");
        var stream = new StreamWriter(new FileStream(Path.GetFullPath(path), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, true));
        try { await stream.WriteLineAsync("Timestamp,Channel,Voltage_V,Current_A,Power_W,SetVoltage_V,CurrentLimit_A,OutputState,CV_CC"); await stream.FlushAsync(); }
        catch { await stream.DisposeAsync(); throw; }
        Rows = 0;
        var channel = Channel.CreateBounded<MeasurementPoint>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        queue = channel;
        writer = Task.Run(async () =>
        {
            try
            {
                await foreach (var p in channel.Reader.ReadAllAsync())
                {
                    await stream.WriteLineAsync(FormattableString.Invariant($"{p.Timestamp:O},{p.Channel},{p.Voltage:F5},{p.Current:F5},{p.Power:F5},{p.SetVoltage:F3},{p.CurrentLimit:F4},{(p.OutputOn ? "ON" : "OFF")},Unknown"));
                    Rows++; if (Rows % 10 == 0) await stream.FlushAsync();
                }
            }
            catch (Exception ex) { queue = null; channel.Writer.TryComplete(); Faulted?.Invoke(ex); }
            finally { await stream.DisposeAsync(); }
        });
    }
    public void Add(MeasurementPoint point)
    {
        var q = queue; if (q == null) return;
        if (!q.Writer.TryWrite(point)) { queue = null; q.Writer.TryComplete(); Faulted?.Invoke(new IOException("Logger buffer is full; recording stopped. Existing queued rows are being saved.")); }
    }
    public async Task StopAsync() { var q = queue; queue = null; q?.Writer.TryComplete(); if (writer != null) await writer; }
}
