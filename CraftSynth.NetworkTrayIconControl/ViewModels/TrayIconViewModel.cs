using System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Input;
using CraftSynth.NetworkTrayIconControl.Helpers;
using CraftSynth.NetworkTrayIconControl.Services;

namespace CraftSynth.NetworkTrayIconControl.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Predicate<object> _canExecute;

    public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object parameter) => _execute(parameter);
}

public class NetworkDeviceViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    public string Name { get; set; }
    public string Description { get; set; }
    public string DisplayName => $"{Name} ({Description})";
    public ICommand ToggleCommand { get; }

    public NetworkDeviceViewModel(Action<string> onToggle)
    {
        ToggleCommand = new RelayCommand(_ => onToggle(Name));
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    private string _ipAddress;
    public string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (_ipAddress != value)
            {
                _ipAddress = value;
                OnPropertyChanged();
            }
        }
    }

    private string _subnetMask;
    public string SubnetMask
    {
        get => _subnetMask;
        set
        {
            if (_subnetMask != value)
            {
                _subnetMask = value;
                OnPropertyChanged();
            }
        }
    }

    private string _dnsServers;
    public string DnsServers
    {
        get => _dnsServers;
        set
        {
            if (_dnsServers != value)
            {
                _dnsServers = value;
                OnPropertyChanged();
            }
        }
    }

    private string _speed;
    public string Speed
    {
        get => _speed;
        set
        {
            if (_speed != value)
            {
                _speed = value;
                OnPropertyChanged();
            }
        }
    }

    private string _type;
    public string Type
    {
        get => _type;
        set
        {
            if (_type != value)
            {
                _type = value;
                OnPropertyChanged();
            }
        }
    }

    private string _ssid;
    public string Ssid
    {
        get => _ssid;
        set
        {
            if (_ssid != value)
            {
                _ssid = value;
                OnPropertyChanged();
                // Also notify TypeLabel which depends on Ssid
                OnPropertyChanged(nameof(TypeLabel));
            }
        }
    }

    public string TypeLabel => GetTypeLabel();

    private string GetTypeLabel()
    {
        if (InterfaceType == NetworkInterfaceType.Wireless80211)
            return string.IsNullOrEmpty(Ssid) ? "Wi-Fi" : $"Wi-Fi ({Ssid})";

        return InterfaceType switch
        {
            NetworkInterfaceType.Ethernet => "Ethernet",
            NetworkInterfaceType.Ppp => "PPP",
            NetworkInterfaceType.Tunnel => "VPN",
            NetworkInterfaceType.Loopback => "Loopback",
            _ => "Network"
        };
    }

    private NetworkInterfaceType _interfaceType;
    public NetworkInterfaceType InterfaceType
    {
        get => _interfaceType;
        set
        {
            if (_interfaceType != value)
            {
                _interfaceType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeLabel));
            }
        }
    }

    private bool _isOperationallyUp;
    public bool IsOperationallyUp
    {
        get => _isOperationallyUp;
        set
        {
            if (_isOperationallyUp != value)
            {
                _isOperationallyUp = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class TrayIconViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly string IniPath = AppConfig.IniPath;

    private string _tooltipText;
    private System.Drawing.Icon _currentIcon;
    private ObservableCollection<NetworkDeviceViewModel> _devices;
    private bool _keepOnlySingleAdapterEnabled;
    private readonly NetworkService _networkService;
    private CancellationTokenSource _debounceCts;
    private CancellationTokenSource _verificationCts;
    private CancellationTokenSource _ssidRefreshCts;
    private readonly object _scheduleLock = new();
    private bool? _lastKnownHasInternet;
    private bool _disposed;
    private bool _mouseOverIcon;
    private System.Threading.Timer _mouseLeaveTimer;
    private const int DebounceDelayMs = 1500;
    private const int VerificationDelayMs = 5000;
    private const int SsidRefreshDelayMs = 10000;
    private const int MouseLeaveTimeoutMs = 300;

    public string TooltipText
    {
        get => _tooltipText;
        set
        {
            if (_tooltipText != value)
            {
                _tooltipText = value;
                OnPropertyChanged();
            }
        }
    }

    public System.Drawing.Icon CurrentIcon
    {
        get => _currentIcon;
        private set
        {
            if (_currentIcon != value)
            {
                _currentIcon = value;
                OnPropertyChanged();
            }
        }
    }

    public bool? HasInternet { get; private set; }

    public bool KeepOnlySingleAdapterEnabled
    {
        get => _keepOnlySingleAdapterEnabled;
        set
        {
            if (_keepOnlySingleAdapterEnabled != value)
            {
                _keepOnlySingleAdapterEnabled = value;
                OnPropertyChanged();
                IniFile.Write("UI", "KeepOnlySingleAdapterEnabled", value ? "true" : "false", IniPath);
            }
        }
    }

    public ObservableCollection<NetworkDeviceViewModel> Devices
    {
        get => _devices;
        set
        {
            if (_devices != value)
            {
                _devices = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenNetworkSettingsCommand { get; }
    public ICommand OpenIpv4PropertiesCommand { get; }
    public ICommand DisableAllCommand { get; }
    public ICommand RestartAdapterCommand { get; }

    public TrayIconViewModel()
    {
        _networkService = new NetworkService();
        Devices = new ObservableCollection<NetworkDeviceViewModel>();
        _keepOnlySingleAdapterEnabled = IniFile.Read("UI", "KeepOnlySingleAdapterEnabled",
                AppConfig.DefaultKeepOnlySingleAdapterEnabled ? "true" : "false", IniPath)
            .Equals("true", StringComparison.OrdinalIgnoreCase);

        RefreshCommand = new RelayCommand(_ => RefreshDevices());
        ExitCommand = new RelayCommand(_ => System.Windows.Application.Current.Shutdown());
        OpenNetworkSettingsCommand = new RelayCommand(_ => OpenNetworkSettings());
        OpenIpv4PropertiesCommand = new RelayCommand(_ => OpenIpv4Properties());
        DisableAllCommand = new RelayCommand(_ => DisableAllDevices());
        RestartAdapterCommand = new RelayCommand(_ => RestartAdapter());

        // Fast initial refresh without internet check to avoid blocking the UI thread on startup.
        RefreshDevices(checkInternet: false);

        _mouseLeaveTimer = new System.Threading.Timer(_ => _mouseOverIcon = false);

        // Set up event-based monitoring instead of polling timer
        _networkService.NetworkChanged += (s, e) => ScheduleRefresh();
        _networkService.StartMonitoring();

        System.Diagnostics.Debug.WriteLine("[TrayIconViewModel] Initialized with event-based monitoring");
    }

    public void OnMouseMove()
    {
        if (!_mouseOverIcon)
        {
            _mouseOverIcon = true;
            Task.Run(() => RefreshDevices());
        }
        _mouseLeaveTimer.Change(MouseLeaveTimeoutMs, System.Threading.Timeout.Infinite);
    }

    private void ScheduleRefresh()
    {
        CancellationTokenSource cts;
        lock (_scheduleLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }
        Task.Delay(DebounceDelayMs, cts.Token)
            .ContinueWith(_ => RefreshDevices(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }

    private void RefreshDevices(bool checkInternet = true, bool scheduleVerification = true)
    {
        if (_disposed) return;

        var devices = _networkService.GetNetworkDevices();
        var activeDevice = _networkService.GetActiveDevice(devices);
        if (checkInternet)
            _lastKnownHasInternet = NetworkService.HasInternetAccess();
        bool? hasInternet = _lastKnownHasInternet;

        // Pending refresh continuations can outlive the app: once shutdown starts,
        // Application.Current becomes null, so bail instead of dereferencing it.
        var app = System.Windows.Application.Current;
        if (_disposed || app == null || app.Dispatcher.HasShutdownStarted) return;

        try
        {
            app.Dispatcher.Invoke(() =>
            {
                // Set before Devices.Clear() so RebuildDeviceItems (deferred at Background priority) reads the updated value.
                HasInternet = hasInternet;

                // Beep when debugger is attached (for monitoring UI updates)
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    System.Console.Beep(800, 50); // 800 Hz, 50ms
                }

                Devices.Clear();
                foreach (var device in devices)
                {
                    var vmDevice = new NetworkDeviceViewModel(ToggleDevice)
                    {
                        Name = device.Name,
                        Description = device.Description,
                        IsEnabled = device.IsEnabled,
                        IpAddress = device.IpAddress,
                        SubnetMask = device.SubnetMask,
                        DnsServers = device.DnsServers,
                        Speed = _networkService.FormatSpeed(device.Speed),
                        Type = device.Type.ToString(),
                        Ssid = device.Ssid,
                        InterfaceType = device.Type,
                        IsOperationallyUp = device.IsOperationallyUp,
                        IsActive = device.Name == activeDevice?.Name
                    };

                    // Debug: Log SSID assignment for troubleshooting
                    if (!string.IsNullOrEmpty(device.Ssid))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TrayIconViewModel] Device '{device.Name}' assigned SSID: '{device.Ssid}'");
                    }

                    Devices.Add(vmDevice);
                }

                UpdateTooltip(activeDevice, hasInternet);
                UpdateIcon(activeDevice, hasInternet);
            });
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            // Dispatcher shut down between the check above and the Invoke — app is exiting.
            return;
        }

        // Schedule a verification check after 5 seconds to ensure UI reflects the correct state
        // This catches any missed events and provides a safety net
        // NOTE: scheduleVerification=false when called from verification itself to prevent infinite recursion
        if (scheduleVerification)
        {
            ScheduleVerificationCheck();
        }
    }

    private void ScheduleVerificationCheck()
    {
        CancellationTokenSource cts;
        lock (_scheduleLock)
        {
            _verificationCts?.Cancel();
            _verificationCts?.Dispose();
            _verificationCts = new CancellationTokenSource();
            cts = _verificationCts;
        }
        Task.Delay(VerificationDelayMs, cts.Token)
            .ContinueWith(_ =>
            {
                System.Diagnostics.Debug.WriteLine("[TrayIconViewModel] Verification check: ensuring UI state is correct");
                RefreshDevices(checkInternet: true, scheduleVerification: false);
            },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

        System.Diagnostics.Debug.WriteLine("[TrayIconViewModel] Verification check scheduled in 5 seconds");
        ScheduleSsidRefresh();
    }

    private void ScheduleSsidRefresh()
    {
        CancellationTokenSource cts;
        lock (_scheduleLock)
        {
            _ssidRefreshCts?.Cancel();
            _ssidRefreshCts?.Dispose();
            _ssidRefreshCts = new CancellationTokenSource();
            cts = _ssidRefreshCts;
        }
        Task.Delay(SsidRefreshDelayMs, cts.Token)
            .ContinueWith(_ =>
            {
                System.Diagnostics.Debug.WriteLine("[TrayIconViewModel] SSID refresh: updating Wi-Fi network names");
                RefreshDevices(checkInternet: false, scheduleVerification: false);
            },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

        System.Diagnostics.Debug.WriteLine("[TrayIconViewModel] SSID refresh scheduled in 10 seconds");
    }

    private static string GetTypeLabel(NetworkDevice device) => device.Type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet      => "Ethernet",
        NetworkInterfaceType.Ppp           => "PPP",
        NetworkInterfaceType.Tunnel        => "Tunnel",
        _                                  => "Network"
    };

    private static string GetAdapterNameLine(NetworkDevice device)
    {
        var ssid = !string.IsNullOrEmpty(device.Ssid) ? $" ({device.Ssid})" : "";
        return $"{device.Name}{ssid}";
    }

    private void UpdateTooltip(NetworkDevice device, bool? hasInternet)
    {
        if (device == null || !device.IsEnabled)
        {
            TooltipText = "Not connected\nNo network access";
            return;
        }

        if (string.IsNullOrEmpty(device.IpAddress))
        {
            var lines = new List<string> { GetAdapterNameLine(device), GetTypeLabel(device), "Connected, no IP" };
            if (hasInternet.HasValue)
                lines.Add(hasInternet.Value ? "Internet access" : "No internet access");
            TooltipText = string.Join("\n", lines);
            return;
        }

        var tooltipLines = new List<string>
        {
            $"{GetAdapterNameLine(device)}",
            $"Type: {GetTypeLabel(device)}",
            $"IPv4: {device.IpAddress}"
        };

        if (!string.IsNullOrEmpty(device.SubnetMask))
            tooltipLines.Add($"Mask: {device.SubnetMask}");

        if (!string.IsNullOrEmpty(device.DnsServers))
            tooltipLines.Add($"DNS: {device.DnsServers}");

        if (device.Speed > 0)
            tooltipLines.Add($"Speed: {_networkService.FormatSpeed(device.Speed)}");

        if (hasInternet.HasValue)
            tooltipLines.Add(hasInternet.Value ? "Internet access" : "No internet access");

        TooltipText = string.Join("\n", tooltipLines);
    }

    private void UpdateIcon(NetworkDevice device, bool? hasInternet)
    {
        bool hasIp = device != null && device.IsEnabled && !string.IsNullOrEmpty(device.IpAddress);
        var type = device?.Type ?? NetworkInterfaceType.Unknown;
        CurrentIcon = IconService.GetTrayIcon(type, hasIp, hasInternet);
    }

    private void ToggleDevice(string deviceName)
    {
        // Snapshot UI-thread state before going off-thread.
        bool singleMode = _keepOnlySingleAdapterEnabled;
        string[] allNames = singleMode ? Devices.Select(d => d.Name).ToArray() : null;
        var device = Devices.FirstOrDefault(d => d.Name == deviceName);
        if (device == null) return;
        bool targetEnabled = !device.IsEnabled;

        TooltipText = "Switching adapter...";

        Task.Run(() =>
        {
            // In single mode clicking a disabled adapter enables only it; clicking
            // the enabled adapter disables it, leaving none enabled.
            if (singleMode && allNames != null)
                foreach (var name in allNames)
                    _networkService.SetDeviceEnabled(name, name == deviceName && targetEnabled);
            else
                _networkService.SetDeviceEnabled(deviceName, targetEnabled);

            RefreshDevices();
        });
    }

    private void DisableAllDevices()
    {
        string[] enabledNames = Devices.Where(d => d.IsEnabled).Select(d => d.Name).ToArray();
        if (enabledNames.Length == 0) return;

        TooltipText = "Disabling all adapters...";

        Task.Run(() =>
        {
            foreach (var name in enabledNames)
                _networkService.SetDeviceEnabled(name, false);

            RefreshDevices();
        });
    }

    private void RestartAdapter()
    {
        // Restart the active (bolded) adapter; fall back to any enabled one.
        var device = Devices.FirstOrDefault(d => d.IsActive && d.IsEnabled)
            ?? Devices.FirstOrDefault(d => d.IsEnabled);
        if (device == null) return;
        string name = device.Name;

        TooltipText = "Restarting adapter...";

        Task.Run(() =>
        {
            _networkService.SetDeviceEnabled(name, false);
            // Give the adapter a moment to fully release before re-enabling.
            Thread.Sleep(1000);
            _networkService.SetDeviceEnabled(name, true);

            RefreshDevices();
        });
    }

    private void OpenNetworkSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ncpa.cpl",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening network settings: {ex.Message}");
        }
    }

    private void OpenIpv4Properties()
    {
        var activeDevice = Devices.FirstOrDefault(d => d.IsActive);
        if (activeDevice == null)
        {
            OpenNetworkSettings();
            return;
        }

        if (!OpenAdapterProperties(activeDevice.Name))
        {
            OpenNetworkSettings();
            return;
        }

        Task.Run(() => AutomateIpv4PropertiesClick(activeDevice.Name));
    }

    private bool OpenAdapterProperties(string adapterName)
    {
        try
        {
            const string networkConnectionsGuid = "shell:::{7007ACC7-3202-11D1-AAD2-00805FC1270E}";
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            dynamic folder = shell.NameSpace(networkConnectionsGuid);
            if (folder == null) return false;

            foreach (dynamic item in folder.Items())
            {
                if (!string.Equals((string)item.Name, adapterName, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (dynamic verb in item.Verbs())
                {
                    if (((string)verb.Name).Replace("&", "").Trim().Equals("Properties", StringComparison.OrdinalIgnoreCase))
                    {
                        verb.DoIt();
                        return true;
                    }
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening adapter properties: {ex.Message}");
            return false;
        }
    }

    private void AutomateIpv4PropertiesClick(string adapterName)
    {
        try
        {
            // Poll for the adapter Properties dialog (up to 5 seconds)
            string exactTitle = $"{adapterName} Properties";
            AutomationElement propWindow = null;

            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(250);
                propWindow = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.NameProperty, exactTitle));
                if (propWindow != null) break;

                // Broader fallback: title contains both adapter name and "Properties"
                if (i >= 3)
                {
                    var all = AutomationElement.RootElement.FindAll(
                        TreeScope.Children,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
                    foreach (AutomationElement el in all)
                    {
                        var n = el.Current.Name;
                        if (n.Contains(adapterName, StringComparison.OrdinalIgnoreCase) &&
                            n.Contains("Properties", StringComparison.OrdinalIgnoreCase))
                        {
                            propWindow = el;
                            break;
                        }
                    }
                    if (propWindow != null) break;
                }
            }

            if (propWindow == null) return;

            // Bring dialog to foreground so mouse clicks land on it
            SetForegroundWindow(new IntPtr(propWindow.Current.NativeWindowHandle));
            Thread.Sleep(100);

            // Find the network components list
            var list = propWindow.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
            if (list == null) return;

            // Find the IPv4 item
            var ipv4Item = list.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.NameProperty, "Internet Protocol Version 4 (TCP/IPv4)"));
            if (ipv4Item == null) return;

            // Double-click the item — same as what the user would do to open its properties
            var rect = ipv4Item.Current.BoundingRectangle;
            int cx = (int)(rect.Left + rect.Width / 2);
            int cy = (int)(rect.Top + rect.Height / 2);

            SetCursorPos(cx, cy);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error automating IPv4 properties: {ex.Message}");
        }
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP   = 0x0004;

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_scheduleLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _verificationCts?.Cancel();
            _verificationCts?.Dispose();
            _ssidRefreshCts?.Cancel();
            _ssidRefreshCts?.Dispose();
        }
        _mouseLeaveTimer?.Dispose();
        _networkService?.StopMonitoring();
        _networkService?.Dispose();
        _disposed = true;
    }
}
