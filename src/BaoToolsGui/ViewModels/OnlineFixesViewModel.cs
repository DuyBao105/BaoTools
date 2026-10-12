using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BaoToolsGui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BaoToolsGui.ViewModels;

public partial class OnlineFixGameCardVm(long appId, string name, string installDir, bool hasOnlineFix = false) : ObservableObject
{
    public long AppId { get; } = appId;
    public string Name { get; } = name;
    public string InstallDir { get; } = installDir;

    [ObservableProperty] private bool _hasOnlineFix = hasOnlineFix;
    [ObservableProperty] private string? _cover;
    private int _resolving;

    public bool Matches(string q) =>
        Name.Contains(q, StringComparison.OrdinalIgnoreCase) || AppId.ToString().Contains(q);

    public async Task EnsureCoverAsync(CoverCache covers)
    {
        if (Cover is not null) return;
        if (Interlocked.Exchange(ref _resolving, 1) == 1) return;
        try
        {
            string? local = covers.GetLocalPath(AppId);
            if (local is null)
            {
                local = await covers.EnsureAsync(AppId, SteamAppInfoCache.GuessHeaderImageUrl(AppId));
            }
            if (local is not null)
            {
                Application.Current?.Dispatcher.Invoke(() => Cover = local);
            }
        }
        catch { }
        finally { Interlocked.Exchange(ref _resolving, 0); }
    }
}

public enum OnlineFixFilterMode
{
    All,
    OnlineOnly,
    OfflineOnly
}

public partial class OnlineFixesViewModel(SteamLibraryService library, CoverCache covers, ToastService toast) : ObservableObject
{
    public ObservableCollection<OnlineFixGameCardVm> Games { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private OnlineFixFilterMode _filterMode = OnlineFixFilterMode.All;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private int _offlineCount;

    public bool IsFilterAll => FilterMode == OnlineFixFilterMode.All;
    public bool IsFilterOnline => FilterMode == OnlineFixFilterMode.OnlineOnly;
    public bool IsFilterOffline => FilterMode == OnlineFixFilterMode.OfflineOnly;

    public string FilterAllLabel => string.Format(Resources.Strings.OnlineFixes_Filter_All, TotalCount);
    public string FilterOnlineLabel => string.Format(Resources.Strings.OnlineFixes_Filter_Online, OnlineCount);
    public string FilterOfflineLabel => string.Format(Resources.Strings.OnlineFixes_Filter_Offline, OfflineCount);

    public bool IsEmpty => !IsLoading && Games.Count == 0;
    public string EmptyMessage => string.IsNullOrWhiteSpace(SearchText)
        ? Resources.Strings.OnlineFixes_EmptyLibrary
        : Resources.Strings.OnlineFixes_EmptySearch;

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnFilterModeChanged(OnlineFixFilterMode value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterOnline));
        OnPropertyChanged(nameof(IsFilterOffline));
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectFilter(string mode)
    {
        if (Enum.TryParse<OnlineFixFilterMode>(mode, true, out var m))
        {
            FilterMode = m;
        }
    }

    private List<OnlineFixGameCardVm> _allGames = [];

    public async Task InitializeAsync(bool force = false)
    {
        if (!force && (_allGames.Count > 0 || IsLoading)) return;
        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));

        try
        {
            var apps = await Task.Run(() =>
            {
                return library.GetAllInstalledApps()
                    .Where(a => !IsToolOrRedistributable(a.AppId, a.Name))
                    .OrderBy(a => a.Name)
                    .Select(a =>
                    {
                        bool hasFix = GameLauncherService.IsOnlineFixPresent(a.InstallDir)
                                      || WindowsDefenderService.HasBackup(a.AppId);
                        return new OnlineFixGameCardVm(a.AppId, a.Name, a.InstallDir, hasFix);
                    })
                    .ToList();
            });

            _allGames = apps;
            UpdateCounts();
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EmptyMessage));
        }
    }

    private void UpdateCounts()
    {
        TotalCount = _allGames.Count;
        OnlineCount = _allGames.Count(g => g.HasOnlineFix);
        OfflineCount = TotalCount - OnlineCount;

        OnPropertyChanged(nameof(FilterAllLabel));
        OnPropertyChanged(nameof(FilterOnlineLabel));
        OnPropertyChanged(nameof(FilterOfflineLabel));
    }

    private void ApplyFilter()
    {
        IEnumerable<OnlineFixGameCardVm> query = _allGames;

        if (FilterMode == OnlineFixFilterMode.OnlineOnly)
            query = query.Where(g => g.HasOnlineFix);
        else if (FilterMode == OnlineFixFilterMode.OfflineOnly)
            query = query.Where(g => !g.HasOnlineFix);

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(g => g.Matches(SearchText));

        Games.Clear();
        foreach (var g in query)
        {
            Games.Add(g);
            _ = g.EnsureCoverAsync(covers);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await InitializeAsync(force: true);
    }

    [RelayCommand]
    private void DownloadFix(OnlineFixGameCardVm game)
    {
        // Open online-fix.me search page in browser
        string url = $"https://online-fix.me/index.php?do=search&subaction=search&story={Uri.EscapeDataString(game.Name)}";
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenFolder(OnlineFixGameCardVm game)
    {
        if (Directory.Exists(game.InstallDir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = game.InstallDir,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private void CopyAppId(OnlineFixGameCardVm game)
    {
        Clipboard.SetText(game.AppId.ToString());
        toast.Show("BaoTools", $"Copied AppID: {game.AppId}");
    }

    [RelayCommand]
    private async Task InstallFixAsync(OnlineFixGameCardVm game)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Select Online-Fix ZIP for {game.Name}",
            Filter = "ZIP Archives (*.zip)|*.zip|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        IsLoading = true;
        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(dlg.FileName);
                int failed = 0;
                int extracted = 0;

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    string relativePath = entry.FullName;
                    string dest = Path.Combine(game.InstallDir, relativePath);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        entry.ExtractToFile(dest, overwrite: true);
                        extracted++;
                        WindowsDefenderService.UnblockFile(dest);
                    }
                    catch { failed++; }
                }

                // Strip Mark of the Web & backup OnlineFix binaries so Defender won't eat them
                WindowsDefenderService.UnblockDirectory(game.InstallDir);
                WindowsDefenderService.BackupOnlineFixFiles(game.AppId, game.InstallDir);

                // Auto-add folder exclusion to Windows Defender
                _ = WindowsDefenderService.AddFolderExclusionAsync(game.InstallDir);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (failed > 0)
                        toast.Show("Install with warnings", $"{extracted} files extracted, {failed} failed.", error: true);
                    else
                        toast.Show("Online Fix Installed", $"{game.Name} has been successfully patched!\n(Protected against Defender)");

                    game.HasOnlineFix = true;
                    UpdateCounts();
                    ApplyFilter();
                });
            });
        }
        catch (Exception ex)
        {
            toast.Show("Install Failed", ex.Message, error: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddDefenderExclusionAsync(OnlineFixGameCardVm game)
    {
        WindowsDefenderService.UnblockDirectory(game.InstallDir);
        await WindowsDefenderService.AddFolderExclusionAsync(game.InstallDir);
        toast.Show(
            Resources.Strings.OnlineFixes_DefenderExclusion,
            string.Format(Resources.Strings.OnlineFixes_DefenderExclusion_Success, game.Name));
    }

    [RelayCommand]
    private async Task AddAllDefenderExclusionsAsync()
    {
        var dirs = _allGames.Select(g => g.InstallDir).ToList();
        foreach (var dir in dirs)
        {
            WindowsDefenderService.UnblockDirectory(dir);
        }
        await WindowsDefenderService.AddFoldersExclusionAsync(dirs);
        toast.Show(
            Resources.Strings.OnlineFixes_DefenderExclusion_All,
            Resources.Strings.OnlineFixes_DefenderExclusion_All_Success);
    }

    private static bool IsToolOrRedistributable(long appId, string name)
    {
        // Steam tools, runtimes, SDKs, and redistributables
        if (appId is 228980 or 250820 or 1070560 or 1391110 or 896660 or 217030 or 244850)
            return true;

        if (name.Contains("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Dedicated Server", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Proton ", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(" SDK", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(" Tool", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Shared Resources", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
