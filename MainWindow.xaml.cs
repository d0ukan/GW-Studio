using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.ViewModels;

namespace Dou_GW_Studio;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    private bool mayClose, closing;
    private HwndSource? hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) =>
        {
            var prefs = viewModel.GetWindowPreferences();
            if (!prefs.Maximized)
            {
                Width = prefs.Width; Height = prefs.Height; WindowState = WindowState.Normal;
            }
            await viewModel.InitializeAsync();
        };
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmGetMinMaxInfo)
        {
            ApplyMonitorWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyMonitorWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            return;

        var minMax = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
        var work = monitorInfo.Work;
        var monitorRect = monitorInfo.Monitor;

        // A borderless maximized WPF window can otherwise extend behind the Windows taskbar.
        // Constrain the maximized bounds to the current monitor's work area so the bottom
        // status/message bar always remains visible.
        minMax.MaxPosition.X = Math.Abs(work.Left - monitorRect.Left);
        minMax.MaxPosition.Y = Math.Abs(work.Top - monitorRect.Top);
        minMax.MaxSize.X = Math.Abs(work.Right - work.Left);
        minMax.MaxSize.Y = Math.Abs(work.Bottom - work.Top);
        minMax.MaxTrackSize.X = minMax.MaxSize.X;
        minMax.MaxTrackSize.Y = minMax.MaxSize.Y;

        Marshal.StructureToPtr(minMax, lParam, true);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UiMode_Click(object sender, RoutedEventArgs e)
    {
        viewModel.IsAdvancedMode = !viewModel.IsAdvancedMode;
        if (!viewModel.IsAdvancedMode)
            MainTabs.SelectedItem = DashboardTab;
        viewModel.ShowInfo(viewModel.IsAdvancedMode ? "Advanced view enabled." : "Compact view enabled.");
    }

    private void Mil704_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = Mil704Tab;
        viewModel.ShowInfo("704 Lab opened — build and preview a waveform before ARM/RUN.");
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (mayClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;

        try
        {
            var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, ActualWidth, ActualHeight);
            viewModel.UpdateWindowPreferences(bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
            await viewModel.ShutdownAsync();
            mayClose = true;
            if (hwndSource is not null)
            {
                hwndSource.RemoveHook(WindowProc);
                hwndSource = null;
            }
            Close();
        }
        catch (Exception ex)
        {
            viewModel.ReportError(ex);
        }
        finally
        {
            closing = false;
        }
    }
}
