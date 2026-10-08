# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.0.0] - Unreleased

### Added
- Windows 11 Widgets board provider showing today's prayer times and a countdown to the next salah, in small, medium and large sizes.
- Location from Windows Location Services, with an approximate IP-based fallback when location access is off.
- Prayer times from the AlAdhan API, with an automatic calculation method or a user-selected one, and a Standard or Hanafi Asr setting, in the widget's Customize menu.
- Hijri date, Jumuʿah on Fridays, Arabic prayer names, and Imsak and Midnight times on the large widget.
- Offline cache, so the widget renders instantly after a restart and works without a connection.
- Install scripts for Developer Mode (`Register-DevPackage.ps1`) and for a signed install without Developer Mode (`New-DevCert.ps1`, `Build-Package.ps1`, `Install-Package.ps1`), plus `Uninstall-Package.ps1`.
- Unit tests and GitHub Actions CI (x64 and ARM64 builds, tests, MSIX layout validation).
