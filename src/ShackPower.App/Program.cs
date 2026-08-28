// Shack Power — cross-platform monitor for a Victron SmartShunt over VE.Direct serial.
// Copyright (C) 2026 David Erickson (AB0R)
//
// This program is free software: you can redistribute it and/or modify it under the terms of
// the GNU General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version. See LICENSE.
using Avalonia;
using ShackPower.App.Services;
using ShackPower.Core;

namespace ShackPower.App;

internal static class Program
{
    /// <summary>An interactive uninstall was requested; the UI starts only to ask and act.</summary>
    public static bool PendingUninstall { get; private set; }

    // Don't use any Avalonia/UI types before AppMain is called.
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLog.Install();   // FIRST — before install switches and before Avalonia
        var request = InstallCommandLine.Parse(args);
        try
        {
            switch (request.Action)
            {
                case InstallAction.Install:
                    var installed = InstallService.Install();
                    if (!request.Quiet) InstallService.LaunchDetached(installed.ExePath);
                    if (!installed.Registered) Environment.ExitCode = 2;   // installed but not listed
                    return;                                                 // no UI at all
                case InstallAction.Uninstall when request.Quiet:
                    // A quiet uninstall always keeps settings and logs: Windows gives the user no
                    // way to answer a dialog it did not expect, and no switch may delete history.
                    InstallService.Uninstall(new UninstallOptions(RemoveSettings: false, RemoveLogs: false));
                    return;
                case InstallAction.Uninstall:
                    PendingUninstall = true;                                // fall through to the UI
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("install", ex);
            Environment.ExitCode = 1;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
