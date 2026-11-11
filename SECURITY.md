# Intended use

**Do not store important passwords in this app.** AuthLocal is a personal project
for tests and low-risk accounts. The first launch requires acknowledgement of
this warning. Keep independent backups and recovery methods.

Ordinary entries open with the current Windows user through DPAPI. Private
content uses a separate password-encrypted vault; its names remain visible on
home. A five-character master password is permitted by design and is not a
substitute for a strong secret.

The app never clears clipboard contents automatically. Once you deliberately
copy a value, it remains available to paste until you or another program replaces
it. Locking private content prevents new access through the app; it does not
revoke copies already made.

Report reproducible problems through this repository's Issues using invented
sample data. Do not attach actual passwords, OTP seeds, vault files, settings
from real accounts, or recovery codes. For sensitive details, use GitHub's private
vulnerability reporting if available rather than posting them publicly.
