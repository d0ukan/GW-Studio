using Dou_GW_Studio.Models;
namespace Dou_GW_Studio.Services;
public sealed class MeasurementHistoryService
{
    public const int Capacity = 24000;
    private readonly Queue<MeasurementPoint> points = new();
    private readonly object sync = new();
    public void Add(MeasurementPoint point) { lock (sync) { points.Enqueue(point); while (points.Count > Capacity) points.Dequeue(); } }
    public MeasurementPoint[] Snapshot() { lock (sync) return points.ToArray(); }
    public void Clear() { lock (sync) points.Clear(); }
}
