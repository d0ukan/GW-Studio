using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Dou_GW_Studio.ViewModels;

namespace Dou_GW_Studio;

public partial class GraphFullScreenWindow : Window
{
    private bool _isFullScreen;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;
    private WindowStyle _styleBeforeFullScreen = WindowStyle.SingleBorderWindow;
    private ResizeMode _resizeModeBeforeFullScreen = ResizeMode.CanResizeWithGrip;
    private Rect _boundsBeforeFullScreen;

    public GraphFullScreenWindow(MainViewModel vm, string signal)
    {
        InitializeComponent();
        DataContext = vm;
        Chart.Signal = signal;
        HeadingText.Text = signal.ToUpperInvariant() + " GRAPH";
        Title = $"GW Studio — {signal} Graph";

        var statsProperty = signal switch
        {
            "Current" => nameof(GraphViewModel.CurrentStats),
            "Power" => nameof(GraphViewModel.PowerStats),
            _ => nameof(GraphViewModel.VoltageStats)
        };
        StatsText.SetBinding(TextBlock.TextProperty, new Binding(statsProperty) { Source = vm.Graph });

        Loaded += (_, _) => vm.Graph.ForceRefresh();
        StateChanged += (_, _) => UpdateWindowButtons();
    }

    private void ResetView_Click(object sender, RoutedEventArgs e) => Chart.ResetView();

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullScreen)
            ExitFullScreen();

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        UpdateWindowButtons();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullScreen)
            ExitFullScreen();
        else
            EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        if (_isFullScreen) return;

        _stateBeforeFullScreen = WindowState;
        _styleBeforeFullScreen = WindowStyle;
        _resizeModeBeforeFullScreen = ResizeMode;
        _boundsBeforeFullScreen = RestoreBounds;

        // Apply fullscreen to whichever monitor currently contains this graph window.
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        _isFullScreen = true;
        UpdateWindowButtons();
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen) return;

        WindowState = WindowState.Normal;
        WindowStyle = _styleBeforeFullScreen;
        ResizeMode = _resizeModeBeforeFullScreen;

        if (_stateBeforeFullScreen == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
        else
        {
            if (_boundsBeforeFullScreen.Width > 0 && _boundsBeforeFullScreen.Height > 0)
            {
                Left = _boundsBeforeFullScreen.Left;
                Top = _boundsBeforeFullScreen.Top;
                Width = Math.Max(MinWidth, _boundsBeforeFullScreen.Width);
                Height = Math.Max(MinHeight, _boundsBeforeFullScreen.Height);
            }
            WindowState = WindowState.Normal;
        }

        _isFullScreen = false;
        UpdateWindowButtons();
    }

    private void UpdateWindowButtons()
    {
        if (FullScreenButton is not null)
            FullScreenButton.Content = _isFullScreen ? "EXIT FULL SCREEN" : "FULL SCREEN";

        if (MaximizeButton is not null)
            MaximizeButton.Content = (!_isFullScreen && WindowState == WindowState.Maximized) ? "RESTORE" : "MAXIMIZE";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // This graph window owns its own zoom/pan state. Closing it leaves the
        // embedded graph at its original scale and does not stop measurement polling.
        Close();
    }
}
