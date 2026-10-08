# Removes the Salah Times widget (either install method).
#
#   .\scripts\Uninstall-Package.ps1                      remove the widget, keep settings/cache
#   .\scripts\Uninstall-Package.ps1 -RemoveSettings      also delete cached times, location and logs
#   .\scripts\Uninstall-Package.ps1 -RemoveCertificate   also delete the signing certificate
#                                                        (run elevated to remove the machine-wide trust too)
param(
    [switch] $RemoveSettings,
    [switch] $RemoveCertificate
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"

Stop-Provider
$existing = Get-AppxPackage $PackageName
if ($existing) {
    $existing | Remove-AppxPackage
    Write-Host 'Salah Times widget removed.'
} else {
    Write-Host 'Salah Times widget is not installed.'
}

if ($RemoveSettings) {
    Remove-Item (Join-Path $env:LOCALAPPDATA 'SalahWidget') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Settings, cache and logs removed.'
}

if ($RemoveCertificate) {
    $publisher = (Get-PackageIdentity).Publisher
    $certs = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.FriendlyName -eq $CertFriendlyName })
    $thumbprints = $certs.Thumbprint
    $certs | Remove-Item
    Remove-Item $CertFile -ErrorAction SilentlyContinue

    $trusted = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq $publisher -and ($thumbprints -contains $_.Thumbprint -or $_.FriendlyName -eq $CertFriendlyName) })
    if ($trusted.Count -gt 0) {
        if (Test-Elevated) {
            $trusted | Remove-Item
            Write-Host 'Signing certificate and its machine-wide trust removed.'
        } else {
            Write-Host 'Signing certificate removed. To remove the machine-wide trust too, run this again in an elevated PowerShell:' -ForegroundColor Yellow
            Write-Host "    powershell -ExecutionPolicy Bypass -File `"$(Join-Path $PSScriptRoot 'Uninstall-Package.ps1')`" -RemoveCertificate"
        }
    } else {
        Write-Host 'Signing certificate removed.'
    }
}
