using System.IO;
using BaoToolsGui.Services;
using BaoToolsGui.Services.Downloads;
using Xunit;

namespace BaoToolsGui.Tests;

public class SteamLibraryRawDepotTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cacheFile;
    private readonly CacheService _cache;
    private readonly SteamService _steam;

    public SteamLibraryRawDepotTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BaoTools_DepotLibTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _cacheFile = Path.Combine(_tempDir, "cache.json");
        _cache = new CacheService(_cacheFile);

        var settings = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        _steam = new SteamService(settings);
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
    public void ResolveGameRootFolder_NestedSubfolder_ResolvesSubfolder()
    {
        // Structure like: DepotOutDir/How to Fish/How to Fish.exe
        string depotOutDir = Path.Combine(_tempDir, "Depots", "4001890");
        string gameFolder = Path.Combine(depotOutDir, "How to Fish");
        Directory.CreateDirectory(gameFolder);

        string exePath = Path.Combine(gameFolder, "How to Fish.exe");
        File.WriteAllBytes(exePath, new byte[1024 * 1024]);

        string resolved = SteamLibraryService.ResolveGameRootFolder(depotOutDir);
        Assert.Equal(gameFolder, resolved);
    }

    [Fact]
    public void ResolveGameRootFolder_FlatFolder_ResolvesDepotRoot()
    {
        string depotOutDir = Path.Combine(_tempDir, "Depots", "12345");
        Directory.CreateDirectory(depotOutDir);

        string exePath = Path.Combine(depotOutDir, "Game.exe");
        File.WriteAllBytes(exePath, new byte[1024 * 1024]);

        string resolved = SteamLibraryService.ResolveGameRootFolder(depotOutDir);
        Assert.Equal(depotOutDir, resolved);
    }

    [Fact]
    public void GetInstallDir_FindsRawDepotGameFromCache()
    {
        long appId = 4001890;
        string depotOutDir = Path.Combine(_tempDir, "Depots", "4001890");
        string gameFolder = Path.Combine(depotOutDir, "How to Fish");
        Directory.CreateDirectory(gameFolder);

        string exePath = Path.Combine(gameFolder, "How to Fish.exe");
        File.WriteAllBytes(exePath, new byte[1024 * 1024]);

        var record = new DownloadHistoryRecord(
            Id: "test-depot-1",
            Kind: "depot",
            AppId: appId,
            Title: "How to Fish",
            SubTitle: "Depot Download",
            Bytes: 1024 * 1024,
            Status: "Completed",
            Message: null,
            CompletedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RevealPath: depotOutDir);

        _cache.SaveDownloadHistory([record]);

        var library = new SteamLibraryService(_steam, _cache);
        string? installDir = library.GetInstallDir(appId);

        Assert.NotNull(installDir);
        Assert.Equal(gameFolder, installDir);
    }

    [Fact]
    public void GetAllInstalledApps_IncludesRawDepotGame()
    {
        long appId = 999999;
        string depotOutDir = Path.Combine(_tempDir, "Depots", "999999");
        Directory.CreateDirectory(depotOutDir);
        string exe = Path.Combine(depotOutDir, "CustomGame.exe");
        File.WriteAllBytes(exe, new byte[2 * 1024 * 1024]);

        var record = new DownloadHistoryRecord(
            Id: "test-depot-2",
            Kind: "depot",
            AppId: appId,
            Title: "Custom Game",
            SubTitle: "Depot",
            Bytes: 2 * 1024 * 1024,
            Status: "Completed",
            Message: null,
            CompletedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RevealPath: depotOutDir);

        _cache.SaveDownloadHistory([record]);

        var library = new SteamLibraryService(_steam, _cache);
        var apps = library.GetAllInstalledApps().ToList();

        Assert.Contains(apps, a => a.AppId == appId && a.Name == "Custom Game" && a.InstallDir == depotOutDir);
    }

    [Theory]
    [InlineData("Depot")]
    [InlineData("depot")]
    [InlineData("raw_depot")]
    [InlineData("DEPOT")]
    public void IsDepotKind_RecognizesVariousForms(string kind)
    {
        Assert.True(SteamLibraryService.IsDepotKind(kind));
    }

    [Fact]
    public void GetInstallDir_TitleCaseDepotKind_FindsRawDepotGameFromCache()
    {
        long appId = 888888;
        string depotOutDir = Path.Combine(_tempDir, "Depots", "888888");
        Directory.CreateDirectory(depotOutDir);
        string gameFolder = Path.Combine(depotOutDir, "Game888");
        Directory.CreateDirectory(gameFolder);
        File.WriteAllBytes(Path.Combine(gameFolder, "Game.exe"), new byte[1024 * 1024]);

        var record = new DownloadHistoryRecord(
            Id: "test-depot-titlecase",
            Kind: "Depot", // TitleCase as written by DownloadKind.Depot
            AppId: appId,
            Title: "Game 888",
            SubTitle: "Depot",
            Bytes: 1024 * 1024,
            Status: "Completed",
            Message: null,
            CompletedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RevealPath: depotOutDir);

        _cache.SaveDownloadHistory([record]);

        var library = new SteamLibraryService(_steam, _cache);
        string? installDir = library.GetInstallDir(appId);

        Assert.NotNull(installDir);
        Assert.Equal(gameFolder, installDir);
    }
}
