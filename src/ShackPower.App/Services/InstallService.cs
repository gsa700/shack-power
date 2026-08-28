using System.Diagnostics;
using System.Reflection;
using System.Text;
using ShackPower.Core;

namespace ShackPower.App.Services;

/// <summary>What an uninstall should take with it besides the program itself.</summary>
/// <param name="RemoveSettings">
/// Delete <c>config.json</c> (and its <c>.bak</c>). Defaults to false at every call site:
/// settings are cheap to recreate but hold the cable's chip-serial pinning.
/// </param>
/// <param name="RemoveLogs">
/// Delete the daily power CSVs. Defaults to false everywhere, and <b>no command-line switch sets
/// it</b> — only a person answering the interactive prompt can destroy operating history (the
/// LP-100A transmission-log rule, applied to power logs).
/// </param>
public readonly record struct UninstallOptions(bool RemoveSettings, bool RemoveLogs);

/// <summary>Outcome of an install.</summary>
/// <param name="ExePath">The installed executable.</param>
/// <param name="Registered">
/// Whether the desktop registration is verifiably in place. The install itself succeeded either
/// way — but when this is false it will not appear in Settings → Apps → Installed apps, which is
/// the only route most people have to uninstall it. Worth telling the user about.
/// </param>
public readonly record struct InstallResult(string ExePath, bool Registered);

/// <summary>
/// An install could not proceed for a reason the user can act on — almost always because the
/// installed copy is still running. Carries a message meant to be shown as-is.
/// </summary>
public sealed class InstallBlockedException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Installs and removes the per-user copy of the app. Ported from W2 Monitor, the family's most
/// refined version (mode-guarded uninstall, one-reg-import registration re-asserted every
/// launch, registration audit log); the LP-100A log-protection policy is grafted on for the
/// power CSVs. The reasoning behind every non-obvious choice is documented in the siblings'
/// files and CLAUDE.mds — the short form: per-user because the in-place updater must never need
/// elevation; reg.exe because the registry APIs need a -windows TFM and this app cross-publishes
/// Linux/Pi from plain net10.0; one reg import because eleven spawns are eleven silent failure
/// modes.
/// </summary>
public static class InstallService
{
    private const string UninstallKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\ShackPower";

    /// <summary>Display name, used for the installed-apps entry and the Start Menu shortcut.</summary>
    public const string DisplayName = "Shack Power";

    private const string Description = "Monitor for a Victron SmartShunt over VE.Direct serial";

    public static string ExeFileName => OperatingSystem.IsWindows() ? "ShackPower.exe" : "ShackPower";

    /// <summary>Full path of the running executable.</summary>
    public static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine the current executable path.");

    public static string ExeDirectory => Path.GetDirectoryName(ExePath)!;

    /// <summary>
    /// Per-user programs directory: <c>%LOCALAPPDATA%\Programs</c> on Windows,
    /// <c>~/.local/share</c> on Linux.
    /// </summary>
    public static string ProgramsDirectory
    {
        get
        {
            var b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return OperatingSystem.IsWindows() ? Path.Combine(b, "Programs") : b;
        }
    }

    private static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where the menu entry goes: <c>~/.local/share/applications</c>.</summary>
    private static string DesktopFilePath =>
        Path.Combine(ProgramsDirectory, "applications", DesktopEntry.FileName);

    /// <summary>Icon path in the XDG hicolor theme, at the 256px size the embedded PNG carries.</summary>
    private static string IconFilePath => Path.Combine(
        ProgramsDirectory, "icons", "hicolor", "256x256", "apps", "shack-power.png");

    /// <summary>Convenience symlink so <c>shack-power</c> works from a terminal.</summary>
    private static string SymlinkPath =>
        Path.Combine(HomeDirectory, ".local", "bin", "shack-power");

    public static string InstallDirectory => InstallLayout.InstallDirectoryUnder(ProgramsDirectory);

    public static string InstalledExePath => Path.Combine(InstallDirectory, ExeFileName);

    /// <summary>Directories accepted as installed — the canonical one plus pre-installer hand-installs.</summary>
    public static IEnumerable<string> InstalledDirectories =>
        InstallLayout.InstalledDirectoriesUnder(ProgramsDirectory);

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", DisplayName + ".lnk");

    /// <summary>
    /// The user's desktop directory, or null if there isn't one. On Linux this comes from
    /// <c>XDG_DESKTOP_DIR</c>, never from the BCL's <c>$HOME/Desktop</c> guess — see W2's
    /// v0.7.0-beta symlink bug for why the BCL is not trusted here.
    /// </summary>
    private static string? DesktopDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return string.IsNullOrEmpty(d) ? null : d;
            }

            var conf = Path.Combine(HomeDirectory, ".config", "user-dirs.dirs");
            var dir = XdgUserDirs.Resolve(TryReadAllText(conf), XdgUserDirs.DesktopKey, HomeDirectory);

            if (dir is null)
            {
                var guess = Path.Combine(HomeDirectory, "Desktop");
                return Directory.Exists(guess) ? guess : null;
            }
            return dir;
        }
    }

    /// <summary>Desktop shortcut this installer owns.</summary>
    private static string? DesktopShortcutPath => DesktopDirectory is { } d
        ? Path.Combine(d, OperatingSystem.IsWindows() ? DisplayName + ".lnk" : DesktopEntry.FileName)
        : null;

    /// <summary>How this copy is running. Derived from its path every time — never cached or stored.</summary>
    public static InstallMode Mode => InstallLayout.Detect(
        ExeDirectory,
        File.Exists(Path.Combine(ExeDirectory, InstallLayout.PortableMarker)),
        InstallDirectory,
        InstalledDirectories);

    /// <summary>
    /// Copy this executable into the install directory and register it. Returns the path of the
    /// installed copy, which the caller should launch before exiting. Copying only the executable
    /// is sufficient: the published build is self-contained single-file, and settings/logs live
    /// in <see cref="ConfigStore.DataDir"/> either way.
    /// </summary>
    public static InstallResult Install()
    {
        Directory.CreateDirectory(InstallDirectory);

        var target = InstalledExePath;
        if (!InstallLayout.SamePath(ExeDirectory, InstallDirectory))
        {
            try
            {
                File.Copy(ExePath, target, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new InstallBlockedException(
                    $"{DisplayName} is already running from the install folder. "
                    + "Close it and try installing again.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InstallBlockedException(
                    $"Could not write to {InstallDirectory}. Check the folder's permissions.", ex);
            }
        }

        if (!OperatingSystem.IsWindows()) MakeExecutable(target);

        return new InstallResult(target, Register(target));
    }

    public static bool Register(string exePath) => Register(exePath, "install");

    /// <summary>
    /// Register, and record what happened — every path writes exactly one log line, because an
    /// attempt that leaves no trace is indistinguishable from one that never happened (the W2
    /// lesson that took a registry timestamp to diagnose).
    /// </summary>
    public static bool Register(string exePath, string trigger)
    {
        var detail = "";
        var ok = false;
        try
        {
            ok = OperatingSystem.IsWindows()
                ? RegisterWindows(exePath, out detail)
                : RegisterUnix(exePath, out detail);
        }
        catch (Exception ex)
        {
            detail = $"threw {ex.GetType().Name}: {ex.Message}";
        }
        RecordAttempt(trigger, ok, detail);
        return ok;
    }

    /// <summary>The most recent attempt this process made, or the last one on file.</summary>
    public static RegistrationAttempt? LastAttempt
    {
        get
        {
            if (_lastAttempt is not null) return _lastAttempt;
            try
            {
                if (!File.Exists(LogFilePath)) return null;
                return File.ReadAllLines(LogFilePath)
                    .Select(RegistrationLog.Parse)
                    .LastOrDefault(a => a is not null);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    private static RegistrationAttempt? _lastAttempt;

    /// <summary>Audit trail of registration attempts, beside the config it diagnoses alongside.</summary>
    public static string LogFilePath => Path.Combine(ConfigStore.DataDir, "registration.log");

    private static void RecordAttempt(string trigger, bool succeeded, string detail)
    {
        var attempt = new RegistrationAttempt(
            DateTime.UtcNow, UpdateService.CurrentVersion, trigger, succeeded, detail);
        _lastAttempt = attempt;

        try
        {
            var existing = File.Exists(LogFilePath) ? File.ReadAllLines(LogFilePath) : [];
            var kept = RegistrationLog.Tail(existing.Append(RegistrationLog.Format(attempt)));
            File.WriteAllLines(LogFilePath, kept);
        }
        catch (IOException) { /* the in-memory copy still reaches the UI */ }
        catch (UnauthorizedAccessException) { }
    }

    private static bool RegisterUnix(string exePath, out string detail)
    {
        var steps = new List<string>();
        string? icon = null;
        try
        {
            if (File.Exists(IconFilePath))
            {
                icon = IconFilePath;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IconFilePath)!);
                using var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("app-icon.png");
                if (src is not null)
                {
                    using var dst = File.Create(IconFilePath);
                    src.CopyTo(dst);
                    icon = IconFilePath;
                }
            }
        }
        catch (IOException) { /* an entry without an icon still launches */ }
        catch (UnauthorizedAccessException) { }
        if (icon is null) steps.Add("no icon");

        var wanted = DesktopEntry.Build(DisplayName, exePath, icon, Description);
        var current = TryReadAllText(DesktopFilePath);
        if (current != wanted)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesktopFilePath)!);
            File.WriteAllText(DesktopFilePath, wanted);
            steps.Add("entry rewritten");
            Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);
        }
        else
        {
            steps.Add("entry already current");
        }

        try
        {
            Symlink.Ensure(SymlinkPath, exePath);
        }
        catch (IOException ex) { steps.Add($"symlink failed: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { steps.Add($"symlink failed: {ex.Message}"); }

        steps.Add(EnsureDesktopShortcut(exePath, Path.GetDirectoryName(exePath)!));

        var ok = File.Exists(DesktopFilePath);
        if (!ok) steps.Add("no .desktop entry on disk afterwards");
        detail = string.Join("; ", steps);
        return ok;
    }

    /// <summary>
    /// Put a launcher on the desktop, unless something is already there. <b>Never overwrites</b> —
    /// an existing file at that path is the user's, and this runs at every start.
    /// </summary>
    private static string EnsureDesktopShortcut(string exePath, string workingDirectory)
    {
        if (DesktopShortcutPath is not { } shortcut) return "no desktop directory";

        try
        {
            if (File.Exists(shortcut)) return "desktop shortcut already there";

            if (OperatingSystem.IsWindows())
            {
                CreateShortcut(shortcut, exePath, workingDirectory, Description);
            }
            else
            {
                var icon = File.Exists(IconFilePath) ? IconFilePath : null;
                File.WriteAllText(shortcut, DesktopEntry.Build(DisplayName, exePath, icon, Description));
                MakeExecutable(shortcut);   // an unexecutable .desktop shows an "untrusted" prompt
            }

            return File.Exists(shortcut) ? "desktop shortcut created" : "desktop shortcut could not be created";
        }
        catch (IOException ex) { return $"desktop shortcut failed: {ex.Message}"; }
        catch (UnauthorizedAccessException ex) { return $"desktop shortcut failed: {ex.Message}"; }
    }

    private static string? TryReadAllText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Write the installed-apps entry and the Start Menu shortcut, verified and retried rather
    /// than written once and assumed (the LP-100A field failure: a reg spawn that silently
    /// doesn't take looks exactly like success).
    /// </summary>
    private static bool RegisterWindows(string exePath, out string detail)
    {
        detail = "";
        if (!OperatingSystem.IsWindows()) { detail = "not Windows"; return false; }

        var dir = Path.GetDirectoryName(exePath)!;

        var wrote = WriteUninstallEntry(exePath, dir, out var importExit);
        var verified = wrote && IsRegistered();
        var retried = false;
        if (!verified)
        {
            retried = true;
            Thread.Sleep(250);
            wrote = WriteUninstallEntry(exePath, dir, out importExit);
            verified = wrote && IsRegistered();
        }

        CreateShortcut(StartMenuShortcut, exePath, dir, Description);
        EnsureDesktopShortcut(exePath, dir);

        detail = $"reg import exit {importExit}{(retried ? ", after retry" : "")}" +
                 (verified ? "" : wrote ? ", but the verify query found no entry" : "");
        return verified;
    }

    /// <summary>
    /// The whole installed-apps entry in one <c>reg import</c> — one action that can fail loudly
    /// instead of eleven that can fail silently, and cheap enough to repeat on every launch.
    /// </summary>
    private static bool WriteUninstallEntry(string exePath, string dir, out int importExit)
    {
        importExit = -1;
        var values = new List<RegValue>
        {
            RegFile.Sz("DisplayName", DisplayName),
            RegFile.Sz("DisplayVersion", UpdateService.CurrentVersion),
            RegFile.Sz("Publisher", "David Erickson (AB0R)"),
            RegFile.Sz("DisplayIcon", exePath),
            RegFile.Sz("InstallLocation", dir),
            RegFile.Sz("URLInfoAbout", $"https://github.com/{UpdateService.Repo}"),

            // Windows gives the user no way to answer a dialog it did not expect, so the entry's
            // own button runs the quiet path — which keeps settings AND logs.
            RegFile.Sz("UninstallString", $"\"{exePath}\" --uninstall"),
            RegFile.Sz("QuietUninstallString", $"\"{exePath}\" --uninstall --quiet"),

            RegFile.Dword("NoModify", 1),
            RegFile.Dword("NoRepair", 1),
        };

        var sizeKb = FileSizeKb(exePath);
        if (sizeKb > 0) values.Add(RegFile.Dword("EstimatedSize", sizeKb));

        var file = Path.Combine(Path.GetTempPath(), "shackpower-register.reg");
        try
        {
            File.WriteAllText(file, RegFile.Build(UninstallKey, values), new UnicodeEncoding(false, true));
            importExit = Run(RegExe, ["import", file]);
            return importExit == 0;
        }
        catch (IOException) { importExit = -2; return false; }
        catch (UnauthorizedAccessException) { importExit = -3; return false; }
        finally
        {
            try { File.Delete(file); } catch { /* a leftover in temp is not worth failing over */ }
        }
    }

    /// <summary>
    /// Called at every startup. Re-asserts (never check-and-skip: the early-out is exactly how a
    /// lost entry stayed lost on W2) and adopts hand-installed copies where they stand.
    /// </summary>
    public static void EnsureRegistered(string trigger = "startup")
    {
        if (Mode != InstallMode.Installed)
        {
            RecordAttempt(trigger, false, $"skipped: mode is {Mode}, not Installed");
            return;
        }
        Register(ExePath, trigger);
    }

    /// <summary>Whether the desktop environment already knows about this copy.</summary>
    public static bool IsRegistered() => OperatingSystem.IsWindows()
        ? Run(RegExe, ["query", UninstallKey, "/v", "DisplayName"]) == 0
        : File.Exists(DesktopFilePath);

    /// <summary>
    /// Remove the registrations, then hand off to a detached helper that deletes the install
    /// directory once this process has exited. The caller must exit immediately after.
    /// </summary>
    public static void Uninstall(UninstallOptions options)
    {
        Unregister();

        var toDelete = new List<string>();

        // Only ever remove a directory the app owns: a Loose copy's directory might be Downloads
        // itself. Installed directories are private to the app; shared directories are removed
        // one named file at a time in Unregister. (W2's divergence from LP-100A, kept.)
        if (Mode == InstallMode.Installed) toDelete.Add(ExeDirectory);

        toDelete.AddRange(DataFilesToRemove(options));

        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "shackpower-uninstall.ps1");
            var lines = new List<string>
            {
                $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}",
            };
            lines.AddRange(toDelete.Select(p =>
                $"Remove-Item -LiteralPath '{p.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue"));
            lines.Add($"Remove-Item -LiteralPath '{script.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "shackpower-uninstall.sh");
            var lines = new List<string>
            {
                "#!/bin/sh",
                $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done",
            };
            lines.AddRange(toDelete.Select(p => $"rm -rf {ShellQuote(p)}"));
            lines.Add($"rm -f {ShellQuote(script)}");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            MakeExecutable(script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { script },
                UseShellExecute = false,
            });
        }
    }

    private static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Which files under the data directory an uninstall should take. The directory itself is
    /// never removed wholesale, and the logs are enumerated file by file — archives included only
    /// when the person explicitly chose to lose the history.
    /// </summary>
    public static IEnumerable<string> DataFilesToRemove(UninstallOptions options)
    {
        if (options.RemoveSettings)
        {
            var config = ConfigStore.ConfigFilePath;
            if (File.Exists(config)) yield return config;
            var bak = config + ".bak";
            if (File.Exists(bak)) yield return bak;
        }

        if (options.RemoveLogs && Directory.Exists(ConfigStore.LogDir))
        {
            foreach (var f in Directory.GetFiles(ConfigStore.LogDir, "power-*.csv"))
                yield return f;
        }
    }

    /// <summary>How many days of power logs exist — for the uninstall prompt, which states the
    /// stake in days of history rather than filenames.</summary>
    public static int CountLogDays()
    {
        try { return PowerLogReader.ListDays(ConfigStore.LogDir).Count; }
        catch { return 0; }
    }

    private static void Unregister()
    {
        if (DesktopShortcutPath is { } shortcut) TryDelete(shortcut);

        if (OperatingSystem.IsWindows())
        {
            Run(RegExe, ["delete", UninstallKey, "/f"]);
            TryDelete(StartMenuShortcut);
            return;
        }

        // Each removed as a single file. ~/.local/bin and the icon theme are shared directories:
        // nothing here may delete a directory it does not own.
        TryDelete(DesktopFilePath);
        TryDelete(IconFilePath);
        TryDelete(SymlinkPath);
        Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            // Ask the link itself as well as File.Exists: whether File.Exists follows a dangling
            // symlink varies by runtime.
            if (File.Exists(path) || Symlink.ResolveTarget(path) is not null)
                File.Delete(path);
        }
        catch (IOException) { /* a locked or vanished file is not worth failing an uninstall over */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Launch a copy of the app detached from this process. The working directory is set
    /// explicitly — inheriting this one's would pin the folder the user installed FROM.</summary>
    public static void LaunchDetached(string exePath) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true,
        });

    private static int FileSizeKb(string path)
    {
        try { return (int)(new FileInfo(path).Length / 1024); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static string RegExe => Path.Combine(Environment.SystemDirectory, "reg.exe");

    /// <summary>Run a console tool with no window and return its exit code (-1 if it wouldn't start).</summary>
    private static int Run(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    /// <summary>
    /// Write a .lnk via Windows Script Host, reached by reflection so nothing depends on the C#
    /// runtime binder in a single-file build. WSH can be disabled by policy; a missing shortcut
    /// is not worth failing an install over, so every failure here is swallowed.
    /// </summary>
    private static void CreateShortcut(string lnkPath, string target, string workingDirectory, string description)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);

            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [lnkPath]);
            if (link is null) return;

            var linkType = link.GetType();
            void Set(string property, object value) =>
                linkType.InvokeMember(property, BindingFlags.SetProperty, null, link, [value]);

            Set("TargetPath", target);
            Set("WorkingDirectory", workingDirectory);
            Set("IconLocation", target + ",0");
            Set("Description", description);
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception)
        {
            // Windows Script Host can be disabled by policy. The app is fully usable without a
            // Start Menu entry, so this must not take the install down with it.
        }
    }
}
