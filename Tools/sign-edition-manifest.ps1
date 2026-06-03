# ============================================================================
#  sign-edition-manifest.ps1
#
#  Signs an EditionManifest JSON file with the issuer's Ed25519 private key
#  and emits a `.bundle` file (4-byte BE length + JSON + 64-byte sig) that
#  the brand pack can embed as <EmbeddedResource>.
#
#  Wraps `dotnet run --project Tools/LicenseKeyGen -- sign-manifest ...`
#  so the layout stays defined in one C# helper
#  (LicenseIssuer.SignManifestBundle) and the PowerShell is pure plumbing.
#
#  Usage:
#    powershell -ExecutionPolicy Bypass -File Tools\sign-edition-manifest.ps1 `
#      -Input  Panel.Branding\Assets\source\edition.manifest.json `
#      -Output Panel.Branding\Assets\edition.manifest.bundle `
#      -Key    "$env:USERPROFILE\.panel-calculator-secrets\license-private.key"
# ============================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $Input,
    [Parameter(Mandatory=$true)] [string] $Output,
    [Parameter()]                [string] $Key
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Input)) {
    Write-Error "Input JSON not found: $Input"
    exit 1
}

if (-not $Key) {
    $Key = Join-Path $env:USERPROFILE '.panel-calculator-secrets\license-private.key'
}
if (-not (Test-Path $Key)) {
    Write-Error "Private key not found: $Key. Generate via LicenseKeyGen 'generate-keypair' first."
    exit 1
}

# Resolve script-relative repo root (Tools/.. is repo root)
$repoRoot   = Split-Path $PSScriptRoot -Parent
$keygenProj = Join-Path $repoRoot 'Tools\LicenseKeyGen\LicenseKeyGen.csproj'

if (-not (Test-Path $keygenProj)) {
    Write-Error "LicenseKeyGen project not found at $keygenProj"
    exit 1
}

# Ensure output dir exists
$outputDir = Split-Path $Output -Parent
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

Write-Host "Signing manifest..."
Write-Host "  Input  : $Input"
Write-Host "  Output : $Output"
Write-Host "  Key    : $Key"
Write-Host ""

& dotnet run --project $keygenProj --configuration Release -- sign-manifest `
    --input  $Input `
    --output $Output `
    --key    $Key

if ($LASTEXITCODE -ne 0) {
    Write-Error "sign-manifest exited with code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "OK. Bundle written to $Output"
Write-Host "Make sure the brand pack csproj includes:"
Write-Host "  <EmbeddedResource Include=`"Assets\edition.manifest.bundle`" />"
