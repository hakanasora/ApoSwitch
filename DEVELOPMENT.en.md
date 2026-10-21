# ApoSwitch development guide

[Product overview](README.en.md) · [中文](DEVELOPMENT.md)

## Build and distribution

Requires Windows and the .NET 10 SDK. There are no third-party NuGet dependencies. Normal builds use the committed ICO and need no graphics tools. Run from the repository root:

```powershell
.\build.ps1 -RunChecks
# Also build the separate device debugging tools:
.\build.ps1 -IncludeDeviceDebug -OutputDirectory .\artifacts-debug -RunChecks
# Download and verify the optional official runtime installer, without installing:
.\build.ps1 -IncludeRuntime -OutputDirectory .\artifacts-with-runtime
```

The output directory must be empty or not yet exist; the default is `artifacts`.

| Directory | Contents |
| --- | --- |
| `app` | The production single-file `ApoSwitch.exe` |
| `docs` | Product and developer documentation in both languages, MIT license and artwork |
| `device-debug` | Optional production EXE, debug tray and device simulator |
| `runtime` | Optional Microsoft runtime installer and `verification.json` |

Publishing targets Windows x64 with `SelfContained=false` and `PublishSingleFile=true`. ReadyToRun, trimming and debug symbols are disabled. Application code, the core library and artwork are bundled; Windows Forms and framework resources come from the shared .NET 10 Windows Desktop Runtime x64. The SDK may restore Microsoft build packages.

`tools/Get-Runtime.ps1` currently pins the official 10.0.12 x64 desktop runtime installer. It verifies the SHA512 from Microsoft's release metadata and the Microsoft Authenticode signature, then records the results. It never installs the runtime; the pinned installer does not require users to run that exact runtime patch version.

Publish the main EXE, optional runtime and debug tools as separate downloads. Product download links point to the latest GitHub Release rather than a fixed version or local packaging directory. The build copies documentation into `docs`, preserving language links and artwork references. Source, tools and build commands are used from the [repository](https://github.com/hakanasora/ApoSwitch).

## Configuration discovery and writes

Automatic discovery reads `HKLM\SOFTWARE\EqualizerAPO`: first `ConfigPath` in the 64-bit and 32-bit registry views; only when neither provides an explicit path, consider `InstallPath\config`, then the default Program Files directory. Missing or unreadable explicit paths produce an error rather than silently selecting another configuration.

A non-null `ConfigFilePath` setting overrides discovery. Manual selection reads the candidate first, saves preferences, then changes the target and watcher. Cancellation, read failures and save failures preserve the previous selection. A missing manual target remains an error until another selection or a return to automatic discovery. Selecting a location does not modify APO's registry, configuration contents or active preset.

Automatic mode resolves again when opening the menu, applying a preset and polling. The watcher follows the main file, with a 180 ms debounce and a five-second refresh as fallback.

Only direct `Include:` lines and their commented equivalents are parsed. No nested files are read and no directories are scanned. Duplicate target paths form one candidate at their first position. Applying a candidate enables one Include for that target and disables the other candidate lines, preserving other text, directives, encoding and line endings.

Writes check that the target file exists, write and flush a temporary file in the same directory, verify that the original has not changed, then use `File.Replace` with an `.aposwitch.bak` backup. Unchanged contents are not rewritten. Resource contents and audible results are outside these checks.

## Preferences and tray behavior

Preferences live in `%LOCALAPPDATA%\ApoSwitch\settings.json`. `CyclePresetIds` stores membership; absent or null `ConfigFilePath` means automatic discovery. Older settings remain compatible, and changing membership preserves the chosen location.

Cycling follows candidate order in the main file, not selection order. A preset outside the cycle starts at the first member; an empty cycle does nothing; an already exclusively active single member is not rewritten. `UserSettings.Initial` defines first-run membership, including legacy preset-name compatibility. Later candidates do not automatically join the cycle. Startup only reads the existing configuration.

A left-click waits for the system double-click interval, right-click opens immediately, and double-click cycles. The cycle submenu allows consecutive selections. Native menus and dynamic icons respond to system theme and scaling changes.

Startup is off by default and uses the current-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ApoSwitch` entry. Both production and debug trays target `ApoSwitch.exe` in their own directory. Keep that filename and re-toggle startup after moving it.

## Real devices and separate debugging tools

The real device source follows the Core Audio default multimedia playback endpoint. Device notifications, five-second polling, menu opening and pre-application queries refresh the state. It checks APO effects registration for LFX/GFX, SFX/MFX and SFX/EFX. This does not guarantee an effective audio processing chain; exclusive playback or disabled effects do not mean APO is uninstalled.

The production entry point references only the core library, without a simulation dependency, simulation argument or named-pipe server entry point. The separate debug host links shared UI source and injects an `IDeviceStateSource`, reusing configuration and cycle logic. Both tray hosts share a single-instance mutex.

To use the debugging tools:

1. Exit the production tray and run `ApoSwitch.DeviceSimulator.exe`.
2. Click **启动调试托盘 → 连接** to launch and connect to `ApoSwitch.Debug.exe` in the same directory.
3. Select APO installed, APO not installed, no playback device or detection failure, or restore real detection.

The simulator connects through a current-user-only local named pipe. Overrides stay in memory; disconnecting or restarting restores real detection. The tools share the installed desktop runtime, and startup still targets production.

**Only device state is simulated. Preset changes in the debug tray still modify the real selected main configuration.**

## Checks

`-RunChecks` runs configuration, cycle, path and device-state checks, followed by production release isolation checks. Normal checks modify temporary configurations and query the real device read-only.

`-LiveConfigChecks` briefly changes the real APO configuration and restores the original bytes. Use it only when explicitly needed, never as part of routine documentation validation.

Release isolation checks take both the single-file output and pre-bundle assemblies. The `ApoSwitch.Checks` entry is `--verify-release <app-directory> <pre-bundle-assembly-directory>`. It checks that production contains only one EXE no larger than 2 MB, is framework-dependent, and has no simulation dependency or pipe-server entry point in production assemblies.

Keep both documentation languages aligned. After changing the build's documentation copy list, check the resulting files, images and relative links. Repository source and tool links should point to GitHub so distributed developer guides remain usable.

## Artwork and references

`assets/ApoSwitch.svg` is the vector master, using paths, smooth gradients and shadows without embedded bitmaps, texture noise or font dependencies. The embedded ICO uses lossless PNG frames rendered independently at 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 px. Display PNGs are also generated at 512 and 1024 px.

Normal builds do not regenerate artwork. To update it, install Node.js and sharp, then run from the repository root:

```powershell
npm install --no-save sharp
node tools/render-icon.cjs
```

Dynamic tray indicators use the system Segoe UI / Segoe Fluent Icons fonts, which are not redistributed. APO not installed uses Blocked (E733) with an EQ badge; other errors use WarningSolid (F736).

- [Equalizer APO device detection source](https://github.com/mirror/equalizerapo/blob/master/DeviceAPOInfo.cpp)
- [Equalizer APO configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)
- [Microsoft .NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [MIT license](LICENSE)
