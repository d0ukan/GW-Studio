using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.Services;
/// <summary>One complete request/response per gate lease. Blocking driver work stays on a worker thread.</summary>
public sealed class SerialDeviceService : ISerialTransport
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private SerialPort? port;
    private long lastResponseUtcTicks;
    private int timeoutCount, errorCount, successfulResponses;
    public bool IsConnected => port?.IsOpen == true;
    public int TimeoutMs { get; set; } = 1500;
    public int TimeoutCount => Volatile.Read(ref timeoutCount);
    public int ErrorCount => Volatile.Read(ref errorCount);
    public int SuccessfulResponses => Volatile.Read(ref successfulResponses);
    public DateTimeOffset? LastResponse
    {
        get
        {
            var ticks = Interlocked.Read(ref lastResponseUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }
    public event Action<TrafficEntry>? Traffic;
    public static string[] GetPorts() => SerialPort.GetPortNames().OrderBy(s => s.Length).ThenBy(s => s).ToArray();
    public async Task ConnectAsync(string portName, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); ClosePort();
                var next = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                { Handshake = Handshake.None, Encoding = Encoding.ASCII, NewLine = "\n", ReadTimeout = 50,
                  WriteTimeout = TimeoutMs, DtrEnable = false, RtsEnable = false };
                try { next.Open(); next.DiscardInBuffer(); token.ThrowIfCancellationRequested(); port = next; }
                catch (UnauthorizedAccessException ex)
                {
                    next.Dispose();
                    var message = $"{portName} portuna erişim reddedildi. Port başka bir uygulamada açık olabilir veya Windows/seri port sürücüsü erişime izin vermiyor olabilir. Hercules, seri terminal ve uygulamanın diğer kopyalarında bağlantıyı kapatın. Ardından doğru COM portunu seçip yeniden bağlanın.";
                    Traffic?.Invoke(new(DateTimeOffset.Now, "ERR", message, false));
                    throw new IOException(message, ex);
                }
                catch { next.Dispose(); throw; }
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task DisconnectAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { await Task.Run(ClosePort).ConfigureAwait(false); } finally { gate.Release(); }
    }
    private void ClosePort() { var old = port; port = null; if (old != null) { try { old.Close(); } finally { old.Dispose(); } } }
    public static string NormalizeCommand(string command)
    {
        var s = command.Trim();
        if (s.Length == 0 || s.Length > 512 || s.Any(c => c < 32 || c > 126) || s.Contains(';'))
            throw new ArgumentException("Enter one ASCII command, without line endings or command separators.");
        return s;
    }
    public Task WriteAsync(string command, CancellationToken token = default, bool polling = false) => TransactionAsync(command, false, token, polling);
    public Task<string> QueryAsync(string command, CancellationToken token = default, bool polling = false) => TransactionAsync(command, true, token, polling);
    private async Task<string> TransactionAsync(string command, bool query, CancellationToken token, bool polling)
    {
        var cmd = NormalizeCommand(command);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var active = port;
                if (active?.IsOpen != true) throw new IOException("Device is disconnected. Reconnect the selected COM port.");
                bool sent = false;
                try
                {
                    active.WriteTimeout = Math.Clamp(TimeoutMs, 100, 10000);
                    active.Write(cmd + "\n"); sent = true;
                    Traffic?.Invoke(new(DateTimeOffset.Now, "TX", cmd, polling));
                    if (!query) return "";
                    var reply = new StringBuilder(); var clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < TimeoutMs)
                    {
                        token.ThrowIfCancellationRequested();
                        int c;
                        try { c = active.ReadChar(); } catch (TimeoutException) { continue; }
                        if (c == '\n')
                        {
                            var line = reply.ToString().Trim();
                            if (line.Length == 0) { reply.Clear(); continue; }
                            Interlocked.Exchange(ref lastResponseUtcTicks, DateTimeOffset.UtcNow.Ticks);
                            Interlocked.Increment(ref successfulResponses);
                            Traffic?.Invoke(new(DateTimeOffset.Now, "RX", line, polling)); return line;
                        }
                        if (c >= 0) reply.Append((char)c);
                        if (reply.Length > 4096) throw new IOException("Device response exceeded 4096 characters.");
                    }
                    throw new TimeoutException("Device did not respond before the serial timeout. Connection closed to avoid using a late response.");
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
                {
                    if (ex is TimeoutException) Interlocked.Increment(ref timeoutCount);
                    if (ex is not OperationCanceledException) Interlocked.Increment(ref errorCount);
                    if (sent || ex is not OperationCanceledException) { try { ClosePort(); } catch { } }
                    Traffic?.Invoke(new(DateTimeOffset.Now, "ERR", ex.Message, polling)); throw;
                }
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync() { await DisconnectAsync().ConfigureAwait(false); gate.Dispose(); }
}
