# Validation

This project tests behavior with public RFC seeds and invented passwords. Never
run regression checks against a real vault. The Windows suite creates isolated
forms and directories; installer tests require a fresh Windows test profile.

## Clipboard regression

The original 1.3 implementation was reproduced before changing it: a symbol-containing
password copied correctly, then minimizing the app erased the clipboard and a
separate native text box pasted an empty string. Symbols were not the cause.

The clipboard writer now transfers UTF-16 text to Windows without expiry or any
lifecycle cleanup. Regression checks save through the real password editor, click
actual cards, and paste through a separate native Windows TextBox using `Paste()`
(the native WM_PASTE operation used by Ctrl+V).

The matrix covers a short symbol password, alphanumeric text, ASCII punctuation,
leading/trailing spaces, Chinese, French, Japanese and emoji. It checks switching
windows, minimizing, closing to tray, private locking, Windows session lock/sleep
handlers, the former 30-second timeout and complete exit. It also checks clipboard
contention, failed-copy feedback and content subsequently copied by another app.
Private contents must still lock while already copied text remains available.

## Coverage

| Check | Coverage |
| --- | --- |
| Core suite | 73 checks: RFC 6238, Base32/URI validation, encryption/tamper handling, atomic storage, five-digit master password, protection-transfer order and rollback |
| Windows suite | 176 checks: editor/save/restart, DPAPI, unified home, private metadata/index recovery, authenticated copy, cancellation, tray, clipboard matrix, theme/language persistence, first-use warning, cross-process restore, tray favorite copy and paused background refresh |
| Localization validator | 234 message IDs in four translated catalogs; matching IDs, format placeholders and native file-dialog filter pairs |
| Installer smoke suite | Per-user deployment/registry, running-app replacement block, version upgrade with one uninstall entry, byte-preserved account files/settings, uninstall and retained data |
| Visual review | Light/dark home, all five languages at narrow size, settings and first-use warning; synthetic accounts only |

## Recorded validation

Core checks passed on .NET SDK 10.0.403. Windows desktop checks passed under Wine
10 on Linux with a virtual display. The actual installed executable displayed its
first-use warning; canceling did not record acknowledgement. Install, blocked
running-app update, upgrade and uninstall checks passed, including byte-for-byte
data preservation.

Wine-specific .NET cryptography compatibility settings were confined to the test
environment. No Wine workaround is shipped in the application or installer.
These local compatibility results are not physical Windows 10/11 testing.

Version 1.5.0 also passed all checks on the real GitHub-hosted Windows Server 2025
runner: [successful run](https://github.com/tobeQAQ/AuthLocal/actions/runs/43079906607).
The 1.6.0 extension passed 176 local desktop checks on 2026-11-13 before upload;
its native Windows result is recorded by the release workflow.

The [Windows workflow](https://github.com/tobeQAQ/AuthLocal/actions/workflows/windows.yml)
runs the core and desktop suites, validates translations, builds both packages,
and tests the installer on an actual GitHub-hosted Windows runner. Consult the
linked run for native Windows results; package upload occurs only after all of
its checks pass. Deliberate release commits publish packages only after this job
succeeds.

## Reproduce

```powershell
dotnet run --project tests/LocalAuth.Tests -c Release
dotnet run --project tests/LocalAuth.Windows.Tests -c Release -r win-x64
python scripts/check-localization.py
./build.ps1 -Installer
```

Allow the Windows suite's real 31-second timer to finish. Do not interact with its
windows or change the clipboard while it runs. The suite restores the previous
system appearance preference. Installer tests use a second setup compiled with
an older published installer selected by `scripts/Get-UpgradeBaseline.ps1`,
validated against its SHA256 asset digest. The actual new installer is used as
the upgrade target; there is no synthetic higher-version release fixture.

Manual Windows checks that remain useful include high-DPI/multi-monitor placement,
the physical taskbar overflow tray, and OS-specific title-bar/SmartScreen behavior.
