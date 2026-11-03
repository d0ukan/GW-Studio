using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;
using System.Globalization;
using Dou_GW_Studio.Models;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Services;

int passed = 0;
async Task Test(string name, Func<Task> test) { await test(); passed++; Console.WriteLine("PASS " + name); }
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Task Sync(Action action) { action(); return Task.CompletedTask; }

await Test("Turkish input -> invariant device command", async () =>
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
    var transport = new FakeTransport(); var service = new GppDeviceService(transport, DeviceProfiles.Gpp4323);
    await service.ApplySettingsAsync(1, Numbers.Parse("28,5", "Voltage"), Numbers.Parse("1,25", "Current"));
    Check(transport.Commands.Contains("VSET1:28.500") && transport.Commands.Contains("ISET1:1.2500"));
});
await Test("Reject ambiguous separators / NaN / Infinity", async () =>
{
    foreach (var input in new[] { "1,234.5", "NaN", "Infinity", "1e3", "", "--5", "1,2,3" }) await Throws<ArgumentException>(() => Sync(() => Numbers.Parse(input, "Value")));
});
await Test("Unit suffix and output-state parsing", () => Sync(() =>
{
    Check(GppDeviceService.ParseNumber("4.996V") == 4.996); Check(GppDeviceService.ParseNumber("0.0000A\r") == 0);
    Check(GppDeviceService.ParseOutput("ON") && !GppDeviceService.ParseOutput("0"));
}));
await Test("Malformed responses rejected", async () =>
{
    await Throws<FormatException>(() => Sync(() => GppDeviceService.ParseOutput("UNKNOWN")));
    await Throws<FormatException>(() => Sync(() => GppDeviceService.ParseNumber("NaNV")));
});
await Test("Identity requires exact model and manufacturer", async () =>
{
    Check(DeviceInfo.Parse("GW Instek,GPP-4323,SN:GEY854295,V1.07").SerialNumber == "SN:GEY854295");
    Check(DeviceInfo.Parse("GW Instek,GPP-3060,SN:TEST,V1.00").Model == "GPP-3060");
    foreach (var id in new[] { "GW Instek,GPP-6030,SN:x,V1", "Other,GPP-4323", "GW Instek,GPP-4323X", "" })
        await Throws<InvalidOperationException>(() => Sync(() => DeviceInfo.Parse(id)));
});
await Test("Auto-detected model changes channel count and limits", async () =>
{
    var t = new FakeTransport { IdentityModel = "GPP-3060" }; var d = new GppDeviceService(t);
    await d.GetIdentityAsync(); Check(d.RequireProfile().Channels.Count == 3);
    await d.ApplySettingsAsync(1, 32, 6); Check(t.Commands.Contains("ISET1:6.0000"));
    t.Commands.Clear(); await d.ApplySettingsAsync(3, 3.3, 5);
    Check(t.Commands.Contains(":SOURce3:VOLTage 3.300")); Check(!t.Commands.Any(c => c.StartsWith("ISET3")));
    await Throws<ArgumentOutOfRangeException>(() => d.GetOutputStateAsync(4));
    await Throws<ArgumentException>(() => d.ApplySettingsAsync(3, 4.0, 5));
    t.Commands.Clear(); await d.SetAllOutputsOnAsync();
    Check(t.Commands.Count(c => c.EndsWith(" ON")) == 3); Check(!t.Commands.Any(c => c.Contains("OUTPut4")));
});
await Test("GPP-4323 per-channel limits reject invalid values before writes", async () =>
{
    var t = new FakeTransport(); var d = new GppDeviceService(t, DeviceProfiles.Gpp4323);
    await Throws<ArgumentException>(() => d.ApplySettingsAsync(1, 5, 3.001)); Check(t.Commands.Count == 0);
    await d.ApplySettingsAsync(1, 32, 1); Check(t.Commands.Contains("VSET1:32.000")); t.Commands.Clear();
    await Throws<ArgumentException>(() => d.ApplySettingsAsync(1, 32.001, 1)); Check(t.Commands.Count == 0);
    await Throws<ArgumentException>(() => d.ApplySettingsAsync(2, double.NaN, 1));
    await d.ApplySettingsAsync(3, 5, 1); Check(t.Commands.Contains("VSET3:5.000")); t.Commands.Clear();
    await Throws<ArgumentException>(() => d.ApplySettingsAsync(3, 5.001, 1)); Check(t.Commands.Count == 0);
    await d.ApplySettingsAsync(4, 15, 1); Check(t.Commands.Contains("VSET4:15.000")); t.Commands.Clear();
    await Throws<ArgumentException>(() => d.ApplySettingsAsync(4, 15.001, 1)); Check(t.Commands.Count == 0);
});
await Test("Connect-style initialization is read-only; existing ON preserved", async () =>
{
    var t = new FakeTransport(); t.Output[1] = true; var d = new GppDeviceService(t, DeviceProfiles.Gpp4323);
    await t.ConnectAsync("TEST"); await d.GetIdentityAsync(); var state = await d.ReadChannelAsync(1, true); await t.DisconnectAsync();
    Check(state.OutputOn); Check(t.Output[1]); Check(t.Commands.All(s => s.Contains('?')));
});
await Test("Applying values never sends an output command", async () =>
{
    var t = new FakeTransport(); var d = new GppDeviceService(t, DeviceProfiles.Gpp4323); await d.ApplySettingsAsync(2, 12, 2);
    Check(!t.Commands.Any(c => c.StartsWith(":OUTPut"))); Check(!t.Output[2]);
});
await Test("Readback mismatch is reported", async () =>
{
    var t = new FakeTransport { IgnoreVoltageWrites = true }; var d = new GppDeviceService(t, DeviceProfiles.Gpp4323);
    await Throws<InvalidOperationException>(() => d.ApplySettingsAsync(1, 28, 1));
});
await Test("Polling and settings transactions cannot interleave", async () =>
{
    var t = new FakeTransport { DelayMs = 2 }; var d = new GppDeviceService(t, DeviceProfiles.Gpp4323);
    var read = d.ReadChannelAsync(1, true); var write = d.ApplySettingsAsync(2, 20, 2); await Task.WhenAll(read, write);
    Check(t.PeakConcurrent == 1); var commands = t.Commands.ToArray(); Check(Array.IndexOf(commands, "ISET2:2.0000") > Array.IndexOf(commands, ":OUTPut1:STATe?"));
});
await Test("ALL ON sends every ON before verification", async () =>
{
    var t = new FakeTransport(); var d = new GppDeviceService(t, DeviceProfiles.Gpp4323); await d.SetAllOutputsOnAsync();
    Check(t.Output.Values.All(v => v)); var c = t.Commands.ToArray(); Check(c.Take(4).All(s => s.EndsWith("ON"))); Check(c.Skip(4).All(s => s.EndsWith('?')));
});
await Test("ALL OFF sends every OFF before any verification", async () =>
{
    var t = new FakeTransport(); t.Output[1] = t.Output[2] = t.Output[3] = t.Output[4] = true; var d = new GppDeviceService(t, DeviceProfiles.Gpp4323); await d.SetAllOutputsOffAsync();
    Check(t.Output.Values.All(v => !v)); var c = t.Commands.ToArray(); Check(c.Take(4).All(s => s.EndsWith("OFF"))); Check(c.Skip(4).All(s => s.EndsWith('?')));
});
await Test("OFF verification failures are explicit and all channels attempted", async () =>
{
    var t = new FakeTransport { IgnoreOffChannel = 2 }; t.Output[2] = true; var d = new GppDeviceService(t, DeviceProfiles.Gpp4323);
    await Throws<InvalidOperationException>(() => d.SetAllOutputsOffAsync()); Check(t.Commands.Contains(":OUTPut4:STATe OFF"));
});
await Test("Completed profile retains requested final ON state", async () =>
{
    var t = new FakeTransport(); var engine = new CustomTestExecutionService(new(t, DeviceProfiles.Gpp4323));
    await engine.RunAsync(new() { TargetModel = "GPP-4323", Steps = [new(1, 5, 1, false, 0), new(1, 5, 1, true, 1)] }, default); Check(t.Output[1]);
});
await Test("STOP cancels remaining steps and confirms involved channel OFF", async () =>
{
    var t = new FakeTransport(); var engine = new CustomTestExecutionService(new(t, DeviceProfiles.Gpp4323)); var cts = new CancellationTokenSource();
    engine.Progress += p => { if (p.State == "Running") cts.Cancel(); };
    await Throws<OperationCanceledException>(() => engine.RunAsync(new() { TargetModel = "GPP-4323", Steps = [new(1, 5, 1, true, 5000), new(1, 20, 1, true, 5000)] }, cts.Token));
    Check(!t.Output[1]); Check(!t.Commands.Contains("VSET1:20.000")); Check(t.Commands.Last() == ":OUTPut1:STATe?");
});
await Test("Test STOP with failed OFF reports uncertainty", async () =>
{
    var t = new FakeTransport { IgnoreOffChannel = 1 }; var engine = new CustomTestExecutionService(new(t, DeviceProfiles.Gpp4323)); var cts = new CancellationTokenSource();
    engine.Progress += p => { if (p.State == "Running") cts.Cancel(); };
    await Throws<InvalidOperationException>(() => engine.RunAsync(new() { TargetModel = "GPP-4323", Steps = [new(1, 5, 1, true, 5000)] }, cts.Token));
});
await Test("Invalid entire profile rejected before its first write", async () =>
{
    var t = new FakeTransport(); var engine = new CustomTestExecutionService(new(t, DeviceProfiles.Gpp4323));
    await Throws<ArgumentException>(() => engine.RunAsync(new() { TargetModel = "GPP-4323", Steps = [new(1, 5, 1, true, 10), new(2, 5, 99, true, 1)] }, default)); Check(t.Commands.Count == 0);
});
await Test("Native backend cannot accidentally run as PC timed", async () =>
{
    var t = new FakeTransport(); var engine = new CustomTestExecutionService(new(t, DeviceProfiles.Gpp4323));
    await Throws<NotSupportedException>(() => engine.RunAsync(new() { TargetModel = "GPP-4323", Backend = ExecutionBackend.GppNativeSequence, Steps = [new(1, 5, 1, true, 10)] }, default)); Check(t.Commands.Count == 0);
});
await Test("Saved profile cannot run on the other detected model", async () =>
{
    var t = new FakeTransport(); var engine = new CustomTestExecutionService(new(t, DeviceProfiles.Gpp3060));
    await Throws<InvalidOperationException>(() => engine.RunAsync(new() { TargetModel = "GPP-4323", Steps = [new(1, 5, 1, false, 0)] }, default));
    Check(t.Commands.Count == 0);
});
await Test("OFF step sends OFF before updating setpoints", async () =>
{
    var t = new FakeTransport(); var d = new GppDeviceService(t, DeviceProfiles.Gpp4323); await d.ApplyStepAsync(new(1, 5, 1, false, 0), default); Check(t.Commands.First() == ":OUTPut1:STATe OFF");
});
await Test("Bounded measurement history discards oldest samples", () => Sync(() =>
{
    var h = new MeasurementHistoryService(); for (int i = 0; i < 25000; i++) h.Add(new(DateTimeOffset.Now, 1, i, 1, 1, 1, false));
    Check(h.Snapshot().Length == MeasurementHistoryService.Capacity); Check(h.Snapshot()[0].Voltage == 1000); h.Clear(); Check(h.Snapshot().Length == 0);
}));
await Test("CSV flushes rows, uses invariant decimals and never overwrites", async () =>
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"); var logger = new CsvLoggerService();
    try
    {
        await logger.StartAsync(path); logger.Add(new(DateTimeOffset.Now, 1, 28.5, 1.25, 28.5, 2, true)); await logger.StopAsync();
        var rows = await File.ReadAllLinesAsync(path); Check(rows.Length == 2); Check(rows[1].Contains(",28.50000,1.25000,"));
        await Throws<IOException>(() => logger.StartAsync(path)); Check((await File.ReadAllLinesAsync(path)).Length == 2);
    }
    finally { await logger.StopAsync(); File.Delete(path); }
});
await Test("Polling stop drains active transaction and sends no mutations", async () =>
{
    var t = new FakeTransport { DelayMs = 2 }; var poll = new DevicePollingService(new(t, DeviceProfiles.Gpp4323)) { IntervalMs = 100 }; var gotSample = new TaskCompletionSource();
    poll.Sample += _ => gotSample.TrySetResult(); poll.Start(); await gotSample.Task.WaitAsync(TimeSpan.FromSeconds(5)); await poll.StopAsync();
    Check(t.Commands.All(c => c.Contains('?'))); Check(t.PeakConcurrent == 1);
});
await Test("Serial normalization rejects multiple commands", async () =>
{
    Check(SerialDeviceService.NormalizeCommand(" *IDN? ") == "*IDN?");
    foreach (var command in new[] { "VSET1:5;:OUTPut1:STATe ON", "VSET1:5\n:OUTPut1:STATe ON", "ö", "" })
        await Throws<ArgumentException>(() => Sync(() => SerialDeviceService.NormalizeCommand(command)));
});

await Test("Update version comparison is numeric and accepts v prefix", () => Sync(() =>
{
    Check(AppUpdateService.CompareVersions("0.9.9", "0.10.0") < 0);
    Check(AppUpdateService.CompareVersions("v1.0.1", "1.0.0") > 0);
    Check(AppUpdateService.CompareVersions("1.1.0", "v1.1.0") == 0);
}));
await Test("Update API equal version does not report an update", async () =>
{
    var json = $"{{\"tag_name\":\"v{Dou_GW_Studio.Helpers.AppVersionInfo.DisplayVersion}\",\"assets\":[{{\"name\":\"GW-Studio-Setup-v1.exe\",\"browser_download_url\":\"https://example.test/GW-Studio-Setup.exe\",\"size\":1}}]}}";
    var service = new AppUpdateService(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") })));
    var info = await service.CheckLatestAsync();
    Check(!service.IsUpdateAvailable(info));
});
await Test("Update API newer version reports an update", async () =>
{
    var json = "{\"tag_name\":\"v99.0.0\",\"body\":\"notes\",\"assets\":[{\"name\":\"GW-Studio-Portable-v99.0.0.zip\",\"browser_download_url\":\"https://example.test/p.zip\"},{\"name\":\"GW-Studio-Setup-v99.0.0.exe\",\"browser_download_url\":\"https://example.test/GW-Studio-Setup-v99.0.0.exe\",\"size\":5}]}";
    var service = new AppUpdateService(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") })));
    var info = await service.CheckLatestAsync();
    Check(service.IsUpdateAvailable(info));
    Check(info.FileName == "GW-Studio-Setup-v99.0.0.exe" && info.DownloadUrl.EndsWith("GW-Studio-Setup-v99.0.0.exe") && info.Notes == "notes");
});
await Test("GitHub release without Setup asset is rejected", async () =>
{
    var json = "{\"tag_name\":\"v99.0.0\",\"assets\":[{\"name\":\"GW-Studio-Portable-v99.0.0.zip\",\"browser_download_url\":\"https://example.test/p.zip\"}]}";
    var service = new AppUpdateService(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") })));
    await Throws<InvalidDataException>(() => service.CheckLatestAsync());
});
await Test("Direct Setup exe download returns the exe without extraction", async () =>
{
    var service = new AppUpdateService(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) })));
    var path = await service.DownloadExtractAndFindSetupAsync(new() { Version = "99.0.0", DownloadUrl = "https://example.test/GW-Studio-Setup-v99.0.0.exe", FileName = "GW-Studio-Setup-v99.0.0.exe" });
    Check(File.Exists(path) && path.EndsWith(".exe") && new FileInfo(path).Length == 3);
});
await Test("Corrupt update ZIP fails without starting an installer", async () =>
{
    var service = new AppUpdateService(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not a zip")) })));
    await Throws<InvalidDataException>(() => service.DownloadExtractAndFindSetupAsync(new() { Version = "99.0.0", DownloadUrl = "https://example.test/update.zip", FileName = "update.zip" }));
});
await Test("Update ZIP without Setup exe is rejected", async () =>
{
    byte[] zipBytes;
    using (var ms = new MemoryStream())
    {
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("readme.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("no installer");
        }
        zipBytes = ms.ToArray();
    }
    var service = new AppUpdateService(new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) })));
    await Throws<FileNotFoundException>(() => service.DownloadExtractAndFindSetupAsync(new() { Version = "99.0.0", DownloadUrl = "https://example.test/update.zip", FileName = "update.zip" }));
});

if (args.Length == 1)
{
    await Test("REAL SERIAL PTY: LF, CRLF parsing and concurrent queries", async () =>
    {
        await using var serial = new SerialDeviceService { TimeoutMs = 300 };
        await serial.ConnectAsync(args[0]); Check((await serial.QueryAsync("*IDN?")).Contains("GPP-4323"));
        var a = serial.QueryAsync("VOUT1?"); var b = serial.QueryAsync("IOUT1?"); await Task.WhenAll(a, b);
        Check(a.Result == "4.996V" && b.Result == "0.1250A");
        await serial.WriteAsync("VSET1:5.000"); await serial.DisconnectAsync();
    });
    await Test("REAL SERIAL PTY: timeout closes port; late reply cannot be reused", async () =>
    {
        await using var serial = new SerialDeviceService { TimeoutMs = 100 }; await serial.ConnectAsync(args[0]);
        await Throws<TimeoutException>(() => serial.QueryAsync("SLOW?")); Check(!serial.IsConnected);
        await Throws<IOException>(() => serial.QueryAsync("VOUT1?"));
    });
    await Test("REAL SERIAL PTY: pre-cancelled transaction sends nothing and keeps link", async () =>
    {
        await Task.Delay(400); await using var serial = new SerialDeviceService { TimeoutMs = 300 }; await serial.ConnectAsync(args[0]);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Throws<OperationCanceledException>(() => serial.QueryAsync("*IDN?", cts.Token)); Check(serial.IsConnected); Check((await serial.QueryAsync("*IDN?")).Contains("GPP-4323"));
    });
    await Test("REAL SERIAL PTY: cancelled in-flight query invalidates connection", async () =>
    {
        await using var serial = new SerialDeviceService { TimeoutMs = 1000 }; await serial.ConnectAsync(args[0]);
        using var cts = new CancellationTokenSource(70); await Throws<OperationCanceledException>(() => serial.QueryAsync("SLOW?", cts.Token)); Check(!serial.IsConnected);
    });
}
if (args.Length == 1)
{
    await Test("REAL SERIAL PTY: unplug during query closes the connection", async () =>
    {
        await Task.Delay(400); await using var serial = new SerialDeviceService { TimeoutMs = 300 }; await serial.ConnectAsync(args[0]);
        try { await serial.QueryAsync("UNPLUG?"); throw new Exception("Expected an I/O error"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException) { Check(!serial.IsConnected); }
    });
}
Console.WriteLine($"RESULT: {passed} tests passed.");

sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(responder(request));
}

sealed class FakeTransport : ISerialTransport
{
    public string IdentityModel { get; set; } = "GPP-4323";
    public bool IsConnected { get; private set; } = true;
    public int TimeoutMs { get; set; } = 1500;
    public int DelayMs { get; set; }
    public bool IgnoreVoltageWrites { get; set; }
    public int IgnoreOffChannel { get; set; }
    public int PeakConcurrent { get; private set; }
    private int active;
    public ConcurrentQueue<string> Commands { get; } = new();
    public Dictionary<int, bool> Output { get; } = new() { [1] = false, [2] = false, [3] = false, [4] = false };
    private readonly Dictionary<int, double> voltage = new() { [1] = 5, [2] = 5, [3] = 5, [4] = 5 };
    private readonly Dictionary<int, double> current = new() { [1] = .1, [2] = .1, [3] = .1, [4] = .1 };
    public event Action<TrafficEntry>? Traffic;
    public Task ConnectAsync(string name, CancellationToken token = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
    public Task WriteAsync(string command, CancellationToken token = default, bool polling = false) => Exchange(command, token, polling);
    public Task<string> QueryAsync(string command, CancellationToken token = default, bool polling = false) => Exchange(command, token, polling);
    private async Task<string> Exchange(string c, CancellationToken ct, bool polling)
    {
        ct.ThrowIfCancellationRequested(); var concurrency = Interlocked.Increment(ref active); PeakConcurrent = Math.Max(PeakConcurrent, concurrency);
        try
        {
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            Commands.Enqueue(c); Traffic?.Invoke(new(DateTimeOffset.Now, "TX", c, polling));
            if (c == "*IDN?") return $"GW Instek,{IdentityModel},SN:TEST,V1.07";
            int ch = c.Where(char.IsDigit).Select(x => x - '0').First();
            if (c.StartsWith(":OUTPut"))
            { if (c.EndsWith('?')) return Output[ch] ? "ON" : "OFF"; if (!(c.EndsWith("OFF") && ch == IgnoreOffChannel)) Output[ch] = c.EndsWith("ON"); return ""; }
            if (c.StartsWith("VSET")) { if (c.EndsWith('?')) return Numbers.F(voltage[ch]); if (!IgnoreVoltageWrites) voltage[ch] = double.Parse(c.Split(':')[1], CultureInfo.InvariantCulture); return ""; }
            if (c.StartsWith("ISET")) { if (c.EndsWith('?')) return Numbers.F(current[ch], 4); current[ch] = double.Parse(c.Split(':')[1], CultureInfo.InvariantCulture); return ""; }
            if (c.StartsWith(":SOURce") && c.Contains(":VOLTage")) { if (c.EndsWith('?')) return Numbers.F(voltage[ch]); if (!IgnoreVoltageWrites) voltage[ch] = double.Parse(c.Split(' ', 2)[1], CultureInfo.InvariantCulture); return ""; }
            if (c.StartsWith("VOUT")) return Numbers.F(Output[ch] ? voltage[ch] : 0) + "V";
            if (c.StartsWith("IOUT")) return "0.0000A";
            throw new InvalidOperationException(c);
        }
        finally { Interlocked.Decrement(ref active); }
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
