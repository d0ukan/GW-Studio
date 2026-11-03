# GW Studio

Windows desktop application for controlling, monitoring, logging and automating tests on **GW Instek GPP-3060** and **GPP-4323** programmable power supplies over USB CDC (virtual COM port).

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF-0078D4) ![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey) ![Version](https://img.shields.io/badge/version-1.1.1-00BCD4)

Current version: **1.1.1** — see [CHANGELOG.md](CHANGELOG.md) for release history.

## Features

- **Automatic model detection** — the channel layout and safety limits come from the verified `*IDN?` response.
- **Dashboard** — large live V / A / W readouts, device setpoint readback, per-channel locks, quick presets, soft warnings and live CV / CC / CR / TRIP status badges.
- **Live Graph** — full-screen graphs with pause, cursor, pan/zoom/reset, event markers and visible-window CSV / PNG export (OxyPlot).
- **Logger** — continuous CSV logging and on-demand PDF session reports.
- **Custom / Sequence** — PC-timed numeric V/I profiles and the instrument's native sequence.
- **704 Lab** — waveform builder (Linear, Exponential, Logarithmic, Inverse-time, reference equation), 704 Test Library, Segment Editor, Drag Editor, loop/repeat, and a save/load library. PC-timed bench aid only; **not** a MIL-STD-704 compliance tool.
- **Terminal** — raw SCPI console.
- **Compact / Advanced view**, auto reconnect, connection-health and acquisition-rate indicators.
- **Optional online update** — asks before downloading or installing anything.

## Supported models

| Model | Channel | Programming behavior |
| --- | --- | --- |
| GPP-3060 | CH1 / CH2 | 0–32 V, 0–6 A; programmable V/I |
| GPP-3060 | CH3 | selectable 1.8 / 2.5 / 3.3 / 5 V; fixed 5 A rating; output control only |
| GPP-4323 | CH1 / CH2 | 0–32 V, 0–3 A; programmable V/I |
| GPP-4323 | CH3 | 0–5 V, 0–1 A; programmable V/I |
| GPP-4323 | CH4 | 0–15 V, 0–1 A; programmable V/I |

CONNECT opens the selected COM port and sends `*IDN?`. Only an exact supported model is accepted; no channel object is created and no channel command is allowed until identification succeeds.

- Tracking Series/Parallel, Load CV/CC/CR and native Sequence are CH1/CH2-only on both models.
- GPP-3060 CH3 is excluded from PC-timed numeric V/I profiles because its current is not programmable. It stays available on the Dashboard, graphs, logging and output controls.
- Saved quick presets and Custom profiles carry a `TargetModel`. A profile made for one model is never loaded or run on the other; legacy untagged profiles are rejected and must be rebuilt after connecting.

## Safety behavior

- Connecting reads identity/settings/states and **never changes an output**.
- APPLY SETTINGS changes setpoints only; turning an output ON always needs an explicit action.
- If any output is ON or states are mixed, ALL ON/OFF first turns every detected channel OFF. ALL ON is only allowed from a verified all-OFF state.
- Disconnect/close preserves outputs by default; OFF-on-disconnect is opt-in.
- Unsupported models, nonexistent channels, out-of-range values, fixed-current writes and cross-model profiles are rejected **before** any device write.
- An always-visible **ALL OUTPUT OFF** button is provided.
- Software current auto-off depends on polling/serial latency and does **not** replace the instrument's hardware OVP/OCP or external protection.

## Requirements

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or Visual Studio 2022 with the *.NET desktop development* workload)
- GW Instek USB CDC driver for the instrument

Serial settings: 115200 baud, 8 data bits, no parity, 1 stop bit, LF termination.

## Build, run and test

```powershell
dotnet restore GW-Studio.sln
dotnet build GW-Studio.sln -c Release
dotnet run --project GW-Studio.csproj

# console test runner (no hardware needed)
dotnet run --project Tests/GW-Studio.Tests.csproj -c Release
```

`Tests/serial_pty_test.py` is a Linux-only integration fixture that drives the production `SerialDeviceService` through a real pseudo-terminal:

```bash
python3 Tests/serial_pty_test.py /path/to/dotnet
```

Package versions are pinned via `packages.lock.json` (`RestorePackagesWithLockFile`); commit it when dependencies change.

## Project layout

```text
GW-Studio.sln
GW-Studio.csproj        WPF app (net8.0-windows), assembly name "GW Studio"
App.xaml / MainWindow   Application shell
Controls/               ChannelCard, GraphPanel, LiveChartControl
Views/                  Dashboard, 704 Lab, Custom, Sequence, Logger, Terminal, Settings, Help
ViewModels/             MVVM view models (Main, Channel, Graph, Custom, Sequence, Mil704, ...)
Models/                 Device models/profiles, custom test profile, update info
Services/               Serial/GPP device, polling, CSV/PDF/plot export, settings, logging, updates
Helpers/                Commands, observable base, number parsing, version info
Themes/                 Dark/cyan theme
Assets/                 Icons and logo
Tests/                  Console test project + serial PTY fixture
docs/                   SCPI commands, hardware checklist, online update, manual notes
```

Dependencies: `System.IO.Ports` 8.0.0, `OxyPlot.Wpf` 2.2.0.

## Data locations

| Data | Location |
| --- | --- |
| Settings | `%APPDATA%\Dou-GW-Studio\settings.json` |
| Custom profiles | `%LOCALAPPDATA%\Dou-GW-Studio\Profiles` |
| Logs / reports | `Logs\yyyy-MM-dd\` beside the executable, falling back to `%LOCALAPPDATA%\GW Studio\Logs\yyyy-MM-dd\` |
| Application / update log | `GW_Studio_App.log` in the active daily Logs folder |

## Online update

GW Studio quietly checks `https://portal.doukan.io/api/updates/gw-studio/latest` after startup. Updates are optional: when a newer version exists the app asks before downloading or installing, and **Later** leaves it running normally. **Check for Updates** is also available in the Help tab. Downloads and extraction use Windows TEMP, and the app exits only after the Setup `.exe` has started successfully. Portal/network failures never block startup or device control. Details: [docs/ONLINE-UPDATE.md](docs/ONLINE-UPDATE.md).

## Documentation

- [docs/COMMANDS.md](docs/COMMANDS.md) — SCPI commands used
- [docs/HARDWARE-CHECK.md](docs/HARDWARE-CHECK.md) — hardware acceptance checklist (run on both a GPP-3060 and a GPP-4323)
- [docs/GPP-SERIES-MANUAL-NOTES.md](docs/GPP-SERIES-MANUAL-NOTES.md) — manual notes
- [docs/ONLINE-UPDATE.md](docs/ONLINE-UPDATE.md) — update flow and package naming
- [docs/TEST-RESULTS.txt](docs/TEST-RESULTS.txt) — validation notes
- [REVISION-SUMMARY.md](REVISION-SUMMARY.md) — summary of the latest revision

---

Made by Dou.
