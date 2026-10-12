# PowerShell script to build all BaoTools v10.5 release packages
$ErrorActionPreference = "Stop"

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host " Building BaoTools v10.5 Release Packages" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# 1. Run Tests
Write-Host "`n[1/4] Running unit tests..." -ForegroundColor Yellow
dotnet test tests/BaoToolsGui.Tests/BaoToolsGui.Tests.csproj --no-build --nologo -v q
if ($LASTEXITCODE -ne 0) {
    dotnet test tests/BaoToolsGui.Tests/BaoToolsGui.Tests.csproj --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Unit tests failed. Build aborted."
        exit 1
    }
}

# 2. Build Portable Directory & Zip
Write-Host "`n[2/4] Building portable folder & zip..." -ForegroundColor Yellow
dotnet publish src/BaoToolsGui/BaoToolsGui.csproj -c Release -o out_portable --nologo -v q
New-Item -ItemType Directory -Force -Path "release_v10.5" | Out-Null
Compress-Archive -Path "out_portable\*" -DestinationPath "release_v10.5\BaoTools_v10.5_Portable.zip" -Force

# 3. Build Modern Gaming WPF Installer (1-Click, Borderless, Cyber Aesthetics)
Write-Host "`n[3/4] Building modern gaming installer (BaoTools_v10.5_Setup.exe)..." -ForegroundColor Yellow
dotnet publish src/BaoToolsInstaller/BaoToolsInstaller.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o out_setup --nologo -v q
if (Test-Path "out_setup\BaoTools_v10.5_Setup.exe") {
    Copy-Item "out_setup\BaoTools_v10.5_Setup.exe" "release_v10.5\BaoTools_v10.5_Setup.exe" -Force
}

# 4. Build Standalone Single-File Executable
Write-Host "`n[4/4] Building standalone single-file BaoTools.exe (self-contained)..." -ForegroundColor Yellow
dotnet publish src/BaoToolsGui/BaoToolsGui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o out_single_standalone --nologo -v q
Copy-Item "out_single_standalone\BaoTools.exe" "release_v10.5\BaoTools.exe" -Force

Write-Host "`nAll release assets successfully created in release_v10.5/:" -ForegroundColor Green
Get-ChildItem "release_v10.5" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
