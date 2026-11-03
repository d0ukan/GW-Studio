# GW Studio Online Update — v1.1.0

Portal slug: `gw-studio`

Latest API:
`https://portal.doukan.io/api/updates/gw-studio/latest`

GW Studio performs an optional delayed check after startup. **Updates are optional. When a newer version is available, GW Studio asks before downloading or installing it.**

Expected portal package naming for this application release:

- Installer: `GW-Studio-Setup-v1.1.0.exe`
- ZIP: `GW-Studio-Setup-v1.1.0.zip`

The project archive does not contain an Inno Setup `.iss` source file, so installer metadata cannot be changed from this repository. When building the installer from the separate Inno template, set its application version to `1.1.0` and keep the EXE/ZIP names synchronized with that version.

Update flow:
1. GET latest JSON from the public portal API.
2. Compare numeric versions (leading `v` accepted).
3. Ask Update Now / Later only when the portal version is newer.
4. Download asynchronously into Windows TEMP.
5. Extract into a unique Windows TEMP folder.
6. Search recursively for an `.exe` whose name contains `Setup`.
7. Start the installer with Windows shell execution.
8. Close GW Studio only after the installer process starts successfully.

Portal/network/download/ZIP/setup errors are logged to `GW_Studio_App.log` and do not terminate the running application.
