using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Dou_GW_Studio.Helpers;
using Dou_GW_Studio.Models;
using Dou_GW_Studio.Services;

namespace Dou_GW_Studio.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SerialDeviceService serial = new();
    private readonly SettingsService settingsStore = new();
    private readonly AppSettings settings;
    private readonly DevicePollingService polling;
    private readonly MeasurementHistoryService history = new();
    private readonly CsvLoggerService logger = new();
    private readonly PdfReportService pdfReports = new();
    private readonly AppUpdateService updateService = new();
    private readonly CustomTestExecutionService executor;
    private readonly SemaphoreSlim softwareProtectionGate = new(1, 1);
    private readonly ConcurrentDictionary<int, GppChannelState> pendingSamples = new();
    private readonly ConcurrentDictionary<int, GppChannelState> pendingPeakCurrentSamples = new();
    private readonly Stopwatch sampleUiClock = Stopwatch.StartNew();
    private readonly Stopwatch metricsUiClock = Stopwatch.StartNew();
    private readonly Stopwatch trafficUiClock = Stopwatch.StartNew();
    private int sampleUiQueued;

    private CancellationTokenSource? actionCancellation, testCancellation, reconnectCancellation;
    private Task? actionTask, testTask, reconnectTask;
    private bool connected, busy, running, stopping, shuttingDown, hidePolling = true, connectionLost;
    private DeviceInfo? connectedDeviceInfo;
    private string selectedPort = "", identity = "No instrument connected", status = "DISCONNECTED", message = "Select a COM port to begin.", terminalCommand = "*IDN?", logPath;
    private bool isError;
    private bool updatePromptOpen, manualUpdateCheckStarted;
    private string updateStatus = "Automatic update check runs quietly after startup.";
    private string pollText, timeoutText;
    private string pdfRange = "Visible graph window";
    private bool pdfIncludeCh1 = true, pdfIncludeCh2 = true, pdfIncludeCh3 = true, pdfIncludeCh4 = true;
    private bool pdfIncludeVoltage = true, pdfIncludeCurrent = true, pdfIncludePower = true, pdfIncludeMarkers = true;
    private string acquisitionStatus = "Waiting for acquisition data.";
    private string connectionHealth = "No connection health data yet.";
    private bool offOnDisconnect, autoReconnect, monitorCh2, monitorCh3, monitorCh4, isAdvancedMode;
    private DeviceProfile? activeProfile;

    public GppDeviceService Device { get; }
    public DeviceProfile ActiveProfile => activeProfile ?? throw new InvalidOperationException("No supported instrument has been identified.");
    public string DetectedModel => activeProfile?.ModelName ?? string.Empty;
    public bool HasChannel3 => activeProfile?.Channels.Any(c => c.Channel == 3) == true;
    public bool HasChannel4 => activeProfile?.Channels.Any(c => c.Channel == 4) == true;
    public string DeviceSummary => activeProfile switch
    {
        { Model: GppModel.Gpp3060 } => "GPP-3060 detected: CH1/CH2 are programmable 32 V / 6 A outputs; CH3 uses selectable 1.8/2.5/3.3/5 V with fixed current rating.",
        { Model: GppModel.Gpp4323 } => "GPP-4323 detected: CH1/CH2 are 32 V / 3 A, CH3 is 5 V / 1 A, and CH4 is 15 V / 1 A; all four outputs are programmable.",
        _ => "Connect a GPP-3060 or GPP-4323. The application will identify the model and build the correct channels automatically."
    };
    public ObservableCollection<string> Ports { get; } = [];
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];
    public ObservableCollection<TrafficEntry> Traffic { get; } = [];
    public ICollectionView TrafficView { get; }
    public GraphViewModel Graph { get; }
    public CustomViewModel Custom { get; }
    public Mil704ViewModel Mil704 { get; }
    public OperatingModeViewModel Operating { get; }
    public SequenceViewModel Sequence { get; }

    public string Version => AppVersionInfo.DisplayVersionWithPrefix;
    public string AppDisplayVersion => AppVersionInfo.DisplayVersion;
    public string HelpTitle => $"GW Studio {Version} — Help";
    public string SelectedPort { get => selectedPort; set { Set(ref selectedPort, value); ConnectCommand?.Refresh(); } }
    public bool Connected { get => connected; private set { Set(ref connected, value); Refresh(); } }
    public bool IsBusy { get => busy; private set { Set(ref busy, value); Refresh(); } }
    public bool IsRunning { get => running; private set { Set(ref running, value); Refresh(); } }
    public bool CanControl => Connected && !IsBusy && !IsRunning && !stopping && !shuttingDown;
    public bool CanQuickControl => serial.IsConnected && activeProfile != null && !stopping && !shuttingDown;
    public bool CanChoosePort => !Connected && !IsBusy && !IsRunning && !shuttingDown;
    public string ConnectLabel => Connected ? "DISCONNECT" : connectionLost ? "RECONNECT" : "CONNECT";
    public string Identity { get => identity; private set => Set(ref identity, value); }
    public string ConnectionStatus { get => status; private set => Set(ref status, value); }
    public string Message { get => message; private set => Set(ref message, value); }
    public bool IsError { get => isError; private set => Set(ref isError, value); }
    public string UpdateStatus { get => updateStatus; private set => Set(ref updateStatus, value); }
    public string TerminalCommand { get => terminalCommand; set => Set(ref terminalCommand, value); }
    public bool HidePolling { get => hidePolling; set { Set(ref hidePolling, value); TrafficView.Refresh(); } }
    public string PollText { get => pollText; set => Set(ref pollText, value); }
    public string TimeoutText { get => timeoutText; set => Set(ref timeoutText, value); }
    public string AcquisitionStatus { get => acquisitionStatus; private set => Set(ref acquisitionStatus, value); }
    public string ConnectionHealth { get => connectionHealth; private set => Set(ref connectionHealth, value); }
    public bool OffOnDisconnect { get => offOnDisconnect; set => Set(ref offOnDisconnect, value); }
    public bool IsAdvancedMode
    {
        get => isAdvancedMode;
        set
        {
            if (!Set(ref isAdvancedMode, value)) return;
            settings.AdvancedMode = value;
            OnChanged(nameof(UiModeLabel));
            OnChanged(nameof(UiModeHint));
            foreach (var ch in Channels) ch.NotifyUiModeChanged();
            _ = settingsStore.SaveAsync(settings);
        }
    }
    public string UiModeLabel => IsAdvancedMode ? "COMPACT" : "ADVANCED";
    public string UiModeHint => IsAdvancedMode ? "Advanced controls and diagnostics are visible." : "Compact bench-control view is active.";

    public bool AutoReconnect
    {
        get => autoReconnect;
        set
        {
            if (!Set(ref autoReconnect, value)) return;
            if (!value) CancelAutoReconnect();
            else if (connectionLost && !Connected) StartAutoReconnect();
        }
    }
    public bool ActiveMonitorCh2 => settings.MonitorCh2;
    public bool ActiveMonitorCh3 => settings.MonitorCh3;
    public bool ActiveMonitorCh4 => settings.MonitorCh4;
    public bool MonitorCh2 { get => monitorCh2; set => Set(ref monitorCh2, value); }
    public bool MonitorCh3 { get => monitorCh3; set => Set(ref monitorCh3, value); }
    public bool MonitorCh4 { get => monitorCh4; set => Set(ref monitorCh4, value); }
    public string LogPath { get => logPath; set => Set(ref logPath, value); }
    public string LogStatus => logger.IsRecording ? $"RECORDING  •  {logger.Rows:N0} rows written" : $"STOPPED  •  {logger.Rows:N0} rows written";
    public bool IsRecording => logger.IsRecording;
    public string LogFolderPath => Path.GetDirectoryName(LogPath) ?? LogStorageService.GetLogDirectory();
    public string PdfRange { get => pdfRange; set => Set(ref pdfRange, value); }
    public string[] PdfRanges { get; } = ["Visible graph window", "Full in-memory history"];
    public bool PdfIncludeCh1 { get => pdfIncludeCh1; set => Set(ref pdfIncludeCh1, value); }
    public bool PdfIncludeCh2 { get => pdfIncludeCh2; set => Set(ref pdfIncludeCh2, value); }
    public bool PdfIncludeCh3 { get => pdfIncludeCh3; set => Set(ref pdfIncludeCh3, value); }
    public bool PdfIncludeCh4 { get => pdfIncludeCh4; set => Set(ref pdfIncludeCh4, value); }
    public bool PdfIncludeVoltage { get => pdfIncludeVoltage; set => Set(ref pdfIncludeVoltage, value); }
    public bool PdfIncludeCurrent { get => pdfIncludeCurrent; set => Set(ref pdfIncludeCurrent, value); }
    public bool PdfIncludePower { get => pdfIncludePower; set => Set(ref pdfIncludePower, value); }
    public bool PdfIncludeMarkers { get => pdfIncludeMarkers; set => Set(ref pdfIncludeMarkers, value); }
    public bool AllOutputsOn => Channels.Count > 0 && Channels.All(c => c.OutputOn);
    public bool AnyOutputOn => Channels.Any(c => c.OutputOn);
    public string AllOutputsText => AllOutputsOn ? "ALL ON" : AnyOutputOn ? "MIXED" : "ALL OFF";

    public AsyncCommand RefreshPortsCommand { get; }
    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand AllOffCommand { get; }
    public AsyncCommand AllOutputToggleCommand { get; }
    public AsyncCommand SendCommand { get; }
    public RelayCommand ClearTerminalCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }
    public AsyncCommand StartLogCommand { get; }
    public AsyncCommand StopLogCommand { get; }
    public AsyncCommand ExportPdfCommand { get; }
    public AsyncCommand CheckForUpdatesCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }

    public MainViewModel()
    {
        settings = settingsStore.Load(); pollText = settings.PollingIntervalMs.ToString(); timeoutText = settings.SerialTimeoutMs.ToString();
        offOnDisconnect = settings.OutputsOffOnDisconnect; autoReconnect = settings.AutoReconnect; isAdvancedMode = settings.AdvancedMode; monitorCh2 = settings.MonitorCh2; monitorCh3 = settings.MonitorCh3; monitorCh4 = settings.MonitorCh4; selectedPort = settings.LastPort;
        serial.TimeoutMs = settings.SerialTimeoutMs;
        Device = new(serial); polling = new(Device) { IntervalMs = settings.PollingIntervalMs, ShouldMonitor = IsChannelMonitored }; executor = new(Device);
        Graph = new(history);
        Graph.ApplyPreferences(settings.GraphWindow, settings.GraphCh1, settings.GraphCh2, settings.GraphCh3, settings.GraphCh4, settings.GraphVoltage, settings.GraphCurrent, settings.GraphPower);
        logPath = NewLogPath();
        TrafficView = CollectionViewSource.GetDefaultView(Traffic);
        TrafficView.Filter = item => item is TrafficEntry t && (!HidePolling || !t.IsPolling);

        serial.Traffic += entry =>
        {
            if (entry.IsPolling)
            {
                if (HidePolling) return;
                // Even when polling traffic is visible, cap terminal rendering so 1 ms polling
                // cannot flood the WPF dispatcher. Device communication itself is not throttled here.
                if (trafficUiClock.ElapsedMilliseconds < 100) return;
                trafficUiClock.Restart();
            }
            Ui(() => { Traffic.Add(entry); while (Traffic.Count > 500) Traffic.RemoveAt(0); });
        };

        polling.Sample += sample =>
        {
            if (!Connected) return;

            // Preserve every acquired point off the UI thread. Only the latest channel state is
            // rendered periodically so aggressive 1 ms post-sweep polling cannot flood WPF.
            var point = new MeasurementPoint(DateTimeOffset.Now, sample.Channel, sample.Voltage, sample.Current, sample.SetVoltage, sample.CurrentLimit, sample.OutputOn);
            history.Add(point);
            logger.Add(point);
            Mil704?.RecordMeasurement(point);
            pendingSamples[sample.Channel] = sample;
            pendingPeakCurrentSamples.AddOrUpdate(sample.Channel, sample, (_, existing) => sample.Current >= existing.Current ? sample : existing);

            if (sampleUiClock.ElapsedMilliseconds >= 50 && Interlocked.Exchange(ref sampleUiQueued, 1) == 0)
            {
                sampleUiClock.Restart();
                Ui(FlushPendingSamplesToUi);
            }
        };

        polling.Metrics += metrics =>
        {
            if (metricsUiClock.ElapsedMilliseconds < 250) return;
            metricsUiClock.Restart();
            Ui(() =>
            {
                var period = metrics.EffectivePeriodMs > 0 ? $"{metrics.EffectivePeriodMs:0.0} ms" : "measuring…";
                var rate = metrics.EffectiveRateHz > 0 ? $"{metrics.EffectiveRateHz:0.0} Hz/channel" : "measuring…";
                AcquisitionStatus = $"Requested delay {metrics.RequestedDelayMs} ms  •  Sweep {metrics.SweepDurationMs:0.0} ms  •  Actual period {period}  •  Actual rate {rate}  •  Channels {metrics.MonitoredChannels}";
                var last = serial.LastResponse?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
                ConnectionHealth = $"Last response {last}  •  Timeouts {serial.TimeoutCount}  •  Errors {serial.ErrorCount}  •  Replies {serial.SuccessfulResponses}  •  Poll {rate}";
            });
        };

        polling.Faulted += ex => Ui(() => { _ = HandleLinkFaultAsync(ex); });
        executor.Progress += progress => Ui(() =>
        {
            Custom?.UpdateProgress(progress);
            Mil704?.UpdateProgress(progress);
            if (progress.State == "Applying" && progress.Requested is { } step)
            {
                AddGraphEvent($"CH{step.Channel} TEST SET {step.Voltage:0.0000} V / {step.CurrentLimit:0.0000} A", "Setpoint", step.Channel);
                AddGraphEvent($"CH{step.Channel} TEST OUTPUT {(step.OutputOn ? "ON" : "OFF")}", step.OutputOn ? "OutputOn" : "OutputOff", step.Channel);
            }
        });
        logger.Faulted += ex => Ui(() => { ReportError(ex); RefreshLogger(); });

        RefreshPortsCommand = new(RefreshPortsAsync, ReportError, () => CanChoosePort);
        ConnectCommand = new(async () =>
        {
            if (Connected) await DisconnectAsync();
            else { CancelAutoReconnect(); await ConnectAsync(false, connectionLost); }
        }, ReportError,
            () => !IsBusy && !IsRunning && !stopping && !shuttingDown && (Connected || !string.IsNullOrWhiteSpace(SelectedPort)));
        AllOffCommand = new(AllOffAsync, ReportError, () => (Connected || serial.IsConnected || IsRunning) && !stopping);
        AllOutputToggleCommand = new(ToggleAllOutputsAsync, ReportError, () => CanControl);
        SendCommand = new(async () =>
        {
            var cmd = TerminalCommand;
            await OperateAsync(async ct => { var reply = await Device.TerminalAsync(cmd, ct); Inform(reply); });
        }, ReportError, () => CanControl);
        ClearTerminalCommand = new(() => Traffic.Clear());
        SaveSettingsCommand = new(SaveSettingsAsync, ReportError, () => !IsBusy && !IsRunning && !stopping);
        StartLogCommand = new(async () =>
        {
            if (File.Exists(LogPath)) LogPath = NewLogPath();
            await logger.StartAsync(LogPath); RefreshLogger(); Inform($"CSV recording started: {Path.GetFileName(LogPath)}");
        }, ReportError, () => Connected && !logger.IsRecording);
        StopLogCommand = new(async () =>
        {
            var saved = LogPath; await logger.StopAsync(); RefreshLogger(); LogPath = NewLogPath();
            OnChanged(nameof(LogFolderPath)); Inform($"CSV saved: {saved}");
        }, ReportError, () => logger.IsRecording);
        ExportPdfCommand = new(ExportPdfAsync, ReportError, () => !IsBusy && !IsRunning);
        CheckForUpdatesCommand = new(CheckForUpdatesManualAsync, ex => { UpdateStatus = "Update check failed."; MessageBox.Show(ex.Message, "GW Studio Update", MessageBoxButton.OK, MessageBoxImage.Information); }, () => !updatePromptOpen && !IsRunning && !IsBusy);
        OpenLogsFolderCommand = new(() => LogStorageService.OpenFolder(LogFolderPath));
        Custom = new(this);
        Mil704 = new(this);
        Operating = new(this);
        Sequence = new(this);

        if (settingsStore.LoadWarning != null) ReportError(new IOException(settingsStore.LoadWarning));
    }

    public IEnumerable<QuickPreset> GetQuickPresets(int channel) => settings.QuickPresets
        .Where(p => p.Channel == channel && string.Equals(p.TargetModel, ActiveProfile.ModelName, StringComparison.OrdinalIgnoreCase))
        .Select(p => new QuickPreset { TargetModel = p.TargetModel, Channel = p.Channel, Name = p.Name, Voltage = p.Voltage, Current = p.Current });

    public async Task SaveQuickPresetsAsync(int channel, IEnumerable<QuickPreset> presets)
    {
        settings.QuickPresets.RemoveAll(p => p.Channel == channel && string.Equals(p.TargetModel, ActiveProfile.ModelName, StringComparison.OrdinalIgnoreCase));
        settings.QuickPresets.AddRange(presets.Select(p => new QuickPreset { TargetModel = ActiveProfile.ModelName, Channel = channel, Name = p.Name, Voltage = p.Voltage, Current = p.Current }));
        await settingsStore.SaveAsync(settings);
    }

    public (double Width, double Height, bool Maximized) GetWindowPreferences() => (settings.WindowWidth, settings.WindowHeight, settings.WindowMaximized);

    public void UpdateWindowPreferences(double width, double height, bool maximized)
    {
        if (double.IsFinite(width) && width >= 1080) settings.WindowWidth = width;
        if (double.IsFinite(height) && height >= 720) settings.WindowHeight = height;
        settings.WindowMaximized = maximized;
    }

    private async Task PersistUiPreferencesAsync()
    {
        settings.LastPort = SelectedPort;
        settings.GraphWindow = Graph.Window; settings.GraphCh1 = Graph.Ch1; settings.GraphCh2 = Graph.Ch2; settings.GraphCh3 = Graph.Ch3; settings.GraphCh4 = Graph.Ch4;
        settings.GraphVoltage = Graph.Voltage; settings.GraphCurrent = Graph.Current; settings.GraphPower = Graph.Power;
        await settingsStore.SaveAsync(settings);
    }

    private static string NewLogPath() => LogStorageService.NewCsvPath();

    public bool IsChannelMonitored(int channel) => channel switch
    {
        1 => true,
        2 => settings.MonitorCh2,
        3 => settings.MonitorCh3,
        4 => settings.MonitorCh4,
        _ => false
    };

    public string GetChannelModeText(int channel) => channel switch
    {
        1 => Operating.Ch1Mode,
        2 => Operating.Ch2Mode,
        _ => "Source"
    };

    public void AddGraphEvent(string note, string kind, int? channel = null) => Graph.AddSystemMarker(note, kind, channel);

    private void SetConnectionLost(bool value)
    {
        connectionLost = value;
        OnChanged(nameof(ConnectLabel));
    }

    private void CancelAutoReconnect()
    {
        try { reconnectCancellation?.Cancel(); } catch { }
        reconnectCancellation?.Dispose(); reconnectCancellation = null; reconnectTask = null;
    }

    private void StartAutoReconnect()
    {
        if (!AutoReconnect || shuttingDown || string.IsNullOrWhiteSpace(SelectedPort) || Connected) return;
        if (reconnectTask is { IsCompleted: false }) return;
        reconnectCancellation?.Dispose(); reconnectCancellation = new();
        reconnectTask = AutoReconnectLoopAsync(reconnectCancellation.Token);
    }

    private async Task AutoReconnectLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested && AutoReconnect && connectionLost && !Connected && !shuttingDown)
        {
            attempt++;
            ConnectionStatus = $"RECONNECTING • attempt {attempt}";
            IsError = false; Message = $"Connection lost. Auto reconnect attempt {attempt} on {SelectedPort}…";
            try
            {
                await Task.Delay(attempt == 1 ? 1000 : 3000, ct);
                await ConnectAsync(true, true);
                if (Connected) return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                IsError = true; Message = $"Auto reconnect attempt {attempt} failed: {ex.Message}";
                ConnectionStatus = "CONNECTION LOST • retrying";
            }
        }
    }

    private void ConfigureActiveProfile(DeviceProfile profile)
    {
        activeProfile = profile;
        Channels.Clear();
        foreach (var capability in profile.Channels) Channels.Add(new ChannelViewModel(this, capability));
        Graph.ConfigureChannels(profile.Channels.Select(c => c.Channel));
        Custom.ConfigureProfile(profile);
        Mil704.ConfigureProfile(profile);
        OnChanged(nameof(DetectedModel));
        OnChanged(nameof(DeviceSummary));
        OnChanged(nameof(HasChannel3));
        OnChanged(nameof(HasChannel4));
        OnChanged(nameof(AllOutputsOn));
        OnChanged(nameof(AnyOutputOn));
        OnChanged(nameof(AllOutputsText));
        Refresh();
    }

    private static void Ui(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.HasShutdownStarted) return;
        if (d.CheckAccess()) action(); else d.BeginInvoke(action);
    }

    private void FlushPendingSamplesToUi()
    {
        try
        {
            if (!Connected) return;

            foreach (var item in pendingSamples.ToArray())
            {
                if (!pendingSamples.TryRemove(item.Key, out var sample)) continue;
                var channelVm = Channels.FirstOrDefault(c => c.Channel == sample.Channel);
                if (channelVm == null) continue;

                channelVm.Update(sample, false);

                // Keep the highest current seen since the previous UI flush so a short spike is not
                // hidden by a later lower readback. Hardware OCP is still the proper fast protection.
                var protectionSample = pendingPeakCurrentSamples.TryRemove(item.Key, out var peak) ? peak : sample;
                if (channelVm.TryStartMaxCurrentTrip(protectionSample, out var softwareLimit))
                    _ = TripChannelForSoftwareCurrentLimitAsync(channelVm, protectionSample.Current, softwareLimit);
            }

            Graph.Tick();
            OnChanged(nameof(LogStatus));
        }
        finally
        {
            Interlocked.Exchange(ref sampleUiQueued, 0);
            // A sample may have arrived while this UI pass was running. Schedule another pass
            // without queueing one dispatcher operation per serial response.
            if (!pendingSamples.IsEmpty && sampleUiClock.ElapsedMilliseconds >= 50 && Interlocked.Exchange(ref sampleUiQueued, 1) == 0)
            {
                sampleUiClock.Restart();
                Ui(FlushPendingSamplesToUi);
            }
        }
    }

    public async Task InitializeAsync()
    {
        AppLogService.Write($"GW Studio {Version} startup");
        try { await RefreshPortsAsync(); } catch (Exception ex) { ReportError(ex); }
        _ = CheckForUpdatesBackgroundAsync();
    }

    private async Task CheckForUpdatesBackgroundAsync()
    {
        try
        {
            await Task.Delay(1800);
            var info = await updateService.CheckLatestAsync();
            if (manualUpdateCheckStarted) return;
            if (!updateService.IsUpdateAvailable(info))
            {
                Ui(() => UpdateStatus = "You are using the latest version.");
                return;
            }
            Ui(() =>
            {
                if (IsRunning || IsBusy)
                    UpdateStatus = $"New version available: v{AppUpdateService.NormalizeVersionText(info.Version)}. Check again after the active operation finishes.";
                else
                    ShowUpdatePrompt(info);
            });
        }
        catch (Exception ex)
        {
            AppLogService.Write($"update check/download error: {ex.GetType().Name}: {ex.Message}");
            Ui(() => UpdateStatus = "Automatic update check unavailable; GW Studio is running normally.");
        }
    }

    private async Task CheckForUpdatesManualAsync()
    {
        manualUpdateCheckStarted = true;
        UpdateStatus = "Checking GitHub releases…";
        try
        {
            var info = await updateService.CheckLatestAsync();
            if (!updateService.IsUpdateAvailable(info))
            {
                UpdateStatus = "You are using the latest version.";
                MessageBox.Show("You are using the latest version.", "GW Studio Update", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            UpdateStatus = $"New version available: v{AppUpdateService.NormalizeVersionText(info.Version)}";
            ShowUpdatePrompt(info);
        }
        catch (Exception ex)
        {
            UpdateStatus = "Could not check for updates.";
            AppLogService.Write($"update check/download error: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show("Could not check for updates right now. GW Studio will continue normally.\n\n" + ex.Message, "GW Studio Update", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ShowUpdatePrompt(AppUpdateInfo info)
    {
        if (updatePromptOpen) return;
        updatePromptOpen = true;
        CheckForUpdatesCommand?.Refresh();
        try
        {
            var window = new UpdatePromptWindow(updateService, info);
            if (Application.Current.MainWindow is Window owner && owner.IsVisible) window.Owner = owner;
            window.ShowDialog();
        }
        finally
        {
            updatePromptOpen = false;
            CheckForUpdatesCommand?.Refresh();
        }
    }

    private async Task RefreshPortsAsync()
    {
        var ports = await Task.Run(SerialDeviceService.GetPorts); var old = SelectedPort; Ports.Clear(); foreach (var port in ports) Ports.Add(port);
        SelectedPort = ports.Contains(old) ? old : ports.FirstOrDefault() ?? "";
        if (ports.Length == 0) Inform("No COM ports found. Connect the GPP USB cable and refresh.");
    }

    private async Task ConnectAsync(bool preserveHistory = false, bool isReconnect = false)
    {
        IsBusy = true; ConnectionStatus = "CONNECTING"; Custom.Disarm(); Mil704.Disarm();
        try
        {
            await polling.StopAsync();
            await serial.ConnectAsync(SelectedPort);
            var info = await Device.GetIdentityAsync(); connectedDeviceInfo = info; Identity = $"{info.Model}   •   {info.SerialNumber}   •   FW: {info.Firmware}";
            ConfigureActiveProfile(Device.RequireProfile());

            foreach (var channel in Channels)
            {
                if (!IsChannelMonitored(channel.Channel)) { channel.MarkStale(); continue; }
                channel.Update(await Device.ReadChannelAsync(channel.Channel, true), true);
            }
            if (!preserveHistory) history.Clear();
            Graph.ForceRefresh(); AcquisitionStatus = "Measuring acquisition rate…"; Connected = true; SetConnectionLost(false); ConnectionStatus = isReconnect ? "RECONNECTED" : "CONNECTED";
            AddGraphEvent(isReconnect ? $"RECONNECTED {SelectedPort}" : $"CONNECTED {SelectedPort}", isReconnect ? "Reconnect" : "Connect");
            try { await Operating.RefreshFromDeviceAsync(); } catch (Exception ex) { Message = "Connected; mode read warning: " + ex.Message; }
            settings.LastPort = SelectedPort;
            settings.GraphWindow = Graph.Window; settings.GraphCh1 = Graph.Ch1; settings.GraphCh2 = Graph.Ch2; settings.GraphCh3 = Graph.Ch3; settings.GraphCh4 = Graph.Ch4;
            settings.GraphVoltage = Graph.Voltage; settings.GraphCurrent = Graph.Current; settings.GraphPower = Graph.Power;
            Inform(isReconnect
                ? $"{ActiveProfile.ModelName} reconnected automatically on {SelectedPort}. Existing output states preserved."
                : $"{ActiveProfile.ModelName} detected automatically. Existing output states preserved; no output was changed during connect.");
            polling.Start();
        }
        catch
        {
            await serial.DisconnectAsync(); Connected = false;
            if (isReconnect)
            {
                SetConnectionLost(true); ConnectionStatus = "CONNECTION LOST";
                foreach (var channel in Channels) channel.MarkStale();
            }
            else
            {
                connectedDeviceInfo = null; activeProfile = null; Channels.Clear(); ConnectionStatus = "DISCONNECTED"; Identity = "No verified instrument"; SetConnectionLost(false);
                OnChanged(nameof(DetectedModel)); OnChanged(nameof(DeviceSummary)); OnChanged(nameof(HasChannel3)); OnChanged(nameof(HasChannel4));
            }
            throw;
        }
        finally { IsBusy = false; }
    }

    private async Task DisconnectAsync()
    {
        CancelAutoReconnect(); SetConnectionLost(false);
        AddGraphEvent($"DISCONNECTED {SelectedPort}", "Disconnect");
        IsBusy = true; Custom.Disarm(); Mil704.Disarm();
        try
        {
            await polling.StopAsync();
            if (settings.OutputsOffOnDisconnect) await Device.SetAllOutputsOffAsync();
            Inform(settings.OutputsOffOnDisconnect ? "Disconnected. All outputs OFF confirmed." : "Disconnected. Output states were not changed.");
        }
        finally
        {
            await serial.DisconnectAsync(); Connected = false; ConnectionStatus = "DISCONNECTED"; AcquisitionStatus = "Disconnected.";
            foreach (var ch in Channels) ch.MarkStale();
            await logger.StopAsync(); RefreshLogger(); IsBusy = false;
        }
    }

    public async Task OperateAsync(Func<CancellationToken, Task> operation)
    {
        if (!CanControl) throw new InvalidOperationException("Connect the device and stop any running test before changing settings.");
        IsBusy = true; Custom.Disarm(); Mil704.Disarm(); Mil704.Disarm(); actionCancellation?.Dispose(); actionCancellation = new();
        try { actionTask = operation(actionCancellation.Token); await actionTask; }
        finally { actionTask = null; IsBusy = false; }
    }

    public async Task RunProfileAsync(CustomTestProfile profile)
    {
        if (!CanControl) throw new InvalidOperationException("Device is not ready.");
        profile.Validate(ActiveProfile); IsRunning = true; testCancellation?.Dispose(); testCancellation = new();
        try
        {
            testTask = executor.RunAsync(profile, testCancellation.Token); await testTask;
            Inform("Profile completed. Final step output state is retained.");
        }
        catch (OperationCanceledException) { Inform("Test stopped. Involved outputs OFF confirmed."); }
        catch (Exception ex) { Custom.SetStatus("FAULT — " + ex.Message); throw; }
        finally { testTask = null; IsRunning = false; }
    }

    public async Task StopTestAsync()
    {
        testCancellation?.Cancel(); var task = testTask;
        if (task != null) try { await task; } catch (OperationCanceledException) { }
    }

    private async Task AllOffAsync()
    {
        stopping = true; Refresh(); Custom.Disarm(); Mil704.Disarm();
        try
        {
            testCancellation?.Cancel(); actionCancellation?.Cancel();
            if (testTask != null) try { await testTask; } catch { }
            if (actionTask != null) try { await actionTask; } catch { }
            await polling.StopAsync();
            await Device.SetAllOutputsOffAsync();
            foreach (var ch in Channels.Where(c => IsChannelMonitored(c.Channel))) ch.Update(await Device.ReadChannelAsync(ch.Channel, true), false);
            AddGraphEvent("ALL OUTPUTS OFF", "OutputOff");
            Inform($"ALL OUTPUT OFF — {string.Join(" / ", Channels.Select(c => c.Title))} OFF confirmed.");
        }
        finally { stopping = false; Refresh(); if (Connected && serial.IsConnected) polling.Start(); }
    }

    public async Task QuickToggleOutputAsync(int channel)
    {
        var wasRunning = IsRunning;
        stopping = true; Refresh(); Custom.Disarm(); Mil704.Disarm();
        try
        {
            testCancellation?.Cancel(); actionCancellation?.Cancel();
            if (testTask != null) try { await testTask; } catch { }
            if (actionTask != null) try { await actionTask; } catch { }
            await polling.StopAsync();
            var current = await Device.GetOutputStateAsync(channel);
            // During a running custom test the always-available quick controls are safety controls:
            // they may cancel the test and force the selected channel OFF, never unexpectedly re-enable it.
            var target = wasRunning ? false : !current;
            await Device.SetOutputStateAsync(channel, target);
            var confirmed = await Device.GetOutputStateAsync(channel);
            if (confirmed != target) throw new InvalidOperationException($"CH{channel} output state did not change as requested.");
            if (IsChannelMonitored(channel))
                Channels.First(c => c.Channel == channel).Update(await Device.ReadChannelAsync(channel, true), false);
            AddGraphEvent($"CH{channel} OUTPUT {(confirmed ? "ON" : "OFF")}", confirmed ? "OutputOn" : "OutputOff", channel);
            Inform($"CH{channel} quick output {(confirmed ? "ON" : "OFF")} confirmed.");
        }
        finally { stopping = false; Refresh(); if (Connected && serial.IsConnected) polling.Start(); }
    }

    private async Task ToggleAllOutputsAsync()
    {
        // Safety-first toggle semantics:
        // - if ANY channel is ON (including mixed states), the first press turns ALL channels OFF
        // - only when every channel is currently OFF does the next press turn ALL channels ON
        IsBusy = true; Custom.Disarm(); Mil704.Disarm();
        await polling.StopAsync();
        try
        {
            var actualStates = new List<bool>();
            foreach (var channel in ActiveProfile.Channels)
                actualStates.Add(await Device.GetOutputStateAsync(channel.Channel));
            var turnOn = actualStates.All(on => !on);
            if (turnOn) await Device.SetAllOutputsOnAsync(); else await Device.SetAllOutputsOffAsync();
            foreach (var ch in Channels.Where(c => IsChannelMonitored(c.Channel)))
                ch.Update(await Device.ReadChannelAsync(ch.Channel, true), false);
            var channelList = string.Join(" / ", Channels.Select(c => c.Title));
            AddGraphEvent(turnOn ? "ALL OUTPUTS ON" : "ALL OUTPUTS OFF", turnOn ? "OutputOn" : "OutputOff");
            Inform(turnOn ? $"ALL OUTPUTS ON — {channelList} confirmed." : $"ALL OUTPUTS OFF — {channelList} confirmed.");
        }
        finally { IsBusy = false; if (Connected && serial.IsConnected) polling.Start(); }
    }

    public void NotifyOutputStates()
    {
        OnChanged(nameof(AllOutputsOn)); OnChanged(nameof(AnyOutputOn)); OnChanged(nameof(AllOutputsText));
        AllOutputToggleCommand?.Refresh();
    }

    private async Task TripChannelForSoftwareCurrentLimitAsync(ChannelViewModel channel, double actualCurrent, double limit)
    {
        await softwareProtectionGate.WaitAsync();
        try
        {
            if (!serial.IsConnected || !Connected) return;
            stopping = true; Refresh(); Custom.Disarm(); Mil704.Disarm();

            // A running profile/action could immediately change the channel again, so cancel it first.
            testCancellation?.Cancel(); actionCancellation?.Cancel();
            if (testTask != null) try { await testTask; } catch { }
            if (actionTask != null) try { await actionTask; } catch { }

            await polling.StopAsync();
            await Device.SetOutputStateAsync(channel.Channel, false);
            var confirmed = await Device.GetOutputStateAsync(channel.Channel);
            if (confirmed) throw new InvalidOperationException($"CH{channel.Channel} software max-current protection could not confirm OUTPUT OFF.");

            var refreshed = await Device.ReadChannelAsync(channel.Channel, true);
            channel.Update(refreshed, false);
            channel.MarkMaxCurrentTripCompleted(actualCurrent, limit);
            AddGraphEvent($"CH{channel.Channel} TRIP • {actualCurrent:0.0000} A > {limit:0.0000} A", "Trip", channel.Channel);
            Inform($"CH{channel.Channel} MAX CURRENT AUTO-OFF — {actualCurrent:0.0000} A exceeded {limit:0.0000} A; output OFF confirmed.");
        }
        catch (Exception ex)
        {
            channel.MarkMaxCurrentTripFailed(ex);
            AddGraphEvent($"CH{channel.Channel} TRIP ERROR", "Error", channel.Channel);
            ReportError(ex);
        }
        finally
        {
            stopping = false; Refresh();
            if (Connected && serial.IsConnected) polling.Start();
            softwareProtectionGate.Release();
        }
    }

    private async Task HandleLinkFaultAsync(Exception ex)
    {
        IsBusy = true;
        Connected = false; SetConnectionLost(true); ConnectionStatus = "CONNECTION LOST"; NotifyOutputStates(); testCancellation?.Cancel(); actionCancellation?.Cancel(); Custom.Disarm(); Mil704.Disarm();
        AddGraphEvent($"CONNECTION LOST • {SelectedPort}", "Disconnect");
        foreach (var ch in Channels) ch.MarkStale(); ReportError(ex);
        try { await polling.StopAsync(); await serial.DisconnectAsync(); await logger.StopAsync(); }
        catch (Exception cleanup) { ReportError(cleanup); }
        finally { IsBusy = false; RefreshLogger(); StartAutoReconnect(); }
    }

    private async Task StopLoggerAfterFaultAsync() { try { await logger.StopAsync(); } catch (Exception ex) { ReportError(ex); } finally { RefreshLogger(); } }

    public void ReportError(Exception ex)
    {
        IsError = true; Message = ex.Message;
        if (Connected && !serial.IsConnected)
        {
            Connected = false; SetConnectionLost(true); ConnectionStatus = "CONNECTION LOST"; AddGraphEvent($"CONNECTION LOST • {SelectedPort}", "Disconnect");
            foreach (var ch in Channels) ch.MarkStale(); testCancellation?.Cancel(); Custom?.Disarm(); _ = StopLoggerAfterFaultAsync(); StartAutoReconnect();
        }
    }

    private void Inform(string text) { IsError = false; Message = text; }
    public void ShowInfo(string text) => Inform(text);

    private async Task SaveSettingsAsync()
    {
        int Number(string input, string label, int minimum)
        {
            var n = Numbers.Parse(input, label);
            if (n != Math.Truncate(n) || n < minimum || n > 10000)
                throw new ArgumentException($"{label}: {minimum}–10000 ms, whole numbers only.");
            return (int)n;
        }

        var interval = Number(PollText, "Polling interval", 1);
        var timeout = Number(TimeoutText, "Serial timeout", 10);

        if (Connected && (MonitorCh2 != settings.MonitorCh2 || MonitorCh3 != settings.MonitorCh3 || MonitorCh4 != settings.MonitorCh4))
            throw new InvalidOperationException("Disconnect before changing monitored channels.");

        IsBusy = true;
        try
        {
            await polling.StopAsync();
            settings.PollingIntervalMs = interval;
            settings.SerialTimeoutMs = timeout;
            settings.OutputsOffOnDisconnect = OffOnDisconnect;
            settings.AutoReconnect = AutoReconnect;
            settings.AdvancedMode = IsAdvancedMode;
            settings.MonitorCh2 = MonitorCh2;
            settings.MonitorCh3 = MonitorCh3;
            settings.MonitorCh4 = MonitorCh4;
            settings.LastPort = SelectedPort;
            settings.GraphWindow = Graph.Window; settings.GraphCh1 = Graph.Ch1; settings.GraphCh2 = Graph.Ch2; settings.GraphCh3 = Graph.Ch3; settings.GraphCh4 = Graph.Ch4;
            settings.GraphVoltage = Graph.Voltage; settings.GraphCurrent = Graph.Current; settings.GraphPower = Graph.Power;
            serial.TimeoutMs = timeout;
            polling.IntervalMs = interval;
            await settingsStore.SaveAsync(settings);
            Custom.Disarm();
            Inform("Settings saved.");
        }
        finally
        {
            if (Connected && serial.IsConnected) polling.Start();
            IsBusy = false;
        }
    }

    private async Task ExportPdfAsync()
    {
        var points = PdfRange == "Visible graph window" ? Graph.GetVisiblePoints() : history.Snapshot();
        bool IncludeChannel(int channel) => Channels.FirstOrDefault(c => c.Channel == channel)?.IncludeInReport == true;
        points = points.Where(p => IncludeChannel(p.Channel)).ToArray();
        if (points.Length == 0) throw new InvalidOperationException("No measurement samples are available for the selected PDF report range/channels.");

        var reportChannels = Channels
            .Where(c => IncludeChannel(c.Channel))
            .Select(c => new PdfReportChannel(
                c.Channel,
                c.Channel switch { 1 => Operating.Ch1Mode, 2 => Operating.Ch2Mode, _ => "Source / Independent" },
                c.OutputText,
                c.DeviceSetVoltage,
                c.DeviceCurrentLimit,
                c.ActualVoltage,
                c.ActualCurrent,
                c.ActualPower,
                c.WarningStatus,
                c.MaxCurrentProtectionStatus))
            .ToArray();

        var path = LogStorageService.NewPdfPath();
        var data = new PdfReportData(
            Version,
            DateTimeOffset.Now,
            connectedDeviceInfo,
            SelectedPort,
            ConnectionHealth,
            PdfRange,
            points,
            PdfIncludeMarkers ? Graph.GetMarkers() : [],
            reportChannels,
            Operating.Status,
            Custom.Status,
            Sequence.Status,
            PdfIncludeVoltage,
            PdfIncludeCurrent,
            PdfIncludePower,
            PdfIncludeMarkers,
            Mil704.CurrentPdfWaveformTitle,
            Mil704.GetRequestedWaveformSnapshot(),
            Mil704.GetMeasuredWaveformSnapshot());

        IsBusy = true;
        try
        {
            await pdfReports.ExportAsync(path, data);
            Inform($"PDF report exported: {path}");
        }
        finally { IsBusy = false; }
    }

    private void RefreshLogger() { OnChanged(nameof(LogStatus)); OnChanged(nameof(IsRecording)); OnChanged(nameof(LogFolderPath)); StartLogCommand?.Refresh(); StopLogCommand?.Refresh(); ExportPdfCommand?.Refresh(); }

    private void Refresh()
    {
        OnChanged(nameof(CanControl)); OnChanged(nameof(CanQuickControl)); OnChanged(nameof(CanChoosePort)); OnChanged(nameof(ConnectLabel));
        ConnectCommand?.Refresh(); RefreshPortsCommand?.Refresh(); AllOffCommand?.Refresh(); AllOutputToggleCommand?.Refresh(); SendCommand?.Refresh(); SaveSettingsCommand?.Refresh(); ExportPdfCommand?.Refresh(); CheckForUpdatesCommand?.Refresh();
        foreach (var c in Channels) c.Refresh(); Custom?.Refresh(); Mil704?.Refresh(); Operating?.Refresh(); Sequence?.Refresh(); RefreshLogger();
    }

    public async Task ShutdownAsync()
    {
        if (shuttingDown) return; shuttingDown = true; CancelAutoReconnect(); Refresh();
        try
        {
            testCancellation?.Cancel(); actionCancellation?.Cancel();
            if (testTask != null) try { await testTask; } catch (OperationCanceledException) { }
            if (actionTask != null) try { await actionTask; } catch (OperationCanceledException) { }
            await polling.StopAsync();
            if (serial.IsConnected && settings.OutputsOffOnDisconnect) await Device.SetAllOutputsOffAsync();
            await logger.StopAsync();
            await PersistUiPreferencesAsync();
            await serial.DisposeAsync();
        }
        catch
        {
            shuttingDown = false; Refresh(); throw;
        }
    }
}
