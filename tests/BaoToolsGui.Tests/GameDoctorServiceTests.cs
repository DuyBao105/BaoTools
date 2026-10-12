using System.Diagnostics;
using System.IO;
using BaoToolsGui.Services;
using Xunit;

namespace BaoToolsGui.Tests;

public class GameDoctorServiceTests : IDisposable
{
    private readonly string _tempDir;

    public GameDoctorServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BaoTools_GameDoctorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public void AppSettings_Default_AutoGameDoctor_IsNullInitially_AndSettingsServiceDefaultsToTrue()
    {
        var settings = new AppSettings();
        Assert.Null(settings.AutoGameDoctor);

        string settingsFile = Path.Combine(_tempDir, "fresh_settings.json");
        var svc = new SettingsService(settingsFile);
        Assert.True(svc.AutoGameDoctor);
    }

    [Fact]
    public void SettingsService_FreshInstall_DefaultsAutoGameDoctorToTrue()
    {
        string settingsFile = Path.Combine(_tempDir, "settings.json");
        var svc = new SettingsService(settingsFile);

        Assert.True(svc.AutoGameDoctor);
    }

    [Fact]
    public void SettingsService_CanTurnOffAutoGameDoctor_AndPersist()
    {
        string settingsFile = Path.Combine(_tempDir, "settings.json");
        var svc = new SettingsService(settingsFile);

        Assert.True(svc.AutoGameDoctor);

        svc.AutoGameDoctor = false;
        Assert.False(svc.AutoGameDoctor);

        // Reload from disk
        var reloaded = new SettingsService(settingsFile);
        Assert.False(reloaded.AutoGameDoctor);

        // Turn back on
        reloaded.AutoGameDoctor = true;
        Assert.True(reloaded.AutoGameDoctor);

        var reloaded2 = new SettingsService(settingsFile);
        Assert.True(reloaded2.AutoGameDoctor);
    }

    [Fact]
    public void DiagnoseExe_OnCurrentProcess_DetectsValidArchitectureAndImports()
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            return;

        var doctor = new GameDoctorService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
        var result = doctor.DiagnoseExe(currentExe);

        Assert.True(File.Exists(result.ExePath));
        Assert.NotEqual(PeArchitecture.Unknown, result.Architecture);
        Assert.True(result.TotalImportedDlls > 0);
        Assert.NotNull(result.Dependencies);
    }

    [Fact]
    public void IsExcludedExe_FiltersHelperAndRedistExecutables()
    {
        Assert.True(GameLauncherService.IsExcludedExe("UnityCrashHandler64.exe"));
        Assert.True(GameLauncherService.IsExcludedExe("vc_redist.x64.exe"));
        Assert.True(GameLauncherService.IsExcludedExe("dxsetup.exe"));
        Assert.True(GameLauncherService.IsExcludedExe("unins000.exe"));
        Assert.False(GameLauncherService.IsExcludedExe("EldenRing.exe"));
        Assert.False(GameLauncherService.IsExcludedExe("Cyberpunk2077.exe"));
    }

    [Fact]
    public void SettingsView_AllStaticResources_AreDeclaredInResources()
    {
        string? projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        string settingsXaml = Path.Combine(projectRoot, "src", "BaoToolsGui", "Views", "SettingsView.xaml");
        string appXaml = Path.Combine(projectRoot, "src", "BaoToolsGui", "App.xaml");
        string tokensXaml = Path.Combine(projectRoot, "src", "BaoToolsGui", "AppTokens.xaml");

        if (!File.Exists(settingsXaml) || !File.Exists(appXaml)) return;

        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Standard WPF / framework keys
            "FluentFocusVisual"
        };

        // Extract x:Key attributes
        var keyRegex = new System.Text.RegularExpressions.Regex(@"x:Key\s*=\s*""([^""]+)""");
        foreach (string file in new[] { appXaml, tokensXaml, settingsXaml })
        {
            if (File.Exists(file))
            {
                foreach (System.Text.RegularExpressions.Match m in keyRegex.Matches(File.ReadAllText(file)))
                {
                    knownKeys.Add(m.Groups[1].Value);
                }
            }
        }

        // Find all {StaticResource Key} in SettingsView.xaml
        var resRegex = new System.Text.RegularExpressions.Regex(@"\{StaticResource\s+([A-Za-z0-9_]+)\}");
        var missingKeys = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in resRegex.Matches(File.ReadAllText(settingsXaml)))
        {
            string key = m.Groups[1].Value;
            if (!knownKeys.Contains(key))
            {
                missingKeys.Add(key);
            }
        }

        Assert.Empty(missingKeys);
    }
}
