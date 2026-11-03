using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Dou_GW_Studio.ViewModels;
using Microsoft.Win32;
namespace Dou_GW_Studio.Controls;
public partial class GraphPanel : UserControl
{
    public GraphPanel() { InitializeComponent(); }
    private void SignalFullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm || sender is not FrameworkElement element) return;
        var signal = element.Tag?.ToString() ?? "Voltage";
        var window = new GraphFullScreenWindow(vm, signal);
        // Modeless and independent: keep the graph on another monitor while the main
        // GW Studio window remains usable. The graph window can be minimized separately.
        window.Show();
    }
    private void ExportWindowCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphViewModel graph) return;
        var points = graph.GetVisiblePoints();
        if (points.Length == 0) { MessageBox.Show("No samples are visible in the selected graph window.", "Export graph window", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"GW-Studio-graph-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog() != true) return;
        var sb = new StringBuilder("Timestamp,Channel,Voltage_V,Current_A,Power_W,SetVoltage_V,CurrentLimit_A,OutputOn\r\n");
        foreach (var p in points)
            sb.AppendLine(string.Join(',', p.Timestamp.ToLocalTime().ToString("O", CultureInfo.InvariantCulture), p.Channel, p.Voltage.ToString("0.0000", CultureInfo.InvariantCulture), p.Current.ToString("0.0000", CultureInfo.InvariantCulture), p.Power.ToString("0.0000", CultureInfo.InvariantCulture), p.SetVoltage.ToString("0.0000", CultureInfo.InvariantCulture), p.CurrentLimit.ToString("0.0000", CultureInfo.InvariantCulture), p.OutputOn ? "1" : "0"));
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
        if (Application.Current?.MainWindow?.DataContext is MainViewModel vm) vm.ShowInfo($"Visible graph window exported: {Path.GetFileName(dialog.FileName)}");
    }
}
