<p align="center"><img src="assets/ApoSwitch-512.png" width="128" alt="ApoSwitch application icon"></p>

# ApoSwitch

Switch Equalizer APO calibration presets from the Windows tray: click to choose, double-click to cycle.

**[Download the latest release](https://github.com/hakanasora/ApoSwitch/releases/latest)** · [中文](README.md)

## Why ApoSwitch?

Different listening situations may call for different calibrations. Once you have several Equalizer APO configurations, opening the editor and enabling or disabling them each time interrupts what you are doing.

ApoSwitch puts those actions in the tray. Choose a preset directly, or add your favorites to a cycle and double-click the icon to switch. Indicators such as `EQ1` and `EQ2` show which preset is active at a glance.

## How it works

ApoSwitch runs in the background, automatically locates Equalizer APO's main `config.txt`, and lists the **configuration files directly included by that file**, including commented-out Includes.

Selecting a preset enables its root-level Include and disables the other candidates. Nested configuration files stay untouched. Equalizer APO handles the audio processing; ApoSwitch uses your existing presets and does not create calibrations or filters.

The app follows the Windows default playback device. If APO is not installed on that device, it shows an unavailable state and pauses preset switching. Starting the app reads the current configuration without changing it.

## Download and get started

Requires **Windows 11 x64**, an installed and configured **Equalizer APO**, and the **.NET 10 Windows Desktop Runtime x64**.

1. Open the [latest release](https://github.com/hakanasora/ApoSwitch/releases/latest) and download the main EXE. 
2. Put the EXE in a permanent location, save it as `ApoSwitch.exe`, and run it. Startup at sign-in also expects this filename.
3. If prompted about a missing runtime, install the separately provided `windowsdesktop-runtime-…-win-x64.exe`, then run the app again. Alternatively, select **.NET Desktop Runtime → Windows → x64** on [Microsoft's download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). The plain .NET Runtime does not include desktop UI support; you do not need the SDK.
4. Find the tray icon, click it to open the menu, and choose a preset. The icon may be in the tray's hidden-icons area.

If no configurations are found, use **配置文件位置 → 选择 config.txt…** to select the main file actually loaded by Equalizer APO. The current app menus are in Chinese; the labels below match the interface.

## Everyday controls

| Action | Control |
| --- | --- |
| Open the menu | Left-click or right-click the tray icon |
| Apply a preset immediately | Click its name at the top of the menu |
| Choose cycle members | Check presets in **循环以下配置**; the menu stays open for consecutive selections |
| Switch to the next member | Double-click the tray icon; cycling follows their order in the main configuration |
| Change the main file | **配置文件位置 → 选择 config.txt…**; **恢复自动定位** restores automatic discovery |
| Start at Windows sign-in | Check **开机自启动**; off by default |
| Exit | Choose **退出**; the current APO configuration stays active |

**You can check several cycle members, but applying a preset enables only that one.** Cycle membership only controls which presets double-clicking visits; checking a member does not activate it. Your selection is saved, and new presets do not automatically join the cycle. With no members checked, double-clicking does nothing.

A left-click waits for the system double-click interval before opening the menu so the app can distinguish the two gestures.

## Icons and device state

| Icon or state | Meaning |
| --- | --- |
| `EQ1`, `EQ2`… | The active preset's position in the main file, counting disabled candidates too; independent of cycle membership |
| `EQ+` | Multiple presets were enabled in the APO editor or another external edit; choosing one through the tray makes it the only active candidate |
| `EQ?` | No root-level preset is enabled |
| Blocked symbol with an EQ badge | APO is not installed on the default playback device; the tooltip reads “当前设备不适用” (current device is not applicable) |
| Yellow warning | No playback device, a detection failure, or a configuration read error; open the menu for details |

When the device is unavailable, direct application and double-click switching are disabled. Cycle settings, configuration location, startup and exit remain accessible. Hover over the icon to see the current preset or device state; the menu's error group also identifies the device. The menu and icons follow the system light/dark theme and display scaling.

## Frequently asked questions

### Why are no presets found, or why is a new file missing from the menu?

Only direct `Include:` and commented `# Include:` lines in the main `config.txt` become candidates. Placing a file in the configuration folder is not enough. Add it as a root-level Include and save; the list updates automatically.

If the main file cannot be read, select the correct file under **配置文件位置**. A manual location is remembered; choose **恢复自动定位** to return to automatic discovery. This choice changes the switcher's target, not which main file Equalizer APO loads.

### Will nested Includes be changed?

No. ApoSwitch does not expand nested files and only changes the enabled state of root-level candidates. Other directives, comments, encoding and line endings are preserved. The previous main file is backed up as `config.txt.aposwitch.bak` in the same folder.

### Why does switching fail, or why is there no audible change?

The target preset must exist, and the main configuration folder must be writable. Failures report an error; check the indicated files and permissions. Device detection checks APO's installation registration. For audible results, also confirm Equalizer APO is working on that device and the configuration's resources are available.

### Why does startup stop working after moving the EXE?

Keep the filename `ApoSwitch.exe`, then turn **开机自启动** off and on again in the new location to update the startup entry. Cycle membership and other preferences belong to the current Windows user and do not move with the EXE.

## Development and license

[Development guide](DEVELOPMENT.en.md) · [MIT license](LICENSE)
