using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BaoToolsGui.Services;
using BaoToolsGui.Models;

namespace BaoToolsGui.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AuthService _auth;
    private readonly SteamService _steam;
    private readonly UpdateService _updates;
    private readonly UpdateHistoryService _historyService;
    private readonly ToastService _toast;
    private readonly BaoToolsApiClient _api;
    private readonly PluginInstallerService _pluginInstaller;

    /// <summary>The first-run welcome overlay VM (hosted at the window root, shown via its IsOpen).</summary>
    public OnboardingViewModel Onboarding { get; }

    /// <summary>App version shown in the nav pane footer, e.g. "v1.0.1". Read from the assembly.</summary>
    public string VersionLabel { get; } = "v105.9";

    private static string ReadVersion()
    {
        // InformationalVersion carries the csproj <Version> (may have a "+commit" suffix. Trim it).
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var ver = info ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        int plus = ver.IndexOf('+');
        return plus >= 0 ? ver[..plus] : ver;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRealUser))]
    [NotifyPropertyChangedFor(nameof(FooterStatus))]
    [NotifyPropertyChangedFor(nameof(UserSubtitle))]
    private bool _isGuest = true;

    public bool IsRealUser => !IsGuest;

    /// <summary>Bottom-of-pane line: version plus auth state. No username shown (privacy).</summary>
    public string FooterStatus => $"{VersionLabel} · {(IsGuest ? Resources.Strings.Nav_Footer_Guest : Resources.Strings.Nav_Footer_LoggedIn)}";

    public string UserSubtitle => IsGuest
        ? Resources.Strings.Nav_SignInDiscord
        : $"{VersionLabel}";

    [ObservableProperty] private string? _displayName;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _avatarUrl;
    [ObservableProperty] private System.Windows.Media.ImageSource? _avatarSource;
    [ObservableProperty] private bool _hasAvatar;
    [ObservableProperty] private bool _isProfilePopupOpen;

    // Quota info in Profile Popup
    [ObservableProperty] private string? _dailyQuotaText;
    [ObservableProperty] private int _quotaUsed;
    [ObservableProperty] private int _quotaLimit = 10;
    [ObservableProperty] private double _quotaProgress;
    [ObservableProperty] private bool _isSupporter;
    [ObservableProperty] private bool _hasQuotaInfo;

    // Steam & Plugin status in Profile Popup
    [ObservableProperty] private bool _isSteamOnline;
    [ObservableProperty] private bool _isPluginOnline;
    [ObservableProperty] private string _steamStatusText = "Steam: Offline";
    [ObservableProperty] private string _pluginStatusText = "Plugin: Missing";

    public Action? RequestOpenSettings { get; set; }

    [ObservableProperty] private bool _isSigningIn;
    [ObservableProperty] private string? _signInError;

    // ── Update Notification state ──────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateContent))]
    [NotifyPropertyChangedFor(nameof(IsUpToDate))]
    private bool _hasUpdate;

    [ObservableProperty] private bool _hasUnreadNotification;
    [ObservableProperty] private bool _isNotificationOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateContent))]
    [NotifyPropertyChangedFor(nameof(IsUpToDate))]
    [NotifyPropertyChangedFor(nameof(HasUpdateError))]
    private bool _isCheckingUpdate;

    [ObservableProperty] private string? _latestVersion;
    [ObservableProperty] private string? _updateTitle;
    [ObservableProperty] private string? _updateChangelog;
    [ObservableProperty] private string _updateUrl = "https://baotools.baotranduy666666.workers.dev/";
    [ObservableProperty] private string _releaseNotesUrl = "https://github.com/DevBaor/BaoTools_1005/releases/latest";
    [ObservableProperty] private string? _updatePublishedAt;
    [ObservableProperty] private string _updateStatusMessage = Resources.Strings.Notification_UpToDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpToDate))]
    [NotifyPropertyChangedFor(nameof(HasUpdateError))]
    private string? _updateError;

    private GitHubReleaseInfo? _latestReleaseInfo;
    private PreparedUpdate? _preparedUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private string _updateButtonText = Resources.Strings.Notification_UpdateNow;

    public bool CanUpdate => !IsDownloadingUpdate;

    public bool HasUpdateContent => HasUpdate && !IsCheckingUpdate;
    public bool IsUpToDate => !HasUpdate && !IsCheckingUpdate && string.IsNullOrEmpty(UpdateError);
    public bool HasUpdateError => !string.IsNullOrEmpty(UpdateError) && !IsCheckingUpdate;

    /// <summary>History of past updates displayed in the notification bell flyout.</summary>
    public System.Collections.ObjectModel.ObservableCollection<UpdateHistoryItem> UpdateHistory { get; } = new();

    [ObservableProperty]
    private bool _hasUpdateHistory;

    public AiChatViewModel AiChat { get; }

    public MainViewModel(
        AuthService auth,
        SteamService steam,
        OnboardingViewModel onboarding,
        UpdateService updates,
        UpdateHistoryService historyService,
        ToastService toast,
        AiChatViewModel aiChat,
        BaoToolsApiClient api,
        PluginInstallerService pluginInstaller)
    {
        _auth = auth;
        _steam = steam;
        Onboarding = onboarding;
        _updates = updates;
        _historyService = historyService;
        _toast = toast;
        AiChat = aiChat;
        _api = api;
        _pluginInstaller = pluginInstaller;
        _auth.AuthStateChanged += RefreshAccount;
        SettingsViewModel.LanguageChanged += OnLanguageChanged;

        LoadLocalHistory();
    }

    public void RefreshAccount()
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(async () =>
        {
            IsGuest = _auth.IsGuest;
            DisplayName = _auth.IsGuest ? Resources.Strings.Nav_Footer_Guest : (_auth.DisplayName ?? "User");
            Email = _auth.Email;
            AvatarUrl = _auth.AvatarUrl;

            // System status
            try
            {
                IsSteamOnline = System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;
                IsPluginOnline = _pluginInstaller.IsInstalledLocally();
                UpdateSystemStatusStrings();
            }
            catch { }

            if (_auth.IsGuest || string.IsNullOrWhiteSpace(AvatarUrl))
            {
                AvatarSource = null;
                HasAvatar = false;
                DailyQuotaText = Resources.Strings.Home_DailyQuota_Guest;
                QuotaProgress = 0;
                HasQuotaInfo = true;
            }
            else
            {
                var src = await AvatarLoader.LoadAvatarAsync(AvatarUrl);
                AvatarSource = src;
                HasAvatar = src != null;
                _ = RefreshQuotaAsync();
            }

            OnPropertyChanged(nameof(FooterStatus));
            OnPropertyChanged(nameof(UserSubtitle));
        });
    }

    public async Task RefreshQuotaAsync()
    {
        if (_auth.IsGuest)
        {
            DailyQuotaText = Resources.Strings.Home_DailyQuota_Guest;
            QuotaProgress = 0;
            HasQuotaInfo = true;
            return;
        }

        try
        {
            var q = await _api.GetQuotaSummaryAsync();
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (q is not null)
                {
                    IsSupporter = q.IsSupporter;
                    QuotaUsed = q.Used;
                    QuotaLimit = q.Limit > 0 ? q.Limit : AppConfig.AppDailyDownloadLimit;
                    QuotaProgress = QuotaLimit > 0 ? Math.Clamp((double)QuotaUsed / QuotaLimit * 100.0, 0, 100) : 0;
                    DailyQuotaText = q.IsSupporter
                        ? string.Format(Resources.Strings.Home_DailyQuota_Supporter, q.Used)
                        : string.Format(Resources.Strings.Home_DailyQuota_Used, q.Used, QuotaLimit);
                    HasQuotaInfo = true;
                }
                else
                {
                    DailyQuotaText = Resources.Strings.Home_DailyQuota_Active;
                    HasQuotaInfo = true;
                }
            });
        }
        catch
        {
            DailyQuotaText = Resources.Strings.Home_DailyQuota_Active;
            HasQuotaInfo = true;
        }
    }

    [RelayCommand]
    private void ToggleProfile()
    {
        IsProfilePopupOpen = !IsProfilePopupOpen;
        if (IsProfilePopupOpen)
        {
            try
            {
                IsSteamOnline = System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;
                IsPluginOnline = _pluginInstaller.IsInstalledLocally();
                UpdateSystemStatusStrings();
            }
            catch { }
            if (!_auth.IsGuest)
            {
                _ = RefreshQuotaAsync();
            }
        }
    }

    private void UpdateSystemStatusStrings()
    {
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (lang == "vi")
        {
            SteamStatusText = IsSteamOnline ? "Steam: Đang chạy" : "Steam: Chưa mở";
            PluginStatusText = IsPluginOnline ? "Plugin: Đã cài" : "Plugin: Chưa cài";
        }
        else
        {
            SteamStatusText = IsSteamOnline ? "Steam: Online" : "Steam: Offline";
            PluginStatusText = IsPluginOnline ? "Plugin: Active" : "Plugin: Missing";
        }
    }

    [RelayCommand]
    private void OpenDiscordCommunity()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://discord.com/channels/1543559756504113152",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void CloseProfilePopup() => IsProfilePopupOpen = false;

    [RelayCommand]
    private void OpenSettingsFromProfile()
    {
        IsProfilePopupOpen = false;
        RequestOpenSettings?.Invoke();
    }

    [RelayCommand]
    private void SignOutFromProfile()
    {
        IsProfilePopupOpen = false;
        _auth.SignOut();
        _toast.Show(Resources.Strings.Settings_SignOut, Resources.Strings.Nav_Footer_Guest);
    }

    private void OnLanguageChanged()
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            LoadLocalHistory();
            UpdateButtonText = (_preparedUpdate != null && _preparedUpdate.IsValid)
                ? Resources.Strings.UpdateDialog_RestartNow
                : Resources.Strings.Notification_UpdateNow;
            UpdateStatusMessage = HasUpdate
                ? $"{Resources.Strings.Notification_NewUpdateAvailable} ({LatestVersion})"
                : string.Format(Resources.Strings.Notification_UpToDateDesc, VersionLabel);
            OnPropertyChanged(nameof(FooterStatus));
            OnPropertyChanged(nameof(UserSubtitle));
            if (IsGuest) DisplayName = Resources.Strings.Nav_Footer_Guest;
            UpdateSystemStatusStrings();
            _ = RefreshQuotaAsync();
        });
    }

    private void LoadLocalHistory()
    {
        try
        {
            var items = _historyService.LoadHistory(VersionLabel);
            UpdateHistory.Clear();
            foreach (var it in items)
            {
                UpdateHistory.Add(it);
            }
            HasUpdateHistory = UpdateHistory.Count > 0;
        }
        catch { }
    }

    public async Task InitializeAsync()
    {
        await _auth.InitializeAsync();
        RefreshAccount();
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsSigningIn) return;
        IsSigningIn = true;
        SignInError = null;
        try
        {
            await _auth.SignInAsync();
            IsProfilePopupOpen = false;
        }
        catch (Exception ex)
        {
            SignInError = ex.Message;
            _toast.Show(Resources.Strings.Nav_SignInDiscord, ex.Message);
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    /// <summary>Confirm, then kill + relaunch Steam so newly added/removed luas take effect.</summary>
    [RelayCommand]
    private void RestartSteam()
    {
        var result = ModernMessageBox.Show(
            Resources.Strings.Main_RestartSteam_Ask,
            Resources.Strings.Manage_RestartSteam_Title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        if (!_steam.RestartSteam())
            ModernMessageBox.Show(
                Resources.Strings.Manage_RestartSteam_Failed,
                Resources.Strings.Manage_RestartSteam_Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void ToggleNotification()
    {
        IsNotificationOpen = !IsNotificationOpen;
        if (IsNotificationOpen)
        {
            HasUnreadNotification = false;
        }
    }

    [RelayCommand]
    private void CloseNotification()
    {
        IsNotificationOpen = false;
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        await CheckForUpdatesInternalAsync(showToastIfUpToDate: true);
    }

    public async Task CheckForUpdatesInternalAsync(bool showToastIfUpToDate = false)
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        UpdateError = null;

        try
        {
            var info = await _updates.CheckGitHubReleaseFullAsync(VersionLabel);
            try
            {
                var remoteHistory = await _updates.FetchReleasesHistoryAsync(VersionLabel);
                var merged = _historyService.MergeWithRemoteReleases(remoteHistory, VersionLabel);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    UpdateHistory.Clear();
                    foreach (var item in merged)
                    {
                        UpdateHistory.Add(item);
                    }
                    HasUpdateHistory = UpdateHistory.Count > 0;
                });
            }
            catch { /* history fetch error doesn't block update check */ }

            if (info is null)
            {
                UpdateError = Resources.Strings.Notification_CheckFailed;
                if (showToastIfUpToDate)
                {
                    _toast.Show(Resources.Strings.Notification_Title, Resources.Strings.Notification_CheckFailed, error: true);
                }
                return;
            }

            if (info.IsNewer)
            {
                _latestReleaseInfo = info;
                HasUpdate = true;
                HasUnreadNotification = true;
                LatestVersion = info.TagName;
                UpdateTitle = string.IsNullOrWhiteSpace(info.Title) ? $"Release {info.TagName}" : info.Title;
                UpdateChangelog = info.Body;
                UpdateUrl = string.IsNullOrWhiteSpace(info.DownloadUrl) ? "https://baotools.baotranduy666666.workers.dev/" : info.DownloadUrl;
                ReleaseNotesUrl = string.IsNullOrWhiteSpace(info.HtmlUrl) ? "https://github.com/DevBaor/BaoTools_1005/releases/latest" : info.HtmlUrl;
                UpdatePublishedAt = info.PublishedAt?.ToString("MMM dd, yyyy");
                UpdateStatusMessage = $"{Resources.Strings.Notification_NewUpdateAvailable} ({info.TagName})";

                _toast.ShowAction(
                    Resources.Strings.Notification_Title,
                    $"{Resources.Strings.Notification_NewUpdateAvailable} ({info.TagName})!",
                    Resources.Strings.Notification_UpdateNow,
                    () => _ = DownloadUpdateAsync());
            }
            else
            {
                HasUpdate = false;
                LatestVersion = info.TagName;
                UpdateStatusMessage = string.Format(Resources.Strings.Notification_UpToDateDesc, VersionLabel);
                if (showToastIfUpToDate)
                {
                    _toast.Show(Resources.Strings.Notification_Title, $"{Resources.Strings.Notification_UpToDate} ({VersionLabel})");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateError = ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (IsDownloadingUpdate) return;
        if (_latestReleaseInfo is null)
        {
            OpenBrowserUrl(UpdateUrl);
            return;
        }

        // Close notification popup when opening progress dialog
        IsNotificationOpen = false;

        IsDownloadingUpdate = true;
        try
        {
            await Task.Yield();

            var prep = Views.UpdateProgressDialog.ShowDialog(
                _latestReleaseInfo,
                _updates,
                _preparedUpdate,
                UpdateUrl);

            if (prep != null && prep.IsValid)
            {
                _preparedUpdate = prep;
                UpdateButtonText = Resources.Strings.UpdateDialog_RestartNow;
            }
            else if (_preparedUpdate == null)
            {
                UpdateButtonText = Resources.Strings.Notification_UpdateNow;
            }
        }
        catch (Exception ex)
        {
            _toast.Show(Resources.Strings.Notification_Title, ex.Message, error: true);
            OpenBrowserUrl(UpdateUrl);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private void OpenBrowserUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ReleaseNotesUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
