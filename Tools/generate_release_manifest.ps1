# =============================================================================
#  generate_release_manifest.ps1
#  -----------------------------------------------------------------------------
#  Generates the SHA-256 manifest that ships with every Panel Calculator
#  release. The client-side auto-updater (see UpdateService.cs) DOWNLOADS this
#  file alongside the EXE and refuses to install if hashes do not match.
#
#  Usage (PowerShell):
#      .\Tools\generate_release_manifest.ps1
#      .\Tools\generate_release_manifest.ps1 -ExePath ".\publish\PanelCalculator.exe"
#
#  Output:
#      <same folder as the EXE>\PanelCalculator.exe.sha256
#      Contents (sha256sum format):
#          <64-hex-digest>  PanelCalculator.exe
#
#  IMPORTANT:
#      Upload this .sha256 file to the GitHub Release as an ADDITIONAL asset,
#      next to PanelCalculator.exe. If it is missing, customers running
#      v1.2.4+ will REFUSE to apply the update (fail-secure).
# =============================================================================

[CmdletBinding()]
param(
    [string] $ExePath = ".\publish\PanelCalculator.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: EXE tidak ditemukan di '$ExePath'." -ForegroundColor Red
    Write-Host "Jalankan 'dotnet publish' dulu (lihat CLAUDE.md → 'Cara Rilis Versi Baru')." -ForegroundColor Yellow
    exit 1
}

$exeFull   = (Resolve-Path $ExePath).Path
$exeName   = Split-Path $exeFull -Leaf
$exeDir    = Split-Path $exeFull -Parent
$exeSize   = (Get-Item $exeFull).Length

Write-Host "Menghitung SHA-256 untuk '$exeFull'..." -ForegroundColor Cyan
Write-Host "Ukuran file : $($exeSize.ToString('N0')) byte ($([math]::Round($exeSize / 1MB, 1)) MB)"

$hash = (Get-FileHash -Path $exeFull -Algorithm SHA256).Hash.ToLower()

$manifestPath = Join-Path $exeDir "$exeName.sha256"
# sha256sum format: <64-hex-digest><2 spaces><filename><LF>
# 2 spaces = text-mode marker; both formats are accepted by our parser.
$line = "$hash  $exeName"
# Write as UTF-8 WITHOUT BOM so the manifest is portable across tools.
[System.IO.File]::WriteAllText($manifestPath, "$line`n", [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "SHA-256  : $hash" -ForegroundColor Green
Write-Host "Manifest : $manifestPath" -ForegroundColor Green
Write-Host ""
Write-Host "JANGAN LUPA: upload '$manifestPath' ke GitHub Release sebagai asset tambahan." -ForegroundColor Yellow
Write-Host "Lihat 'gh release create' di CLAUDE.md untuk command lengkap." -ForegroundColor Yellow
