using System.Globalization;
using System.IO;
using System.Text;
using Dou_GW_Studio.Models;

namespace Dou_GW_Studio.Services;

public sealed record PdfReportChannel(
    int Channel,
    string Mode,
    string OutputState,
    string SetVoltage,
    string CurrentLimit,
    string ActualVoltage,
    string ActualCurrent,
    string ActualPower,
    string WarningStatus,
    string ProtectionStatus);

public sealed record PdfReportData(
    string AppVersion,
    DateTimeOffset GeneratedAt,
    DeviceInfo? Device,
    string Port,
    string ConnectionHealth,
    string RangeLabel,
    MeasurementPoint[] Points,
    GraphMarker[] Markers,
    IReadOnlyList<PdfReportChannel> Channels,
    string OperatingStatus,
    string CustomStatus,
    string SequenceStatus,
    bool IncludeVoltageGraph,
    bool IncludeCurrentGraph,
    bool IncludePowerGraph,
    bool IncludeMarkers,
    string Mil704Title,
    IReadOnlyList<(double Time, double Voltage)> Mil704Requested,
    IReadOnlyList<(double Time, double Voltage)> Mil704Measured);

public sealed class PdfReportService
{
    public Task ExportAsync(string path, PdfReportData data, CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(path, data, cancellationToken), cancellationToken);

    private static void Export(string path, PdfReportData data, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var pdf = new SimplePdfDocument();
        var report = new ReportCanvas(pdf, data.AppVersion);

        report.Title("GW Studio", "Session / measurement report - Made by Dou");
        report.KeyValue("Generated", data.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        report.KeyValue("Report range", data.RangeLabel);
        report.KeyValue("COM port", string.IsNullOrWhiteSpace(data.Port) ? "-" : data.Port);
        if (data.Device is not null)
        {
            report.KeyValue("Instrument", $"{data.Device.Manufacturer} {data.Device.Model}");
            report.KeyValue("Serial number", data.Device.SerialNumber);
            report.KeyValue("Firmware", data.Device.Firmware);
        }
        report.KeyValue("Connection health", data.ConnectionHealth);
        report.Rule();

        report.Heading("Channel summary");
        foreach (var ch in data.Channels)
        {
            ct.ThrowIfCancellationRequested();
            report.SubHeading($"CH{ch.Channel} - {ch.Mode} - {ch.OutputState}");
            report.TwoColumn(
                ("Device set voltage", ch.SetVoltage + " V"),
                ("Current limit", ch.CurrentLimit + " A"),
                ("Actual voltage", ch.ActualVoltage + " V"),
                ("Actual current", ch.ActualCurrent + " A"),
                ("Actual power", ch.ActualPower + " W"),
                ("Soft warning", ch.WarningStatus),
                ("Software protection", ch.ProtectionStatus));

            var chPoints = data.Points.Where(p => p.Channel == ch.Channel).ToArray();
            if (chPoints.Length > 0)
            {
                report.Stats("Voltage", chPoints.Select(p => p.Voltage), "V");
                report.Stats("Current", chPoints.Select(p => p.Current), "A");
                report.Stats("Power", chPoints.Select(p => p.Power), "W");
            }
            else report.Text("No samples in the selected report range.", muted: true);
            report.Spacer(6);
        }

        report.Heading("Operating state");
        report.Text(data.OperatingStatus);
        report.Text("Custom: " + data.CustomStatus, muted: true);
        report.Text("Native sequence: " + data.SequenceStatus, muted: true);

        if (data.Points.Length > 1)
        {
            if (data.IncludeVoltageGraph) report.Graph("Voltage vs time", data.Points, p => p.Voltage, "V", data.Markers, data.IncludeMarkers);
            if (data.IncludeCurrentGraph) report.Graph("Current vs time", data.Points, p => p.Current, "A", data.Markers, data.IncludeMarkers);
            if (data.IncludePowerGraph) report.Graph("Power vs time", data.Points, p => p.Power, "W", data.Markers, data.IncludeMarkers);
        }

        if (data.Mil704Requested.Count > 1)
        {
            report.WaveformGraph(string.IsNullOrWhiteSpace(data.Mil704Title) ? "704 waveform snapshot" : data.Mil704Title, data.Mil704Requested, data.Mil704Measured);
        }

        if (data.IncludeMarkers)
        {
            report.Heading("Markers / notes");
            if (data.Markers.Length == 0) report.Text("No graph markers in this session.", muted: true);
            else
            {
                foreach (var m in data.Markers.OrderBy(m => m.Timestamp))
                {
                    ct.ThrowIfCancellationRequested();
                    report.Text($"{m.Timestamp.ToLocalTime():HH:mm:ss.fff}   {m.Note}");
                }
            }
        }

        report.Heading("Measurement data summary");
        if (data.Points.Length == 0) report.Text("No in-memory measurement samples were available for this report.", muted: true);
        else
        {
            var start = data.Points.Min(p => p.Timestamp).ToLocalTime();
            var end = data.Points.Max(p => p.Timestamp).ToLocalTime();
            report.KeyValue("First sample", start.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            report.KeyValue("Last sample", end.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            report.KeyValue("Duration", (end - start).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture));
            report.KeyValue("Samples in report", data.Points.Length.ToString("N0", CultureInfo.InvariantCulture));
            report.Text("Raw high-rate samples are intentionally kept in CSV; the PDF is a readable engineering report.", muted: true);
        }

        File.WriteAllBytes(path, pdf.Build());
    }

    private sealed class ReportCanvas
    {
        private const double PageWidth = 595, PageHeight = 842, Left = 42, Right = 42, Top = 42, Bottom = 42;
        private readonly SimplePdfDocument pdf;
        private readonly string version;
        private SimplePdfPage page;
        private double y;

        public ReportCanvas(SimplePdfDocument pdf, string version)
        {
            this.pdf = pdf; this.version = version;
            page = pdf.AddPage(); y = PageHeight - Top;
            Footer();
        }

        public void Title(string title, string subtitle)
        {
            TextAt(title, Left, y, 20, true, 0.05, 0.18, 0.23); y -= 24;
            TextAt(subtitle, Left, y, 10, false, 0.25, 0.35, 0.39); y -= 20;
        }
        public void Heading(string text) { Ensure(30); y -= 8; TextAt(text, Left, y, 14, true, 0.04, 0.45, 0.52); y -= 20; }
        public void SubHeading(string text) { Ensure(22); TextAt(text, Left, y, 11, true, 0.08, 0.15, 0.18); y -= 16; }
        public void Text(string text, bool muted = false) { foreach (var line in Wrap(text, 105)) { Ensure(14); TextAt(line, Left, y, 8.5, false, muted ? 0.38 : 0.12, muted ? 0.43 : 0.16, muted ? 0.45 : 0.18); y -= 12; } }
        public void KeyValue(string key, string value) { Ensure(14); TextAt(key, Left, y, 8.5, true, .18, .24, .27); TextAt(value, 155, y, 8.5, false, .10, .13, .15); y -= 13; }
        public void Rule() { Ensure(12); page.Line(Left, y, PageWidth - Right, y, .78, .84, .86, .6); y -= 12; }
        public void Spacer(double amount) { y -= amount; }

        public void TwoColumn(params (string key, string value)[] items)
        {
            for (int i = 0; i < items.Length; i += 2)
            {
                Ensure(14);
                DrawPair(items[i], Left, 125);
                if (i + 1 < items.Length) DrawPair(items[i + 1], 305, 395);
                y -= 13;
            }
        }

        public void Stats(string name, IEnumerable<double> values, string unit)
        {
            var v = values.Where(double.IsFinite).ToArray(); if (v.Length == 0) return;
            Text($"{name}:  Min {v.Min():0.0000} {unit}   |   Avg {v.Average():0.0000} {unit}   |   Max {v.Max():0.0000} {unit}   |   {v.Length:N0} samples", muted: true);
        }

        public void Graph(string title, MeasurementPoint[] points, Func<MeasurementPoint, double> selector, string unit, GraphMarker[] markers, bool showMarkers)
        {
            const double height = 215;
            Ensure(height + 35);
            Heading(title);
            var x = Left + 36; var w = PageWidth - Left - Right - 46; var h = 150.0; var bottomY = y - h;
            page.Rect(x, bottomY, w, h, .87, .90, .91, .7);

            var valid = points.Where(p => double.IsFinite(selector(p))).OrderBy(p => p.Timestamp).ToArray();
            if (valid.Length < 2) { Text("Not enough samples to draw this graph.", true); return; }
            var t0 = valid[0].Timestamp; var t1 = valid[^1].Timestamp; var span = Math.Max(.001, (t1 - t0).TotalSeconds);
            var vals = valid.Select(selector).ToArray(); var min = vals.Min(); var max = vals.Max();
            if (Math.Abs(max - min) < 1e-9) { min -= Math.Max(1, Math.Abs(min) * .05); max += Math.Max(1, Math.Abs(max) * .05); }
            else { var pad = (max - min) * .08; min -= pad; max += pad; }

            for (var grid = 0; grid <= 4; grid++)
            {
                var gy = bottomY + h * grid / 4.0;
                page.Line(x, gy, x + w, gy, .92, .94, .95, .35);
                TextAt((min + (max - min) * grid / 4.0).ToString("0.###", CultureInfo.InvariantCulture), Left, gy - 2, 6.8, false, .38, .43, .45);
            }

            foreach (var group in valid.GroupBy(p => p.Channel).OrderBy(g => g.Key))
            {
                var sampled = Downsample(group.ToArray(), 650);
                var color = group.Key switch
                {
                    1 => (r: .03, g: .62, b: .70),
                    2 => (r: .35, g: .48, b: .90),
                    3 => (r: .72, g: .36, b: .78),
                    _ => (r: .35, g: .65, b: .20)
                };
                var coords = sampled.Select(p => (
                    x: x + w * (p.Timestamp - t0).TotalSeconds / span,
                    y: bottomY + h * (selector(p) - min) / (max - min))).ToArray();
                page.Polyline(coords, color.r, color.g, color.b, 1.1);
                TextAt($"CH{group.Key}", x + 8 + (group.Key - 1) * 42, bottomY + h + 8, 7.5, true, color.r, color.g, color.b);
            }

            if (showMarkers)
            {
                foreach (var marker in markers.Where(m => m.Timestamp >= t0 && m.Timestamp <= t1))
                {
                    var mx = x + w * (marker.Timestamp - t0).TotalSeconds / span;
                    page.Line(mx, bottomY, mx, bottomY + h, .65, .20, .20, .6, dashed: true);
                }
            }
            TextAt(t0.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), x, bottomY - 12, 6.8, false, .38, .43, .45);
            TextAt(t1.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), x + w - 38, bottomY - 12, 6.8, false, .38, .43, .45);
            TextAt(unit, Left, bottomY + h + 8, 7.2, true, .25, .32, .35);
            y = bottomY - 22;
        }

        public void WaveformGraph(string title, IReadOnlyList<(double Time, double Voltage)> requested, IReadOnlyList<(double Time, double Voltage)> measured)
        {
            const double height = 215;
            Ensure(height + 35);
            Heading(title);
            var x = Left + 36; var w = PageWidth - Left - Right - 46; var h = 150.0; var bottomY = y - h;
            page.Rect(x, bottomY, w, h, .87, .90, .91, .7);

            var req = requested.Where(p => double.IsFinite(p.Time) && double.IsFinite(p.Voltage)).OrderBy(p => p.Time).ToArray();
            var meas = measured.Where(p => double.IsFinite(p.Time) && double.IsFinite(p.Voltage)).OrderBy(p => p.Time).ToArray();
            if (req.Length < 2) { Text("Not enough 704 waveform points to draw this graph.", true); return; }

            var maxTime = Math.Max(req[^1].Time, meas.Length > 0 ? meas[^1].Time : 0);
            var span = Math.Max(.001, maxTime);
            var allVals = req.Select(p => p.Voltage).Concat(meas.Select(p => p.Voltage)).ToArray();
            var min = allVals.Min(); var max = allVals.Max();
            if (Math.Abs(max - min) < 1e-9) { min -= Math.Max(1, Math.Abs(min) * .05); max += Math.Max(1, Math.Abs(min) * .05); }
            else { var pad = (max - min) * .08; min -= pad; max += pad; }

            for (var grid = 0; grid <= 4; grid++)
            {
                var gy = bottomY + h * grid / 4.0;
                page.Line(x, gy, x + w, gy, .92, .94, .95, .35);
                TextAt((min + (max - min) * grid / 4.0).ToString("0.###", CultureInfo.InvariantCulture), Left, gy - 2, 6.8, false, .38, .43, .45);
            }

            var reqSampled = DownsampleWave(req, 650);
            var reqCoords = reqSampled.Select(p => (x: x + w * p.Time / span, y: bottomY + h * (p.Voltage - min) / (max - min))).ToArray();
            page.Polyline(reqCoords, .03, .62, .70, 1.5);
            TextAt("Requested", x + 8, bottomY + h + 8, 7.5, true, .03, .62, .70);

            if (meas.Length > 1)
            {
                var measSampled = DownsampleWave(meas, 650);
                var measCoords = measSampled.Select(p => (x: x + w * p.Time / span, y: bottomY + h * (p.Voltage - min) / (max - min))).ToArray();
                page.Polyline(measCoords, .95, .62, .18, 1.2);
                TextAt("Measured", x + 88, bottomY + h + 8, 7.5, true, .95, .62, .18);
            }

            TextAt("0.000 s", x, bottomY - 12, 6.8, false, .38, .43, .45);
            TextAt($"{span:0.###} s", x + w - 38, bottomY - 12, 6.8, false, .38, .43, .45);
            TextAt("V", Left, bottomY + h + 8, 7.2, true, .25, .32, .35);
            y = bottomY - 22;
        }

        private static (double Time, double Voltage)[] DownsampleWave((double Time, double Voltage)[] input, int max)
        {
            if (input.Length <= max) return input;
            var step = (double)(input.Length - 1) / (max - 1);
            var result = new (double Time, double Voltage)[max];
            for (var i = 0; i < max; i++) result[i] = input[(int)Math.Round(i * step)];
            return result;
        }

        private void DrawPair((string key, string value) item, double keyX, double valueX)
        {
            TextAt(item.key, keyX, y, 7.7, true, .25, .31, .34);
            TextAt(item.value, valueX, y, 7.7, false, .10, .13, .15);
        }
        private void Ensure(double needed) { if (y - needed >= Bottom + 20) return; page = pdf.AddPage(); y = PageHeight - Top; Footer(); }
        private void Footer() { TextAt($"GW Studio  |  Made by Dou  |  {version}", Left, 22, 6.8, false, .45, .50, .52); }
        private void TextAt(string text, double x, double yy, double size, bool bold, double r, double g, double b) => page.Text(Sanitize(text), x, yy, size, bold, r, g, b);

        private static string[] Wrap(string input, int max)
        {
            var words = Sanitize(input).Split(' ', StringSplitOptions.RemoveEmptyEntries); var lines = new List<string>(); var current = new StringBuilder();
            foreach (var word in words)
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > max) { lines.Add(current.ToString()); current.Clear(); }
                if (current.Length > 0) current.Append(' '); current.Append(word);
            }
            if (current.Length > 0) lines.Add(current.ToString()); return lines.Count == 0 ? [""] : lines.ToArray();
        }
        private static MeasurementPoint[] Downsample(MeasurementPoint[] input, int max)
        {
            if (input.Length <= max) return input;
            var step = (double)(input.Length - 1) / (max - 1); var result = new MeasurementPoint[max];
            for (var i = 0; i < max; i++) result[i] = input[(int)Math.Round(i * step)]; return result;
        }
        private static string Sanitize(string value) => value.Replace("Ω", "Ohm").Replace("—", "-").Replace("•", "|").Replace("–", "-").Replace("→", "->").Replace("≤", "<=").Replace("≥", ">=").Replace("…", "...");
    }

    private sealed class SimplePdfDocument
    {
        private readonly List<SimplePdfPage> pages = [];
        public SimplePdfPage AddPage() { var p = new SimplePdfPage(); pages.Add(p); return p; }
        public byte[] Build()
        {
            var n = pages.Count; var fontRegular = 3 + 2 * n; var fontBold = fontRegular + 1; var objects = new List<byte[]>();
            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            var kids = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{3 + 2 * i} 0 R"));
            objects.Add(Ascii($"<< /Type /Pages /Count {n} /Kids [{kids}] >>"));
            for (var i = 0; i < n; i++)
            {
                var pageId = 3 + 2 * i; var contentId = pageId + 1;
                objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {fontRegular} 0 R /F2 {fontBold} 0 R >> >> /Contents {contentId} 0 R >>"));
                var content = Ascii(pages[i].Content.ToString());
                objects.Add(Concat(Ascii($"<< /Length {content.Length} >>\nstream\n"), content, Ascii("\nendstream")));
            }
            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));

            using var ms = new MemoryStream();
            Write(ms, "%PDF-1.4\n%GWSTUDIO\n"); var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++) { offsets.Add(ms.Position); Write(ms, $"{i + 1} 0 obj\n"); ms.Write(objects[i]); Write(ms, "\nendobj\n"); }
            var xref = ms.Position; Write(ms, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            for (var i = 1; i < offsets.Count; i++) Write(ms, $"{offsets[i]:D10} 00000 n \n");
            Write(ms, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return ms.ToArray();
        }
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
        private static byte[] Concat(params byte[][] arrays) { var len = arrays.Sum(a => a.Length); var result = new byte[len]; var pos = 0; foreach (var a in arrays) { Buffer.BlockCopy(a, 0, result, pos, a.Length); pos += a.Length; } return result; }
        private static void Write(Stream s, string text) { var b = Ascii(text); s.Write(b); }
    }

    private sealed class SimplePdfPage
    {
        public StringBuilder Content { get; } = new();
        public void Text(string text, double x, double y, double size, bool bold, double r, double g, double b)
        {
            Content.AppendFormat(CultureInfo.InvariantCulture, "BT {0:0.###} {1:0.###} {2:0.###} rg /{3} {4:0.##} Tf 1 0 0 1 {5:0.##} {6:0.##} Tm ({7}) Tj ET\n", r, g, b, bold ? "F2" : "F1", size, x, y, Escape(text));
        }
        public void Line(double x1, double y1, double x2, double y2, double r, double g, double b, double width, bool dashed = false)
        {
            Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} RG {3:0.##} w {4} {5:0.##} {6:0.##} m {7:0.##} {8:0.##} l S [] 0 d\n", r, g, b, width, dashed ? "[3 3] 0 d" : "[] 0 d", x1, y1, x2, y2);
        }
        public void Rect(double x, double y, double w, double h, double r, double g, double b, double width)
        {
            Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} RG {3:0.##} w {4:0.##} {5:0.##} {6:0.##} {7:0.##} re S\n", r, g, b, width, x, y, w, h);
        }
        public void Polyline((double x, double y)[] points, double r, double g, double b, double width)
        {
            if (points.Length < 2) return;
            Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} RG {3:0.##} w {4:0.##} {5:0.##} m ", r, g, b, width, points[0].x, points[0].y);
            for (var i = 1; i < points.Length; i++) Content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} l ", points[i].x, points[i].y);
            Content.Append("S\n");
        }
        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
    }
}
