using System.IO;
using BaoToolsGui.Services;
using Xunit;

namespace BaoToolsGui.Tests;

public class GameLauncherServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GameLauncherService _launcher;

    public GameLauncherServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BaoTools_LauncherTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var settings = new SettingsService(Path.Combine(_tempDir, "test_settings.json"));
        var steam = new SteamService(settings);
        _launcher = new GameLauncherService(steam);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* ignored */ }
    }

    [Fact]
    public void InspectFolder_NonExistent_ReturnsNoExe()
    {
        var info = _launcher.InspectFolder(Path.Combine(_tempDir, "nonexistent"));
        Assert.False(info.ExeFound);
        Assert.Null(info.ExecutablePath);
        Assert.False(info.HasOnlineFix);
        Assert.False(info.HasDenuvoFix);
        Assert.False(info.RequiresSteam);
    }

    [Fact]
    public void FindGameExecutable_SimpleRootExe_Found()
    {
        string exe = Path.Combine(_tempDir, "Game.exe");
        File.WriteAllBytes(exe, new byte[1024 * 1024]); // 1MB

        var found = _launcher.FindGameExecutable(_tempDir, "Game");
        Assert.NotNull(found);
        Assert.Equal(exe, found);
    }

    [Fact]
    public void FindGameExecutable_PrefersUnrealShippingOverCrashReporterAndRedist()
    {
        // Root has a launcher and redistributables
        string launcherExe = Path.Combine(_tempDir, "Launcher.exe");
        File.WriteAllBytes(launcherExe, new byte[500 * 1024]);

        string redistExe = Path.Combine(_tempDir, "vcredist_x64.exe");
        File.WriteAllBytes(redistExe, new byte[10 * 1024 * 1024]);

        string crashExe = Path.Combine(_tempDir, "CrashReportClient.exe");
        File.WriteAllBytes(crashExe, new byte[1024 * 1024]);

        // Deep Binaries/Win64 has the shipping binary
        string binDir = Path.Combine(_tempDir, "EngineName", "Binaries", "Win64");
        Directory.CreateDirectory(binDir);

        string shippingExe = Path.Combine(binDir, "Game-Win64-Shipping.exe");
        File.WriteAllBytes(shippingExe, new byte[20 * 1024 * 1024]); // 20MB

        var found = _launcher.FindGameExecutable(_tempDir, "Game");
        Assert.NotNull(found);
        Assert.Equal(shippingExe, found);
    }

    [Fact]
    public void FindGameExecutable_MatchesGameName_OverGeneric()
    {
        string genericExe = Path.Combine(_tempDir, "Run.exe");
        File.WriteAllBytes(genericExe, new byte[5 * 1024 * 1024]);

        string namedExe = Path.Combine(_tempDir, "BlackMythWukong.exe");
        File.WriteAllBytes(namedExe, new byte[5 * 1024 * 1024]);

        var found = _launcher.FindGameExecutable(_tempDir, "Black Myth: Wukong");
        Assert.NotNull(found);
        Assert.Equal(namedExe, found);
    }

    [Fact]
    public void IsOnlineFixPresent_DetectsIniAndDll()
    {
        Assert.False(GameLauncherService.IsOnlineFixPresent(_tempDir));

        string iniPath = Path.Combine(_tempDir, "OnlineFix.ini");
        File.WriteAllText(iniPath, "[Main]\nRealAppId=480");

        Assert.True(GameLauncherService.IsOnlineFixPresent(_tempDir));

        File.Delete(iniPath);
        Assert.False(GameLauncherService.IsOnlineFixPresent(_tempDir));

        // Subdirectory check
        string binDir = Path.Combine(_tempDir, "Binaries", "Win64");
        Directory.CreateDirectory(binDir);
        File.WriteAllBytes(Path.Combine(binDir, "OnlineFix64.dll"), [0x4D, 0x5A]);

        Assert.True(GameLauncherService.IsOnlineFixPresent(_tempDir));
    }

    [Fact]
    public void IsDenuvoFixPresent_DetectsLuatoolsFixFolder()
    {
        Assert.False(GameLauncherService.IsDenuvoFixPresent(_tempDir));

        string fixDir = Path.Combine(_tempDir, ".luatools-fix");
        Directory.CreateDirectory(fixDir);

        // Empty directory is not sufficient
        Assert.False(GameLauncherService.IsDenuvoFixPresent(_tempDir));

        // Adding manifest json triggers detection
        File.WriteAllText(Path.Combine(fixDir, "manifest.json"), "{}");
        Assert.True(GameLauncherService.IsDenuvoFixPresent(_tempDir));
    }

    [Fact]
    public void InspectFolder_SetsRequiresSteam_WhenOnlineFixPresent()
    {
        string exe = Path.Combine(_tempDir, "Game.exe");
        File.WriteAllBytes(exe, new byte[1024 * 1024]);

        var infoNoFix = _launcher.InspectFolder(_tempDir, "Game");
        Assert.True(infoNoFix.ExeFound);
        Assert.False(infoNoFix.RequiresSteam);
        Assert.False(infoNoFix.HasOnlineFix);

        File.WriteAllText(Path.Combine(_tempDir, "OnlineFix.ini"), "[Main]");
        var infoWithFix = _launcher.InspectFolder(_tempDir, "Game");
        Assert.True(infoWithFix.ExeFound);
        Assert.True(infoWithFix.HasOnlineFix);
        Assert.True(infoWithFix.RequiresSteam);
    }

    [Fact]
    public void Settings_ExitOnPlay_PersistsAndDefaultsToFalse()
    {
        var settings = new SettingsService(Path.Combine(_tempDir, "test_exit_settings.json"));
        Assert.False(settings.ExitOnPlay);

        settings.ExitOnPlay = true;
        Assert.True(settings.ExitOnPlay);

        // Reload to verify persistence
        var reloaded = new SettingsService(Path.Combine(_tempDir, "test_exit_settings.json"));
        Assert.True(reloaded.ExitOnPlay);
    }

    [Fact]
    public async Task LaunchGameAsync_NonExistentExe_ReturnsFalse()
    {
        var (success, error) = await _launcher.LaunchGameAsync(Path.Combine(_tempDir, "DoesNotExist.exe"));
        Assert.False(success);
        Assert.Equal("Executable file not found.", error);
    }

    [Fact]
    public void StartSteam_ExposesMethodSafely()
    {
        // Simply ensure StartSteam delegate exists and can be invoked
        var method = typeof(GameLauncherService).GetMethod(nameof(GameLauncherService.StartSteam));
        Assert.NotNull(method);
    }
}
