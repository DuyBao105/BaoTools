using System.IO;
using BaoToolsGui.Services;
using Xunit;

namespace BaoToolsGui.Tests;

public class WindowsDefenderServiceTests
{
    [Fact]
    public void UnblockFile_NonExistentFile_ReturnsFalse()
    {
        bool result = WindowsDefenderService.UnblockFile(@"C:\NonExistentFile_xyz_12345.dll");
        Assert.False(result);
    }

    [Fact]
    public void UnblockDirectory_ValidEmptyFolder_ReturnsZero()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BaoTools_DefenderTest_" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempDir);
            int count = WindowsDefenderService.UnblockDirectory(tempDir);
            Assert.Equal(0, count);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BackupAndRestore_Lifecycle_WorksCorrectly()
    {
        long testAppId = 9999998;
        string tempGameDir = Path.Combine(Path.GetTempPath(), "BaoTools_GameTest_" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempGameDir);
            string fixDll = Path.Combine(tempGameDir, "OnlineFix64.dll");
            string fixIni = Path.Combine(tempGameDir, "OnlineFix.ini");
            File.WriteAllText(fixDll, "dummy dll content");
            File.WriteAllText(fixIni, "[Main]\nRealAppId=480");

            // Backup files
            WindowsDefenderService.BackupOnlineFixFiles(testAppId, tempGameDir);

            // Simulate Windows Defender deleting OnlineFix64.dll
            File.Delete(fixDll);
            Assert.False(File.Exists(fixDll));

            // Auto-recovery should restore it
            bool restored = WindowsDefenderService.TryRestoreOnlineFixFiles(testAppId, tempGameDir);
            Assert.True(restored);
            Assert.True(File.Exists(fixDll));
            Assert.Equal("dummy dll content", File.ReadAllText(fixDll));
        }
        finally
        {
            if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true);
        }
    }
}
