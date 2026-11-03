# GW Studio v1.1.1 revision summary

## Help
- Added a compact, task-oriented Help tab without release-history clutter.
- Help uses the same runtime assembly version shown in the header and Settings/About card.

## Optional online update
- Public update API: `https://portal.doukan.io/api/updates/gw-studio/latest`.
- Slug: `gw-studio`.
- Startup check is delayed/background and failures are silent apart from the application log.
- Manual Check for Updates reports latest-version status or a short connection error.
- Update Now downloads the ZIP asynchronously to Windows TEMP, extracts to a unique TEMP folder, finds a Setup `.exe` recursively and launches it. The running app closes only after successful installer launch.
- Later always leaves GW Studio running.

## Version source
- Runtime version comes from `AppVersionInfo` / assembly metadata rather than repeated hard-coded strings.
- Project version is 1.1.1; assembly/file versions are 1.1.1.0.

## Preserved
- Device communication, 704 Lab, graphs, CSV/PDF reporting, reconnect, Compact/Advanced modes and existing safety behavior are unchanged.

## Small-screen 704 Lab layout
- One outer vertical scrollbar, no nested RUN/SAVE scroll.
- The graph remains visible at page entry; RUN is adjacent and SAVE/LOAD follows below.
- GRAPH / EDITORS shortcuts remain available.


## 704 Lab
- Added four focused workflows: Quick Builder, 704 Test Library, Segment Editor and Drag Editor.
- Library provides ready bench starting points for Normal transient, Power interrupt, Undervoltage, Overvoltage, Recovery and Ramp without manual field entry.
- Segment Editor builds row-based profiles with Hold, Linear Ramp, Exponential Ramp and Output Off segments.
- Drag Editor provides mouse-driven editing of a small control-point set and converts the result into the normal 704 execution path.
- Added 1-100 Loop/Repeat and 0-600000 ms inter-loop delay controls shared by all waveform modes.

## Performance
- Repeat sequences are expanded only at ARM.
- Execution remains capped at 10,000 steps.
- Requested preview rendering is capped at 4,000 points; measured overlay rendering is thinned to 2,500 points while measurement storage remains bounded separately.
- Drag editing uses at most 64 control points and mouse updates are throttled to roughly 40 Hz.
- Segment DataGrid row/column virtualization is enabled.
- Identical consecutive execution states are treated as dwell-only steps and do not create redundant SCPI writes.

## Preserved
- Requested-vs-measured overlay, power interrupt builder, custom points, save/load, ARM hardware validation, Compact/Advanced UI, reconnect, graph controls and the established dark/cyan theme remain intact.
