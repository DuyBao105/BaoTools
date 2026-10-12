using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BaoToolsGui.Resources;
using BaoToolsGui.Services;
using Wpf.Ui.Controls;

namespace BaoToolsGui.Views;

public partial class SplashWindow : FluentWindow
{
    private readonly AuthService _auth;
    private readonly SteamService _steam;

    // Fluent State Brushes
    private static readonly SolidColorBrush PurpleBrush = new(Color.FromRgb(0x8B, 0x5C, 0xF6));
    private static readonly SolidColorBrush CyanBrush = new(Color.FromRgb(0x38, 0xBD, 0xF8));
    private static readonly SolidColorBrush IndigoBrush = new(Color.FromRgb(0x81, 0x8C, 0xF8));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));

    public SplashWindow(AuthService auth, SteamService steam)
    {
        _auth = auth;
        _steam = steam;
        InitializeComponent();
    }

    public async Task WaitForCompletionAsync()
    {
        // 1. Kick off background auth session restore in parallel
        var authTask = _auth.InitializeAsync();

        // 2. Animate progress bar smoothly from 0% to 100%
        const double trackWidth = 418.0;
        int steps = 50;
        int intervalMs = 26; // ~1.3s total animation time

        for (int i = 1; i <= steps; i++)
        {
            double percent = (double)i / steps * 100.0;
            double currentWidth = (percent / 100.0) * trackWidth;
            ProgressFill.Width = currentWidth;
            PercentText.Text = $"{(int)percent}%";

            if (percent < 35)
            {
                StatusText.Text = Strings.Splash_Initializing;
                if (StatusIcon.Symbol != SymbolRegular.ArrowSync24)
                {
                    StatusIcon.Symbol = SymbolRegular.ArrowSync24;
                    StatusIcon.Foreground = PurpleBrush;
                }
            }
            else if (percent < 75)
            {
                StatusText.Text = Strings.Splash_CheckingEnv;
                if (StatusIcon.Symbol != SymbolRegular.ShieldCheckmark24)
                {
                    StatusIcon.Symbol = SymbolRegular.ShieldCheckmark24;
                    StatusIcon.Foreground = CyanBrush;
                }
            }
            else if (percent < 100)
            {
                StatusText.Text = Strings.Splash_Preparing;
                if (StatusIcon.Symbol != SymbolRegular.DesktopPulse24)
                {
                    StatusIcon.Symbol = SymbolRegular.DesktopPulse24;
                    StatusIcon.Foreground = IndigoBrush;
                }
            }

            await Task.Delay(intervalMs);
        }

        // Snap to exactly 100%
        ProgressFill.Width = trackWidth;
        PercentText.Text = "100%";

        // Ensure auth check has settled (capped at 1.5s max so slow network never hangs startup)
        try { await Task.WhenAny(authTask, Task.Delay(1500)); } catch { /* offline or guest fallback */ }

        // Determine user identity: Discord DisplayName or Steam Persona
        bool isSignedIn = _auth.IsSignedIn;
        string? displayName = _auth.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = SteamService.SteamPersonaName;
        }

        // Display user pill if logged in
        if (isSignedIn && !string.IsNullOrWhiteSpace(displayName))
        {
            UserGreetingText.Text = displayName;
            UserBadgeText.Text = "ONLINE";
            UserCard.Visibility = Visibility.Visible;

            // Load Avatar if available (capped at 800ms)
            if (!string.IsNullOrWhiteSpace(_auth.AvatarUrl))
            {
                try
                {
                    var avatarTask = AvatarLoader.LoadAvatarAsync(_auth.AvatarUrl);
                    var completedTask = await Task.WhenAny(avatarTask, Task.Delay(800));
                    if (completedTask == avatarTask)
                    {
                        var avatarSrc = await avatarTask;
                        if (avatarSrc != null)
                        {
                            UserAvatarImage.Source = avatarSrc;
                        }
                    }
                }
                catch { /* fallback to default icon */ }
            }

            StatusText.Text = string.Format(Strings.Splash_WelcomeUser, displayName);
        }
        else
        {
            StatusText.Text = Strings.Splash_WelcomeGuest;
        }

        // Highlight Welcome status with emerald green checkmark
        StatusText.Foreground = Brushes.White;
        StatusText.FontWeight = FontWeights.SemiBold;
        StatusIcon.Symbol = SymbolRegular.CheckmarkCircle24;
        StatusIcon.Foreground = SuccessBrush;

        // Pause for user to read the Welcome message
        await Task.Delay(600);

        // Smooth fade out with guaranteed timeout so startup never stalls
        try
        {
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
            var tcs = new TaskCompletionSource<bool>();
            fade.Completed += (_, _) => tcs.TrySetResult(true);
            BeginAnimation(OpacityProperty, fade);
            await Task.WhenAny(tcs.Task, Task.Delay(300));
        }
        catch { }
        finally
        {
            Close();
        }
    }
}
