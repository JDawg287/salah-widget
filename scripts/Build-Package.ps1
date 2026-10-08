# SIGNED INSTALL, step 2: build the widget and produce a signed artifacts\SalahWidget_<version>_<platform>.msix
# using the certificate created by New-DevCert.ps1.
param(
    [ValidateSet('', 'x64', 'ARM64')] [string] $Platform = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"
if (-not $Platform) { $Platform = Get-DefaultPlatform }

$cert = Get-SigningCertificate
if (-not $cert) {
    throw 'No signing certificate found. Run .\scripts\New-DevCert.ps1 first.'
}

Invoke-Build $Platform

# Stage the build output without build-only files.
$staging = Join-Path $ArtifactsDir "layout-$Platform"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
Copy-Item (Get-LayoutPath $Platform) $staging -Recurse
Get-ChildItem $staging -Recurse -Include '*.appxrecipe', '*.pdb' | Remove-Item -Force

$msix = Get-MsixPath $Platform
& (Get-SdkTool 'makeappx.exe') pack /d $staging /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)." }

& (Get-SdkTool 'signtool.exe') sign /q /fd SHA256 /sha1 $cert.Thumbprint /s My $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)." }

Remove-Item $staging -Recurse -Force
Write-Host "Signed package: $msix" -ForegroundColor Green
Write-Host 'Next: .\scripts\Install-Package.ps1'
