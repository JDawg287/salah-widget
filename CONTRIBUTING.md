# Contributing

Thanks for your interest in improving Salah Times! Bug reports, fixes, translations and ideas are all welcome.

## Before you start

- **Bugs and feature ideas:** open an [issue](../../issues) first, so we can agree on the approach before you spend time on it. Small fixes (typos, obvious bugs) can go straight to a pull request.
- **Security problems:** please don't open a public issue. Follow [SECURITY.md](SECURITY.md) instead.
- By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).

## Development setup

You need Windows 11 with the Widgets board, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and Windows App Runtime 1.8.

```powershell
git clone <your fork>
cd salah-widget

dotnet build src\SalahWidget -c Release -p:Platform=x64   # build
dotnet test SalahWidget.sln                               # run the unit tests
.\scripts\Register-DevPackage.ps1                         # install your build (needs Developer Mode)
```

Then open the Widgets board (<kbd>Win</kbd>+<kbd>W</kbd>) and add **Salah Times**. Re-run `Register-DevPackage.ps1` after each change.

- **Check location + AlAdhan without the board:** `src\SalahWidget\bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\SalahWidget.exe --selftest | Out-String`
- **Logs:** `%LOCALAPPDATA%\SalahWidget\widget.log`

## Making changes

- **Keep it lean.** Don't add dependencies unless they're clearly needed. The widget only references the Widgets component of the Windows App SDK.
- **Match the existing style:** C# with nullable reference types and file-scoped namespaces. Card layouts are Adaptive Card 1.5 templates in `src/SalahWidget/Templates`.
- **Add or update tests** for logic changes in `tests/SalahWidget.Tests`. The template tests check that every `${binding}` in a card resolves against the data the code produces, so a renamed field fails there.
- **Culture matters.** Users run Windows in many languages and calendars (Arabic, Persian, Thai and more). Format anything sent to an API with `CultureInfo.InvariantCulture`, and test display code with `[UseCulture("...")]`.
- **Privacy.** Don't log or commit precise coordinates, IP addresses, or anything else that identifies a user. Use made-up locations in tests.
- **Never commit** certificates, `.pfx` files, packages (`.msix`) or local data. `.gitignore` covers these; please don't override it.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Make your change, with tests where it makes sense.
3. Make sure `dotnet test SalahWidget.sln` passes and the build has no warnings.
4. If you changed anything the user sees, install it with `Register-DevPackage.ps1` and check it on the Widgets board. Screenshots in the PR help a lot.
5. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
6. Open the pull request and fill in the template. CI builds x64 and ARM64 and runs the tests.

Keep pull requests focused: one fix or feature per PR is much easier to review.
