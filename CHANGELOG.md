# Changelog

## 2.0.2 — 2026-11-17

- Limit all application P/Invoke DLL imports to Windows System32.
- Verify DLL substitution rejection and retain existing diagnostics/privacy self-tests.


## 2.0.1 — privacy and input hardening

- The JSON and CSV exports are now masked like the support report (user name, machine name, profile path). Before this, only the text report was masked, although the documentation said all exports were.
- The export note records only the kind of export, not the file name you chose.
- Text reported by a device (radio version, product and manufacturer names) is reduced to printable characters and a limited length.
- The device-notification handler bounds the name it reads by the size the message declares.
- The program no longer suggests running as administrator on an access error; the documentation explains why that is not recommended.
- Document that Windows 11 Smart App Control can block the unsigned program.
- `--self-test` also checks the masked session export and the device-text cleaning.

## 2.0.0 — rebuilt, privacy-safe diagnostics

Rewritten from the recovered source of the earlier internal build (1.3.1), which had no source in the project.

- Capture USB arrival and removal of the receiver and the headset charging interface from Windows device notifications with exact timestamps, with a 5-second check as fallback and duplicate suppression.
- Mark every charging-USB disconnect on the battery chart and summarise them: count, per hour, median and longest time to reconnect, count while charging.
- Add a support report (text), full session export (JSON) and battery export (CSV), all masked for user name, machine name and profile path.
- Never read the device serial number and never store or export the interface path (guarded by an automated test).
- Remove the MCU version query, which never returned a result and sent a request to the headset on every attempt.
- Store sessions under `%LOCALAPPDATA%\ProXLab` instead of next to the program; compare a problem headset with a healthy one.
- Turkish/English interface in the same design language as Receiver Rescue, with a layout self-check.
- Add `--self-test` (battery decode and estimate, radio-version decode, disconnect analysis, privacy guards, session round trip, reports) and `--ui-test` (layout across Turkish/English, three widths and four views, and USB arrival/removal handling with synthetic device-change messages).
- Replace the GPLv3 licence text that accompanied the earlier build with a proprietary licence.
