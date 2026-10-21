<p align="center"><img src="assets/ApoSwitch-512.png" width="160" alt="APO Switch icon"></p>

# ApoSwitch

A small Windows tray app for switching Equalizer APO calibration presets.

[中文说明](README.md) · [MIT License](LICENSE)

## Use

Requires Windows 11 x64 and an installed Equalizer APO. Self-contained Windows builds include the .NET runtime.

- Left-click or right-click the tray icon to open the menu. Double-click to cycle through selected presets.
- The main preset list activates exactly one preset and disables the other root-level preset Includes.
- The **循环以下配置** submenu selects cycle members independently. It stays open for consecutive selections.
- The tray shows `EQ1`, `EQ2`, etc., following the order in the root `config.txt`, including disabled entries.
- External edits that enable multiple presets show `EQ+`; no enabled presets show `EQ?`. Startup never applies a preset automatically.
- The default playback device is monitored. Applying presets is disabled when APO is not registered, no device is available, or detection fails. Cycle selection and startup settings remain available.
- **开机自启动** enables startup for the current user; **退出** exits without reverting the current APO configuration.

Only direct `Include:` and commented `# Include:` lines in the root `config.txt` become candidates. Nested Includes are never enumerated or modified. Newly added candidates do not automatically join the cycle. The initial cycle recognizes `New Door Open` and `GSR_BK_filters`; other users can select their own members in the submenu.

Switching preserves other root directives, encoding, comments, order and line endings. The previous version is saved as `config.txt.aposwitch.bak`. Write access to the APO configuration directory is required. Resource paths and audible results should be checked with Equalizer APO and listening.

Preferences are stored in `%LOCALAPPDATA%\ApoSwitch\settings.json`.

## Production and device-debug builds

The production executable references only `ApoSwitch.Core`; it has no device-simulation module dependency, debug command-line entry point, or named-pipe server. The debug host is a separate executable, `ApoSwitch.Debug.exe`, sharing the same configuration and UI source files.

The optional `ApoSwitch.DeviceSimulator.exe` launches the debug host and sends device states over a current-user-only local named pipe. Simulation never changes how preset files are written: switching still modifies the real APO root configuration. Disconnecting clears simulated state. Both hosts use the same single-instance mutex, and startup always targets the production executable.

## Build

Install the .NET 10 SDK, then run PowerShell from this directory:

```powershell
./build.ps1 -RunChecks
# Also create the separate device-debug tools:
./build.ps1 -IncludeDeviceDebug -OutputDirectory ./artifacts/with-debug -RunChecks
```

Use a new or empty output directory for each build. The default output is `artifacts/app`. Optional debug tools go into a separate directory with the suffix `-device-debug`. There are no third-party NuGet dependencies; the SDK may download Microsoft Windows runtime packs.

Normal checks use temporary configuration files and query the actual playback device read-only. `-LiveConfigChecks` explicitly enables tests that briefly switch the real APO configuration and restore its original bytes. `--verify-release <directory>` on the checks executable verifies production artifact isolation.

## Artwork

`assets/ApoSwitch.svg` is the editable vector master. The ICO contains ten separately rendered, lossless PNG frames from 16 to 256 pixels; 512 and 1024 pixel PNGs are also included. Ordinary builds need no image tools. To regenerate artwork with Node.js: `npm install --no-save sharp`, then `node tools/render-icon.cjs`.

System Segoe UI / Segoe Fluent Icons fonts are used at runtime for the dynamic tray indicators and are not redistributed.
