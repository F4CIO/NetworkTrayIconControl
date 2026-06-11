using System;
using System.Linq;
using Microsoft.Win32.TaskScheduler;

namespace CraftSynth.NetworkTrayIconControl.Helpers;

internal static class HandlerForStartup
{
    private static string ExeFilePath =>
        System.Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

    public static bool? IsRunOnStartup(string appName, bool blockErrors, bool? resultIfError)
    {
        bool? isEnabled = null;
        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(appName);
            if (task != null)
            {
                string exePath = ExeFilePath;
                var execAction = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
                if (execAction != null && execAction.Path.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine("[HandlerForStartup] Scheduled task for Run on startup is enabled and points to correct exe.");
                    isEnabled = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[HandlerForStartup] Scheduled task exists but does not point to correct exe.");
                    isEnabled = false;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[HandlerForStartup] Scheduled task for Run on startup not found.");
                isEnabled = false;
            }
        }
        catch (Exception e)
        {
            if (blockErrors)
            {
                System.Diagnostics.Debug.WriteLine($"[HandlerForStartup] Error while checking Run on startup state: {e.GetBaseException().Message}");
                isEnabled = resultIfError;
            }
            else
            {
                throw;
            }
        }
        return isEnabled;
    }

    public static bool? ChangeRunOnStartup(bool requestedState, string appName, bool blockErrors, bool? resultIfError)
    {
        bool? newState = null;
        try
        {
            string appPath = ExeFilePath;
            bool? oldState = IsRunOnStartup(appName, blockErrors, resultIfError);
            if (oldState == requestedState)
            {
                System.Diagnostics.Debug.WriteLine($"[HandlerForStartup] ChangeRunOnStartup called with {requestedState} but already in that state. No action taken.");
                return oldState;
            }

            System.Diagnostics.Debug.WriteLine($"[HandlerForStartup] Changing Run on startup from {oldState} to {requestedState}...");
            using var ts = new TaskService();
            if (requestedState)
            {
                var td = ts.NewTask();
                td.RegistrationInfo.Description = "Run CraftSynth.NetworkTrayIconControl at startup";
                td.Principal.RunLevel = TaskRunLevel.Highest;
                td.Triggers.Add(new LogonTrigger());
                td.Actions.Add(new ExecAction(appPath, null, null));
                td.Settings.DisallowStartIfOnBatteries = false;
                td.Settings.StopIfGoingOnBatteries = false;
                td.Settings.StartWhenAvailable = true;
                td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
                ts.RootFolder.RegisterTaskDefinition(appName, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
                System.Diagnostics.Debug.WriteLine("[HandlerForStartup] Scheduled task created/updated for Run on startup.");
            }
            else
            {
                ts.RootFolder.DeleteTask(appName, false);
                System.Diagnostics.Debug.WriteLine("[HandlerForStartup] Scheduled task deleted for Run on startup.");
            }

            newState = IsRunOnStartup(appName, blockErrors, resultIfError);
        }
        catch (Exception e)
        {
            if (blockErrors)
            {
                System.Diagnostics.Debug.WriteLine($"[HandlerForStartup] Error while changing Run on startup state: {e.GetBaseException().Message}");
                newState = resultIfError;
            }
            else
            {
                throw;
            }
        }
        return newState;
    }
}
