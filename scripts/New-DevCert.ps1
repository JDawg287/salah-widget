# SIGNED INSTALL, step 1: create a signing certificate for this machine.
#
#   .\scripts\New-DevCert.ps1          creates the certificate (normal PowerShell)
#   <full path>\New-DevCert.ps1 -Trust makes Windows trust it (ELEVATED PowerShell, once per machine)
#
# The private key stays in your Windows certificate store (Cert:\CurrentUser\My) and never leaves
# this machine. Only the public part is exported to artifacts\SalahWidget.cer, which is git-ignored.
param(
    [switch] $Trust
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Common.ps1"

if ($Trust) {
    if (-not (Test-Elevated)) {
        throw 'The -Trust step must run in an elevated PowerShell (Run as administrator).'
    }
    if (-not (Test-Path $CertFile)) {
        throw "No certificate at $CertFile. Run .\scripts\New-DevCert.ps1 (without -Trust, not elevated) first."
    }
    Import-Certificate -FilePath $CertFile -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Write-Host 'Windows now trusts the Salah Times signing certificate.' -ForegroundColor Green
    Write-Host 'Back in a normal PowerShell, run .\scripts\Build-Package.ps1 and then .\scripts\Install-Package.ps1'
    return
}

$cert = Get-SigningCertificate
if ($cert) {
    Write-Host "Using existing certificate $($cert.Thumbprint) (valid until $($cert.NotAfter.ToShortDateString()))."
} else {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject (Get-PackageIdentity).Publisher `
        -FriendlyName $CertFriendlyName `
        -KeyUsage DigitalSignature `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    Write-Host "Created certificate $($cert.Thumbprint) (valid until $($cert.NotAfter.ToShortDateString()))." -ForegroundColor Green
}

New-Item -ItemType Directory -Force $ArtifactsDir | Out-Null
Export-Certificate -Cert $cert -FilePath $CertFile | Out-Null

if (Test-CertificateTrusted $cert.Thumbprint) {
    Write-Host 'Windows already trusts this certificate. Next: .\scripts\Build-Package.ps1' -ForegroundColor Green
} else {
    $self = Join-Path $PSScriptRoot 'New-DevCert.ps1'
    Write-Host ''
    Write-Host 'Next, let Windows trust the certificate. Open PowerShell as administrator and run:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "    powershell -ExecutionPolicy Bypass -File `"$self`" -Trust"
    Write-Host ''
    Write-Host '(An elevated PowerShell starts in the system folder, so use the full path above.)'
}
