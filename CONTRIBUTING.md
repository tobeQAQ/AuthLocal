# Contributing

Use Windows 10/11 x64 and .NET 10 SDK. `global.json` selects a compatible .NET 10
feature band. Do not test with real account secrets.

```powershell
dotnet run --project tests/LocalAuth.Tests -c Release
dotnet run --project tests/LocalAuth.Windows.Tests -c Release -r win-x64
python scripts/check-localization.py
./build.ps1
```

The Windows suite opens isolated test forms and uses synthetic credentials. It
includes a real 31-second wait to catch regressions of the removed clipboard
expiry. Let it finish without changing the clipboard or interacting with its
windows. It restores the test user's prior system theme preference.

For an installer, use Inno Setup 7 and `./build.ps1 -Installer -IsccPath <ISCC.exe>`.
The workflow installs a checksum-verified compiler and tests install, upgrade
registration and uninstall before uploading packages.

Keep changes focused. Include the trigger, expected behavior, and validation in
pull requests. Add regression checks for behavior bugs, especially save failures,
clipboard lifecycle and private-content access. Layout-only edits should be
checked visually at default and narrow window sizes rather than mirrored pixel tests.

Translations are documented in `src/LocalAuth.App/Locales/README.md`.
