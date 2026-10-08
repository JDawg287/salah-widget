# Shared paths and helpers for the install scripts. Dot-source it: . "$PSScriptRoot\Common.ps1"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ProjectPath = Join-Path $RepoRoot 'src\SalahWidget\SalahWidget.csproj'
$ManifestPath = Join-Path $RepoRoot 'src\SalahWidget\Package.appxmanifest'
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'
$PackageName = 'SalahWidget'
$CertFriendlyName = 'Salah Times widget (local signing)'
$CertFile = Join-Path $ArtifactsDir 'SalahWidget.cer'

function Get-PackageIdentity {
    ([xml](Get-Content $ManifestPath -Raw)).Package.Identity
}

function Get-DefaultPlatform {
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
}

function Get-LayoutPath([string] $Platform, [string] $Configuration = 'Release') {
    $rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    Join-Path $RepoRoot "src\SalahWidget\bin\$Platform\$Configuration\net8.0-windows10.0.22621.0\$rid"
}

function Get-MsixPath([string] $Platform) {
    Join-Path $ArtifactsDir ("{0}_{1}_{2}.msix" -f $PackageName, (Get-PackageIdentity).Version, $Platform)
}

# The newest valid signing certificate created by New-DevCert.ps1 for this user.
function Get-SigningCertificate {
    $publisher = (Get-PackageIdentity).Publisher
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $publisher -and $_.FriendlyName -eq $CertFriendlyName -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

function Test-CertificateTrusted([string] $Thumbprint) {
    [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople, Cert:\LocalMachine\Root |
        Where-Object Thumbprint -eq $Thumbprint)
}

function Test-Elevated {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# makeappx.exe / signtool.exe from the Microsoft.Windows.SDK.BuildTools NuGet package (restored by the build).
function Get-SdkTool([string] $Name) {
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $tool = Get-ChildItem (Join-Path $packages 'microsoft.windows.sdk.buildtools') -Recurse -Filter $Name -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object { [version]($_.FullName -replace '.*microsoft\.windows\.sdk\.buildtools\\([\d.]+)\\.*', '$1') } -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "$Name not found. Build the project once so NuGet restores Microsoft.Windows.SDK.BuildTools." }
    $tool.FullName
}

function Test-WindowsAppRuntime {
    [bool](Get-AppxPackage 'Microsoft.WindowsAppRuntime.1.8' -ErrorAction SilentlyContinue)
}

function Stop-Provider {
    Get-Process $PackageName -ErrorAction SilentlyContinue | Stop-Process -Force
}

# Restart the Widgets board so it re-reads provider definitions (and releases files it has mapped).
# Process names differ between Windows builds; Windows restarts them on demand.
function Restart-WidgetsBoard {
    Get-Process Widgets, WidgetBoard, WidgetService -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Invoke-Build([string] $Platform) {
    dotnet build $ProjectPath -c Release -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
