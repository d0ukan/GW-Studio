using System.Windows;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
using Dou_GW_Studio.Services;

namespace Dou_GW_Studio;

public partial class UpdatePromptWindow : Window
{
    private readonly AppUpdateService service;
    private readonly AppUpdateInfo info;
    private bool installing;

    public UpdatePromptWindow(AppUpdateService service, AppUpdateInfo info)
    {
        this.service = service;
        this.info = info;
        InitializeComponent();
        InstalledText.Text = "v" + AppVersionInfo.DisplayVersion;
        AvailableText.Text = "v" + AppUpdateService.NormalizeVersionText(info.Version);
        NotesText.Text = string.IsNullOrWhiteSpace(info.Notes) ? "No release notes were supplied." : info.Notes.Trim();
        if (AppUpdateService.IsPortable)
        {
            IntroText.Text = "This is the Portable edition. It is not updated automatically: download the new Portable ZIP and replace this folder.";
            StatusText.Text = "Portable edition detected.";
            UpdateButton.Content = "OPEN DOWNLOAD PAGE";
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (!installing) Close();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (installing) return;
        if (AppUpdateService.IsPortable)
        {
            try { service.OpenReleasesPage(); Close(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "GW Studio Update", MessageBoxButton.OK, MessageBoxImage.Warning); }
            return;
        }
        installing = true;
        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        StatusText.Text = "Preparing update…";

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                StatusText.Text = p.Stage switch
                {
                    "Downloading" => p.Percent is { } percent ? $"Downloading… {percent:0}%" : "Downloading…",
                    "Extracting" => "Extracting update…",
                    "Ready" => "Starting installer…",
                    _ => p.Stage
                };
                if (p.Percent is { } value)
                {
                    Progress.IsIndeterminate = false;
                    Progress.Value = Math.Clamp(value, 0, 100);
                }
                else Progress.IsIndeterminate = true;
            });

            var setup = await service.DownloadExtractAndFindSetupAsync(info, progress);
            service.StartSetup(setup);
            installing = false;
            Close();
            Application.Current.Dispatcher.BeginInvoke(() => Application.Current.MainWindow?.Close());
        }
        catch (Exception ex)
        {
            AppLogService.Write($"update check/download error: {ex.GetType().Name}: {ex.Message}");
            StatusText.Text = "Update failed. GW Studio will continue running.";
            MessageBox.Show(this, ex.Message, "GW Studio Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            installing = false;
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (installing) e.Cancel = true;
        base.OnClosing(e);
    }
}
