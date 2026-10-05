using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BaoToolsGui.Services;
using BaoToolsGui.Services.Downloads;

namespace BaoToolsGui.ViewModels;

/// <summary>
/// The Downloads page. A thin projection over <see cref="DownloadQueue"/>: it holds no download state
/// of its own, so the queue stays the single source of truth for every entry point (Add, Fixes, the
/// store plugin and the protocol handler).
/// </summary>
public partial class DownloadsViewModel : ObservableObject
{
    private readonly DownloadQueue _queue;

    private readonly ManifestJobFactory _jobs;
    private readonly SteamAutoCrackService _sac;
    private readonly GameLauncherService _launcher;
    private readonly ToastService _toast;
    private readonly CoverCache _covers;
    private readonly SteamService _steam;

    public DownloadsViewModel(DownloadQueue queue, ManifestJobFactory jobs, SteamAutoCrackService sac,
        GameLauncherService launcher, ToastService toast, CoverCache covers, SteamService steam)
    {
        _queue = queue;
        _jobs = jobs;
        _sac = sac;
        _launcher = launcher;
        _toast = toast;
        _covers = covers;
        _steam = steam;

        _queue.Items.CollectionChanged += (_, _) => RaiseCounts();
        _queue.History.CollectionChanged += (_, _) =>
        {
            RaiseCounts();
            EnsureCovers();
        };
        _queue.StateChanged += RaiseCounts;

        EnsureCovers();
    }

    private void EnsureCovers()
    {
        foreach (var entry in _queue.History.ToList())
        {
            if (entry.AppId <= 0) continue;
            string? local = _covers.GetLocalPath(entry.AppId);
            if (local is not null)
            {
                entry.SetResolvedCover(local);
            }
            else
            {
                string url = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{entry.AppId}/header.jpg";
                _ = Task.Run(async () =>
                {
                    string? path = await _covers.EnsureAsync(entry.AppId, url);
                    if (path is not null)
                    {
                        Application.Current?.Dispatcher.Invoke(() => entry.SetResolvedCover(path));
                    }
                });
            }
        }
    }

    /// <summary>Bound directly by the view: <c>Queue.Items</c> and <c>Queue.History</c>.</summary>
    public DownloadQueue Queue => _queue;

    /// <summary>Set by App: jump to the page that owns an item's pending confirmation.</summary>
    public Action<DownloadItem>? RevealItem { get; set; }

    /// <summary>Set by App: jump to the Add / Search Game page.</summary>
    public Action? NavigateToAdd { get; set; }

    public bool HasItems => _queue.Items.Count > 0;
    public bool HasHistory => _queue.History.Count > 0;
    public bool IsEmpty => !HasItems;
    public bool IsCompletelyEmpty => !HasItems && !HasHistory;

    public int ActiveCount => _queue.Items.Count;
    public int CompletedCount => _queue.History.Count(h => h.Status == DownloadStatus.Completed);
    public string TotalCompletedSize => ByteFormat.Size(_queue.History.Where(h => h.Status == DownloadStatus.Completed).Sum(h => h.Record.Bytes));
    public string TotalSpeedLabel
    {
        get
        {
            double speed = _queue.Items.Where(i => i.IsRunning).Sum(i => i.BytesPerSecond);
            return speed > 0 ? ByteFormat.Rate(speed) : "";
        }
    }

    public string ActiveSubLabel
    {
        get
        {
            if (HasItems)
            {
                string speed = TotalSpeedLabel;
                return !string.IsNullOrEmpty(speed) ? speed : $"{ActiveCount} items";
            }
            return Resources.Strings.Downloads_Stats_Active_Idle;
        }
    }

    public string DiskSpaceLabel
    {
        get
        {
            try
            {
                string targetPath = GetDefaultDownloadsFolder();
                var firstExisting = _queue.History
                    .Select(h => h.Record.RevealPath)
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && (Directory.Exists(p) || File.Exists(p)));

                if (!string.IsNullOrWhiteSpace(firstExisting))
                {
                    targetPath = Directory.Exists(firstExisting) ? firstExisting : (Path.GetDirectoryName(firstExisting) ?? targetPath);
                }

                string? root = Path.GetPathRoot(Path.GetFullPath(targetPath));
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady)
                    {
                        string driveName = drive.Name.TrimEnd('\\');
                        return string.Format(Resources.Strings.Downloads_Stats_DiskFree, driveName, ByteFormat.Size(drive.AvailableFreeSpace));
                    }
                }
            }
            catch
            {
            }

            return "";
        }
    }

    [ObservableProperty]
    private string _historySearchText = "";

    partial void OnHistorySearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredHistory));
        OnPropertyChanged(nameof(HasFilteredHistory));
        OnPropertyChanged(nameof(IsSearchEmpty));
    }

    public IEnumerable<DownloadHistoryEntry> FilteredHistory
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HistorySearchText))
                return _queue.History;

            string query = HistorySearchText.Trim();
            return _queue.History.Where(h =>
                (!string.IsNullOrEmpty(h.Title) && h.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (h.AppId > 0 && h.AppId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(h.SubTitle) && h.SubTitle.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public bool HasFilteredHistory => FilteredHistory.Any();
    public bool IsSearchEmpty => HasHistory && !HasFilteredHistory;

    public Action? ScrollToQueueAction { get; set; }
    public Action? ScrollToHistoryAction { get; set; }

    public static string GetDefaultDownloadsFolder()
    {
        try
        {
            const string downloadsGuid = "{374DE290-123F-4565-9164-39C4925E467B}";
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (key?.GetValue(downloadsGuid) is string raw && raw.Length > 0)
            {
                string expanded = Environment.ExpandEnvironmentVariables(raw);
                if (Directory.Exists(expanded))
                {
                    string depots = Path.Combine(expanded, "BaoTools Depots");
                    return Directory.Exists(depots) ? depots : expanded;
                }
            }
        }
        catch
        {
        }

        string userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string userDepots = Path.Combine(userDownloads, "BaoTools Depots");
        if (Directory.Exists(userDepots)) return userDepots;
        if (Directory.Exists(userDownloads)) return userDownloads;

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsCompletelyEmpty));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(TotalCompletedSize));
        OnPropertyChanged(nameof(TotalSpeedLabel));
        OnPropertyChanged(nameof(ActiveSubLabel));
        OnPropertyChanged(nameof(DiskSpaceLabel));
        OnPropertyChanged(nameof(FilteredHistory));
        OnPropertyChanged(nameof(HasFilteredHistory));
        OnPropertyChanged(nameof(IsSearchEmpty));
    }

    // ── Commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseGames()
    {
        NavigateToAdd?.Invoke();
        (Application.Current?.MainWindow as MainWindow)?.NavigateToAdd();
    }

    /// <summary>
    /// Fetch SteamAutoCrack (and the .NET runtime it needs) and open it.
    /// </summary>
    /// <remarks>
    /// Goes through the queue rather than running inline: the first run pulls roughly 100 MB, which needs
    /// a progress row and a Cancel button rather than a frozen-looking window. The job's DedupeKey means
    /// repeated clicks join the running item instead of stacking up.
    /// </remarks>
    [RelayCommand]
    private async Task LaunchSteamAutoCrack()
    {
        // Already installed and runnable → open it now. Going through the queue here would flash a
        // progress row for a launch that transfers nothing AND leave a permanent history entry beside
        // the user's real game downloads, which is the whole reason this fast path exists.
        if (await _sac.TryLaunchIfReadyAsync())
        {
            _ = CheckSteamAutoCrackUpdateAsync();
            return;
        }

        // First run, or the runtime is missing: real work, so it earns a queue row.
        _queue.Enqueue(_jobs.CreateSteamAutoCrackJob());
    }

    /// <summary>Throttled background update probe. Only queues anything if a newer build actually exists.</summary>
    private async Task CheckSteamAutoCrackUpdateAsync()
    {
        // Fire-and-forget off a UI command: an unobserved fault here must never reach the user.
        try
        {
            if (_queue.FindActive("tool:steamautocrack") is not null) return; // one already in flight
            if (await _sac.IsUpdateAvailableAsync())
                _queue.Enqueue(_jobs.CreateSteamAutoCrackJob(launchWhenDone: false));
        }
        catch { /* background nicety; never surfaces */ }
    }

    /// <summary>
    /// Cancel an item. A depot download that has already written to disk asks what to do with the files
    /// first — cancelling leaves them behind otherwise, and they are not small.
    /// </summary>
    /// <remarks>
    /// Only depot downloads prompt: they are the only kind that writes a folder rather than a staged file
    /// the queue already cleans up. And only when the downloader actually used that folder, which
    /// <see cref="DepotDownloaderService.HasDownloadedContent"/> establishes from its own marker — the
    /// output directory is user-chosen and may contain unrelated files.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Cancel(DownloadItem item)
    {
        string? outDir = item.Job.OutputPath;
        if (item.Job.Kind is not DownloadKind.Depot
            || !DepotDownloaderService.HasDownloadedContent(outDir))
        {
            _queue.Cancel(item);
            return;
        }

        // Yes = stop and delete (the default), No = stop and keep, Cancel = keep downloading.
        // Escape lands on Cancel, so the accidental keypress is the harmless one even though the
        // destructive option is what Enter selects.
        var choice = ModernMessageBox.Show(
            string.Format(Resources.Strings.Depot_Cancel_Body, ByteFormat.Size(item.BytesRead), outDir),
            Resources.Strings.Depot_Cancel_Title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        if (choice == MessageBoxResult.Cancel) return; // leave the download running

        _queue.Cancel(item);
        if (choice != MessageBoxResult.Yes) return;

        // The kill is asynchronous and the downloader holds handles on everything it pre-allocated, so
        // deleting before the item settles would just fail on a locked file.
        await item.Completion;
        if (!DepotDownloaderService.TryDeleteCreatedFiles(outDir, item.CreatedFiles))
            _toast.Show(Resources.Strings.Depot_Cancel_Title,
                string.Format(Resources.Strings.Depot_Cancel_DeleteFailed, outDir), error: true);
    }

    [RelayCommand]
    private void Retry(DownloadItem item) => _queue.Retry(item);

    /// <summary>Depot downloads only — see <see cref="DownloadItem.CanPause"/>.</summary>
    [RelayCommand]
    private void Pause(DownloadItem item) => _queue.Pause(item);

    [RelayCommand]
    private void Resume(DownloadItem item) => _queue.Resume(item);

    [RelayCommand]
    private void Remove(DownloadItem item) => _queue.Remove(item);

    // ── Row actions (right-click) ────────────────────────────────────
    // Two commands per action because RelayCommand is typed and the queue and the history list bind
    // different row types. Both funnel into the same pair of helpers so the behaviour can't diverge.

    [RelayCommand]
    private void CopyAppId(DownloadItem item) => CopyId(item.AppId);

    [RelayCommand]
    private void CopyHistoryAppId(DownloadHistoryEntry entry) => CopyId(entry.AppId);

    [RelayCommand]
    private void ShowInFolder(DownloadItem item) => Show(item.RevealPath);

    [RelayCommand]
    private void ShowHistoryInFolder(DownloadHistoryEntry entry) => Show(entry.Record.RevealPath);

    [RelayCommand]
    private async Task Play(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.RevealPath)) return;
        await PlayGameFromFolderAsync(item.RevealPath, item.Title, item.AppId);
    }

    [RelayCommand]
    private async Task PlayHistory(DownloadHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Record.RevealPath)) return;
        await PlayGameFromFolderAsync(entry.Record.RevealPath, entry.Title, entry.AppId);
    }

    [RelayCommand]
    private async Task UninstallHistory(DownloadHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Record.RevealPath)) return;
        bool ok = await UninstallGameAsync(entry.Record.RevealPath, entry.Title, entry.AppId);
        if (ok)
        {
            _queue.RemoveHistory(entry);
            RaiseCounts();
        }
    }

    [RelayCommand]
    private void ScrollToQueue() => ScrollToQueueAction?.Invoke();

    [RelayCommand]
    private void ScrollToHistory() => ScrollToHistoryAction?.Invoke();

    [RelayCommand]
    private void OpenDownloadsFolder()
    {
        string folder = GetDefaultDownloadsFolder();
        var firstExisting = _queue.History
            .Select(h => h.Record.RevealPath)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && (Directory.Exists(p) || File.Exists(p)));

        if (!string.IsNullOrWhiteSpace(firstExisting))
        {
            folder = Directory.Exists(firstExisting) ? firstExisting : (Path.GetDirectoryName(firstExisting) ?? folder);
        }

        if (!Directory.Exists(folder))
        {
            try { Directory.CreateDirectory(folder); } catch { }
        }

        SteamService.ShowInExplorer(folder);
    }

    [RelayCommand]
    private async Task Uninstall(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.RevealPath)) return;
        bool ok = await UninstallGameAsync(item.RevealPath, item.Title, item.AppId);
        if (ok)
        {
            item.RaiseStateChanged();
        }
    }

    [RelayCommand]
    private void CreateShortcutHistory(DownloadHistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Record.RevealPath)) return;
        CreateShortcutForGame(entry.Record.RevealPath, entry.Title);
    }

    [RelayCommand]
    private void CreateShortcut(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.RevealPath)) return;
        CreateShortcutForGame(item.RevealPath, item.Title);
    }

    private void CreateShortcutForGame(string folderPath, string gameName)
    {
        var (ok, path, err) = _launcher.CreateDesktopShortcut(folderPath, gameName);
        if (ok)
        {
            _toast.Show(
                Resources.Strings.Downloads_Action_CreateShortcut,
                string.Format(Resources.Strings.Downloads_Shortcut_Created, gameName));
        }
        else
        {
            _toast.Show(
                Resources.Strings.Downloads_Action_CreateShortcut,
                string.Format(Resources.Strings.Downloads_Shortcut_Failed, err ?? ""),
                error: true);
        }
    }

    private async Task<bool> UninstallGameAsync(string folderPath, string gameName, long appId)
    {
        string target = System.IO.Directory.Exists(folderPath)
            ? SteamLibraryService.ResolveGameRootFolder(folderPath)
            : folderPath;

        var choice = ModernMessageBox.Show(
            string.Format(Resources.Strings.Downloads_Uninstall_Confirm, gameName, target),
            Resources.Strings.Downloads_Uninstall_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (choice != MessageBoxResult.Yes) return false;

        bool deleted = false;
        try
        {
            deleted = await Task.Run(() =>
            {
                bool success = false;
                if (System.IO.Directory.Exists(target))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            target,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch
                    {
                        System.IO.Directory.Delete(target, recursive: true);
                    }
                    success = true;
                }
                else if (System.IO.File.Exists(target))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            target,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch
                    {
                        System.IO.File.Delete(target);
                    }
                    success = true;
                }

                if (appId > 0)
                {
                    try
                    {
                        string? plugInDir = _steam.StPlugInDir;
                        if (!string.IsNullOrWhiteSpace(plugInDir))
                        {
                            string luaFile = System.IO.Path.Combine(plugInDir, $"{appId}.lua");
                            if (System.IO.File.Exists(luaFile)) System.IO.File.Delete(luaFile);
                        }
                    }
                    catch { /* best-effort */ }
                }

                _launcher.RemoveDesktopShortcut(gameName, target);

                return success;
            });

            if (deleted)
            {
                _toast.Show(
                    Resources.Strings.Downloads_Uninstall_Title,
                    string.Format(Resources.Strings.Downloads_Uninstall_Success, gameName));
                return true;
            }
        }
        catch (Exception ex)
        {
            _toast.Show(
                Resources.Strings.Downloads_Uninstall_Title,
                string.Format(Resources.Strings.Downloads_Uninstall_Failed, gameName, ex.Message),
                error: true);
        }

        return false;
    }

    private async Task PlayGameFromFolderAsync(string folderPath, string gameName, long appId)
    {
        string gameRoot = SteamLibraryService.ResolveGameRootFolder(folderPath);

        // Auto-apply Online Fix from server or Goldberg offline crack if game is not yet patched
        await _launcher.EnsureFixOrCrackAppliedAsync(appId, gameName, gameRoot, _toast);

        var info = _launcher.InspectFolder(gameRoot, gameName);
        if (!info.ExeFound || info.ExecutablePath is null)
        {
            _toast.Show(
                Resources.Strings.Downloads_Action_Play,
                string.Format(Resources.Strings.Launcher_ExeNotFound, gameName),
                error: true);
            _ = SteamService.ShowInExplorer(gameRoot);
            return;
        }

        if (info.RequiresSteam && !SteamService.IsSteamRunning())
        {
            _launcher.StartSteam();
            _toast.Show(
                Resources.Strings.Downloads_Action_Play,
                Resources.Strings.Launcher_StartingSteam);
            return;
        }

        var (success, error) = await _launcher.LaunchGameAsync(info.ExecutablePath);
        if (success)
        {
            _toast.Show(
                Resources.Strings.Downloads_Action_Play,
                string.Format(Resources.Strings.Launcher_GameStarted, gameName));

            await _launcher.HandlePostLaunchExitAsync(_toast);
        }
        else
        {
            _toast.Show(
                Resources.Strings.Downloads_Action_Play,
                string.Format(Resources.Strings.Launcher_LaunchFailed, error ?? ""),
                error: true);
        }
    }

    private void CopyId(long appId)
    {
        if (!SteamService.CopyToClipboard(appId.ToString()))
            _toast.Show(Resources.Strings.Common_CopyAppId, Resources.Strings.Err_ClipboardBusy, error: true);
    }

    /// <summary>
    /// Open the install location. The path is recorded when the job finishes, so by the time a row can be
    /// right-clicked it is either real or absent — but the user can still have deleted it since.
    /// </summary>
    private void Show(string? path)
    {
        if (!SteamService.ShowInExplorer(path))
            _toast.Show(Resources.Strings.Common_ShowInFolder,
                string.Format(Resources.Strings.Err_PathMissing, path ?? ""), error: true);
    }

    [RelayCommand]
    private void MoveUp(DownloadItem item) => _queue.Move(item, -1);

    [RelayCommand]
    private void MoveDown(DownloadItem item) => _queue.Move(item, +1);

    [RelayCommand]
    private void ClearHistory()
    {
        // Deliberately NOT async: ModernMessageBox.Show already blocks and returns a result, and an async
        // command would become an AsyncRelayCommand, which disables itself while running.
        var choice = ModernMessageBox.Show(
            string.Format(Resources.Strings.Downloads_ClearHistory_Confirm, _queue.History.Count),
            Resources.Strings.Downloads_Action_ClearHistory,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No); // Enter must not wipe the list

        if (choice == MessageBoxResult.Yes) _queue.ClearHistory();
    }

    /// <summary>
    /// Remove one history row. No confirmation: it deletes a record, not a download, and prompting per
    /// row would be tedious. The bulk Clear history above does confirm, because it cannot be undone.
    /// </summary>
    [RelayCommand]
    private void RemoveHistoryEntry(DownloadHistoryEntry entry) => _queue.RemoveHistory(entry);

    /// <summary>Jump to the page that can resolve this item (e.g. the pending overwrite confirmation).</summary>
    [RelayCommand]
    private void Review(DownloadItem item)
    {
        item.Job.OnReveal?.Invoke();
        RevealItem?.Invoke(item);
    }
}
