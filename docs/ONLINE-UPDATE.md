# GW Studio online update

GW Studio checks **GitHub Releases** for new versions:

`https://api.github.com/repos/d0ukan/GW-Studio/releases/latest`

The check is optional and quiet: it runs shortly after startup and from **Help → Check for Updates**. **Updates are optional. When a newer version is available, GW Studio asks before downloading or installing it.**

## Publishing a release (maintainer)

See [RELEASING.md](RELEASING.md) for the full checklist and release-notes guide.

1. Bump `Version`, `AssemblyVersion`, `FileVersion` and `InformationalVersion` in `GW-Studio.csproj` and `installer/GW-Studio.iss` default, and add a `CHANGELOG.md` entry.
2. Create and push a tag `vX.Y.Z` (for example `v1.1.2`).
3. `.github/workflows/release.yml` builds a self-contained win-x64 publish and attaches to the GitHub Release:
   - `GW-Studio-Setup-vX.Y.Z.exe` (Inno Setup installer; used by the in-app updater)
   - `GW-Studio-Portable-vX.Y.Z.zip` (contains `portable.txt`)
4. The release must be **published** (not draft, not pre-release): the "latest" API endpoint ignores drafts and pre-releases. The release body is shown as release notes in the update prompt.

## Update flow (Setup edition)

1. GET the latest release JSON; `tag_name` (leading `v` accepted) is compared numerically with the installed version.
2. Ask **Update Now / Later** only when the release is newer.
3. Pick the asset whose name contains `Setup` (`.exe` preferred, `.zip` accepted and searched recursively for a Setup `.exe`).
4. Download asynchronously into Windows TEMP.
5. Start the installer with Windows shell execution.
6. Close GW Studio only after the installer process starts successfully.

## Portable edition

A `portable.txt` file next to the executable marks the Portable edition. It never runs an installer: **OPEN DOWNLOAD PAGE** opens the latest release in the browser and the user replaces the folder with the new ZIP.

Network, download, ZIP or installer errors are logged to `GW_Studio_App.log` and never terminate the running application. Unauthenticated GitHub API calls are limited to 60 per hour per IP, which is plenty for one check per launch.
