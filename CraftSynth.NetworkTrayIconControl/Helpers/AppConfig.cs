using System;
using System.IO;

namespace CraftSynth.NetworkTrayIconControl.Helpers;

public static class AppConfig
{
    public static readonly string IniPath = Path.Combine(AppContext.BaseDirectory, "CraftSynth.NetworkTrayIconControl.ini");

    public const string DefaultConnectivityCheckUrl = "http://www.msftconnecttest.com/connecttest.txt";
    public const bool DefaultKeepOnlySingleAdapterEnabled = true;

    private static readonly string DefaultIniContent =
        "[Network]" + Environment.NewLine +
        "; URL used to verify internet access (same endpoint Windows NCSI uses)." + Environment.NewLine +
        "; Must return HTTP 200 to be considered reachable." + Environment.NewLine +
        $"ConnectivityCheckUrl={DefaultConnectivityCheckUrl}" + Environment.NewLine +
        Environment.NewLine +
        "[UI]" + Environment.NewLine +
        "; When true, enabling an adapter from the tray menu disables all other adapters." + Environment.NewLine +
        $"KeepOnlySingleAdapterEnabled={(DefaultKeepOnlySingleAdapterEnabled ? "true" : "false")}" + Environment.NewLine;

    public static void EnsureIniFileExists()
    {
        if (File.Exists(IniPath))
            return;

        try
        {
            File.WriteAllText(IniPath, DefaultIniContent);
        }
        catch (Exception ex)
        {
            // Install dir may be read-only; the app still works on in-code defaults.
            System.Diagnostics.Debug.WriteLine($"Could not create default ini file: {ex.Message}");
        }
    }
}
