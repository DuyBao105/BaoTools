using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BaoToolsGui.Services;

/// <summary>
/// Provides utilities to prevent Windows Defender and Antivirus from deleting or quarantining
/// OnlineFix files (such as OnlineFix64.dll). Includes Zone.Identifier unblocking, folder exclusions,
/// and automated backup/recovery for Online-Fix binaries.
/// </summary>
public static class WindowsDefenderService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFileW(string lpFileName);

    private static readonly string BackupRootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BaoToolsGui",
        "OnlineFixBackup");

    /// <summary>
    /// Removes the NTFS "Zone.Identifier" (Mark of the Web) alternate data stream from a file.
    /// This prevents Windows Defender and SmartScreen from treating the file as an untrusted
    /// internet download, drastically reducing false-positive deletion of OnlineFix DLLs.
    /// </summary>
    public static bool UnblockFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        try
        {
            // Deleting the :Zone.Identifier stream is native and requires no admin rights
            return DeleteFileW(filePath + ":Zone.Identifier");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Recursively strips Mark of the Web from all files in a directory.
    /// </summary>
    public static int UnblockDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath)) return 0;
        int count = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories))
            {
                if (UnblockFile(file)) count++;
            }
        }
        catch { }
        return count;
    }

    /// <summary>
    /// Adds a folder path to the Windows Defender Exclusion list via PowerShell Add-MpPreference.
    /// Uses elevated privileges (UAC) so Windows Defender permits the exclusion.
    /// </summary>
    public static async Task<bool> AddFolderExclusionAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return false;
        return await AddFoldersExclusionAsync([folderPath]);
    }

    /// <summary>
    /// Adds multiple folders to Windows Defender exclusions in a single command (at most one UAC prompt).
    /// </summary>
    public static async Task<bool> AddFoldersExclusionAsync(IEnumerable<string> folderPaths)
    {
        var validPaths = folderPaths
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Select(p => Path.GetFullPath(p).TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validPaths.Count == 0) return false;

        return await Task.Run(() =>
        {
            try
            {
                // Build PowerShell command: Add-MpPreference -ExclusionPath 'p1','p2'
                string joined = string.Join(",", validPaths.Select(p => $"'{p.Replace("'", "''")}'"));
                string script = $"Add-MpPreference -ExclusionPath {joined} -ErrorAction SilentlyContinue";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                    Verb = "runas", // Elevation required for Windows Defender exclusions
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc is null) return false;
                proc.WaitForExit(6000);
                return proc.ExitCode == 0;
            }
            catch
            {
                // User might have cancelled UAC, or Defender service is inactive
                return false;
            }
        });
    }

    /// <summary>
    /// Backs up Online-Fix essential files (OnlineFix64.dll, OnlineFix.ini, SteamOverlay.dll)
    /// to a protected AppData location so they can be restored if Defender deletes them.
    /// </summary>
    public static void BackupOnlineFixFiles(long appId, string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return;

        try
        {
            string backupDir = Path.Combine(BackupRootDir, appId.ToString());

            var targets = new[] { "OnlineFix64.dll", "OnlineFix.dll", "OnlineFix.ini", "SteamOverlay64.dll", "SteamOverlay.dll" };
            foreach (string target in targets)
            {
                var matches = Directory.EnumerateFiles(gameRoot, target, SearchOption.AllDirectories).ToList();
                foreach (string match in matches)
                {
                    string rel = Path.GetRelativePath(gameRoot, match);
                    string dest = Path.Combine(backupDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(match, dest, overwrite: true);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Checks if a game previously had OnlineFix (or has OnlineFix.ini), but OnlineFix64.dll is missing
    /// (typically eaten by Windows Defender). If so, automatically restores it from the backup!
    /// </summary>
    /// <returns>True if at least one file was restored from backup.</returns>
    public static bool TryRestoreOnlineFixFiles(long appId, string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return false;

        try
        {
            string backupDir = Path.Combine(BackupRootDir, appId.ToString());
            if (!Directory.Exists(backupDir)) return false;

            bool restored = false;
            foreach (string backupFile in Directory.EnumerateFiles(backupDir, "*.*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(backupDir, backupFile);
                string targetPath = Path.Combine(gameRoot, rel);

                if (!File.Exists(targetPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(backupFile, targetPath, overwrite: true);
                    UnblockFile(targetPath);
                    restored = true;
                }
            }

            if (restored)
            {
                // Auto-unblock and queue exclusion for the game directory
                UnblockDirectory(gameRoot);
                _ = AddFolderExclusionAsync(gameRoot);
            }

            return restored;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether an OnlineFix backup exists for this appId.
    /// </summary>
    public static bool HasBackup(long appId)
    {
        try
        {
            string backupDir = Path.Combine(BackupRootDir, appId.ToString());
            return Directory.Exists(backupDir) && Directory.EnumerateFiles(backupDir, "*.*", SearchOption.AllDirectories).Any();
        }
        catch { return false; }
    }
}
