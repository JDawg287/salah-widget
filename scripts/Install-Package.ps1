# SIGNED INSTALL, step 3: install the signed package built by Build-Package.ps1.
# Does not need Developer Mode, only a certificate that Windows trusts (New-DevCert.ps1 -Trust).
param(
    [ValidateSet('', 'x64', 'ARM64')] [string] $Platform = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"
if (-not $Platform) { $Platform = Get-DefaultPlatform }

$msix = Get-MsixPath $Platform
if (-not (Test-Path $msix)) {
    throw "No package at $msix. Run .\scripts\Build-Package.ps1 first."
}

$cert = Get-SigningCertificate
if (-not $cert -or -not (Test-CertificateTrusted $cert.Thumbprint)) {
    Write-Host 'Windows does not trust the signing certificate yet. Run .\scripts\New-DevCert.ps1 and follow its instructions.' -ForegroundColor Yellow
    exit 1
}

if (-not (Test-WindowsAppRuntime)) {
    Write-Host 'Windows App Runtime 1.8 is not installed. Get it from https://aka.ms/windowsappsdk and run this script again.' -ForegroundColor Yellow
    exit 1
}

Stop-Provider

# A Developer Mode registration, or a signed install of the same version, must be removed first.
# (A higher version in Package.appxmanifest upgrades in place and keeps pinned widgets.)
$version = (Get-PackageIdentity).Version
$existing = Get-AppxPackage $PackageName
if ($existing -and ($existing.IsDevelopmentMode -or $existing.Version -eq $version)) {
    Write-Host "Removing the existing install ($($existing.Version))..."
    $existing | Remove-AppxPackage
}

Add-AppxPackage -Path $msix -ForceApplicationShutdown
Restart-WidgetsBoard

Get-AppxPackage $PackageName | Select-Object Name, Version, InstallLocation | Format-List
Write-Host 'Installed. Press Win+W, click "+" (Add widgets) and pick "Salah Times".' -ForegroundColor Green
