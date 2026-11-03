using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dou_GW_Studio;
using Dou_GW_Studio.Services;
using Dou_GW_Studio.ViewModels;
using OxyPlot;

namespace Dou_GW_Studio.Views;

public partial class Mil704View : UserControl
{
    private int dragIndex = -1;
    private int selectedDragIndex = -1;
    private readonly Stopwatch dragThrottle = Stopwatch.StartNew();
    private Mil704GraphWindow? fullScreenWindow;

    public Mil704View()
    {
        InitializeComponent();
        // The drag editor owns the left mouse button. An empty OxyPlot controller
        // prevents the default tracker/pan bindings from consuming drag gestures.
        DragPlot.Controller = new PlotController();
    }

    private Mil704ViewModel? Vm => DragPlot?.DataContext as Mil704ViewModel;

    private void DragPlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var vm = Vm;
        var model = DragPlot.Model;
        if (vm == null || model == null || vm.DragPoints.Count == 0) return;
        var area = model.PlotArea;
        if (area.Width <= 0 || area.Height <= 0) return;
        var p = e.GetPosition(DragPlot);
        if (p.X < area.Left || p.X > area.Right || p.Y < area.Top || p.Y > area.Bottom) return;

        var xAxis = model.DefaultXAxis;
        var yAxis = model.DefaultYAxis;
        if (xAxis == null || yAxis == null) return;
        var best = -1;
        var bestD2 = 18.0 * 18.0;
        for (var i = 0; i < vm.DragPoints.Count; i++)
        {
            var sx = xAxis.Transform(vm.DragPoints[i].Time);
            var sy = yAxis.Transform(vm.DragPoints[i].Voltage);
            var dx = sx - p.X; var dy = sy - p.Y; var d2 = dx * dx + dy * dy;
            if (d2 < bestD2) { bestD2 = d2; best = i; }
        }
        if (best < 0) return;
        dragIndex = best;
        selectedDragIndex = best;
        UpdateDragOverlay(vm.DragPoints[best].Time, vm.DragPoints[best].Voltage);
        DragPlot.CaptureMouse();
        e.Handled = true;
    }

    private void DragPlot_MouseMove(object sender, MouseEventArgs e)
    {
        if (dragIndex < 0 || e.LeftButton != MouseButtonState.Pressed || dragThrottle.ElapsedMilliseconds < 25) return;
        var vm = Vm; var model = DragPlot.Model;
        if (vm == null || model == null) return;
        var area = model.PlotArea; var xAxis = model.DefaultXAxis; var yAxis = model.DefaultYAxis;
        if (area.Width <= 0 || area.Height <= 0 || xAxis == null || yAxis == null) return;
        var p = e.GetPosition(DragPlot);
        var x = xAxis.InverseTransform(Math.Clamp(p.X, area.Left, area.Right));
        var y = yAxis.InverseTransform(Math.Clamp(p.Y, area.Top, area.Bottom));
        vm.UpdateDragPoint(dragIndex, x, y, finalize: false);
        if (dragIndex >= 0 && dragIndex < vm.DragPoints.Count)
            UpdateDragOverlay(vm.DragPoints[dragIndex].Time, vm.DragPoints[dragIndex].Voltage);
        dragThrottle.Restart();
        e.Handled = true;
    }

    private void DragPlot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (dragIndex < 0) return;
        var vm = Vm;
        dragIndex = -1;
        DragPlot.ReleaseMouseCapture();
        vm?.FinalizeDragEdit();
        DragInfoOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void DragPlot_MouseLeave(object sender, MouseEventArgs e)
    {
        if (dragIndex < 0) DragInfoOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateDragOverlay(double time, double voltage)
    {
        DragInfoText.Text = $"Time:    {time:0.0000} s\nVoltage: {voltage:0.0000} VDC";
        DragInfoOverlay.Visibility = Visibility.Visible;
    }

    private void AddDragPoint_Click(object sender, RoutedEventArgs e)
    {
        var vm = Vm; if (vm == null) return;
        try
        {
            selectedDragIndex = vm.AddDragPoint(selectedDragIndex);
            if (selectedDragIndex >= 0 && selectedDragIndex < vm.DragPoints.Count)
                UpdateDragOverlay(vm.DragPoints[selectedDragIndex].Time, vm.DragPoints[selectedDragIndex].Voltage);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "GW Studio", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void RemoveDragPoint_Click(object sender, RoutedEventArgs e)
    {
        var vm = Vm; if (vm == null) return;
        try
        {
            selectedDragIndex = vm.RemoveDragPoint(selectedDragIndex);
            DragInfoOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "GW Studio", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.DataContext as Mil704ViewModel ?? DataContext as Mil704ViewModel;
        if (vm == null) return;
        try
        {
            var path = PlotExportService.ExportPngWithDialog(vm.CreatePreviewPlotSnapshot(), vm.BuildExportBaseName());
            if (!string.IsNullOrWhiteSpace(path)) vm.NotifyExternalStatus($"704 PNG exported: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "GW Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void JumpToGraph_Click(object sender, RoutedEventArgs e)
    {
        LabScroll.ScrollToTop();
        GraphSection.BringIntoView();
    }

    private void JumpToEditors_Click(object sender, RoutedEventArgs e) => EditorsSection.BringIntoView();

    private void OpenFullScreen_Click(object sender, RoutedEventArgs e)
    {
        var vm = (sender as FrameworkElement)?.DataContext as Mil704ViewModel ?? DataContext as Mil704ViewModel;
        if (vm == null) return;

        if (fullScreenWindow is { IsVisible: true })
        {
            if (fullScreenWindow.WindowState == WindowState.Minimized) fullScreenWindow.WindowState = WindowState.Normal;
            fullScreenWindow.Activate();
            return;
        }

        fullScreenWindow = new Mil704GraphWindow(vm);
        fullScreenWindow.Closed += (_, _) => fullScreenWindow = null;
        fullScreenWindow.Show();
        fullScreenWindow.Activate();
    }

    private static Point Transform(double x, double y, OxyRect area, double xmin, double xmax, double ymin, double ymax)
    {
        var sx = area.Left + (x - xmin) / Math.Max(1e-12, xmax - xmin) * area.Width;
        var sy = area.Top + (ymax - y) / Math.Max(1e-12, ymax - ymin) * area.Height;
        return new Point(sx, sy);
    }
}
