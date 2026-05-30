using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lantern.Application;
using Lantern.Application.Abstractions;
using Lantern.Application.Services;
using Lantern.Domain;
using Lantern.Infrastructure.Integrations.MikroTik;

namespace Lantern.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private sealed record DashboardSnapshot(
        IReadOnlyList<NetworkDevice> AllDevices,
        IReadOnlyList<NetworkDevice> FilteredDevices,
        IReadOnlyList<NetworkEvent> Timeline);

    private readonly DeviceInventoryService _inventory;
    private readonly IDeviceRepository _repository;
    private readonly ISecurityInsightService _securityInsights;
    private readonly INotificationService _notifications;
    private readonly MikroTikSettingsStore _mikroTikSettingsStore;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly IExportService _exports;
    private readonly ILocalIntegrationService _integrations;
    private readonly ICompanionDashboardService _companionDashboard;
    private readonly IVendorDatabaseService _vendorDatabase;
    private readonly ILocalSubnetService _localSubnets;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private string _searchText = string.Empty;
    private string _selectedStatusOption = "All statuses";
    private string _selectedTypeOption = "All types";
    private string _selectedVendorOption = "All vendors";
    private bool _unknownOnly;
    private bool _recentlySeenOnly;
    private bool _isScanning;
    private string _progressMessage = "Ready";
    private string _lastScanText = "No scan yet";
    private DeviceCardViewModel? _selectedDevice;
    private string _editableFriendlyName = string.Empty;
    private string _editableNotes = string.Empty;
    private string _editableLocationLabel = string.Empty;
    private bool _suppressFilterReload;
    private bool _isDarkMode;
    private DateTimeOffset _lastScanRefreshUtc = DateTimeOffset.MinValue;
    private bool _initialLoadStarted;
    private Guid? _detailsLoadedForDeviceId;
    private bool _isSettingsVisible;
    private bool _settingsLoaded;
    private bool _mikroTikEnabled;
    private string _mikroTikHost = string.Empty;
    private string _mikroTikUsername = string.Empty;
    private string _mikroTikPassword = string.Empty;
    private bool _autoScanEnabled = true;
    private int _autoScanIntervalMinutes = 15;
    private string _selectedScanProfile = ScanProfile.Home.ToString();
    private bool _companionDashboardEnabled;
    private int _companionDashboardPort = 5274;
    private string _companionDashboardStatus = "Disabled";
    private string _vendorCsvPath = string.Empty;
    private readonly DispatcherTimer _autoScanTimer;

    public MainWindowViewModel(
        DeviceInventoryService inventory,
        IDeviceRepository repository,
        ISecurityInsightService securityInsights,
        INotificationService notifications,
        MikroTikSettingsStore mikroTikSettingsStore,
        IAppSettingsStore appSettingsStore,
        IExportService exports,
        ILocalIntegrationService integrations,
        ICompanionDashboardService companionDashboard,
        IVendorDatabaseService vendorDatabase,
        ILocalSubnetService localSubnets)
    {
        _inventory = inventory;
        _repository = repository;
        _securityInsights = securityInsights;
        _notifications = notifications;
        _mikroTikSettingsStore = mikroTikSettingsStore;
        _appSettingsStore = appSettingsStore;
        _exports = exports;
        _integrations = integrations;
        _companionDashboard = companionDashboard;
        _vendorDatabase = vendorDatabase;
        _localSubnets = localSubnets;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning, ShowError);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsScanning, ShowError);
        SaveDeviceCommand = new AsyncRelayCommand(SaveSelectedDeviceAsync, () => SelectedDevice is not null, ShowError);
        ShowDashboardCommand = new RelayCommand(() => Navigate("dashboard"));
        ShowSettingsCommand = new RelayCommand(() => Navigate("settings"));
        PurgeDatabaseCommand = new AsyncRelayCommand(PurgeDatabaseAsync, onError: ShowError);
        SaveMikroTikSettingsCommand = new RelayCommand(SaveMikroTikSettings);
        ExportDevicesCommand = new AsyncRelayCommand(ExportDevicesAsync, onError: ShowError);
        ExportTimelineCommand = new AsyncRelayCommand(ExportTimelineAsync, onError: ShowError);
        ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, onError: ShowError);
        ImportVendorCsvCommand = new AsyncRelayCommand(ImportVendorCsvAsync, onError: ShowError);

        StatusOptions = ["All statuses", "Online", "Offline"];
        TypeOptions = ["All types", .. Enum.GetValues<DeviceType>().Select(DeviceCardViewModel.ToFriendlyType)];
        VendorOptions = ["All vendors"];
        ScanProfileOptions = [.. Enum.GetNames<ScanProfile>()];

        IsDarkMode = IsSystemDarkMode();
        global::Avalonia.Application.Current!.ActualThemeVariantChanged += (_, _) =>
        {
            if (SetProperty(ref _isDarkMode, IsSystemDarkMode(), nameof(IsDarkMode)))
            {
                ApplyTheme();
                ReplaceStats(Devices.Select(device => device.Device).ToArray());
                OnPropertyChanged(nameof(ThemeLabel));
                QueueSettingsSave();
            }
        };
        ApplyTheme();
        SeedEmptyDashboard();

        _autoScanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _autoScanTimer.Tick += async (_, _) =>
        {
            if (!IsScanning)
            {
                await ScanAsync();
            }
        };
        _autoScanTimer.Start();

    }

    public ObservableCollection<DeviceCardViewModel> Devices { get; } = [];
    public ObservableCollection<TimelineItemViewModel> Timeline { get; } = [];
    public ObservableCollection<SecurityInsightViewModel> SecurityInsights { get; } = [];
    public ObservableCollection<StatCardViewModel> Stats { get; } = [];
    public ObservableCollection<string> StatusOptions { get; }
    public ObservableCollection<string> TypeOptions { get; }
    public ObservableCollection<string> VendorOptions { get; }
    public ObservableCollection<string> ScanProfileOptions { get; }
    public ObservableCollection<IntegrationItemViewModel> Integrations { get; } = [];

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveDeviceCommand { get; }
    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public AsyncRelayCommand PurgeDatabaseCommand { get; }
    public RelayCommand SaveMikroTikSettingsCommand { get; }
    public AsyncRelayCommand ExportDevicesCommand { get; }
    public AsyncRelayCommand ExportTimelineCommand { get; }
    public AsyncRelayCommand ExportPdfCommand { get; }
    public AsyncRelayCommand ImportVendorCsvCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                QueueLoad();
            }
        }
    }

    public string SelectedStatusOption
    {
        get => _selectedStatusOption;
        set
        {
            if (SetProperty(ref _selectedStatusOption, value))
            {
                QueueLoad();
            }
        }
    }

    public string SelectedTypeOption
    {
        get => _selectedTypeOption;
        set
        {
            if (SetProperty(ref _selectedTypeOption, value))
            {
                QueueLoad();
            }
        }
    }

    public string SelectedVendorOption
    {
        get => _selectedVendorOption;
        set
        {
            if (SetProperty(ref _selectedVendorOption, value))
            {
                QueueLoad();
            }
        }
    }

    public bool UnknownOnly
    {
        get => _unknownOnly;
        set
        {
            if (SetProperty(ref _unknownOnly, value))
            {
                QueueLoad();
            }
        }
    }

    public bool RecentlySeenOnly
    {
        get => _recentlySeenOnly;
        set
        {
            if (SetProperty(ref _recentlySeenOnly, value))
            {
                QueueLoad();
            }
        }
    }

    public bool NotificationsEnabled
    {
        get => _notifications.IsEnabled;
        set
        {
            if (_notifications.IsEnabled != value)
            {
                _notifications.IsEnabled = value;
                OnPropertyChanged();
                QueueSettingsSave();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ScanButtonText));
                ScanCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public string LastScanText
    {
        get => _lastScanText;
        private set => SetProperty(ref _lastScanText, value);
    }

    public string ScanButtonText => IsScanning ? "Scanning..." : "Scan network";
    public string ThemeLabel => IsDarkMode ? "Dark mode" : "Light mode";
    public string OnlineSummary => $"{Stats.FirstOrDefault(stat => stat.Label == "Online")?.Value ?? "0"} online";
    public bool IsDashboardVisible => !IsSettingsVisible;
    public string VersionInfo => $"Version {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0"}";
    public string SoftwareInfo => ".NET 9, Avalonia UI, SQLite. Local-first network inventory for private LANs.";
    public string LicenseInfo => "License: MIT. Third-party dependencies keep their respective licenses.";
    public string DataInfo => "Database: local SQLite inventory stored under your user AppData folder.";
    public string MikroTikInfo => "Optional: import active DHCP lease hostnames from RouterOS API port 8728. Enable the API service on your router and use a read-only account.";
    public string PrivateSubnetSummary => _localSubnets.GetPrivateSubnets() is { Count: > 0 } subnets
        ? $"Private interfaces: {string.Join(", ", subnets)}. LANtern scans each safely and caps broad ranges to 254 addresses."
        : "No active private IPv4 interface is currently available.";
    public string CompanionDashboardStatus
    {
        get => _companionDashboardStatus;
        private set => SetProperty(ref _companionDashboardStatus, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set
        {
            if (SetProperty(ref _isSettingsVisible, value))
            {
                OnPropertyChanged(nameof(IsDashboardVisible));
            }
        }
    }

    public bool AutoScanEnabled
    {
        get => _autoScanEnabled;
        set
        {
            if (SetProperty(ref _autoScanEnabled, value))
            {
                UpdateAutoScanSchedule();
                QueueSettingsSave();
            }
        }
    }

    public int AutoScanIntervalMinutes
    {
        get => _autoScanIntervalMinutes;
        set
        {
            var clamped = Math.Clamp(value, 5, 1440);
            if (SetProperty(ref _autoScanIntervalMinutes, clamped))
            {
                UpdateAutoScanSchedule();
                QueueSettingsSave();
            }
        }
    }

    public string SelectedScanProfile
    {
        get => _selectedScanProfile;
        set
        {
            if (SetProperty(ref _selectedScanProfile, value))
            {
                QueueSettingsSave();
            }
        }
    }

    public bool CompanionDashboardEnabled
    {
        get => _companionDashboardEnabled;
        set
        {
            if (SetProperty(ref _companionDashboardEnabled, value))
            {
                QueueSettingsSave();
                _ = ApplyCompanionDashboardAsync();
            }
        }
    }

    public int CompanionDashboardPort
    {
        get => _companionDashboardPort;
        set
        {
            var clamped = Math.Clamp(value, 1024, 65535);
            if (SetProperty(ref _companionDashboardPort, clamped))
            {
                QueueSettingsSave();
                if (CompanionDashboardEnabled)
                {
                    _ = RestartCompanionDashboardAsync();
                }
            }
        }
    }

    public string VendorCsvPath
    {
        get => _vendorCsvPath;
        set
        {
            if (SetProperty(ref _vendorCsvPath, value))
            {
                QueueSettingsSave();
            }
        }
    }

    public bool MikroTikEnabled
    {
        get => _mikroTikEnabled;
        set
        {
            if (SetProperty(ref _mikroTikEnabled, value))
            {
                QueueSettingsSave();
            }
        }
    }

    public string MikroTikHost
    {
        get => _mikroTikHost;
        set
        {
            if (SetProperty(ref _mikroTikHost, value))
            {
                QueueSettingsSave();
            }
        }
    }

    public string MikroTikUsername
    {
        get => _mikroTikUsername;
        set
        {
            if (SetProperty(ref _mikroTikUsername, value))
            {
                QueueSettingsSave();
            }
        }
    }

    public string MikroTikPassword
    {
        get => _mikroTikPassword;
        set
        {
            if (SetProperty(ref _mikroTikPassword, value))
            {
                QueueSettingsSave();
            }
        }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                ApplyTheme();
                ReplaceStats(Devices.Select(device => device.Device).ToArray());
                OnPropertyChanged(nameof(ThemeLabel));
                QueueSettingsSave();
            }
        }
    }

    public DeviceCardViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice?.Id == value?.Id)
            {
                _selectedDevice = value;
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _selectedDevice, value))
            {
                EditableFriendlyName = value?.FriendlyName ?? string.Empty;
                EditableNotes = value?.Device.Notes ?? string.Empty;
                EditableLocationLabel = value?.Device.LocationLabel ?? string.Empty;
                _detailsLoadedForDeviceId = null;
                SaveDeviceCommand.RaiseCanExecuteChanged();
                _ = LoadSelectedDetailsAsync();
            }
        }
    }

    public string EditableFriendlyName
    {
        get => _editableFriendlyName;
        set => SetProperty(ref _editableFriendlyName, value);
    }

    public string EditableNotes
    {
        get => _editableNotes;
        set => SetProperty(ref _editableNotes, value);
    }

    public string EditableLocationLabel
    {
        get => _editableLocationLabel;
        set => SetProperty(ref _editableLocationLabel, value);
    }

    public void BeginInitialLoad()
    {
        if (_initialLoadStarted)
        {
            return;
        }

        _initialLoadStarted = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await LoadSettingsAsync().ConfigureAwait(false);
                await LoadAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }
        }, DispatcherPriority.ContextIdle);
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            var result = await Task.Run(async () =>
            {
                var observedDevices = new HashSet<Guid>();
                var newEvents = 0;

                await foreach (var update in _inventory.StreamScanAsync(Lantern.Application.ScanProfileOptions.Create(ParseScanProfile()), default).ConfigureAwait(false))
                {
                    await RunOnUiAsync(() =>
                    {
                        ProgressMessage = update.Total is > 0
                            ? $"{update.Message} ({update.Completed ?? 0}/{update.Total})"
                            : update.Message;
                    }).ConfigureAwait(false);

                    if (update.Event is not null)
                    {
                        newEvents++;
                    }

                    if (update.InventoryDevice is not null)
                    {
                        observedDevices.Add(update.InventoryDevice.Id);
                    }

                    if (update.UpdateType is NetworkScanUpdateType.KnownDeviceLoaded
                        or NetworkScanUpdateType.DeviceUpdated
                        or NetworkScanUpdateType.DeviceEnriched
                        or NetworkScanUpdateType.DeviceClassified
                        or NetworkScanUpdateType.ScanCompleted)
                    {
                        await LoadDuringScanAsync(update.UpdateType == NetworkScanUpdateType.ScanCompleted).ConfigureAwait(false);
                    }
                }

                return (ObservedDeviceCount: observedDevices.Count, NewEvents: newEvents);
            });

            LastScanText = $"Last scan found {result.ObservedDeviceCount} devices";
            ProgressMessage = result.NewEvents == 0 ? "No major changes found" : $"{result.NewEvents} changes found";
            await LoadAsync();
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task LoadDuringScanAsync(bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastScanRefreshUtc < TimeSpan.FromSeconds(1.25))
        {
            return;
        }

        _lastScanRefreshUtc = now;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var filter = BuildFilter();
        var previousSelectedId = SelectedDevice?.Id;

        await _loadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await Task.Run(async () =>
            {
                var allDevices = await _repository.GetDevicesAsync().ConfigureAwait(false);
                var devices = await _inventory.GetDevicesAsync(filter).ConfigureAwait(false);
                var timeline = await _inventory.GetTimelineAsync(take: 50).ConfigureAwait(false);
                return new DashboardSnapshot(allDevices, devices, timeline);
            }).ConfigureAwait(false);

            await RunOnUiAsync(() => ApplySnapshot(snapshot, previousSelectedId), DispatcherPriority.Background).ConfigureAwait(false);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private void ApplySnapshot(DashboardSnapshot snapshot, Guid? previousSelectedId)
    {
        MergeDeviceCards(snapshot.FilteredDevices);

        ReplaceStats(snapshot.AllDevices);
        OnPropertyChanged(nameof(OnlineSummary));

        if (!IsScanning)
        {
            RebuildVendorOptions(snapshot.AllDevices);
        }

        var nextSelectedDevice = Devices.FirstOrDefault(device => device.Id == previousSelectedId) ?? Devices.FirstOrDefault();
        if (_selectedDevice?.Id == nextSelectedDevice?.Id)
        {
            _selectedDevice = nextSelectedDevice;
            OnPropertyChanged(nameof(SelectedDevice));
        }
        else
        {
            SelectedDevice = nextSelectedDevice;
        }

        MergeTimeline(snapshot.Timeline);
    }

    private async Task LoadTimelineAsync()
    {
        var events = await _inventory.GetTimelineAsync(take: 50);
        Timeline.Clear();
        foreach (var networkEvent in events)
        {
            Timeline.Add(new TimelineItemViewModel(networkEvent));
        }
    }

    private async Task LoadSelectedDetailsAsync()
    {
        var selectedDevice = SelectedDevice;
        if (selectedDevice is null)
        {
            _detailsLoadedForDeviceId = null;
            await RunOnUiAsync(SecurityInsights.Clear, DispatcherPriority.Background).ConfigureAwait(false);
            await RunOnUiAsync(Integrations.Clear, DispatcherPriority.Background).ConfigureAwait(false);
            return;
        }

        if (_detailsLoadedForDeviceId == selectedDevice.Id)
        {
            return;
        }

        var insights = await Task.Run(async () =>
        {
            var ports = await _repository.GetOpenPortsAsync(selectedDevice.Id).ConfigureAwait(false);
            var integrationItems = await _integrations.InspectAsync(selectedDevice.Id).ConfigureAwait(false);
            return (
                Insights: _securityInsights.BuildInsights(selectedDevice.Device, ports)
                .Select(insight => new SecurityInsightViewModel(insight))
                .ToArray(),
                Integrations: integrationItems.Select(summary => new IntegrationItemViewModel(summary)).ToArray());
        }).ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            if (SelectedDevice?.Id != selectedDevice.Id)
            {
                return;
            }

            SecurityInsights.Clear();
            foreach (var insight in insights.Insights)
            {
                SecurityInsights.Add(insight);
            }
            Integrations.Clear();
            foreach (var integration in insights.Integrations)
            {
                Integrations.Add(integration);
            }

            _detailsLoadedForDeviceId = selectedDevice.Id;
        }, DispatcherPriority.Background).ConfigureAwait(false);
    }

    private async Task SaveSelectedDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var deviceId = SelectedDevice.Id;
        var friendlyName = EditableFriendlyName;
        var notes = EditableNotes;
        var locationLabel = EditableLocationLabel;

        await Task.Run(async () =>
        {
            await _inventory.RenameDeviceAsync(deviceId, friendlyName).ConfigureAwait(false);
            await _inventory.SaveNotesAsync(deviceId, notes).ConfigureAwait(false);
            await _inventory.SaveLocationLabelAsync(deviceId, locationLabel).ConfigureAwait(false);
        }).ConfigureAwait(false);
        await LoadAsync();
    }

    private async Task PurgeDatabaseAsync()
    {
        await Task.Run(() => _repository.PurgeAsync()).ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            Devices.Clear();
            Timeline.Clear();
            SecurityInsights.Clear();
            SelectedDevice = null;
            EditableFriendlyName = string.Empty;
            EditableNotes = string.Empty;
            EditableLocationLabel = string.Empty;
            LastScanText = "Database purged";
            ProgressMessage = "All remembered devices and timeline events were deleted.";
            SeedEmptyDashboard();
        }).ConfigureAwait(false);
    }

    private void SaveMikroTikSettings()
    {
        ApplyMikroTikSettings();
        QueueSettingsSave();
        ProgressMessage = MikroTikEnabled
            ? "MikroTik settings saved. Active DHCP lease hostnames will be imported during scans."
            : "MikroTik hostname import disabled.";
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _appSettingsStore.LoadAsync().ConfigureAwait(false);

        await RunOnUiAsync(() =>
        {
            _settingsLoaded = false;

            _notifications.IsEnabled = settings.NotificationsEnabled;
            OnPropertyChanged(nameof(NotificationsEnabled));

            if (settings.DarkModeEnabled is { } darkModeEnabled)
            {
                _isDarkMode = darkModeEnabled;
                OnPropertyChanged(nameof(IsDarkMode));
                ApplyTheme();
                ReplaceStats(Devices.Select(device => device.Device).ToArray());
                OnPropertyChanged(nameof(ThemeLabel));
            }

            _mikroTikEnabled = settings.MikroTikEnabled;
            _mikroTikHost = settings.MikroTikHost;
            _mikroTikUsername = settings.MikroTikUsername;
            _mikroTikPassword = settings.MikroTikPassword;
            OnPropertyChanged(nameof(MikroTikEnabled));
            OnPropertyChanged(nameof(MikroTikHost));
            OnPropertyChanged(nameof(MikroTikUsername));
            OnPropertyChanged(nameof(MikroTikPassword));
            ApplyMikroTikSettings();

            _autoScanEnabled = settings.AutoScanEnabled;
            _autoScanIntervalMinutes = Math.Clamp(settings.AutoScanIntervalMinutes, 5, 1440);
            _selectedScanProfile = settings.ScanProfile.ToString();
            _companionDashboardEnabled = settings.CompanionDashboardEnabled;
            _companionDashboardPort = Math.Clamp(settings.CompanionDashboardPort, 1024, 65535);
            _vendorCsvPath = settings.VendorCsvPath;
            OnPropertyChanged(nameof(AutoScanEnabled));
            OnPropertyChanged(nameof(AutoScanIntervalMinutes));
            OnPropertyChanged(nameof(SelectedScanProfile));
            OnPropertyChanged(nameof(CompanionDashboardEnabled));
            OnPropertyChanged(nameof(CompanionDashboardPort));
            OnPropertyChanged(nameof(VendorCsvPath));
            UpdateAutoScanSchedule();

            _settingsLoaded = true;
        }).ConfigureAwait(false);
        await ApplyCompanionDashboardAsync().ConfigureAwait(false);
    }

    private void QueueSettingsSave()
    {
        if (!_settingsLoaded)
        {
            return;
        }

        ApplyMikroTikSettings();
        var settings = new AppSettings
        {
            NotificationsEnabled = NotificationsEnabled,
            DarkModeEnabled = IsDarkMode,
            AutoScanEnabled = AutoScanEnabled,
            AutoScanIntervalMinutes = AutoScanIntervalMinutes,
            ScanProfile = ParseScanProfile(),
            CompanionDashboardEnabled = CompanionDashboardEnabled,
            CompanionDashboardPort = CompanionDashboardPort,
            VendorCsvPath = VendorCsvPath,
            MikroTikEnabled = MikroTikEnabled,
            MikroTikHost = MikroTikHost,
            MikroTikUsername = MikroTikUsername,
            MikroTikPassword = MikroTikPassword
        };

        _ = Task.Run(() => _appSettingsStore.SaveAsync(settings));
    }

    private void ApplyMikroTikSettings()
    {
        _mikroTikSettingsStore.Current.IsEnabled = MikroTikEnabled;
        _mikroTikSettingsStore.Current.Host = MikroTikHost.Trim();
        _mikroTikSettingsStore.Current.Username = MikroTikUsername.Trim();
        _mikroTikSettingsStore.Current.Password = MikroTikPassword;
    }

    private void Navigate(string page)
    {
        IsSettingsVisible = page == "settings";
        OnPropertyChanged(nameof(IsDashboardVisible));
    }

    private void UpdateAutoScanSchedule()
    {
        _autoScanTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(AutoScanIntervalMinutes, 5, 1440));
        if (AutoScanEnabled)
        {
            _autoScanTimer.Start();
        }
        else
        {
            _autoScanTimer.Stop();
        }
    }

    private async Task ApplyCompanionDashboardAsync()
    {
        try
        {
            if (CompanionDashboardEnabled)
            {
                await _companionDashboard.StartAsync(CompanionDashboardPort).ConfigureAwait(false);
            }
            else
            {
                await _companionDashboard.StopAsync().ConfigureAwait(false);
            }

            await RunOnUiAsync(() => CompanionDashboardStatus = _companionDashboard.IsRunning
                ? $"Read-only companion: {_companionDashboard.Address}"
                : "Disabled").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RunOnUiAsync(() =>
            {
                CompanionDashboardStatus = $"Could not start companion dashboard: {exception.Message}";
                _companionDashboardEnabled = false;
                OnPropertyChanged(nameof(CompanionDashboardEnabled));
            }).ConfigureAwait(false);
        }
    }

    private async Task RestartCompanionDashboardAsync()
    {
        await _companionDashboard.StopAsync().ConfigureAwait(false);
        await ApplyCompanionDashboardAsync().ConfigureAwait(false);
    }

    private async Task ExportDevicesAsync()
    {
        var path = await _exports.ExportDevicesCsvAsync().ConfigureAwait(false);
        await RunOnUiAsync(() => ProgressMessage = $"Device CSV saved to {path}").ConfigureAwait(false);
    }

    private async Task ExportTimelineAsync()
    {
        var path = await _exports.ExportTimelineCsvAsync().ConfigureAwait(false);
        await RunOnUiAsync(() => ProgressMessage = $"Timeline CSV saved to {path}").ConfigureAwait(false);
    }

    private async Task ExportPdfAsync()
    {
        var path = await _exports.ExportPdfReportAsync().ConfigureAwait(false);
        await RunOnUiAsync(() => ProgressMessage = $"PDF report saved to {path}").ConfigureAwait(false);
    }

    private async Task ImportVendorCsvAsync()
    {
        await _vendorDatabase.ImportAsync(VendorCsvPath).ConfigureAwait(false);
        await RunOnUiAsync(() => ProgressMessage = "Vendor database imported. The next scan will use the updated OUI data.").ConfigureAwait(false);
    }

    private ScanProfile ParseScanProfile()
        => Enum.TryParse<ScanProfile>(SelectedScanProfile, out var profile) ? profile : ScanProfile.Home;

    private DeviceFilter BuildFilter()
        => new(
            SearchText,
            SelectedStatusOption switch
            {
                "Online" => DeviceStatus.Online,
                "Offline" => DeviceStatus.Offline,
                _ => null
            },
            ParseDeviceType(SelectedTypeOption),
            SelectedVendorOption == "All vendors" ? null : SelectedVendorOption,
            UnknownOnly,
            RecentlySeenOnly);

    private static DeviceType? ParseDeviceType(string value)
    {
        if (value == "All types")
        {
            return null;
        }

        return Enum.GetValues<DeviceType>().FirstOrDefault(type => DeviceCardViewModel.ToFriendlyType(type) == value);
    }

    private void RebuildVendorOptions(IReadOnlyCollection<NetworkDevice> devices)
    {
        var selected = SelectedVendorOption;
        VendorOptions.Clear();
        VendorOptions.Add("All vendors");
        foreach (var vendor in devices.Select(device => device.Vendor).Where(vendor => !string.IsNullOrWhiteSpace(vendor)).Distinct().Order())
        {
            VendorOptions.Add(vendor!);
        }

        _suppressFilterReload = true;
        try
        {
            SelectedVendorOption = VendorOptions.Contains(selected) ? selected : "All vendors";
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    private void QueueLoad()
    {
        if (!_suppressFilterReload)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await LoadAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    ShowError(exception);
                }
            }, DispatcherPriority.Background);
        }
    }

    private void ShowError(Exception exception)
    {
        _ = RunOnUiAsync(() => ProgressMessage = exception.Message);
    }

    private void ApplyTheme()
    {
        var application = global::Avalonia.Application.Current;
        if (application is null)
        {
            return;
        }

        application.RequestedThemeVariant = ThemeVariant.Default;

        SetResource("AppBackgroundBrush", IsDarkMode ? "#0B1220" : "#F6F7FB");
        SetResource("CardBackgroundBrush", IsDarkMode ? "#111A2C" : "#FFFFFF");
        SetResource("PrimaryTextBrush", IsDarkMode ? "#F8FAFC" : "#14213D");
        SetResource("SecondaryTextBrush", IsDarkMode ? "#AAB6C8" : "#667085");
        SetResource("SubtlePanelBrush", IsDarkMode ? "#172B4D" : "#EEF4FF");
        SetResource("SubtlePanelTextBrush", IsDarkMode ? "#B9D6FE" : "#175CD3");
        SetResource("InputBackgroundBrush", IsDarkMode ? "#0F172A" : "#FFFFFF");

        void SetResource(string key, string color)
        {
            application.Resources[key] = SolidColorBrush.Parse(color);
        }
    }

    private static bool IsSystemDarkMode()
        => global::Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    private void SeedEmptyDashboard()
    {
        Stats.Clear();
        foreach (var stat in BuildStats([]))
        {
            Stats.Add(stat);
        }
    }

    private void MergeDeviceCards(IReadOnlyList<NetworkDevice> devices)
    {
        var desired = devices.Select(device => new DeviceCardViewModel(device)).ToArray();
        var desiredIds = desired.Select(device => device.Id).ToHashSet();

        for (var index = Devices.Count - 1; index >= 0; index--)
        {
            if (!desiredIds.Contains(Devices[index].Id))
            {
                Devices.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Length; index++)
        {
            var existingIndex = IndexOfDevice(desired[index].Id);
            if (existingIndex < 0)
            {
                Devices.Insert(Math.Min(index, Devices.Count), desired[index]);
                continue;
            }

            if (existingIndex != index && index < Devices.Count)
            {
                Devices.Move(existingIndex, index);
            }

            if (!DeviceCardsEquivalent(Devices[index], desired[index]))
            {
                Devices[index] = desired[index];
            }
        }
    }

    private void ReplaceStats(IReadOnlyCollection<NetworkDevice> allDevices)
    {
        var desired = BuildStats(allDevices);

        for (var index = 0; index < desired.Count; index++)
        {
            if (Stats.Count <= index)
            {
                Stats.Add(desired[index]);
            }
            else if (Stats[index] != desired[index])
            {
                Stats[index] = desired[index];
            }
        }

        while (Stats.Count > desired.Count)
        {
            Stats.RemoveAt(Stats.Count - 1);
        }
    }

    private void MergeTimeline(IReadOnlyList<NetworkEvent> events)
    {
        var desired = events.Select(networkEvent => new TimelineItemViewModel(networkEvent)).ToArray();
        if (Timeline.Count == desired.Length && Timeline.Zip(desired).All(pair => pair.First.Id == pair.Second.Id))
        {
            return;
        }

        Timeline.Clear();
        foreach (var item in desired)
        {
            Timeline.Add(item);
        }
    }

    private int IndexOfDevice(Guid id)
    {
        for (var index = 0; index < Devices.Count; index++)
        {
            if (Devices[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool DeviceCardsEquivalent(DeviceCardViewModel left, DeviceCardViewModel right)
        => left.Id == right.Id
            && left.FriendlyName == right.FriendlyName
            && left.IpAddress == right.IpAddress
            && left.MacAddress == right.MacAddress
            && left.Hostname == right.Hostname
            && left.HostnameLabel == right.HostnameLabel
            && left.Vendor == right.Vendor
            && left.DeviceTypeText == right.DeviceTypeText
            && left.StatusText == right.StatusText
            && left.LocationLabel == right.LocationLabel
            && left.ClassificationExplanation == right.ClassificationExplanation;

    private IReadOnlyList<StatCardViewModel> BuildStats(IReadOnlyCollection<NetworkDevice> allDevices)
        =>
        [
            new("Total", allDevices.Count.ToString(), IsDarkMode ? "#F8FAFC" : "#14213D"),
            new("Online", allDevices.Count(device => device.Status == DeviceStatus.Online).ToString(), IsDarkMode ? "#32D583" : "#12B76A"),
            new("Offline", allDevices.Count(device => device.Status == DeviceStatus.Offline).ToString(), IsDarkMode ? "#CBD5E1" : "#667085"),
            new("New", allDevices.Count(device => DateTimeOffset.UtcNow - device.FirstSeenUtc < TimeSpan.FromHours(24)).ToString(), IsDarkMode ? "#84CAFF" : "#2E90FA"),
            new("Unknown", allDevices.Count(device => device.DeviceType == DeviceType.UnknownDevice).ToString(), IsDarkMode ? "#FDB022" : "#F79009")
        ];

    private static Task RunOnUiAsync(Action action, DispatcherPriority priority = default)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action, priority == default ? DispatcherPriority.Normal : priority).GetTask();
    }
}
