<p align="center"><img src="assets/ApoSwitch-512.png" width="160" alt="APO Switch icon"></p>

# ApoSwitch 1.0.12

A small Windows tray app for switching Equalizer APO calibration presets.

[中文说明](README.md) · [MIT License](LICENSE) · [GitHub](https://github.com/hakanasora/ApoSwitch)

## Run

Requires Windows 11 x64, Equalizer APO, and a compatible **.NET 10 Windows Desktop Runtime x64**. The production `app` directory contains only `ApoSwitch.exe`; application code, the core library and icon are bundled. No adjacent DLLs, runtime JSON, language folders or display images are needed.

If the desktop runtime is missing, run the optional official `runtime/windowsdesktop-runtime-10.0.12-win-x64.exe`, follow the installer, then run ApoSwitch again. The plain .NET Runtime does not include Windows Forms support. No SDK, commands or manual DLL copying are required. System-wide runtime installation requires administrator permission. The application never downloads or installs it automatically.

The original installer is verified against Microsoft's SHA512 and digital signature. Its verification record is in `runtime/verification.json`. You may also use a compatible desktop runtime from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

Distribution layout: `app` for the production EXE, `runtime` for the optional installer, `docs` for documentation/license/artwork, `device-debug` for separate debug tools, and `source` for source code. Existing runtime installations are reused; the framework is not repeatedly bundled in the main download.

## Use

- Left-click or right-click the tray icon to open the menu. Double-click cycles through checked members. Single-click waits for the system double-click interval.
- The main preset list enables exactly one preset and disables other root-level preset Includes.
- **循环以下配置** independently selects cycle members and stays open for consecutive selections.
- **配置文件位置 → 选择 config.txt…** selects the root file actually used by APO. **恢复自动定位** returns to registry-based discovery.
- **开机自启动** enables startup for the current user; **退出** exits without reverting the active APO configuration.

`EQ1`, `EQ2`, etc. follow the order in the root file, including disabled candidates. External edits enabling multiple presets show `EQ+`; none enabled shows `EQ?`. Selecting a preset through the app still enables only that preset. Startup never applies a preset automatically. The initial cycle recognizes `New Door Open` and `GSR_BK_filters`, falling back to currently enabled items when no names match. New candidates do not automatically join the cycle.

Only direct `Include:` and commented `# Include:` lines in the root file become candidates. Nested files are never enumerated or modified. Switching preserves other directives, encoding, comments and line endings, and saves the previous root as `config.txt.aposwitch.bak`. Write permission is required; failures are reported. Check resource paths and audible results using Equalizer APO and listening.

## Configuration discovery

Automatic discovery reads `HKLM\SOFTWARE\EqualizerAPO`: 64-bit then 32-bit `ConfigPath`, followed by `InstallPath/config` only when no explicit ConfigPath exists, then the default Program Files location. It does not hard-code the developer's machine path.

An explicit but missing/unreadable ConfigPath is reported rather than replaced by an unrelated default file. A manually selected root overrides automatic discovery and is remembered. If it disappears, the app continues reporting the missing location until the user selects another or restores automatic mode. Invalid selections and preference-save failures leave the previous selection intact; cancelling the picker changes nothing.

Selecting a file only changes the app's target. It does not update APO's registry, create files or activate presets. Select the real root `config.txt` loaded by APO. Automatic discovery refreshes before opening the menu or applying a preset. File monitoring follows location changes, with periodic polling as fallback.

Preferences remain in `%LOCALAPPDATA%\ApoSwitch\settings.json`; nullable `ConfigFilePath` selects automatic mode when absent/null. Older settings remain compatible. Cycle edits preserve the location, and preferences do not travel with the EXE. Startup uses the current-user Run registry entry; re-toggle it after moving the executable.

## Device state and debug tools

The default multimedia playback device is monitored through Core Audio and a five-second poll. APO registration determines whether preset application is enabled. Missing APO uses a monochrome Blocked symbol with an EQ badge; no device, detection failures and config errors use a yellow warning. Device errors include the device name. Cycle/location/startup settings remain available when application is disabled.

Production references only `ApoSwitch.Core`, with no simulation module dependency, debug argument or named-pipe server. The optional debug directory contains `ApoSwitch.exe`, `ApoSwitch.Debug.exe` and `ApoSwitch.DeviceSimulator.exe`, each bundled as a single EXE using the shared desktop runtime. The debug host shares UI/configuration source and injects a device-state source.

Exit the regular tray, run the simulator, launch the debug tray, connect, and select one of four device states. Disconnecting or restarting clears the override. Both hosts share one instance mutex and startup always targets the production executable. **Only device state is simulated: preset changes still modify the real selected APO root file.**

## Build

Install the .NET 10 SDK. There are no third-party NuGet dependencies. Use an empty output directory:

```powershell
./build.ps1 -RunChecks
./build.ps1 -IncludeDeviceDebug -OutputDirectory ./artifacts-debug -RunChecks
# Explicitly download and verify the optional runtime installer, without installing:
./build.ps1 -IncludeRuntime -OutputDirectory ./artifacts-with-runtime
```

Default output is `artifacts`, with `app` and `docs` subdirectories. Publishing is framework-dependent, single-file, with ReadyToRun and trimming disabled. The SDK may restore Microsoft build packages.

Normal checks modify only temporary configuration files and query the actual device read-only. `-LiveConfigChecks` explicitly enables tests that briefly switch and restore the real APO configuration. Production isolation checks use `--verify-release <app-directory> <pre-bundle-assembly-directory>` on the checks executable.

`assets/ApoSwitch.svg` is the editable vector master. Lossless multi-size ICO artwork is embedded in the executable. Dynamic indicators use the system Segoe UI/Segoe Fluent Icons fonts, which are not redistributed. Ordinary builds need no image tools. To regenerate artwork: install Node.js and sharp, then run `node tools/render-icon.cjs`.