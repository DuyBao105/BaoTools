using System.Diagnostics;
using System.IO;
using BaoToolsGui.Models;
using BaoToolsGui.Services.Downloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaoToolsGui.Services;

/// <summary>
/// Status information about a game's installation directory, executable, and applied fixes.
/// </summary>
public record GameLaunchInfo(
    string FolderPath,
    string? ExecutablePath,
    bool ExeFound,
    bool HasOnlineFix,
    bool HasDenuvoFix,
    bool RequiresSteam);

/// <summary>
/// Service to locate game binaries, check for Online / Denuvo fixes, and launch games reliably.
/// </summary>
public class GameLauncherService
{
    private readonly SteamService _steam;
    private readonly SettingsService? _settings;
    private readonly IServiceProvider? _services;
    private readonly ILogger<GameLauncherService>? _log;

    // Folders to avoid scanning deeply or entirely
    private static readonly HashSet<string> ExcludedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".luatools-fix", "_CommonRedist", "DirectX", "Redist",
        "Support", "Prerequisites", "Installers", "Engine\\Binaries\\DotNET"
    };

    // Substrings in executable names that indicate non-game helpers / redistributables
    private static readonly string[] ExcludedExeSubstrings =
    [
        "unitycrashhandler",
        "crashreport",
        "crashreporter",
        "werfault",
        "vcredist",
        "dxsetup",
        "ue4prereq",
        "ue5prereq",
        "prereqsetup",
        "dotnetfx",
        "vc_redist",
        "oalinst",
        "uninstall",
        "unins000",
        "unins001",
        "steam_api",
        "baotools",
        "depotdownloader"
    ];

    public GameLauncherService(
        SteamService steam,
        SettingsService? settings = null,
        IServiceProvider? services = null,
        ILogger<GameLauncherService>? log = null)
    {
        _steam = steam;
        _settings = settings;
        _services = services;
        _log = log;
    }

    /// <summary>
    /// Inspect a folder to find its main game executable and identify if Online Fix or Denuvo Fix are active.
    /// </summary>
    public GameLaunchInfo InspectFolder(string folderPath, string? gameName = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return new GameLaunchInfo(folderPath, null, false, false, false, false);
        }

        string? exe = FindGameExecutable(folderPath, gameName);
        bool hasOnlineFix = IsOnlineFixPresent(folderPath);
        bool hasDenuvoFix = IsDenuvoFixPresent(folderPath);
        bool requiresSteam = hasOnlineFix;

        return new GameLaunchInfo(
            folderPath,
            exe,
            exe is not null && File.Exists(exe),
            hasOnlineFix,
            hasDenuvoFix,
            requiresSteam);
    }

    /// <summary>
    /// Scan a directory and pick the best executable file to run.
    /// Uses scoring heuristic: prioritizes root and Binaries/Win64, shipping binaries, matching names, and file size.
    /// </summary>
    public string? FindGameExecutable(string folderPath, string? gameName = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return null;

        try
        {
            var candidates = new List<(string Path, int Score)>();
            string rootDirName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Enumerate .exe files up to 4 directory levels deep
            var allExes = EnumerateCandidateFiles(folderPath, maxDepth: 4);

            foreach (string exePath in allExes)
            {
                string fileName = Path.GetFileName(exePath);
                string fileNameNoExt = Path.GetFileNameWithoutExtension(exePath);

                // Quick filter for redists and crash tools
                if (IsExcludedExe(fileName)) continue;

                int score = CalculateScore(exePath, fileName, fileNameNoExt, folderPath, rootDirName, gameName);
                if (score > 0)
                {
                    candidates.Add((exePath, score));
                }
            }

            if (candidates.Count == 0) return null;

            // Sort by score descending; if tied, by file size descending
            return candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => SafeGetFileSize(c.Path))
                .First().Path;
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Failed scanning for game executable in {Path}", folderPath);
            return null;
        }
    }

    private static int CalculateScore(
        string fullPath, string fileName, string fileNameNoExt,
        string rootFolder, string rootDirName, string? gameName)
    {
        int score = 50; // base score

        string relative = Path.GetRelativePath(rootFolder, fullPath);
        string relativeDir = Path.GetDirectoryName(relative) ?? "";
        long size = SafeGetFileSize(fullPath);

        // Heavy positive boost for Unreal Engine Shipping binaries
        if (fileName.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Shipping.exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
        }

        // Location scoring
        if (string.IsNullOrEmpty(relativeDir))
        {
            // Root folder
            score += 80;
        }
        else if (relativeDir.Equals(@"Binaries\Win64", StringComparison.OrdinalIgnoreCase) ||
                 relativeDir.Equals("Binaries/Win64", StringComparison.OrdinalIgnoreCase) ||
                 relativeDir.Equals(@"bin\x64", StringComparison.OrdinalIgnoreCase) ||
                 relativeDir.Equals("bin/x64", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (relativeDir.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                 relativeDir.StartsWith("game", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        // Matching game name or folder name
        if (!string.IsNullOrWhiteSpace(gameName))
        {
            string cleanGameName = CleanAlphanumeric(gameName);
            string cleanFileName = CleanAlphanumeric(fileNameNoExt);

            if (cleanFileName.Equals(cleanGameName, StringComparison.OrdinalIgnoreCase))
                score += 90;
            else if (cleanFileName.Contains(cleanGameName, StringComparison.OrdinalIgnoreCase) ||
                     cleanGameName.Contains(cleanFileName, StringComparison.OrdinalIgnoreCase))
                score += 50;
        }

        if (!string.IsNullOrWhiteSpace(rootDirName))
        {
            string cleanDir = CleanAlphanumeric(rootDirName);
            string cleanFileName = CleanAlphanumeric(fileNameNoExt);

            if (cleanFileName.Equals(cleanDir, StringComparison.OrdinalIgnoreCase))
                score += 70;
            else if (cleanFileName.Contains(cleanDir, StringComparison.OrdinalIgnoreCase))
                score += 40;
        }

        // File size bonus: real game binaries are typically large (> 10 MB)
        if (size > 50 * 1024 * 1024) score += 40;
        else if (size > 10 * 1024 * 1024) score += 25;
        else if (size > 2 * 1024 * 1024) score += 10;
        else if (size < 200 * 1024) score -= 30; // very small files (< 200 KB) are rarely the actual game

        // Penalize launchers/updaters/anticheat unless nothing else exists
        if (fileNameNoExt.Contains("launcher", StringComparison.OrdinalIgnoreCase)) score -= 20;
        if (fileNameNoExt.Contains("config", StringComparison.OrdinalIgnoreCase) ||
            fileNameNoExt.Contains("setting", StringComparison.OrdinalIgnoreCase)) score -= 40;
        if (fileNameNoExt.Contains("easyanticheat", StringComparison.OrdinalIgnoreCase) ||
            fileNameNoExt.Contains("start_protected_game", StringComparison.OrdinalIgnoreCase)) score -= 20;

        return score;
    }

    public static bool IsExcludedExe(string fileName)
    {
        foreach (string sub in ExcludedExeSubstrings)
        {
            if (fileName.Contains(sub, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root, int maxDepth)
    {
        var result = new List<string>();
        WalkDirectory(new DirectoryInfo(root), 0, maxDepth, result);
        return result;
    }

    private static void WalkDirectory(DirectoryInfo dir, int currentDepth, int maxDepth, List<string> result)
    {
        if (currentDepth > maxDepth || !dir.Exists) return;

        // Skip excluded directories
        if (ExcludedFolderNames.Contains(dir.Name)) return;

        try
        {
            foreach (var fi in dir.EnumerateFiles("*.exe"))
            {
                result.Add(fi.FullName);
            }
        }
        catch { /* skip inaccessible files */ }

        if (currentDepth < maxDepth)
        {
            try
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    WalkDirectory(sub, currentDepth + 1, maxDepth, result);
                }
            }
            catch { /* skip inaccessible directories */ }
        }
    }

    private static long SafeGetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string CleanAlphanumeric(string input)
    {
        var chars = input.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Check whether an Online Fix (e.g. from online-fix.me) is present in the folder.
    /// Usually manifests as OnlineFix.ini or OnlineFix64.dll / OnlineFix.dll.
    /// </summary>
    public static bool IsOnlineFixPresent(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return false;

        try
        {
            // Direct checks in root or common subdirectories
            string[] checkPaths =
            [
                Path.Combine(folderPath, "OnlineFix.ini"),
                Path.Combine(folderPath, "OnlineFix64.dll"),
                Path.Combine(folderPath, "OnlineFix.dll"),
                Path.Combine(folderPath, "SteamOverlay64.dll"),
                Path.Combine(folderPath, "Binaries", "Win64", "OnlineFix.ini"),
                Path.Combine(folderPath, "Binaries", "Win64", "OnlineFix64.dll"),
                Path.Combine(folderPath, "bin", "x64", "OnlineFix.ini"),
                Path.Combine(folderPath, "bin", "x64", "OnlineFix64.dll")
            ];

            foreach (string p in checkPaths)
            {
                if (File.Exists(p)) return true;
            }

            // Top level check for any file starting with OnlineFix
            return Directory.EnumerateFiles(folderPath, "OnlineFix*.*", SearchOption.AllDirectories).Any();
        }
        catch { return false; }
    }

    /// <summary>
    /// Check whether a Denuvo fix is applied (has .luatools-fix directory).
    /// </summary>
    public static bool IsDenuvoFixPresent(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return false;
        try
        {
            string fixDir = Path.Combine(folderPath, ".luatools-fix");
            return Directory.Exists(fixDir) && Directory.EnumerateFiles(fixDir, "*.json").Any();
        }
        catch { return false; }
    }

    /// <summary>
    /// Check whether this game has a Fix (Online Fix or Denuvo Fix) listed on the server.
    /// </summary>
    public async Task<DenuvoFix?> CheckAvailableFixAsync(long appId)
    {
        if (_services is null) return null;
        try
        {
            var api = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<BaoToolsApiClient>(_services);
            if (api is null) return null;

            var data = await api.GetDenuvoFixesAsync(appId.ToString());
            return data?.Fixes.FirstOrDefault(f => f.HasFix);
        }
        catch { return null; }
    }

    /// <summary>
    /// Apply Goldberg Steam emulator for offline compatibility.
    /// Backs up original steam_api(64).dll and creates steam_appid.txt.
    /// </summary>
    public static (bool Success, string? Message) ApplyGoldbergEmulator(string folderPath, long appId)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return (false, "Folder does not exist.");

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string goldbergRoot = Path.Combine(appData, "BaoToolsGui", "steamautocrack", "Goldberg", "regular");
            string goldberg64 = Path.Combine(goldbergRoot, "x64", "steam_api64.dll");
            string goldberg32 = Path.Combine(goldbergRoot, "x86", "steam_api.dll");

            if (!File.Exists(goldberg64) && !File.Exists(goldberg32))
                return (false, "Goldberg emulator DLLs not found in steamautocrack directory.");

            var allDlls = Directory.EnumerateFiles(folderPath, "steam_api*.dll", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".orig", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (allDlls.Count == 0)
            {
                File.WriteAllText(Path.Combine(folderPath, "steam_appid.txt"), appId.ToString());
                return (true, "steam_appid.txt created.");
            }

            int patched = 0;
            foreach (string dllPath in allDlls)
            {
                string name = Path.GetFileName(dllPath);
                string dllDir = Path.GetDirectoryName(dllPath) ?? folderPath;
                bool is64 = name.Contains("64", StringComparison.OrdinalIgnoreCase);

                string sourceEmulator = is64 ? goldberg64 : goldberg32;
                if (!File.Exists(sourceEmulator)) continue;

                string backupPath = dllPath + ".orig";
                if (!File.Exists(backupPath))
                {
                    File.Copy(dllPath, backupPath, overwrite: false);
                }

                File.Copy(sourceEmulator, dllPath, overwrite: true);

                File.WriteAllText(Path.Combine(dllDir, "steam_appid.txt"), appId.ToString());
                File.WriteAllText(Path.Combine(folderPath, "steam_appid.txt"), appId.ToString());
                patched++;
            }

            return (patched > 0, patched > 0 ? "Goldberg emulator applied." : "No matching steam_api DLLs patched.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Ensures a game has a working fix or offline patch before launching.
    /// If unpatched, checks for an Online Fix on the server and automatically downloads & applies it.
    /// If no online fix is found or user is guest, falls back to applying Goldberg emulator for offline play.
    /// </summary>
    public async Task<bool> EnsureFixOrCrackAppliedAsync(
        long appId, string gameName, string folderPath, ToastService? toast = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        string gameRoot = SteamLibraryService.ResolveGameRootFolder(folderPath);

        // Strip SteamStub DRM from game executable(s) if present (avoids "Application load error 3:0000065432")
        try
        {
            var steamless = _services?.GetService<SteamlessService>();
            if (steamless is not null)
            {
                await steamless.PatchGameAsync(appId, null);
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Steamless DRM check skipped or failed for {AppId}", appId);
        }

        // Try restoring any OnlineFix files that may have been quarantined by Windows Defender
        if (WindowsDefenderService.TryRestoreOnlineFixFiles(appId, gameRoot))
        {
            toast?.Show(
                Resources.Strings.Fixes_Title,
                Resources.Strings.Launcher_RestoredOnlineFix);
        }

        var info = InspectFolder(gameRoot, gameName);
        if (info.HasOnlineFix || info.HasDenuvoFix)
        {
            if (info.HasOnlineFix)
            {
                WindowsDefenderService.UnblockDirectory(gameRoot);
                WindowsDefenderService.BackupOnlineFixFiles(appId, gameRoot);
            }
            return true;
        }

        // Check server for Online Fix / Denuvo Fix
        try
        {
            var fix = await CheckAvailableFixAsync(appId);
            if (fix is not null && fix.HasFix)
            {
                var auth = _services?.GetService<AuthService>();
                if (auth is not null && !auth.IsSignedIn)
                {
                    try { await auth.InitializeAsync(); } catch { }
                }

                var jobs = _services?.GetService<ManifestJobFactory>();
                var queue = _services?.GetService<DownloadQueue>();

                if (auth?.IsSignedIn == true && jobs is not null && queue is not null)
                {
                    string tagName = fix.Tags.FirstOrDefault()?.Name ?? "Online Fix";
                    toast?.Show(
                        Resources.Strings.Fixes_Title,
                        string.Format(Resources.Strings.Launcher_DownloadingFix, tagName, gameName));

                    var tcs = new TaskCompletionSource<bool>();
                    string fallback = fix.FixFilename ?? $"{appId}_fix.zip";

                    var job = jobs.CreateDenuvoJob(
                        fix.Id,
                        "fix",
                        fallback,
                        appId,
                        gameName,
                        fix.Title,
                        onFinished: (item, result) =>
                        {
                            if (result?.Ok == true)
                            {
                                tcs.TrySetResult(true);
                            }
                            else
                            {
                                tcs.TrySetResult(false);
                            }
                        });

                    queue.Enqueue(job);

                    // Wait for fix to download and apply (with 60s timeout)
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(60000));
                    if (completedTask == tcs.Task && await tcs.Task)
                    {
                        WindowsDefenderService.UnblockDirectory(gameRoot);
                        WindowsDefenderService.BackupOnlineFixFiles(appId, gameRoot);
                        _ = WindowsDefenderService.AddFolderExclusionAsync(gameRoot);

                        toast?.Show(
                            Resources.Strings.Fixes_Title,
                            string.Format(Resources.Strings.Launcher_FixApplied, tagName));
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Failed auto-applying fix from server for {AppId}", appId);
        }

        // Check if already patched with Goldberg
        bool hasAppIdTxt = File.Exists(Path.Combine(gameRoot, "steam_appid.txt"));
        bool hasOrigDll = Directory.EnumerateFiles(gameRoot, "steam_api*.dll.orig", SearchOption.AllDirectories).Any();
        if (hasAppIdTxt && hasOrigDll)
        {
            return true;
        }

        try
        {
            var sac = _services?.GetService<SteamAutoCrackService>();
            if (sac is not null)
            {
                await sac.EnsureToolAsync(null);
            }
        }
        catch { /* best effort */ }

        // Fallback: Apply Goldberg offline emulator
        var (goldbergOk, _) = ApplyGoldbergEmulator(gameRoot, appId);
        if (goldbergOk)
        {
            toast?.Show(
                Resources.Strings.Downloads_Action_Play,
                string.Format(Resources.Strings.Launcher_ConfiguredOfflineEmulator, gameName));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Starts Steam if not already running.
    /// </summary>
    public bool StartSteam() => _steam.StartSteam();

    /// <summary>
    /// Launch the game executable. If <paramref name="ensureSteamRunning"/> is true and game has Online Fix,
    /// checks that Steam is running (and launches Steam if not, returning false so the user can wait).
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> LaunchGameAsync(
        string exePath, bool ensureSteamRunning = true)
    {
        await Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return (false, "Executable file not found.");
        }

        try
        {
            string exeDir = Path.GetDirectoryName(exePath) ?? "";
            string rootDir = Path.GetDirectoryName(exeDir) ?? exeDir;

            if (ensureSteamRunning && (IsOnlineFixPresent(exeDir) || IsOnlineFixPresent(rootDir)))
            {
                if (!SteamService.IsSteamRunning())
                {
                    _log?.LogInformation("Online fix detected. Steam is not running. Starting Steam and waiting for user to relaunch...");
                    _steam.StartSteam();
                    return (false, Resources.Strings.Launcher_StartingSteam);
                }
            }

            // Auto Game Doctor: Pre-flight check and heal missing dependencies if enabled (default ON)
            if (_settings is null || _settings.AutoGameDoctor)
            {
                try
                {
                    var doctor = _services?.GetService<GameDoctorService>();
                    if (doctor is not null)
                    {
                        await doctor.AutoHealExeAsync(exePath);
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogDebug(ex, "Game Doctor pre-flight check skipped on error");
                }
            }

            var psi = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = exeDir,
                UseShellExecute = true
            };

            Process.Start(psi);
            return (true, null);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to launch game executable: {Path}", exePath);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// If ExitOnPlay is enabled in settings, cleanly shuts down the app after launching,
    /// or minimizes to system tray / taskbar if active downloads are detected.
    /// </summary>
    public async Task HandlePostLaunchExitAsync(ToastService? toast = null)
    {
        if (_settings is null || !_settings.ExitOnPlay) return;

        bool hasActiveDownloads = false;
        try
        {
            if (_services is not null)
            {
                var queue = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Downloads.DownloadQueue>(_services);
                hasActiveDownloads = queue is not null && queue.ActiveCount > 0;
            }
        }
        catch { /* best effort */ }

        // Short delay so the user sees the launch toast and the game process initializes
        await Task.Delay(1000);

        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (hasActiveDownloads)
            {
                toast?.Show(
                    Resources.Strings.Settings_ExitOnPlay,
                    Resources.Strings.Launcher_MinimizingDueToDownloads);

                if (System.Windows.Application.Current?.MainWindow is MainWindow main)
                {
                    if (_settings.MinimizeToTray)
                    {
                        main.Hide();
                    }
                    else
                    {
                        main.WindowState = System.Windows.WindowState.Minimized;
                    }
                }
            }
            else
            {
                if (System.Windows.Application.Current?.MainWindow is MainWindow main)
                {
                    main.PrepareForShutdown();
                }
                System.Windows.Application.Current?.Shutdown();
            }
        });
    }

    /// <summary>
    /// Creates a Windows Desktop shortcut (.lnk) pointing to the game's executable.
    /// Sets TargetPath, WorkingDirectory, and Icon.
    /// </summary>
    public (bool Success, string? ShortcutPath, string? Error) CreateDesktopShortcut(string folderPath, string gameName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return (false, null, "Game directory does not exist.");

        string gameRoot = SteamLibraryService.ResolveGameRootFolder(folderPath);
        var info = InspectFolder(gameRoot, gameName);
        if (!info.ExeFound || string.IsNullOrWhiteSpace(info.ExecutablePath))
            return (false, null, "Game executable not found.");

        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktop))
                Directory.CreateDirectory(desktop);

            string safeName = SanitizeFileName(gameName);
            string shortcutPath = Path.Combine(desktop, $"{safeName}.lnk");

            string exeDir = Path.GetDirectoryName(info.ExecutablePath)!;
            string vbsPath = EnsureSmartLauncherVbs(info.ExecutablePath, exeDir);
            string wscriptExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wscript.exe");
            if (!File.Exists(wscriptExe)) wscriptExe = "wscript.exe";

            bool created = false;
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is not null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = wscriptExe;
                    shortcut.Arguments = $"\"{vbsPath}\"";
                    shortcut.WorkingDirectory = exeDir;
                    shortcut.Description = gameName;
                    shortcut.IconLocation = $"{info.ExecutablePath},0";
                    shortcut.Save();
                    created = File.Exists(shortcutPath);
                }
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "WScript.Shell in-process shortcut creation failed, trying PowerShell fallback");
            }

            if (!created)
            {
                // Fallback via PowerShell
                string escapedLnk = shortcutPath.Replace("'", "''");
                string escapedTarget = wscriptExe.Replace("'", "''");
                string escapedArgs = $"\"{vbsPath}\"".Replace("'", "''");
                string escapedDir = exeDir.Replace("'", "''");
                string escapedDesc = gameName.Replace("'", "''");
                string escapedIcon = $"{info.ExecutablePath},0".Replace("'", "''");
                string script = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{escapedLnk}'); $s.TargetPath = '{escapedTarget}'; $s.Arguments = '{escapedArgs}'; $s.WorkingDirectory = '{escapedDir}'; $s.Description = '{escapedDesc}'; $s.IconLocation = '{escapedIcon}'; $s.Save()";

                var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                created = File.Exists(shortcutPath);
            }

            if (created)
            {
                _log?.LogInformation("Created desktop shortcut for {GameName} at {Path}", gameName, shortcutPath);
                return (true, shortcutPath, null);
            }

            return (false, null, "Could not create desktop shortcut.");
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to create desktop shortcut for {GameName}", gameName);
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// If CreateDesktopShortcut is enabled in settings (default ON), creates a desktop shortcut.
    /// </summary>
    public void TryAutoCreateDesktopShortcut(string folderPath, string gameName)
    {
        if (_settings is null || _settings.CreateDesktopShortcut)
        {
            var (ok, path, _) = CreateDesktopShortcut(folderPath, gameName);
            if (ok)
            {
                _log?.LogInformation("Auto-created desktop shortcut for {GameName} at {Path}", gameName, path);
            }
        }
    }

    /// <summary>
    /// If AutoGameDoctor is enabled in settings (default ON), checks and heals missing runtime dependencies.
    /// </summary>
    public void TryAutoHealGameEnvironment(string folderPath, string gameName)
    {
        if (_settings is null || _settings.AutoGameDoctor)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var doctor = _services?.GetService<GameDoctorService>();
                    if (doctor is not null)
                    {
                        string gameRoot = SteamLibraryService.ResolveGameRootFolder(folderPath);
                        string? exe = FindGameExecutable(gameRoot, gameName);
                        if (!string.IsNullOrEmpty(exe))
                        {
                            await doctor.AutoHealExeAsync(exe);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogDebug(ex, "Background AutoGameDoctor on download completion failed for {GameName}", gameName);
                }
            });
        }
    }

    /// <summary>
    /// Removes any desktop shortcuts matching the game name or target path.
    /// </summary>
    public void RemoveDesktopShortcut(string gameName, string? folderPath = null)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktop)) return;

            string safeName = SanitizeFileName(gameName);
            string directPath = Path.Combine(desktop, $"{safeName}.lnk");
            if (File.Exists(directPath))
            {
                try { File.Delete(directPath); } catch { }
            }

            // Also check for any existing .lnk pointing into the folderPath
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                string normFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is not null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    foreach (var lnk in Directory.EnumerateFiles(desktop, "*.lnk"))
                    {
                        try
                        {
                            dynamic sc = shell.CreateShortcut(lnk);
                            string target = (string)sc.TargetPath;
                            if (!string.IsNullOrWhiteSpace(target) &&
                                Path.GetFullPath(target).StartsWith(normFolder, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(lnk);
                            }
                        }
                        catch { /* skip unreadable */ }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Failed to remove desktop shortcut for {GameName}", gameName);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string clean = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Game" : clean;
    }

    /// <summary>
    /// Creates or updates a silent VBS launcher in the game directory.
    /// It dynamically checks at launch time whether the game has Online-Fix:
    /// - If Online-Fix is present: checks and auto-starts Steam if needed before launching game.
    /// - If game is Offline: skips Steam completely and launches the game instantly!
    /// </summary>
    public static string EnsureSmartLauncherVbs(string exePath, string workingDir)
    {
        string vbsPath = Path.Combine(workingDir, "LaunchGame.vbs");
        string exeName = Path.GetFileName(exePath);

        string vbsContent =
            "Option Explicit\r\n" +
            "Dim WshShell, fso, scriptDir, hasOnlineFix, service, procs, appData, backupDir\r\n" +
            "Set WshShell = CreateObject(\"WScript.Shell\")\r\n" +
            "Set fso = CreateObject(\"Scripting.FileSystemObject\")\r\n" +
            "scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)\r\n\r\n" +
            "' Auto-recovery: If OnlineFix64.dll was eaten by Defender, restore from BaoTools backup\r\n" +
            "appData = WshShell.ExpandEnvironmentStrings(\"%APPDATA%\")\r\n" +
            "backupDir = appData & \"\\BaoToolsGui\\OnlineFixBackup\"\r\n" +
            "If fso.FileExists(scriptDir & \"\\OnlineFix.ini\") And Not fso.FileExists(scriptDir & \"\\OnlineFix64.dll\") Then\r\n" +
            "    If fso.FolderExists(backupDir) Then\r\n" +
            "        On Error Resume Next\r\n" +
            "        Dim subFolder, candidate\r\n" +
            "        For Each subFolder In fso.GetFolder(backupDir).SubFolders\r\n" +
            "            candidate = subFolder.Path & \"\\OnlineFix64.dll\"\r\n" +
            "            If fso.FileExists(candidate) Then\r\n" +
            "                fso.CopyFile candidate, scriptDir & \"\\OnlineFix64.dll\", True\r\n" +
            "                Exit For\r\n" +
            "            End If\r\n" +
            "        Next\r\n" +
            "        On Error GoTo 0\r\n" +
            "    End If\r\n" +
            "End If\r\n\r\n" +
            "' Check if game has Online-Fix installed\r\n" +
            "hasOnlineFix = False\r\n" +
            "If fso.FileExists(scriptDir & \"\\OnlineFix64.dll\") Or fso.FileExists(scriptDir & \"\\OnlineFix.dll\") Or fso.FileExists(scriptDir & \"\\OnlineFix.ini\") Then\r\n" +
            "    hasOnlineFix = True\r\n" +
            "End If\r\n\r\n" +
            "' Only start Steam if this game requires Steam (Online Fix)\r\n" +
            "If hasOnlineFix Then\r\n" +
            "    On Error Resume Next\r\n" +
            "    Set service = GetObject(\"winmgmts:\")\r\n" +
            "    Set procs = service.ExecQuery(\"Select * from Win32_Process Where Name = 'steam.exe'\")\r\n" +
            "    If procs.Count = 0 Then\r\n" +
            "        WshShell.Run \"steam://open/main\", 0, False\r\n" +
            "        WScript.Sleep 3500\r\n" +
            "    End If\r\n" +
            "    On Error GoTo 0\r\n" +
            "End If\r\n\r\n" +
            "' Launch the game executable\r\n" +
            "WshShell.CurrentDirectory = scriptDir\r\n" +
            $"WshShell.Run Chr(34) & scriptDir & \"\\{exeName}\" & Chr(34), 1, False\r\n";

        try
        {
            File.WriteAllText(vbsPath, vbsContent);
        }
        catch { }

        return vbsPath;
    }
}

