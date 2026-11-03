using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.Services;
public interface ISerialTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    int TimeoutMs { get; set; }
    event Action<TrafficEntry>? Traffic;
    Task ConnectAsync(string portName, CancellationToken token = default);
    Task DisconnectAsync();
    Task WriteAsync(string command, CancellationToken token = default, bool polling = false);
    Task<string> QueryAsync(string command, CancellationToken token = default, bool polling = false);
}
