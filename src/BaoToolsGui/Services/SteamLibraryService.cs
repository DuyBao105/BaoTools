using System.IO;
using System.Text.RegularExpressions;

namespace BaoToolsGui.Services;

/// <summary>
/// Resolves where a Steam game is installed on disk by walking Valve's KeyValues files:
/// registry Steam root → steamapps\libraryfolders.vdf (every library, possibly across drives) →
/// per-library steamapps\appmanifest_&lt;appid&gt;.acf (the game's installdir) → common\&lt;installdir&gt;.
/// Best-effort: returns null if Steam/the game can't be located. Used to apply Denuvo "fix" zips.
/// </summary>
public partial class SteamLibraryService(SteamService steam, CacheService? cache = null)
{
    // "path"        "D:\\SteamLibrary"     → the library root (libraryfolders.vdf)
    // "installdir"  "Elden Ring"           → folder under steamapps\common (appmanifest_*.acf)
    // Values are quoted; backslashes are escaped (\\). One key per line.
    [GeneratedRegex(@"""path""\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PathRegex();

    [GeneratedRegex(@"""installdir""\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex(@"""appid""\s*""(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AppIdRegex();

    [GeneratedRegex(@"""name""\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    /// <summary>An installed game, as its appmanifest describes it.</summary>
    public record InstalledGame(long AppId, string Name, string InstallDir);

    /// <summary>
    /// Every installed game across every library, from the appmanifests. Lazy, and skips anything whose
    /// folder isn't actually on disk.
    /// </summary>
    /// <remarks>
    /// Enumerating the .acf files is the cheap part (~39 ms across three drives here); what costs is
    /// whatever the caller then does per game. Yields rather than returning a list so a caller looking
    /// for one thing can stop early.
    /// </remarks>
    public IEnumerable<InstalledGame> EnumerateInstalled()
    {
        string? steamRoot = steam.EffectivePath;
        var seenAppIds = new HashSet<long>();

        if (steamRoot is not null)
        {
            foreach (string library in GetLibraryRoots(steamRoot))
            {
                string steamapps = Path.Combine(library, "steamapps");
                string[] acfs;
                try
                {
                    if (!Directory.Exists(steamapps)) continue;
                    acfs = Directory.GetFiles(steamapps, "appmanifest_*.acf");
                }
                catch { continue; }

                foreach (string acf in acfs)
                {
                    InstalledGame? game = null;
                    try
                    {
                        string text = File.ReadAllText(acf);

                        var idm = AppIdRegex().Match(text);
                        var dirm = InstallDirRegex().Match(text);
                        if (!idm.Success || !dirm.Success) continue;
                        if (!long.TryParse(idm.Groups[1].Value, out long appId)) continue;

                        string full = Path.Combine(steamapps, "common", Unescape(dirm.Groups[1].Value));
                        if (!Directory.Exists(full)) continue;

                        var nm = NameRegex().Match(text);
                        game = new InstalledGame(appId, nm.Success ? nm.Groups[1].Value : appId.ToString(), full);
                    }
                    catch { /* unreadable or malformed acf: skip this one, not the whole library */ }

                    if (game is not null && seenAppIds.Add(game.AppId)) yield return game;
                }
            }
        }

        if (cache is not null)
        {
            List<Downloads.DownloadHistoryRecord> history;
            try { history = cache.GetDownloadHistory().ToList(); } catch { history = []; }

            foreach (var r in history)
            {
                if (seenAppIds.Contains(r.AppId)) continue;
                if (IsDepotKind(r.Kind) && !string.IsNullOrWhiteSpace(r.RevealPath) && Directory.Exists(r.RevealPath))
                {
                    string root = ResolveGameRootFolder(r.RevealPath);
                    if (Directory.Exists(root) && seenAppIds.Add(r.AppId))
                    {
                        yield return new InstalledGame(r.AppId, r.Title, root);
                    }
                }
            }
        }
    }

    public IEnumerable<(long AppId, string Name, string InstallDir)> GetAllInstalledApps()
    {
        string? steamRoot = steam.EffectivePath;
        var seenAppIds = new HashSet<long>();

        if (steamRoot is not null)
        {
            foreach (string library in GetLibraryRoots(steamRoot))
            {
                string steamapps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamapps)) continue;

                foreach (string acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    string fileName = Path.GetFileNameWithoutExtension(acf);
                    string appIdStr = fileName.Substring("appmanifest_".Length);
                    if (!long.TryParse(appIdStr, out long appId)) continue;

                    string text;
                    try { text = File.ReadAllText(acf); } catch { continue; }

                    var mDir = InstallDirRegex().Match(text);
                    if (!mDir.Success) continue;
                    string installDir = Unescape(mDir.Groups[1].Value);
                    string full = Path.Combine(steamapps, "common", installDir);
                    if (!Directory.Exists(full)) continue;

                    var mName = NameRegex().Match(text);
                    string name = mName.Success ? Unescape(mName.Groups[1].Value) : $"Unknown App {appId}";

                    if (seenAppIds.Add(appId))
                    {
                        yield return (appId, name, full);
                    }
                }
            }
        }

        if (cache is not null)
        {
            List<Downloads.DownloadHistoryRecord> history;
            try { history = cache.GetDownloadHistory().ToList(); } catch { history = []; }

            foreach (var r in history)
            {
                if (seenAppIds.Contains(r.AppId)) continue;
                if (IsDepotKind(r.Kind) && !string.IsNullOrWhiteSpace(r.RevealPath) && Directory.Exists(r.RevealPath))
                {
                    string root = ResolveGameRootFolder(r.RevealPath);
                    if (Directory.Exists(root) && seenAppIds.Add(r.AppId))
                    {
                        yield return (r.AppId, r.Title, root);
                    }
                }
            }
        }
    }

    public string? GetInstallDir(long appId)
    {
        try
        {
            string? steamRoot = steam.EffectivePath;
            if (steamRoot is not null)
            {
                foreach (string library in GetLibraryRoots(steamRoot))
                {
                    string acf = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
                    if (!File.Exists(acf)) continue;

                    var m = InstallDirRegex().Match(File.ReadAllText(acf));
                    if (!m.Success) continue;

                    string installDir = Unescape(m.Groups[1].Value);
                    string full = Path.Combine(library, "steamapps", "common", installDir);
                    if (Directory.Exists(full)) return full;
                }
            }
        }
        catch { /* unreadable VDF/ACF or odd path. Treat as not found */ }

        // Fallback: check Raw Depot downloads from cache
        if (cache is not null)
        {
            try
            {
                var history = cache.GetDownloadHistory();
                var depot = history.FirstOrDefault(r =>
                    r.AppId == appId &&
                    IsDepotKind(r.Kind) &&
                    !string.IsNullOrWhiteSpace(r.RevealPath) &&
                    Directory.Exists(r.RevealPath));

                if (depot is not null && !string.IsNullOrWhiteSpace(depot.RevealPath))
                {
                    string resolved = ResolveGameRootFolder(depot.RevealPath);
                    if (Directory.Exists(resolved)) return resolved;
                }
            }
            catch { /* best effort */ }
        }

        return null;
    }

    /// <summary>
    /// Checks whether a download record kind corresponds to raw depot content (handles "depot", "raw_depot", "Depot").
    /// </summary>
    public static bool IsDepotKind(string? kind) =>
        string.Equals(kind, "depot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "raw_depot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, nameof(Downloads.DownloadKind.Depot), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the actual game root directory within a depot download folder.
    /// Handles both flat layout and nested game folder layout (e.g. outDir\GameName\Game.exe).
    /// </summary>
    public static string ResolveGameRootFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return folderPath;

        try
        {
            // If the folder itself contains .exe files (excluding common redists/crash handlers)
            var rootExes = Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !IsExcludedExe(Path.GetFileName(f)))
                .ToList();
            if (rootExes.Count > 0)
            {
                return folderPath;
            }

            // Check immediate subdirectories (skipping hidden / .DepotDownloader / redists)
            var subDirs = Directory.EnumerateDirectories(folderPath)
                .Where(d => !Path.GetFileName(d).StartsWith('.') &&
                            !Path.GetFileName(d).Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var sub in subDirs)
            {
                var subExes = Directory.EnumerateFiles(sub, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(f => !IsExcludedExe(Path.GetFileName(f)))
                    .ToList();
                if (subExes.Count > 0)
                {
                    return sub;
                }
            }

            // If nested deeper (e.g. Binaries\Win64), find main executable and walk up
            var allExes = Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories)
                .Where(f => !IsExcludedExe(Path.GetFileName(f)))
                .ToList();

            if (allExes.Count > 0)
            {
                string bestExe = allExes.OrderByDescending(f => {
                    try { return new FileInfo(f).Length; } catch { return 0; }
                }).First();

                string exeDir = Path.GetDirectoryName(bestExe) ?? folderPath;
                string? parent = Path.GetDirectoryName(exeDir);
                if (Path.GetFileName(exeDir).Equals("Win64", StringComparison.OrdinalIgnoreCase) && parent is not null)
                {
                    string? grandParent = Path.GetDirectoryName(parent);
                    if (Path.GetFileName(parent).Equals("Binaries", StringComparison.OrdinalIgnoreCase) && grandParent is not null)
                    {
                        return grandParent;
                    }
                }
                return exeDir;
            }
        }
        catch { /* best effort */ }

        return folderPath;
    }

    private static bool IsExcludedExe(string fileName)
    {
        string[] excluded =
        [
            "unitycrashhandler", "crashreport", "crashreporter", "werfault",
            "vcredist", "dxsetup", "ue4prereq", "ue5prereq", "prereqsetup",
            "dotnetfx", "vc_redist", "oalinst", "uninstall", "unins000",
            "depotdownloader"
        ];
        return excluded.Any(sub => fileName.Contains(sub, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every Steam library root (the main install plus any added libraries).</summary>
    private static IEnumerable<string> GetLibraryRoots(string steamRoot)
    {
        // The main install is always a library.
        yield return steamRoot;

        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        string text;
        try { text = File.ReadAllText(vdf); } catch { yield break; }

        foreach (Match m in PathRegex().Matches(text))
        {
            string path = Unescape(m.Groups[1].Value);
            // The main root often appears here too; harmless duplicate (we just probe each).
            if (!string.Equals(path, steamRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
                yield return path;
        }
    }

    /// <summary>VDF strings escape backslashes as "\\"; collapse to a real path.</summary>
    private static string Unescape(string s) => s.Replace(@"\\", @"\");
}

