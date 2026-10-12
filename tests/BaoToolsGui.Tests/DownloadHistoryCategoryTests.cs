using BaoToolsGui.Services.Downloads;
using Xunit;

namespace BaoToolsGui.Tests;

public class DownloadHistoryCategoryTests
{
    [Fact]
    public void HistoryItem_KindDepot_IsGameDownload()
    {
        var record = new DownloadHistoryRecord(
            Id: "test:1",
            Kind: nameof(DownloadKind.Depot),
            AppId: 10,
            Title: "Counter-Strike",
            SubTitle: "Game files",
            Bytes: 500000000,
            Status: nameof(DownloadStatus.Completed),
            Message: null,
            CompletedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var entry = new DownloadHistoryEntry(record);
        Assert.True(entry.IsGameDownload);
        Assert.False(entry.IsLuaUnlock);
    }

    [Fact]
    public void HistoryItem_KindLua_IsLuaUnlock()
    {
        var record = new DownloadHistoryRecord(
            Id: "test:2",
            Kind: "Lua",
            AppId: 730,
            Title: "CS:GO Lua",
            SubTitle: "Lua unlock",
            Bytes: 1024,
            Status: nameof(DownloadStatus.Completed),
            Message: null,
            CompletedAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var entry = new DownloadHistoryEntry(record);
        Assert.False(entry.IsGameDownload);
        Assert.True(entry.IsLuaUnlock);
    }

    [Fact]
    public void AllUsedSymbols_ExistInSymbolRegular()
    {
        Assert.True(Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), "FolderOpenDown24"));
        Assert.True(Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), "Alert24"));
        Assert.True(Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), "History24"));
    }
}
