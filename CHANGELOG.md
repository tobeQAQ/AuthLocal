# Changelog

## 1.5.0

- Publish AuthLocal for Windows as an MIT-licensed project.
- Fix copied passwords disappearing when the app is minimized or closed to tray.
- Remove all automatic clipboard erasure, including timeouts, locks and exit.
- Add Light / Dark / System appearance; System is the default and follows live changes.
- Add Simplified Chinese, Traditional Chinese, English, French and Japanese; language is adjustable.
- Require acknowledgement of the first-launch warning: do not store important passwords.
- Add a per-user Windows installer with a stable application ID, version and uninstall registration.
- Keep existing LocalAuth portable data and encrypted backup compatibility.
- Keep the compact unified home, one-click copy, private entries and tray operation.

The clipboard defect was reproduced against 1.3.0: `123@1` was copied correctly,
but minimizing the window cleared it before paste. It was not a symbol restriction.
