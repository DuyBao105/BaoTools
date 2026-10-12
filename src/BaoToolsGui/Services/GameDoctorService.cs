using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BaoToolsGui.Services;

public enum PeArchitecture
{
    Unknown,
    X86,
    X64,
    Arm64
}

public enum RuntimePackageType
{
    VcRedist2015_2022_x64,
    VcRedist2015_2022_x86,
    VcRedist2013_x64,
    VcRedist2013_x86,
    VcRedist2012_x64,
    VcRedist2012_x86,
    DirectX_June2010
}

public class DllDependencyInfo
{
    public required string DllName { get; init; }
    public bool IsPresent { get; set; }
    public bool FoundInGameDir { get; set; }
    public bool FoundInSystem { get; set; }
    public bool ArchitectureMismatch { get; set; }
    public string? FoundPath { get; set; }
    public string? RequiredPackage { get; set; }
    public bool IsSystemCore { get; set; }
}

public class GameDiagnosisResult
{
    public required string ExePath { get; init; }
    public string? GameName { get; set; }
    public PeArchitecture Architecture { get; init; }
    public int TotalImportedDlls { get; set; }
    public List<DllDependencyInfo> Dependencies { get; set; } = [];
    public List<DllDependencyInfo> MissingDlls { get; set; } = [];
    public HashSet<RuntimePackageType> MissingPackages { get; set; } = [];
    public bool HasArchitectureMismatch { get; set; }
    public bool IsHealthy => MissingDlls.Count == 0 && !HasArchitectureMismatch;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Game Doctor Service: Inspects game binaries via PE header analysis, verifies runtime dependencies
/// (VC++ Redistributables, DirectX runtimes), identifies architecture mismatches, and can automatically
/// heal missing environments silently in the background.
/// </summary>
public class GameDoctorService
{
    private readonly SettingsService _settings;
    private readonly ToastService? _toast;
    private readonly ILogger<GameDoctorService>? _log;

    private static readonly string RedistDownloadCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BaoToolsGui", "RedistCache");

    // Standard Windows core OS DLLs that are built into every modern Windows installation.
    private static readonly HashSet<string> CoreSystemDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel32.dll", "kernelbase.dll", "ntdll.dll", "user32.dll", "gdi32.dll", "gdi32full.dll",
        "advapi32.dll", "shell32.dll", "ole32.dll", "oleaut32.dll", "comdlg32.dll", "comctl32.dll",
        "shlwapi.dll", "ws2_32.dll", "wsock32.dll", "wininet.dll", "winhttp.dll", "version.dll",
        "imm32.dll", "uxtheme.dll", "dwmapi.dll", "crypt32.dll", "bcrypt.dll", "iphlpapi.dll",
        "setupapi.dll", "cfgmgr32.dll", "msvcrt.dll", "ucrtbase.dll", "sechost.dll", "rpcrt4.dll",
        "msimg32.dll", "winmm.dll", "dinput8.dll", "dxgi.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll"
    };

    public GameDoctorService(
        SettingsService settings,
        ToastService? toast = null,
        ILogger<GameDoctorService>? log = null)
    {
        _settings = settings;
        _toast = toast;
        _log = log;
    }

    /// <summary>
    /// Inspects the PE header of an executable and returns its architecture and list of imported DLLs.
    /// Pure C# implementation with zero external dependencies.
    /// </summary>
    public static (PeArchitecture Arch, List<string> ImportedDlls, string? Error) InspectPeHeader(string filePath)
    {
        var dlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return (PeArchitecture.Unknown, [], "File not found");

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            if (fs.Length < 64) return (PeArchitecture.Unknown, [], "File too small");

            // DOS Header
            ushort magic = reader.ReadUInt16();
            if (magic != 0x5A4D) return (PeArchitecture.Unknown, [], "Not a valid PE/MZ executable");

            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset + 24 > fs.Length) return (PeArchitecture.Unknown, [], "Invalid PE header offset");

            fs.Seek(peOffset, SeekOrigin.Begin);
            uint peSig = reader.ReadUInt32();
            if (peSig != 0x00004550) return (PeArchitecture.Unknown, [], "Invalid PE signature");

            // COFF File Header
            ushort machine = reader.ReadUInt16();
            var arch = machine switch
            {
                0x8664 => PeArchitecture.X64,
                0x014C => PeArchitecture.X86,
                0xAA64 => PeArchitecture.Arm64,
                _ => PeArchitecture.Unknown
            };

            ushort numberOfSections = reader.ReadUInt16();
            reader.ReadUInt32(); // TimeDateStamp
            reader.ReadUInt32(); // PointerToSymbolTable
            reader.ReadUInt32(); // NumberOfSymbols
            ushort sizeOfOptionalHeader = reader.ReadUInt16();
            reader.ReadUInt16(); // Characteristics

            if (sizeOfOptionalHeader == 0) return (arch, [], null);

            long optHeaderStart = fs.Position;
            ushort optMagic = reader.ReadUInt16();
            bool is64Bit = optMagic == 0x20B;

            // Offset to DataDirectory (96 for PE32, 112 for PE32+)
            int dataDirOffset = is64Bit ? 112 : 96;
            fs.Seek(optHeaderStart + dataDirOffset, SeekOrigin.Begin);

            // Import Table is entry index 1 (skip export entry 0)
            reader.ReadUInt32(); // Export RVA
            reader.ReadUInt32(); // Export Size
            uint importRva = reader.ReadUInt32();
            uint importSize = reader.ReadUInt32();

            if (importRva == 0 || importSize == 0) return (arch, [], null);

            // Seek to Section Headers (right after Optional Header)
            fs.Seek(optHeaderStart + sizeOfOptionalHeader, SeekOrigin.Begin);
            var sections = new List<(uint VirtAddr, uint VirtSize, uint RawAddr, uint RawSize)>();
            for (int i = 0; i < numberOfSections; i++)
            {
                reader.ReadBytes(8); // Name
                uint virtSize = reader.ReadUInt32();
                uint virtAddr = reader.ReadUInt32();
                uint rawSize = reader.ReadUInt32();
                uint rawAddr = reader.ReadUInt32();
                reader.ReadBytes(16); // Relocs, Lines, Characteristics
                sections.Add((virtAddr, virtSize, rawAddr, rawSize));
            }

            long RvaToOffset(uint rva)
            {
                foreach (var sec in sections)
                {
                    uint span = Math.Max(sec.VirtSize, sec.RawSize);
                    if (rva >= sec.VirtAddr && rva < sec.VirtAddr + span)
                        return sec.RawAddr + (rva - sec.VirtAddr);
                }
                return -1;
            }

            long importOffset = RvaToOffset(importRva);
            if (importOffset < 0 || importOffset >= fs.Length) return (arch, [], null);

            fs.Seek(importOffset, SeekOrigin.Begin);

            // Read IMAGE_IMPORT_DESCRIPTOR array (20 bytes each)
            var nameRvas = new List<uint>();
            while (fs.Position + 20 <= fs.Length)
            {
                uint origFirstThunk = reader.ReadUInt32();
                uint ts = reader.ReadUInt32();
                uint forwarder = reader.ReadUInt32();
                uint nameRva = reader.ReadUInt32();
                uint firstThunk = reader.ReadUInt32();

                if (origFirstThunk == 0 && ts == 0 && forwarder == 0 && nameRva == 0 && firstThunk == 0)
                    break;

                if (nameRva != 0) nameRvas.Add(nameRva);
            }

            foreach (var nameRva in nameRvas)
            {
                long nameOffset = RvaToOffset(nameRva);
                if (nameOffset >= 0 && nameOffset < fs.Length)
                {
                    fs.Seek(nameOffset, SeekOrigin.Begin);
                    var sb = new StringBuilder();
                    int b;
                    while ((b = fs.ReadByte()) > 0 && sb.Length < 260)
                    {
                        sb.Append((char)b);
                    }
                    string dll = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(dll)) dlls.Add(dll);
                }
            }

            return (arch, dlls.OrderBy(x => x).ToList(), null);
        }
        catch (Exception ex)
        {
            return (PeArchitecture.Unknown, dlls.ToList(), ex.Message);
        }
    }

    /// <summary>
    /// Diagnoses an executable file by checking all its imported DLL dependencies against the game folder
    /// and Windows system directories.
    /// </summary>
    public GameDiagnosisResult DiagnoseExe(string exePath, string? gameName = null)
    {
        if (!File.Exists(exePath))
        {
            return new GameDiagnosisResult
            {
                ExePath = exePath,
                GameName = gameName,
                Architecture = PeArchitecture.Unknown,
                ErrorMessage = "Executable file not found"
            };
        }

        var (arch, importedDlls, err) = InspectPeHeader(exePath);
        var result = new GameDiagnosisResult
        {
            ExePath = exePath,
            GameName = gameName ?? Path.GetFileNameWithoutExtension(exePath),
            Architecture = arch,
            TotalImportedDlls = importedDlls.Count,
            ErrorMessage = err
        };

        if (err is not null && importedDlls.Count == 0) return result;

        string gameDir = Path.GetDirectoryName(exePath) ?? "";
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string sysWow64 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        if (string.IsNullOrWhiteSpace(sysWow64)) sysWow64 = sys32;

        bool is64BitGame = arch == PeArchitecture.X64;
        string preferredSysDir = is64BitGame ? sys32 : sysWow64;

        foreach (string dll in importedDlls)
        {
            string cleanName = dll.ToLowerInvariant();
            bool isCore = CoreSystemDlls.Contains(cleanName) ||
                          cleanName.StartsWith("api-ms-win-") ||
                          cleanName.StartsWith("ext-ms-win-");

            string inGame = Path.Combine(gameDir, dll);
            string inSys = Path.Combine(preferredSysDir, dll);
            // Fallback system search
            if (!File.Exists(inSys) && !is64BitGame) inSys = Path.Combine(sys32, dll);

            bool foundInGame = File.Exists(inGame);
            bool foundInSys = File.Exists(inSys);
            bool isPresent = foundInGame || foundInSys;
            string? foundPath = foundInGame ? inGame : (foundInSys ? inSys : null);

            var (pkg, pkgDesc) = MapDllToPackage(cleanName, is64BitGame);

            bool archMismatch = false;
            // If found in game directory, verify that its architecture matches the game (common cause of 0xc000007b)
            if (foundInGame)
            {
                var (dllArch, _, _) = InspectPeHeader(inGame);
                if (dllArch != PeArchitecture.Unknown && dllArch != arch)
                {
                    archMismatch = true;
                    result.HasArchitectureMismatch = true;
                }
            }

            var info = new DllDependencyInfo
            {
                DllName = dll,
                IsPresent = isPresent && !archMismatch,
                FoundInGameDir = foundInGame,
                FoundInSystem = foundInSys,
                ArchitectureMismatch = archMismatch,
                FoundPath = foundPath,
                RequiredPackage = pkgDesc,
                IsSystemCore = isCore
            };

            result.Dependencies.Add(info);

            if (!isPresent || archMismatch)
            {
                result.MissingDlls.Add(info);
                if (pkg.HasValue) result.MissingPackages.Add(pkg.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Diagnoses all major executables in a game folder.
    /// </summary>
    public List<GameDiagnosisResult> DiagnoseGameFolder(string folderPath, string? gameName = null)
    {
        var results = new List<GameDiagnosisResult>();
        if (!Directory.Exists(folderPath)) return results;

        try
        {
            var exes = Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories)
                .Where(p => !GameLauncherService.IsExcludedExe(Path.GetFileName(p)))
                .Take(5); // Limit to top 5 candidates

            foreach (var exe in exes)
            {
                results.Add(DiagnoseExe(exe, gameName));
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to scan folder for game diagnostics: {Path}", folderPath);
        }

        return results;
    }

    /// <summary>
    /// Automatically diagnoses and fixes missing runtimes for a game executable if enabled in Settings.
    /// Returns true if game is ready to play.
    /// </summary>
    public async Task<bool> AutoHealExeAsync(
        string exePath,
        ToastService? toast = null,
        CancellationToken ct = default)
    {
        if (!_settings.AutoGameDoctor) return true;
        if (!File.Exists(exePath)) return true;

        var diag = DiagnoseExe(exePath);
        if (diag.IsHealthy) return true;

        if (diag.MissingPackages.Count == 0 && !diag.HasArchitectureMismatch) return true;

        _log?.LogInformation("Auto Game Doctor healing {ExePath}: {MissingCount} missing packages",
            exePath, diag.MissingPackages.Count);

        bool allSuccess = true;
        foreach (var pkg in diag.MissingPackages)
        {
            bool ok = await InstallPackageAsync(pkg, null, ct);
            if (!ok) allSuccess = false;
        }

        return allSuccess;
    }

    /// <summary>
    /// Downloads and silently installs an official Microsoft runtime package.
    /// </summary>
    public async Task<bool> InstallPackageAsync(
        RuntimePackageType package,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string? url = GetPackageDownloadUrl(package);
        if (string.IsNullOrEmpty(url)) return false;

        string fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = $"{package}.exe";

        Directory.CreateDirectory(RedistDownloadCacheDir);
        string installerPath = Path.Combine(RedistDownloadCacheDir, fileName);

        try
        {
            // Download installer if not cached or size 0
            if (!File.Exists(installerPath) || new FileInfo(installerPath).Length < 1024 * 1024)
            {
                using var client = AppHttp.Create(TimeSpan.FromMinutes(10));
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[64 * 1024];
                long readSoFar = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    readSoFar += bytesRead;
                    if (totalBytes > 0 && progress is not null)
                    {
                        progress.Report((double)readSoFar / totalBytes);
                    }
                }
            }

            // Run silent installation
            string args = GetSilentArguments(package);
            _log?.LogInformation("Running silent installer: {Path} {Args}", installerPath, args);

            var psi = new ProcessStartInfo(installerPath, args)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                await proc.WaitForExitAsync(ct);
                _log?.LogInformation("Installer {Pkg} finished with exit code {Code}", package, proc.ExitCode);

                // DirectX June 2010 self-extractor extracts files; now run DXSETUP.exe /silent to install
                if (package == RuntimePackageType.DirectX_June2010)
                {
                    string dxSetupPath = Path.Combine(RedistDownloadCacheDir, "dx_extract", "DXSETUP.exe");
                    if (File.Exists(dxSetupPath))
                    {
                        var dxPsi = new ProcessStartInfo(dxSetupPath, "/silent")
                        {
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using var dxProc = Process.Start(dxPsi);
                        if (dxProc is not null)
                        {
                            await dxProc.WaitForExitAsync(ct);
                            return dxProc.ExitCode == 0 || dxProc.ExitCode == 3010;
                        }
                    }
                }

                return proc.ExitCode == 0 || proc.ExitCode == 3010; // 3010 = ERROR_SUCCESS_REBOOT_REQUIRED
            }

            return false;
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Failed to download and install runtime package {Pkg}", package);
            return false;
        }
    }

    /// <summary>
    /// Background zero-touch auto-healer: Checks if fundamental system runtimes (VC++ 2015-2022, DirectX)
    /// exist in Windows. If missing (e.g. fresh Windows install), silently installs them in the background.
    /// </summary>
    public async Task CheckAndAutoHealSystemEnvironmentAsync(CancellationToken ct = default)
    {
        if (!_settings.AutoGameDoctor) return;

        try
        {
            var missing = new List<RuntimePackageType>();

            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string sysWow64 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

            // 1. Check VC++ 2015-2022 x64
            if (!File.Exists(Path.Combine(sys32, "vcruntime140.dll")) || !File.Exists(Path.Combine(sys32, "msvcp140.dll")))
            {
                missing.Add(RuntimePackageType.VcRedist2015_2022_x64);
            }

            // 2. Check VC++ 2015-2022 x86
            if (Environment.Is64BitOperatingSystem && Directory.Exists(sysWow64))
            {
                if (!File.Exists(Path.Combine(sysWow64, "vcruntime140.dll")) || !File.Exists(Path.Combine(sysWow64, "msvcp140.dll")))
                {
                    missing.Add(RuntimePackageType.VcRedist2015_2022_x86);
                }
            }

            // 3. Check DirectX June 2010 (d3dx9_43.dll)
            if (!File.Exists(Path.Combine(sys32, "d3dx9_43.dll")))
            {
                missing.Add(RuntimePackageType.DirectX_June2010);
            }

            if (missing.Count == 0) return;

            _log?.LogInformation("Startup Auto Game Doctor detected {Count} missing fundamental runtimes. Silently auto-healing...", missing.Count);

            foreach (var pkg in missing)
            {
                if (ct.IsCancellationRequested) break;
                await InstallPackageAsync(pkg, null, ct);
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Silent startup environment check encountered an error");
        }
    }

    /// <summary>
    /// 1-Click All-in-One installer: Prepares full environment (VC++ 2015-2022 x64/x86 + DirectX End-User).
    /// </summary>
    public async Task<bool> InstallAllCommonRuntimesAsync(
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var packages = new[]
        {
            RuntimePackageType.VcRedist2015_2022_x64,
            RuntimePackageType.VcRedist2015_2022_x86,
            RuntimePackageType.DirectX_June2010
        };

        double stepWeight = 1.0 / packages.Length;
        for (int i = 0; i < packages.Length; i++)
        {
            int idx = i;
            var subProg = new Progress<double>(p =>
            {
                progress?.Report((idx * stepWeight) + (p * stepWeight));
            });
            await InstallPackageAsync(packages[i], subProg, ct);
        }

        progress?.Report(1.0);
        return true;
    }

    private static (RuntimePackageType? Pkg, string? Desc) MapDllToPackage(string dllName, bool is64Bit)
    {
        if (dllName.StartsWith("vcruntime140") || dllName.StartsWith("msvcp140") || dllName.StartsWith("vcomp140") || dllName.StartsWith("concrt140"))
        {
            return (is64Bit ? RuntimePackageType.VcRedist2015_2022_x64 : RuntimePackageType.VcRedist2015_2022_x86,
                    is64Bit ? "Microsoft Visual C++ 2015-2022 (x64)" : "Microsoft Visual C++ 2015-2022 (x86)");
        }

        if (dllName.StartsWith("msvcr120") || dllName.StartsWith("msvcp120") || dllName.StartsWith("vcomp120"))
        {
            return (is64Bit ? RuntimePackageType.VcRedist2013_x64 : RuntimePackageType.VcRedist2013_x86,
                    is64Bit ? "Microsoft Visual C++ 2013 (x64)" : "Microsoft Visual C++ 2013 (x86)");
        }

        if (dllName.StartsWith("msvcr110") || dllName.StartsWith("msvcp110") || dllName.StartsWith("vcomp110"))
        {
            return (is64Bit ? RuntimePackageType.VcRedist2012_x64 : RuntimePackageType.VcRedist2012_x86,
                    is64Bit ? "Microsoft Visual C++ 2012 (x64)" : "Microsoft Visual C++ 2012 (x86)");
        }

        if (dllName.StartsWith("d3dx9_") || dllName.StartsWith("d3dx10_") || dllName.StartsWith("d3dx11_") ||
            dllName.StartsWith("d3dcompiler_") || dllName.StartsWith("xinput1_3") || dllName.StartsWith("xactengine") ||
            dllName.StartsWith("xaudio2_"))
        {
            return (RuntimePackageType.DirectX_June2010, "DirectX End-User Runtimes (June 2010)");
        }

        return (null, null);
    }

    private static string? GetPackageDownloadUrl(RuntimePackageType pkg) => pkg switch
    {
        RuntimePackageType.VcRedist2015_2022_x64 => "https://aka.ms/vs/17/release/vc_redist.x64.exe",
        RuntimePackageType.VcRedist2015_2022_x86 => "https://aka.ms/vs/17/release/vc_redist.x86.exe",
        RuntimePackageType.VcRedist2013_x64 => "https://download.microsoft.com/download/2/E/6/2E61CFA4-993B-4986-B432-BA672FA17BDC/vcredist_x64.exe",
        RuntimePackageType.VcRedist2013_x86 => "https://download.microsoft.com/download/2/E/6/2E61CFA4-993B-4986-B432-BA672FA17BDC/vcredist_x86.exe",
        RuntimePackageType.VcRedist2012_x64 => "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F926/VSU_4/vcredist_x64.exe",
        RuntimePackageType.VcRedist2012_x86 => "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F926/VSU_4/vcredist_x86.exe",
        RuntimePackageType.DirectX_June2010 => "https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe",
        _ => null
    };

    private static string GetSilentArguments(RuntimePackageType pkg) => pkg switch
    {
        RuntimePackageType.DirectX_June2010 => "/Q /T:\"" + Path.Combine(RedistDownloadCacheDir, "dx_extract") + "\"",
        _ => "/install /quiet /norestart"
    };
}
