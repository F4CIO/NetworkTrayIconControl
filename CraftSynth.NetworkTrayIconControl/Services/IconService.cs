using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.NetworkInformation;

namespace CraftSynth.NetworkTrayIconControl.Services;

// Icons live in Resources/Icons/*.ico (embedded as WPF Resource items).
// Three tiers per adapter type: Disconnected (no IP) → Connected (IP, no internet) → Internet (IP + internet).
public static class IconService
{
    private const string EthernetDisconnected = "EthernetDisconnected";
    private const string EthernetConnected    = "EthernetConnected";
    private const string EthernetInternet     = "EthernetInternet";
    private const string WirelessDisconnected = "WirelessDisconnected";
    private const string WirelessConnected    = "WirelessConnected";
    private const string WirelessInternet     = "WirelessInternet";
    private const string NetworkDisconnected  = "NetworkDisconnected";
    private const string NetworkConnected     = "NetworkConnected";
    private const string NetworkInternet      = "NetworkInternet";

    private static readonly Dictionary<string, Icon> _cache = new();

    // hasIp: adapter has an IP address; hasInternet: null = unknown
    public static System.Windows.Media.ImageSource GetMenuIcon(NetworkInterfaceType type, bool hasIp, bool? hasInternet = null)
    {
        var icon = Load(ResolveName(type, hasIp, hasInternet)) ?? Load(NetworkDisconnected) ?? Load(NetworkConnected);
        if (icon == null) return null;

        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
    }

    public static Icon GetTrayIcon(NetworkInterfaceType type, bool hasIp, bool? hasInternet = null)
    {
        return Load(ResolveName(type, hasIp, hasInternet)) ?? Load(NetworkConnected) ?? SystemIcons.Application;
    }

    private static string ResolveName(NetworkInterfaceType type, bool hasIp, bool? hasInternet)
    {
        if (!hasIp)
            return type switch
            {
                NetworkInterfaceType.Wireless80211 => WirelessDisconnected,
                NetworkInterfaceType.Ethernet      => EthernetDisconnected,
                _                                  => NetworkDisconnected
            };

        bool internet = hasInternet ?? false;
        return type switch
        {
            NetworkInterfaceType.Wireless80211 => internet ? WirelessInternet : WirelessConnected,
            NetworkInterfaceType.Ethernet      => internet ? EthernetInternet : EthernetConnected,
            _                                  => internet ? NetworkInternet  : NetworkConnected
        };
    }

    private static Icon Load(string name)
    {
        if (_cache.TryGetValue(name, out var hit))
            return hit;

        Icon icon = null;
        try
        {
            var info = System.Windows.Application.GetResourceStream(
                new Uri($"pack://application:,,,/Resources/Icons/{name}.ico"));
            if (info?.Stream != null)
                icon = new Icon(info.Stream);
        }
        catch { }

        _cache[name] = icon;
        return icon;
    }
}
