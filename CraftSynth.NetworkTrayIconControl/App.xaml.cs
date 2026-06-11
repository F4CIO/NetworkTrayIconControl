using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using CraftSynth.NetworkTrayIconControl.Helpers;
using CraftSynth.NetworkTrayIconControl.Views;

namespace CraftSynth.NetworkTrayIconControl;

public partial class App : Application
{
    private Views.TrayIconView _trayIconView;

    protected override void OnStartup(StartupEventArgs e)
    {
        // The app.manifest requests requireAdministrator, so Windows normally
        // launches us elevated. This is a safety net for cases where the manifest
        // is bypassed (e.g. launched via `dotnet run`): re-launch elevated and exit.
        if (!IsRunningElevated())
        {
            if (TryRestartElevated())
            {
                Shutdown();
                return;
            }
            // Elevation declined/failed — toggling devices will not work, but let
            // the app keep running so the user can still see status and exit.
        }

        base.OnStartup(e);

        // The app runs fine on in-code defaults without an ini file, but create
        // one with the default values so users have something to edit.
        AppConfig.EnsureIniFileExists();

        _trayIconView = new Views.TrayIconView();
        _trayIconView.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }

    private static bool IsRunningElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryRestartElevated()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas", // triggers the UAC elevation prompt
                WorkingDirectory = Environment.CurrentDirectory
            });
            return true;
        }
        catch (Exception ex)
        {
            // Most commonly thrown when the user dismisses the UAC prompt.
            Debug.WriteLine($"Elevation failed: {ex.Message}");
            return false;
        }
    }
}
