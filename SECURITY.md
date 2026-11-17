# Security and privacy notes

PRO X Lab is a read-only diagnostic tool. It has no firmware-write, reset, shell-command, remote-control or telemetry feature, and it makes no network connection.

Report a problem (for example a query that behaves unexpectedly, or personal data that appears in a record) to the repository owner using GitHub's private reporting option when available. Do not attach unredacted personal logs to public issues.

## Design

- **Read-only queries.** A battery query (HID report to the receiver's vendor interface) and, on request, a version query (HID feature report). Nothing is written to firmware.
- **No identifiers stored.** The device serial number is never read. The interface path stays inside the helper process and is never serialized; an automated test fails if that changes.
- **Contained device calls.** Calls that can block in a driver run in a short-lived helper process (the same EXE) that is killed on timeout, so a stuck call cannot freeze the window.
- **Local records.** Sessions are saved under `%LOCALAPPDATA%\ProXLab`. The support report and the JSON and CSV exports mask the Windows user name, machine name and profile path (the JSON and CSV exports since 2.0.1). Text reported by a device is reduced to printable characters and a limited length before it is shown or saved.

## Known limitations

- **Unsigned binary.** The program is not digitally signed, so Windows SmartScreen or antivirus tools may warn, and Windows 11 Smart App Control, where it is switched on, can block it outright. Do not switch off Windows security features for this tool. Compare the SHA-256 value published with the release against your download before running it.
- **Masking is pattern-based.** It removes the user name, machine name and profile paths it can recognise; a user or machine name of one or two characters is not masked on its own, because replacing it would corrupt ordinary text. Always read a report before you post it publicly.
- **Administrator rights are not recommended.** The program is unsigned and runs from a folder that other software in your account can write to, so starting it as administrator would let such software gain administrator rights through it. If the status line reports an access error, close any other program that uses the receiver instead.
- **Estimates.** Battery percentage is an estimate from the reported voltage and is not a capacity or health measurement.

## Supply chain

The repository's build workflow uses actions pinned to commit hashes, read-only repository permission and no secrets. Releases should list the SHA-256 of every file.

## 2.0.2 native DLL search

All application P/Invoke imports are limited to Windows System32. A regression test using a real Windows library substituted as hid.dll reproduced the old search-path issue and passed after the fix. This does not prevent replacement of the unsigned application itself; administrator rights remain unnecessary.
