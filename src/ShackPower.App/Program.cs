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
    /// <summary>An interactive uninstall was requested; the UI starts only to ask and act (Phase 6).</summary>
    public static bool PendingUninstall { get; private set; }

    // Don't use any Avalonia/UI types before AppMain is called.
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLog.Install();   // FIRST — before install switches and before Avalonia
        var request = InstallCommandLine.Parse(args);
        switch (request.Action)
        {
            case InstallAction.Install:
            case InstallAction.Uninstall:
                // InstallService arrives in Phase 6; refusing beats pretending.
                Console.Error.WriteLine("Install/uninstall isn't wired up yet in this build.");
                Environment.ExitCode = 1;
                return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
