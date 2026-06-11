using System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using CraftSynth.NetworkTrayIconControl.Helpers;

namespace CraftSynth.NetworkTrayIconControl.Services;

public class NetworkDevice
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsEnabled { get; set; }
    public string IpAddress { get; set; }
    public string SubnetMask { get; set; }
    public string DnsServers { get; set; }
    public long Speed { get; set; } // bits per second
    public NetworkInterfaceType Type { get; set; }
    public int InterfaceIndex { get; set; }
    public bool IsOperationallyUp { get; set; }
    public string Ssid { get; set; }
}

public class NetworkService : IDisposable
{
    private ManagementEventWatcher _adapterWatcher;
    private NetworkAddressChangedEventHandler _addressChangedHandler;
    private NetworkAvailabilityChangedEventHandler _availabilityChangedHandler;
    private bool _disposed;

    public event EventHandler NetworkChanged;

    public void StartMonitoring()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkService));

        // Monitor for adapter enable/disable and property changes (MSFT_NetAdapter)
        try
        {
            var scope = new ManagementScope(@"root\StandardCimv2");
            var adapterQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceModificationEvent " +
                "WITHIN 1 WHERE TargetInstance ISA 'MSFT_NetAdapter'");
            _adapterWatcher = new ManagementEventWatcher(scope, adapterQuery);
            _adapterWatcher.EventArrived += (s, e) => OnNetworkChanged();
            _adapterWatcher.Start();
            System.Diagnostics.Debug.WriteLine("[NetworkService] WMI adapter monitoring started");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NetworkService] Failed to start WMI monitoring: {ex.Message}");
        }

        // Monitor for IP address changes (DHCP, manual config, etc.)
        _addressChangedHandler = (s, e) => OnNetworkChanged();
        NetworkChange.NetworkAddressChanged += _addressChangedHandler;

        // Monitor for adapter up/down and general connectivity changes
        _availabilityChangedHandler = (s, e) => OnNetworkChanged();
        NetworkChange.NetworkAvailabilityChanged += _availabilityChangedHandler;

        System.Diagnostics.Debug.WriteLine("[NetworkService] Event-based monitoring started");
    }

    private void OnNetworkChanged()
    {
        System.Diagnostics.Debug.WriteLine("[NetworkService] Network change detected");
        NetworkChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopMonitoring()
    {
        _adapterWatcher?.Stop();
        _adapterWatcher?.Dispose();
        if (_addressChangedHandler != null)
        {
            NetworkChange.NetworkAddressChanged -= _addressChangedHandler;
            _addressChangedHandler = null;
        }
        if (_availabilityChangedHandler != null)
        {
            NetworkChange.NetworkAvailabilityChanged -= _availabilityChangedHandler;
            _availabilityChangedHandler = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopMonitoring();
        _disposed = true;
    }

    public List<NetworkDevice> GetNetworkDevices()
    {
        // Enumerate every adapter shown in the Network Connections folder,
        // including administratively disabled ones. NetworkInterface only
        // returns enabled adapters, so MSFT_NetAdapter (ROOT\StandardCimv2) is
        // used as the source of truth for the list and the admin enable state.
        var devices = new List<NetworkDevice>();

        using (var searcher = new ManagementObjectSearcher(
            @"root\StandardCimv2",
            "SELECT Name, InterfaceDescription, InterfaceAdminStatus, Speed, Hidden FROM MSFT_NetAdapter"))
        {
            foreach (var adapter in searcher.Get().Cast<ManagementBaseObject>())
            {
                // Skip hidden/system adapters that don't appear in Network Connections.
                if (adapter["Hidden"] is bool hidden && hidden)
                {
                    continue;
                }

                // InterfaceAdminStatus: 1 = Up (enabled), 2 = Down (disabled).
                var adminStatus = ToInt32(adapter["InterfaceAdminStatus"]);
                var description = adapter["InterfaceDescription"]?.ToString();

                devices.Add(new NetworkDevice
                {
                    Name = adapter["Name"]?.ToString(),
                    Description = description,
                    IsEnabled = adminStatus == 1,
                    Speed = ToInt64(adapter["Speed"]),
                    Type = GuessTypeFromDescription(description)
                });
            }
        }

        EnrichWithIpDetails(devices);
        EnrichWithSsid(devices);

        // Sort alphabetically only, so menu positions stay fixed when
        // adapters are enabled/disabled.
        return devices.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Adds live IP/subnet/DNS info to enabled adapters. NetworkInterface only
    // exposes enabled interfaces, so disabled ones simply keep their empty values.
    private void EnrichWithIpDetails(List<NetworkDevice> devices)
    {
        var liveInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .GroupBy(ni => ni.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            if (device.Name == null || !liveInterfaces.TryGetValue(device.Name, out var ni))
            {
                continue;
            }

            device.Type = ni.NetworkInterfaceType;
            device.IsOperationallyUp = ni.OperationalStatus == OperationalStatus.Up;
            if (device.Speed <= 0 && ni.Speed > 0)
            {
                device.Speed = ni.Speed;
            }

            try { device.InterfaceIndex = ni.GetIPProperties().GetIPv4Properties()?.Index ?? 0; }
            catch { }

            var ipProps = ni.GetIPProperties();
            // Exclude link-local (169.254.x.x) addresses — they indicate no DHCP/connection.
            var ipv4 = ipProps.UnicastAddresses
                .FirstOrDefault(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                  && !x.Address.GetAddressBytes()[0..2].SequenceEqual(new byte[] { 169, 254 }));

            if (ipv4 != null)
            {
                device.IpAddress = ipv4.Address.ToString();
                device.SubnetMask = ipv4.IPv4Mask.ToString();
                var dnsServers = string.Join(", ",
                    ipProps.DnsAddresses
                        .Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(x => x.ToString()));
                device.DnsServers = string.IsNullOrEmpty(dnsServers) ? "Not configured" : dnsServers;
            }
        }
    }

    private static NetworkInterfaceType GuessTypeFromDescription(string description)
    {
        if (string.IsNullOrEmpty(description)) return NetworkInterfaceType.Unknown;
        var d = description.ToLowerInvariant();
        if (d.Contains("wi-fi") || d.Contains("wifi") || d.Contains("wireless") ||
            d.Contains("802.11") || d.Contains("wlan"))
            return NetworkInterfaceType.Wireless80211;
        if (d.Contains("vpn") || d.Contains("tunnel") || d.Contains("tap-windows") ||
            d.Contains("ovpn") || d.Contains("openvpn") || d.Contains("wireguard") ||
            d.Contains("nordvpn") || d.Contains("surfshark") || d.Contains("expressvpn"))
            return NetworkInterfaceType.Tunnel;
        return NetworkInterfaceType.Ethernet;
    }

    private static void EnrichWithSsid(List<NetworkDevice> devices)
    {
        System.Diagnostics.Debug.WriteLine("[SSID] EnrichWithSsid via NLM dynamic");
        try
        {
            // NetworkListManager CLSID: DCB00C01-570F-4A9B-8D69-199FDBA5723B
            // Using dynamic so GetIDsOfNames resolves the correct DispId by name at runtime.
            dynamic nlm = Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")));
            System.Diagnostics.Debug.WriteLine("[SSID] NLM instance created");

            // NLM_ENUM_NETWORK_CONNECTED = 1
            dynamic networks = nlm.GetNetworks(1);
            System.Diagnostics.Debug.WriteLine("[SSID] GetNetworks returned");

            foreach (dynamic network in networks)
            {
                try
                {
                    string networkName = (string)network.GetName();
                    System.Diagnostics.Debug.WriteLine($"[SSID] Network: '{networkName}'");

                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                                 .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                                          && n.OperationalStatus == OperationalStatus.Up))
                    {
                        var device = devices.FirstOrDefault(d =>
                            string.Equals(d.Name, ni.Name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(d.Description, ni.Description, StringComparison.OrdinalIgnoreCase));
                        if (device != null)
                        {
                            device.Ssid = networkName;
                            System.Diagnostics.Debug.WriteLine($"[SSID] Assigned '{networkName}' to '{device.Name}'");
                        }
                    }
                }
                catch (Exception ex2) { System.Diagnostics.Debug.WriteLine($"[SSID] network error: {ex2.Message}"); }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SSID] NLM error: {ex.Message}"); }
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

    private static int ToInt32(object value) => value == null ? 0 : Convert.ToInt32(value);

    private static long ToInt64(object value) => value == null ? 0 : Convert.ToInt64(value);

    public NetworkDevice GetActiveDevice(List<NetworkDevice> devices = null)
    {
        devices ??= GetNetworkDevices();

        // Ask Windows which interface it routes internet traffic through.
        try
        {
            uint dest = BitConverter.ToUInt32(new byte[] { 8, 8, 8, 8 }, 0);
            if (GetBestInterface(dest, out uint bestIfIndex) == 0)
            {
                var best = devices.FirstOrDefault(x => x.InterfaceIndex == (int)bestIfIndex && x.IsEnabled);
                if (best != null) return best;
            }
        }
        catch { }

        // Fallback: first enabled adapter with an IP.
        return devices.FirstOrDefault(x => x.IsEnabled && !string.IsNullOrEmpty(x.IpAddress))
            ?? devices.FirstOrDefault(x => x.IsEnabled)
            ?? devices.FirstOrDefault();
    }

    public static bool HasInternetAccess()
    {
        try
        {
            var url = IniFile.Read("Network", "ConnectivityCheckUrl", AppConfig.DefaultConnectivityCheckUrl, AppConfig.IniPath);

            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = client.GetAsync(url).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void SetDeviceEnabled(string deviceName, bool enabled)
    {
        try
        {
            // The host process already runs elevated (see app.manifest), so netsh
            // inherits admin rights — no per-call UAC prompt is needed and we can
            // capture its output to detect failures.
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"interface set interface name=\"{deviceName}\" admin={GetAdminStatus(enabled)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                if (process == null)
                {
                    return;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"netsh failed (exit {process.ExitCode}): {output} {error}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting device: {ex.Message}");
        }
    }

    private string GetAdminStatus(bool enabled) => enabled ? "enabled" : "disabled";

    public string FormatSpeed(long speedBps)
    {
        const long kb = 1_000;
        const long mb = 1_000_000;
        const long gb = 1_000_000_000;

        return speedBps switch
        {
            >= gb => $"{speedBps / (double)gb:F2} Gbps",
            >= mb => $"{speedBps / (double)mb:F2} Mbps",
            >= kb => $"{speedBps / (double)kb:F2} Kbps",
            _ => $"{speedBps} bps"
        };
    }
}
