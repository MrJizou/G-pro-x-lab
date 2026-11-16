# Validation and limits

## Automated checks

`ProXLab.exe --self-test` runs without any device and checks:

- battery frame, reply decoding, voltage-to-percentage estimate (including bounds and monotonic behaviour), charging / not-charging / unknown state, and rejection of unrelated or short replies;
- radio-version reply decoding and rejection of a wrong reply;
- disconnect analysis: counts, receiver versus charging-USB events, time to reconnect, median, longest and per-hour rate;
- duration formatting;
- **privacy guards:** the device model carries no serial-number member, the interface path is never serialized, and user name, machine name and profile paths are masked;
- session round trip through JSON, Turkish and English support reports, CSV export.

`ProXLab.exe --ui-test` renders the interface with sample data in Turkish and English at three window widths and in all four views, and checks for clipped text, overflow and overlapping blocks. It also registers the real Windows device notifications and feeds the real handler synthetic USB arrival and removal messages (duplicate removals, an unrelated device, a receiver removal) and checks the resulting events and summary.

Both were confirmed to fail on deliberately broken builds: disabling duplicate suppression, swapping the charging flag, serializing the interface path, and shrinking text boxes each made a test fail.

## Trial on real hardware (read-only)

On 2026-11-16, on one Windows PC with a working first-generation PRO X Wireless receiver and the headset's USB cable connected:

- `--scan` found the receiver and headset USB interfaces, one battery interface and two version interfaces.
- The device list returned by the helper process contained no interface path, serial number or user name.
- One battery query returned about 4.17 V, an estimate of 100 % and a charging state; the value matched a manual decode of the reply bytes.
- The program ran in normal mode for 16 seconds, took two battery readings, saved a session and closed cleanly with no helper process left behind. The saved session contained no serial number, interface path, user name or machine name.

## What is not verified

- **A recorded real cable disconnect.** The disconnect path was verified with synthetic device-change messages. The project owner reported, after trying the application on their own PC, that disconnects were counted correctly; no recording of that session is kept as evidence, so treat it as one person's report.
- **The version query** (*Read versions*) was not run on the device in this trial.
- **Behaviour on a system that denies access** to a normal user (running as administrator is not recommended and was not tried).
- Other receiver or headset revisions, and other Windows versions.
- Battery percentage is an estimate; capacity, charge current, temperature and battery health are not measured.
