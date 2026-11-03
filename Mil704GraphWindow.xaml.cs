using System;
using System.ComponentModel;
using System.Windows;
using Dou_GW_Studio.Services;
using Dou_GW_Studio.ViewModels;

namespace Dou_GW_Studio;

public partial class Mil704GraphWindow : Window
{
    private readonly Mil704ViewModel vm;
    private bool isFullScreen;
    private WindowState stateBeforeFullScreen = WindowState.Normal;
    private WindowStyle styleBeforeFullScreen = WindowStyle.SingleBorderWindow;
    private ResizeMode resizeModeBeforeFullScreen = ResizeMode.CanResizeWithGrip;
    private Rect boundsBeforeFullScreen;

    public Mil704GraphWindow(Mil704ViewModel vm)
    {
        this.vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.PropertyChanged += Vm_PropertyChanged;
        Loaded += (_, _) => RefreshPlot();
        StateChanged += (_, _) => UpdateWindowButtons();
        Closed += (_, _) => vm.PropertyChanged -= Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Mil704ViewModel.PreviewModel) or nameof(Mil704ViewModel.ShowMeasured))
            Dispatcher.BeginInvoke(RefreshPlot);
    }

    private void RefreshPlot()
    {
        try { FullPlot.Model = vm.CreatePreviewPlotSnapshot(); } catch { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (isFullScreen) ExitFullScreen();
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowButtons();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (isFullScreen) ExitFullScreen(); else EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        if (isFullScreen) return;
        stateBeforeFullScreen = WindowState;
        styleBeforeFullScreen = WindowStyle;
        resizeModeBeforeFullScreen = ResizeMode;
        boundsBeforeFullScreen = RestoreBounds;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        isFullScreen = true;
        UpdateWindowButtons();
    }

    private void ExitFullScreen()
    {
        if (!isFullScreen) return;
        WindowState = WindowState.Normal;
        WindowStyle = styleBeforeFullScreen;
        ResizeMode = resizeModeBeforeFullScreen;
        if (stateBeforeFullScreen == WindowState.Maximized) WindowState = WindowState.Maximized;
        else
        {
            if (boundsBeforeFullScreen.Width > 0 && boundsBeforeFullScreen.Height > 0)
            {
                Left = boundsBeforeFullScreen.Left; Top = boundsBeforeFullScreen.Top;
                Width = Math.Max(MinWidth, boundsBeforeFullScreen.Width);
                Height = Math.Max(MinHeight, boundsBeforeFullScreen.Height);
            }
            WindowState = WindowState.Normal;
        }
        isFullScreen = false;
        UpdateWindowButtons();
    }

    private void UpdateWindowButtons()
    {
        if (FullScreenButton != null) FullScreenButton.Content = isFullScreen ? "EXIT FULL SCREEN" : "FULL SCREEN";
        if (MaximizeButton != null) MaximizeButton.Content = (!isFullScreen && WindowState == WindowState.Maximized) ? "RESTORE" : "MAXIMIZE";
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = PlotExportService.ExportPngWithDialog(vm.CreatePreviewPlotSnapshot(), vm.BuildExportBaseName() + "_fullscreen");
            if (!string.IsNullOrWhiteSpace(path)) vm.NotifyExternalStatus($"704 PNG exported: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "GW Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
