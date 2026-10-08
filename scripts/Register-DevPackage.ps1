# QUICK INSTALL (requires Developer Mode).
# Builds the widget and registers the build folder as an unsigned package for the current user.
# Nothing is signed and no certificate is needed. Re-run after code changes; pinned widgets stay pinned.
param(
    [ValidateSet('', 'x64', 'ARM64')] [string] $Platform = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"
if (-not $Platform) { $Platform = Get-DefaultPlatform }

$devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($devMode -ne 1) {
    Write-Host 'Developer Mode is off. Turn it on in Settings > System > For developers and run this script again,' -ForegroundColor Yellow
    Write-Host 'or use the signed install (see README) which does not need Developer Mode.' -ForegroundColor Yellow
    Start-Process 'ms-settings:developers'
    exit 1
}

if (-not (Test-WindowsAppRuntime)) {
    Write-Host 'Windows App Runtime 1.8 is not installed. Get it from https://aka.ms/windowsappsdk and run this script again.' -ForegroundColor Yellow
    exit 1
}

Stop-Provider
Invoke-Build $Platform

# A signed install of the same package must be removed before registering the build folder.
$existing = Get-AppxPackage $PackageName
if ($existing -and -not $existing.IsDevelopmentMode) {
    Write-Host 'Removing the signed install first...'
    $existing | Remove-AppxPackage
}

$manifest = Join-Path (Get-LayoutPath $Platform) 'AppxManifest.xml'
try {
    Add-AppxPackage -Register $manifest -ForceApplicationShutdown
} catch {
    # 0x80073CFB: the package manifest changed (e.g. dependencies) but the version didn't, so Windows
    # refuses to re-register in place. Remove and register again; pinned widgets must be re-added.
    if ("$($_.Exception.Message)" -notmatch '0x80073CFB') { throw }
    Write-Host 'The package changed without a version bump; reinstalling. Re-add the widget from the Widgets board afterwards.' -ForegroundColor Yellow
    Get-AppxPackage $PackageName | Remove-AppxPackage
    Add-AppxPackage -Register $manifest -ForceApplicationShutdown
}
Restart-WidgetsBoard

Get-AppxPackage $PackageName | Select-Object Name, Version, InstallLocation | Format-List
Write-Host 'Installed. Press Win+W, click "+" (Add widgets) and pick "Salah Times".' -ForegroundColor Green
Write-Host 'Keep the build folder in place: Windows runs the widget from it.'
