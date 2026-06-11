using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CraftSynth.NetworkTrayIconControl.Helpers;
using CraftSynth.NetworkTrayIconControl.Services;
using CraftSynth.NetworkTrayIconControl.ViewModels;

namespace CraftSynth.NetworkTrayIconControl.Views;

public partial class TrayIconView : Window
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private TrayIconViewModel _viewModel;
    private AboutWindow _aboutWindow;
    private readonly ContextMenu _contextMenu = new();
    private bool _rebuildPending;
    private bool _rebuildNeededAfterClose;
    private MenuItem _wifiSettingsItem;
    private bool _wifiSettingsInMenu;
    private MenuItem _disableAllItem;
    private MenuItem _restartAdapterItem;

    // ItemsBefore is dynamic: 7 normally, 8 when the Wi-Fi settings item is also present.
    private int ItemsBefore => _wifiSettingsInMenu ? 8 : 7;
    private const int ItemsAfter = 5;

    public TrayIconView()
    {
        InitializeComponent();
        _viewModel = new TrayIconViewModel();
        DataContext = _viewModel;

        BuildContextMenu();
        _viewModel.Devices.CollectionChanged += OnDevicesChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        TrayIcon.TrayMouseMove += (_, _) => _viewModel.OnMouseMove();
        TrayIcon.ContextMenu = _contextMenu;

        // Apply the icon that was set during the ViewModel's initial refresh.
        if (_viewModel.CurrentIcon != null)
            TrayIcon.Icon = _viewModel.CurrentIcon;

        // Ensure proper cleanup when window closes
        Closing += (s, e) => _viewModel?.Dispose();
    }

    private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayIconViewModel.CurrentIcon) && _viewModel.CurrentIcon != null)
            TrayIcon.Icon = _viewModel.CurrentIcon;
    }

    private void BuildContextMenu()
    {
        var about = new MenuItem { Header = "About..." };
        about.Click += (_, _) =>
        {
            if (_aboutWindow != null)
            {
                _aboutWindow.Activate();
                return;
            }
            try
            {
                _aboutWindow = new AboutWindow();
                _aboutWindow.Closed += (_, _) => _aboutWindow = null;
                _aboutWindow.Show();
            }
            catch (Exception ex)
            {
                _aboutWindow = null;
                System.Diagnostics.Debug.WriteLine($"Error showing About window: {ex.Message}");
            }
        };
        _contextMenu.Items.Add(about);

        var keepSingle = new MenuItem { Header = "Keep only single adapter enabled", IsCheckable = true };
        keepSingle.IsChecked = _viewModel.KeepOnlySingleAdapterEnabled;
        keepSingle.Click += (_, _) => _viewModel.KeepOnlySingleAdapterEnabled = keepSingle.IsChecked;
        _contextMenu.Items.Add(keepSingle);

        var runOnStartup = new MenuItem
        {
            Header = "Run on startup",
            IsCheckable = true,
            IsChecked = HandlerForStartup.IsRunOnStartup("CraftSynth.NetworkTrayIconControl", true, false) == true
        };
        runOnStartup.Click += (_, _) =>
        {
            bool? actualState = HandlerForStartup.ChangeRunOnStartup(runOnStartup.IsChecked, "CraftSynth.NetworkTrayIconControl", true, false);
            runOnStartup.IsChecked = actualState == true;
        };
        _contextMenu.Items.Add(runOnStartup);
        _contextMenu.Items.Add(new Separator());

        var openNc = new MenuItem { Header = "Open network nonnections..." };
        openNc.Click += (_, _) =>
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
                System.Diagnostics.Debug.WriteLine($"Error opening network connections: {ex.Message}");
            }
        };
        _contextMenu.Items.Add(openNc);

        var openIpv4 = new MenuItem { Header = "Open adapter properties..." };
        openIpv4.Click += (_, _) => _viewModel.OpenIpv4PropertiesCommand.Execute(null);
        _contextMenu.Items.Add(openIpv4);

        _wifiSettingsItem = new MenuItem { Header = "Select Wi-Fi network..." };
        _wifiSettingsItem.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-availablenetworks:",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening Wi-Fi settings: {ex.Message}");
            }
        };

        _contextMenu.Items.Add(new Separator());

        // Device items are inserted here (indices ItemsBefore .. Count - ItemsAfter - 1).

        _contextMenu.Items.Add(new Separator());

        _disableAllItem = new MenuItem { Header = "Disable all" };
        _disableAllItem.Click += (_, _) => _viewModel.DisableAllCommand.Execute(null);
        _contextMenu.Items.Add(_disableAllItem);

        _restartAdapterItem = new MenuItem { Header = "Restart adapter" };
        _restartAdapterItem.Click += (_, _) => _viewModel.RestartAdapterCommand.Execute(null);
        _contextMenu.Items.Add(_restartAdapterItem);

        _contextMenu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Application.Current.Shutdown();
        _contextMenu.Items.Add(exit);

        _contextMenu.Closed += (_, _) =>
        {
            if (_rebuildNeededAfterClose)
            {
                _rebuildNeededAfterClose = false;
                RebuildDeviceItems();
            }
        };

        _contextMenu.Opened += OnContextMenuOpened;

        RebuildDeviceItems();
    }

    private void OnDevicesChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // Debounce: CollectionChanged fires once per Clear() and once per Add(),
        // so batch all changes into a single rebuild after they settle.
        if (_rebuildPending) return;
        _rebuildPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildPending = false;
            if (_contextMenu.IsOpen)
                _rebuildNeededAfterClose = true;
            else
                RebuildDeviceItems();
        }, DispatcherPriority.Background);
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        GetCursorPos(out POINT cursor);

        // Convert physical pixels to device-independent pixels
        double dipX = cursor.X, dipY = cursor.Y;
        var ps = PresentationSource.FromVisual(this);
        if (ps?.CompositionTarget != null)
        {
            var m = ps.CompositionTarget.TransformFromDevice;
            dipX = cursor.X * m.M11;
            dipY = cursor.Y * m.M22;
        }

        var workArea = SystemParameters.WorkArea;
        double x = Math.Max(workArea.Left, Math.Min(dipX, workArea.Right - menu.ActualWidth));
        double y = workArea.Bottom - menu.ActualHeight;

        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = x;
        menu.VerticalOffset = y;
    }

    private void RebuildDeviceItems()
    {
        try
        {
            // Show Wi-Fi settings item only when at least one Wi-Fi adapter is enabled.
            bool hasEnabledWifi = _viewModel.Devices.Any(d =>
                d.InterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 && d.IsEnabled);
            if (hasEnabledWifi && !_wifiSettingsInMenu)
            {
                _contextMenu.Items.Insert(6, _wifiSettingsItem);
                _wifiSettingsInMenu = true;
            }
            else if (!hasEnabledWifi && _wifiSettingsInMenu)
            {
                _contextMenu.Items.Remove(_wifiSettingsItem);
                _wifiSettingsInMenu = false;
            }

            // Disable all/Restart adapter only make sense with an enabled adapter.
            bool hasEnabledDevice = _viewModel.Devices.Any(d => d.IsEnabled);
            _disableAllItem.IsEnabled = hasEnabledDevice;
            _restartAdapterItem.IsEnabled = hasEnabledDevice;

            // Remove previous device items, leaving the fixed items intact.
            while (_contextMenu.Items.Count > ItemsBefore + ItemsAfter)
                _contextMenu.Items.RemoveAt(ItemsBefore);

            // Insert a MenuItem for each device at the device section.
            int insertAt = ItemsBefore;
            // Create a local copy to avoid collection changed issues during enumeration
            var devices = _viewModel.Devices.ToList();

            foreach (var device in devices)
            {
                try
                {
                    var header = new StackPanel { Orientation = Orientation.Horizontal };
                    bool isConnected = device.IsOperationallyUp && !string.IsNullOrEmpty(device.IpAddress);
                    bool? hasInternet = device.IsActive ? _viewModel.HasInternet : (bool?)null;
                    var imageSource = IconService.GetMenuIcon(device.InterfaceType, isConnected, hasInternet);
                    if (imageSource != null)
                        header.Children.Add(new System.Windows.Controls.Image
                        {
                            Source = imageSource,
                            Width = 16,
                            Height = 16,
                            Margin = new Thickness(0, 0, 6, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    var status = isConnected ? "Connected" : "Disconnected";
                    string displayText = $"{device.DisplayName} - {status}";

                    // Append SSID for connected WiFi devices
                    if (isConnected && device.InterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 
                        && !string.IsNullOrEmpty(device.Ssid))
                    {
                        displayText = $"{displayText} ({device.Ssid})";
                    }

                    header.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = displayText,
                        FontWeight = device.IsActive ? FontWeights.Bold : FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var item = new MenuItem
                    {
                        Header = header,
                        IsCheckable = true,
                        IsChecked = device.IsEnabled,
                        Command = device.ToggleCommand
                    };
                    _contextMenu.Items.Insert(insertAt++, item);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error building device menu item for {device?.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in RebuildDeviceItems: {ex.Message}");
        }
    }
}
