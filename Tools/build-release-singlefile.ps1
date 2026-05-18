# =============================================================================
#  build-release-singlefile.ps1
#  -----------------------------------------------------------------------------
#  Build the SINGLE-FILE release EXE (`PanelCalculator.exe`, ~179 MB) that is
#  uploaded to GitHub Releases and consumed by the auto-updater.
#
#  WHY THIS SCRIPT EXISTS (vs. the plain `dotnet publish` in CLAUDE.md):
#    The plain publish produces a single-file EXE whose three application
#    DLLs (PanelCalculator.WinForms / Core / Data) are still UN-obfuscated
#    inside the bundle — trivially extractable with ILSpy / dnSpy. That
#    leaks the SQLCipher key derivation, update endpoint, pricing logic,
#    and all internal method/field names. This script runs Obfuscar BEFORE
#    the single-file bundler picks up the DLLs, so the EXE shipped to
#    customers contains obfuscated IL.
#
#  PIPELINE (5 steps):
#    [1/5] Clean previous staging
#    [2/5] dotnet publish (multi-file) -> release-build-staging\app-raw\
#    [3/5] Obfuscar on 3 application DLLs -> release-build-staging\app-obf\
#    [4/5] Overlay obfuscated DLLs into the build output that the
#          single-file bundler reads (bin\Release\net8.0-windows\win-x64\),
#          then re-publish with PublishSingleFile=true --no-build
#    [5/5] Copy final EXE to publish\PanelCalculator.exe + generate SHA-256
#
#  USAGE:
#    .\Tools\build-release-singlefile.ps1
#    .\Tools\build-release-singlefile.ps1 -SkipManifest    # skip step 5b
#
#  OUTPUT:
#    publish\PanelCalculator.exe          (single-file, obfuscated, ~179 MB)
#    publish\PanelCalculator.exe.sha256   (manifest for auto-updater)
#
#  REQUIREMENTS:
#    - .NET 8 SDK (or newer)
#    - Obfuscar.GlobalTool (auto-installed if missing)
#    - PowerShell 5.1+
# =============================================================================

[CmdletBinding()]
param(
    [switch] $SkipManifest,
    [switch] $SkipObfuscation   # escape hatch — produces an UNPROTECTED EXE
)

$ErrorActionPreference = "Stop"

# --- Locate repo root (script is in <repo>\Tools\) ---------------------------
$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ToolsDir
Set-Location $RepoRoot

Write-Host ""
Write-Host " ============================================================" -ForegroundColor Cyan
Write-Host "  BUILD SINGLE-FILE RELEASE  -  Kalkulator Panel TTS" -ForegroundColor Cyan
Write-Host " ============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Repo root        : $RepoRoot"

# --- Configuration -----------------------------------------------------------
$Configuration     = "Release"
$Runtime           = "win-x64"
$TargetFramework   = "net8.0-windows"
$WinFormsProject   = "PanelCalculator.WinForms\PanelCalculator.WinForms.csproj"
$StagingRoot       = Join-Path $RepoRoot "release-build-staging"
$RawDir            = Join-Path $StagingRoot "app-raw"
$ObfDir            = Join-Path $StagingRoot "app-obf"
$BinTfmDir         = Join-Path $RepoRoot "PanelCalculator.WinForms\bin\$Configuration\$TargetFramework\$Runtime"
$PublishOutDir     = Join-Path $BinTfmDir "publish"
$FinalPublishDir   = Join-Path $RepoRoot "publish"
$ObfuscarConfig    = Join-Path $RepoRoot "Installer\obfuscar-singlefile.xml"

$AppDlls = @(
    "PanelCalculator.WinForms.dll",
    "PanelCalculator.Core.dll",
    "PanelCalculator.Data.dll"
)

# --- Sanity checks -----------------------------------------------------------
if (-not (Test-Path (Join-Path $RepoRoot "PanelCalculator.sln"))) {
    Write-Host "[ERROR] PanelCalculator.sln tidak ditemukan. Pastikan script ada di <repo>\Tools\." -ForegroundColor Red
    exit 1
}

try {
    $dotnetVer = (& dotnet --version) 2>&1
    Write-Host ".NET SDK         : $dotnetVer"
} catch {
    Write-Host "[ERROR] .NET SDK tidak ditemukan. Install dari https://dotnet.microsoft.com" -ForegroundColor Red
    exit 1
}

if (-not $SkipObfuscation -and -not (Test-Path $ObfuscarConfig)) {
    Write-Host "[ERROR] Konfigurasi Obfuscar tidak ada: $ObfuscarConfig" -ForegroundColor Red
    exit 1
}

# =============================================================================
# [1/5] CLEAN STAGING
# =============================================================================
Write-Host ""
Write-Host "[1/5] Membersihkan staging sebelumnya..." -ForegroundColor Yellow
if (Test-Path $StagingRoot)     { Remove-Item -Recurse -Force $StagingRoot }
if (Test-Path $FinalPublishDir) { Remove-Item -Recurse -Force $FinalPublishDir }
# Clean bin/publish too so we know the final EXE is fresh
if (Test-Path $PublishOutDir)   { Remove-Item -Recurse -Force $PublishOutDir }
New-Item -ItemType Directory -Path $RawDir          | Out-Null
New-Item -ItemType Directory -Path $FinalPublishDir | Out-Null
Write-Host "[OK] Bersih."

# =============================================================================
# [2/5] DOTNET PUBLISH (MULTI-FILE)
# =============================================================================
# We need the DLLs as separate files so Obfuscar can process them. This is a
# "throwaway" publish — its only purpose is to give Obfuscar real input.
Write-Host ""
Write-Host "[2/5] Publish multi-file ke staging (untuk input Obfuscar)..." -ForegroundColor Yellow
Write-Host "      Output: $RawDir"

& dotnet publish $WinFormsProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    --output $RawDir `
    --nologo `
    -v minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] dotnet publish (multi-file) gagal." -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Multi-file publish selesai."

# Verify the 3 app DLLs are in app-raw
foreach ($dll in $AppDlls) {
    $p = Join-Path $RawDir $dll
    if (-not (Test-Path $p)) {
        Write-Host "[ERROR] Expected DLL tidak ada di app-raw: $dll" -ForegroundColor Red
        exit 1
    }
}

# =============================================================================
# [3/5] OBFUSCAR
# =============================================================================
if ($SkipObfuscation) {
    Write-Host ""
    Write-Host "[3/5] DISKIP (-SkipObfuscation aktif). EXE final akan UN-OBFUSCATED." -ForegroundColor Magenta
    # When skipping, just copy raw DLLs to obf dir so step 4 has something to overlay
    New-Item -ItemType Directory -Path $ObfDir | Out-Null
    foreach ($dll in $AppDlls) {
        Copy-Item -Force (Join-Path $RawDir $dll) (Join-Path $ObfDir $dll)
    }
} else {
    Write-Host ""
    Write-Host "[3/5] Menjalankan Obfuscar terhadap 3 DLL aplikasi..." -ForegroundColor Yellow

    # --- Locate / install Obfuscar.GlobalTool ----------------------------
    # The Obfuscar.GlobalTool nupkg installs the command as `obfuscar.console`
    # (not `obfuscar`). We probe both names for forward compatibility in case
    # the upstream package ever renames the binary.
    $obfuscarExe = $null
    foreach ($name in @('obfuscar.console', 'obfuscar')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { $obfuscarExe = $cmd.Source; break }
        $candidate = Join-Path $env:USERPROFILE ".dotnet\tools\$name.exe"
        if (Test-Path $candidate) { $obfuscarExe = $candidate; break }
    }

    if (-not $obfuscarExe) {
        Write-Host "      Obfuscar belum terpasang. Memasang Obfuscar.GlobalTool..."
        & dotnet tool install --global Obfuscar.GlobalTool 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            & dotnet tool update --global Obfuscar.GlobalTool 2>&1 | Out-Null
        }
        foreach ($name in @('obfuscar.console', 'obfuscar')) {
            $candidate = Join-Path $env:USERPROFILE ".dotnet\tools\$name.exe"
            if (Test-Path $candidate) { $obfuscarExe = $candidate; break }
            $cmd = Get-Command $name -ErrorAction SilentlyContinue
            if ($cmd) { $obfuscarExe = $cmd.Source; break }
        }
    }

    if (-not $obfuscarExe) {
        Write-Host "[ERROR] Obfuscar.GlobalTool gagal dipasang. Install manual:" -ForegroundColor Red
        Write-Host "        dotnet tool install --global Obfuscar.GlobalTool" -ForegroundColor Red
        exit 1
    }

    Write-Host "[OK] Obfuscar     : $obfuscarExe"

    # Obfuscar config uses paths relative to its working directory — invoke from repo root
    & $obfuscarExe $ObfuscarConfig
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Obfuscar gagal!" -ForegroundColor Red
        exit 1
    }

    # Verify the 3 obfuscated DLLs exist
    foreach ($dll in $AppDlls) {
        $p = Join-Path $ObfDir $dll
        if (-not (Test-Path $p)) {
            Write-Host "[ERROR] Obfuscar tidak menghasilkan: $dll" -ForegroundColor Red
            exit 1
        }
    }
    Write-Host "[OK] Obfuscation selesai. Output: $ObfDir"
}

# =============================================================================
# [4/5] OVERLAY OBFUSCATED DLLS + RE-PUBLISH AS SINGLE-FILE
# =============================================================================
# The .NET 8 single-file bundler reads DLLs from the build output directory
# (bin\<Config>\<TFM>\<RID>\). By overlaying our obfuscated DLLs there BEFORE
# invoking publish with --no-build, we trick the bundler into packing the
# obfuscated versions into the final EXE.
Write-Host ""
Write-Host "[4/5] Overlay obfuscated DLLs ke build output + publish single-file..." -ForegroundColor Yellow

if (-not (Test-Path $BinTfmDir)) {
    Write-Host "[ERROR] Build output tidak ada: $BinTfmDir" -ForegroundColor Red
    Write-Host "        (step [2/5] gagal sebelumnya?)" -ForegroundColor Red
    exit 1
}

foreach ($dll in $AppDlls) {
    $src = Join-Path $ObfDir $dll
    $dst = Join-Path $BinTfmDir $dll
    Copy-Item -Force $src $dst
    Write-Host "      overlay (bin): $dll"
}

# .NET 8 publish chain order:
#   ComputeFilesToPublish -> CopyFilesToPublishDirectory -> GenerateSingleFileBundle
# With --no-build, the Build target does NOT run (so our overlaid DLLs in
# <bin>\<TFM>\<RID>\ are NOT regenerated), but ComputeFilesToPublish reads
# from that build output, CopyFilesToPublishDirectory copies it to <bin>\...\publish\,
# and the bundler then packs those files into the single-file EXE. Because
# step [1/5] wiped <bin>\...\publish\, the copy is fresh and our obfuscated
# DLLs propagate all the way into the final EXE.
& dotnet publish $WinFormsProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --no-build `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --nologo `
    -v minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] dotnet publish (single-file, --no-build) gagal." -ForegroundColor Red
    exit 1
}

$builtExe = Join-Path $PublishOutDir "PanelCalculator.WinForms.exe"
if (-not (Test-Path $builtExe)) {
    Write-Host "[ERROR] Single-file EXE tidak ditemukan di: $builtExe" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] Single-file publish selesai."

# =============================================================================
# [5/5] COPY TO FINAL LOCATION + GENERATE MANIFEST
# =============================================================================
Write-Host ""
Write-Host "[5/5] Menyalin EXE ke $FinalPublishDir dan generate SHA-256..." -ForegroundColor Yellow

$finalExe = Join-Path $FinalPublishDir "PanelCalculator.exe"
Copy-Item -Force $builtExe $finalExe

$exeSize    = (Get-Item $finalExe).Length
$exeSizeMB  = [math]::Round($exeSize / 1MB, 1)

if (-not $SkipManifest) {
    & (Join-Path $ToolsDir "generate_release_manifest.ps1") -ExePath $finalExe
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] generate_release_manifest.ps1 gagal." -ForegroundColor Red
        exit 1
    }
}

# =============================================================================
# DONE
# =============================================================================
$hash = (Get-FileHash -Path $finalExe -Algorithm SHA256).Hash.ToLower()

Write-Host ""
Write-Host " ============================================================" -ForegroundColor Green
Write-Host "  SELESAI" -ForegroundColor Green
Write-Host " ============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  EXE final     : $finalExe"
Write-Host "  Ukuran        : $($exeSize.ToString('N0')) byte ($exeSizeMB MB)"
Write-Host "  SHA-256       : $hash"
if (-not $SkipManifest) {
    Write-Host "  Manifest      : $finalExe.sha256"
}
if ($SkipObfuscation) {
    Write-Host ""
    Write-Host "  ! PERHATIAN: -SkipObfuscation aktif. EXE TIDAK ter-obfuscate." -ForegroundColor Magenta
    Write-Host "    JANGAN release ini ke customer." -ForegroundColor Magenta
} else {
    Write-Host ""
    Write-Host "  3 DLL aplikasi sudah ter-obfuscate (method+field rename, string encrypt)." -ForegroundColor Green
}
Write-Host ""
Write-Host "  Langkah berikut: upload kedua file di atas ke GitHub Release"
Write-Host "  bersama installer Inno Setup. Lihat CLAUDE.md untuk command 'gh release create'."
Write-Host ""
