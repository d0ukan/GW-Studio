using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Dou_GW_Studio.Models;
using Dou_GW_Studio.ViewModels;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using OxyPlot.Wpf;
namespace Dou_GW_Studio.Controls;
/// <summary>Reusable plot adapter: each view owns its PlotModel, both pages share history/settings.</summary>
public sealed class LiveChartControl : PlotView
{
    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(nameof(Graph), typeof(GraphViewModel), typeof(LiveChartControl), new PropertyMetadata(null, GraphChanged));
    public static readonly DependencyProperty SignalProperty = DependencyProperty.Register(nameof(Signal), typeof(string), typeof(LiveChartControl), new PropertyMetadata("Voltage", (_, _) => { }));
    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(nameof(Interactive), typeof(bool), typeof(LiveChartControl), new PropertyMetadata(false, (d, _) => { var control = (LiveChartControl)d; if (control.IsLoaded) control.ConfigureController(); }));
    public GraphViewModel? Graph { get => (GraphViewModel?)GetValue(GraphProperty); set => SetValue(GraphProperty, value); }
    public string Signal { get => (string)GetValue(SignalProperty); set => SetValue(SignalProperty, value); }
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }
    public LiveChartControl()
    {
        Loaded += (_, _) => { if (Graph != null) { Graph.PropertyChanged -= Changed; Graph.PropertyChanged += Changed; } ConfigureController(); Redraw(); };
        Unloaded += (_, _) => { if (Graph != null) Graph.PropertyChanged -= Changed; };
        // Normal embedded charts stay read-only. Full-screen charts can opt into the
        // standard OxyPlot navigation controller (wheel zoom, right-drag pan, etc.).
        ConfigureController();
        // The stock WPF tracker uses a light system tooltip background and inherits
        // the app's light foreground, which is unreadable in dark mode.
        if (Application.Current.TryFindResource("DarkTrackerTemplate") is System.Windows.Controls.ControlTemplate trackerTemplate)
            DefaultTrackerTemplate = trackerTemplate;
    }
    private void ConfigureController()
    {
        if (Interactive)
        {
            // OxyPlot defaults: LMB tracker, RMB drag pan, mouse wheel zoom,
            // MMB drag zoom-rectangle, Home/A reset.
            Controller = new PlotController();
        }
        else
        {
            var controller = new PlotController();
            controller.UnbindAll();
            controller.BindMouseDown(OxyMouseButton.Left, OxyPlot.PlotCommands.SnapTrack);
            Controller = controller;
        }
    }

    public void ResetView()
    {
        if (Model == null) return;
        Model.ResetAllAxes();
        InvalidatePlot(false);
    }

    private static void GraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LiveChartControl)d;
        if (e.OldValue is GraphViewModel old) old.PropertyChanged -= control.Changed;
        if (e.NewValue is GraphViewModel next && control.IsLoaded) next.PropertyChanged += control.Changed;
        if (control.IsLoaded) control.Redraw();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(GraphViewModel.Revision) && IsVisible) Redraw(); }
    private OxyColor Color(string key)
    { var c = ((SolidColorBrush)FindResource(key)).Color; return OxyColor.FromArgb(c.A, c.R, c.G, c.B); }
    private void Redraw()
    {
        var graph = Graph; if (graph == null || !IsLoaded) return;
        var all = graph.GetPoints();
        var end = all.Length == 0 ? DateTimeOffset.Now : all[^1].Timestamp;
        double seconds = graph.WindowSeconds == 0 ? Math.Max(1, all.Length == 0 ? 30 : (end - all[0].Timestamp).TotalSeconds) : graph.WindowSeconds;
        var points = all.Where(p => (end - p.Timestamp).TotalSeconds <= seconds && graph.IsChannelVisible(p.Channel)).ToArray();
        var unit = Signal switch { "Current" => "A", "Power" => "W", _ => "V" };
        var model = new PlotModel
        {
            Title = Signal.ToUpperInvariant() + "  /  " + unit, TitleFontSize = 12, TitleFontWeight = 500,
            
            Background = Color("CardBrush"), PlotAreaBorderColor = Color("BorderBrush"), TextColor = Color("SecondaryBrush"), TitleColor = Color("TextBrush"),
            DefaultFont = "Segoe UI", DefaultFontSize = 11, Padding = new OxyThickness(10),
        };
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = -seconds, Maximum = 0, Title = "Seconds relative to latest sample", TitleFontSize = 10,
            MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = Color("BorderBrush"), TicklineColor = Color("BorderBrush"), AxislineColor = Color("BorderBrush"), IsPanEnabled = Interactive, IsZoomEnabled = Interactive });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Minimum = graph.AutoScale ? double.NaN : 0,
            Maximum = graph.AutoScale ? double.NaN : Signal switch { "Current" => 3, "Power" => 96, _ => 32 }, MinimumPadding = .08, MaximumPadding = .12,
            MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = Color("BorderBrush"), TicklineColor = Color("BorderBrush"), AxislineColor = Color("BorderBrush"), IsPanEnabled = Interactive, IsZoomEnabled = Interactive });
        if (graph.ShowMarkers)
        {
            foreach (var marker in graph.GetMarkers().Where(m => (end - m.Timestamp).TotalSeconds >= 0 && (end - m.Timestamp).TotalSeconds <= seconds))
            {
                var x = (marker.Timestamp - end).TotalSeconds;
                var markerBrush = marker.Kind switch
                {
                    "OutputOn" or "Reconnect" or "Connect" => "SuccessBrush",
                    "Trip" or "Error" or "Disconnect" => "DangerBrush",
                    "Setpoint" => "AccentBrush",
                    _ => "WarningBrush"
                };
                model.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Vertical, X = x, Color = Color(markerBrush), LineStyle = LineStyle.Dot, StrokeThickness = 1.5,
                    Text = marker.Note, TextColor = Color(markerBrush)
                });
            }
        }

        foreach (var group in points.GroupBy(p => p.Channel))
        {
            var color = Color(group.Key switch { 1 => "AccentBrush", 2 => "Channel2Brush", 3 => "Channel3Brush", _ => "Channel4Brush" });
            var actual = new LineSeries { Title = $"CH{group.Key} actual", Color = color, StrokeThickness = 1.8, TrackerFormatString = "{0}\nTime: {2:0.000} s\nValue: {4:0.0000} " + unit };
            foreach (var p in group) actual.Points.Add(new((p.Timestamp - end).TotalSeconds, Signal switch { "Current" => p.Current, "Power" => p.Power, _ => p.Voltage }));
            model.Series.Add(actual);
            if (graph.ShowSetValues && ((Signal == "Voltage" && graph.ReferenceV) || (Signal == "Current" && graph.ReferenceI)))
            {
                var reference = new LineSeries { Title = $"CH{group.Key} setpoint", Color = color, StrokeThickness = 1, LineStyle = LineStyle.Dash, TrackerFormatString = "{0}\nTime: {2:0.000} s\nSet: {4:0.0000} " + unit };
                foreach (var p in group) reference.Points.Add(new((p.Timestamp - end).TotalSeconds, Signal == "Current" ? p.CurrentLimit : p.SetVoltage));
                model.Series.Add(reference);
            }
        }
        Model = model;
    }
}
