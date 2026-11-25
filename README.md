# Red Control Utility (RCU)

A small, open-source control panel for AMD Radeon graphics cards on Windows. Unofficial community project, not affiliated with AMD.

![version](https://img.shields.io/badge/version-0.2.13-blue)

## What is RCU?

RCU replaces AMD Software (Adrenalin) for everyday use. Install only the AMD graphics driver (a driver-only install, without Adrenalin), install RCU, and you can control your card from one light app that sits in the system tray:

- tune the GPU: clocks, undervolt, power limit, fan curve and VRAM
- set up each monitor: FreeSync, GPU scaling, VSR, color
- turn Radeon features on or off: Anti-Lag, AFMF, FSR upgrade, Chill, Image Sharpening and more
- give each game its own profile, applied when the game opens and undone when it closes
- keep all of it in place after a reboot, after sleep, and when the driver resets things

It works with Radeon cards from Polaris (RX 400/500) to RDNA 4, uses very little memory and CPU, and does not need AMD Software to be installed or running.

> **Status:** version 0.2.13, early release. Not every driver and GPU combination behaves the same. If an option does not respond, **Settings → Diagnostics** shows exactly what the driver answered. See [Troubleshooting](#troubleshooting).

## Install

1. Install the AMD graphics driver. A driver-only install is enough.
2. Download **`RCU-Setup-<version>.exe`** from [Releases](https://github.com/Khotyz/RCU/releases) and run it. It asks for administrator rights, installs to `C:\RCU` by default, and downloads the Microsoft Visual C++ runtime only if your PC does not have it.
3. Open **Red Control Utility** from the Start menu. During setup you can choose a desktop shortcut and "Start with Windows".

To update, run the new setup over the old one. The old program files are removed first, so nothing from a previous version is left behind, and your settings are kept. A portable ZIP is attached to each release for testing, but the setup is the supported way to use RCU.

**Requirements:** Windows 10 version 2004 (build 19041) or newer, 64 bit, and an AMD Radeon card with the AMD display driver installed. Nothing else to install: .NET and the Windows App Runtime are bundled.

Your settings live in `%APPDATA%\RedControlUtility` and the logs in `%LOCALAPPDATA%\RedControlUtility\logs`. Delete those folders to reset everything.

## What it can control

| Area | Options |
| --- | --- |
| GPU tuning | min and max clock, max voltage (undervolt), power limit, VRAM clock and timing, fan curve, Zero RPM, voltage and frequency state list on older cards |
| Display (per monitor) | FreeSync, Virtual Super Resolution, GPU scaling, integer scaling, scaling mode, custom color (hue, saturation, brightness, contrast, temperature) |
| 3D features | Radeon Anti-Lag, AMD Fluid Motion Frames (AFMF), FSR upgrade, Radeon Super Resolution, Image Sharpening, Radeon Boost, Radeon Chill, Enhanced Sync, V-Sync, anisotropic filtering, tessellation |
| Game profiles | any mix of the options above, applied only while the game runs |
| Extras | live sensors and history charts, color profiles, game overlay, tray icon, crash recovery, shader cache cleanup, diagnostics |

An option your card or driver does not offer stays greyed out.

## Getting started

1. **Look at the Dashboard.** It shows your GPU, live usage, clocks, temperature, power and fan, plus charts of the last two minutes.
2. **Try a quick preset.** The Dashboard has one-click presets: Safe undervolt (-25 mV), Quiet, Balanced (factory) and Performance. Their values stay inside the ranges your driver reports.
3. **Add your games.** Open **Apps**, press **Search games** (it scans Steam, Epic Games and GOG) or **Add app**, and choose the options each game should use.
4. **Let it sit in the tray.** Turn on **Start with Windows** and **Start minimized** in Settings. RCU then does its work quietly in the background.

## Using the app

### Dashboard

GPU model, architecture, memory, driver version, BIOS, subsystem ID, PCIe link (current and maximum, the current one drops at idle) and live statistics: usage, clocks, temperatures, power, fan speed (and duty when the driver reports it) and VRAM. Four small charts show the last two minutes of usage, hotspot temperature, power and fan speed. They only collect data while the Dashboard is open, so they cost nothing while the app is in the tray. The Dashboard also shows which game profile is active and offers the quick presets.

### Apps (game profiles)

1. Press **Add app** (or drag a `.exe` or `.lnk` onto the page) and pick the game.
2. Select it and set each option to **Driver default**, **On** or **Off**, or give it a value. Anything left on Driver default is not touched.
3. Options with a value (frame cap, Chill range, sharpening, Boost resolution, power limit) show a slider with presets once you turn them on.
4. **Quick setup** fills the whole profile in one click: Competitive, 60 FPS cap, Power saver, Image quality, Clear all.
5. **Search games** scans Steam, Epic Games and GOG and adds several games at once, as a grid or a list.
6. **Add running process** lists the programs open right now. Settings only apply when a game starts, so close it and open it again after adding.
7. **Global profile** is one set of options shared by every game that has *Use the global profile* ticked. New games start with their own settings.

Each game can also run a program when it starts and another when it closes, for example to start a monitoring tool. The program runs hidden, without a shell. Commands are never imported: a profile loaded from a file has them removed, so a shared profile cannot run anything on your PC.

When a game closes, your previous values are always restored.

### Tuning

Manual GPU tuning, depending on what your driver supports: min and max clock, voltage, power limit, VRAM clock and timing, fan curve or direct RPM, and Zero RPM. Cards with a voltage and frequency state list get an editable list and chart.

- Moving a slider only changes the label. Nothing goes to the driver until you press **Apply**, unless live apply is on. Apply sends only the values you changed.
- **Uniform offset** (-10, -25, -50 mV) lowers the voltage in a straight line from the factory value, like MSI Afterburner.
- **Keep min clock 100 MHz below max** makes the minimum follow the maximum.
- **Reset to factory** restores the default values.

> **Warning:** manual tuning can crash or destabilise the system. Change values in small steps. Safe apply and the stability watchdog (below) are there to help.

### Display

Per-monitor options: FreeSync, Virtual Super Resolution, GPU scaling, integer scaling, scaling mode and custom color, plus saved color profiles. VSR and integer scaling need GPU scaling on, so when you turn VSR on the app enables GPU scaling first. Options your monitor or driver does not support are greyed out.

### Profiles

Save the current driver state as a profile, apply it, export it to a JSON file, or import one. The current profile is saved automatically.

### Settings

Language (Brazilian Portuguese, English, Spanish, Simplified Chinese), apply mode, advanced mode, confirmation before tuning, live undervolt, auto revert, game profiles, overlay, start with Windows, start minimized, minimize to tray, backend, GPU selection, sensor interval, shader cache cleanup, update check and diagnostics.

Apply mode: *Instant* sends each change as you make it, *Manual* waits for the Apply button. Fine tuning always uses Apply.

## Your settings stay in place

What you apply is saved and enforced while the app runs, including in the tray. Tuning (clocks, voltage, power limit, VRAM, fan) is reapplied at startup and after sleep, and checked every 45 seconds: if the driver resets a value, RCU puts it back. Display settings (adaptive sync, scaling, color) are kept the same way. Power limits are left alone while a game profile is active.

**Reset to factory** clears the saved tuning. If the app crashed during a tuning change, it does not reapply tuning on the next start, so apply it again from the Tuning page.

## Safe apply

When you apply tuning or a preset, a dialog gives you 15 seconds to confirm. If you do not, the previous values come back automatically. You can turn this off in Settings.

If the PC freezes or reboots before you confirm, RCU notices on the next start, does not reapply anything on its own, and tells you what happened.

## Stability watchdog

If the video driver stops responding and recovers while an undervolt or overclock is active, RCU returns the GPU to factory values, clears the saved tuning and shows a notice. It listens to the Windows event log, so it costs almost nothing. You can turn it off in Settings.

## Simple and Advanced mode

RCU starts in Simple mode: Dashboard, Apps, Tuning and Settings. Advanced mode also shows Display and Profiles, image quality options, VRAM tuning, the fan curve and the voltage state editor. Advanced fields stay in place and are dimmed, so the window never jumps around.

## Game overlay

When a game with a profile starts, a small overlay shows in the corner of its screen listing what the profile turned on or off. It never takes focus, lets clicks through and disappears by itself (3 seconds by default). Configure it in Settings. Games in exclusive fullscreen may hide it.

## Interactive charts

- **Manual editing:** click a point and drag it. On cards with a state list each point moves freely. On cards that take min clock, max clock and one max voltage (such as RDNA 2) the top point is your max clock and max voltage.
- **Linear change:** select several points (click, drag a box, Ctrl to add, Ctrl+A for all, Esc to clear), then drag, use the mouse wheel or the arrow keys. The dashed curve is a ghost of the last applied values.
- The fan curve keeps points coherent: setting a point to 100% raises every later point to 100%.

## Updates

RCU checks this repository's GitHub releases once at startup (you can turn it off in Settings). Nothing is downloaded without your confirmation. When a newer release exists, a bar appears with a **Download** button. Run the new setup to update. The check compares the release tag with your version, for example `0.3.0` is newer than `0.2.8`.

## Game profiles: three ways to use them

AMD offers no public per-game API, so RCU applies the profile itself while the game runs.

1. **Watcher mode (default).** Keep **Enable per app profiles** on. RCU checks running programs once per second and applies the matching profile when a game starts. It can sit in the tray.
2. **Launcher mode.** `RedControlUtility.exe --launch "C:\Games\Game\game.exe"` applies the profile, starts the game, waits for it to exit, restores your values and quits. Use it for games that only read some options (AFMF, FSR) before they start. Nothing stays in memory while you are not playing.
3. **Apply once and exit.** `RedControlUtility.exe --apply "profile.json"` applies a saved profile without opening a window, handy as a scheduled task at logon.

| Argument | What it does |
| --- | --- |
| `--launch "<exe>" [args]` | applies the profile of that app, runs it, restores on exit, then quits |
| `--apply "<profile.json>"` | applies the profile and quits, with no window |
| `--startup` | used by the "start with Windows" entry |
| `--minimized` | starts hidden in the tray |

## GPU support: Polaris to RDNA 4

The card is detected from its PCI device ID and name, covering Polaris, Vega, RDNA 1, 2, 3 and 4. What you can control depends on what your driver reports. RCU disables what it cannot confirm, and where a driver wrongly says "not supported" it tries anyway and reports the real answer.

- **RDNA 1 to RDNA 4:** tuning through ADLX for the features the driver reports.
- **Polaris (RX 400/500):** frequency and voltage per performance state for undervolting and overclocking. If ADLX does not expose core tuning or fan control, RCU uses AMD Display Library Overdrive N for the missing controls, with minimum and target fan RPM instead of a curve. Polaris tuning has not been verified on real hardware yet.
- **Other older cards:** ADLX support where available. The sensor fallback is read-only.

## Feature conflicts the driver enforces

The driver answers "OK" and still ignores some combinations, so RCU resolves them before writing and lists every decision in the log:

- AFMF does not run together with Chill, Image Sharpening, RSR or Boost. AFMF wins and the others are reported as not applied.
- Enhanced Sync needs a FreeSync display. Without it, RCU enables FreeSync first.
- VSR and integer scaling need GPU scaling on.

After applying, RCU reads every value back from the driver. A setting the driver silently ignored is reported as a failure, with what the driver answered, instead of a green "OK".

## Capping the frame rate

RCU has no standalone frame rate limit. For a cap, use Radeon Chill with the same minimum and maximum, which is what the **Limit to 60 FPS** quick setting does. Do not combine it with another cap in the game or another tool.

## FSR upgrade

FSR upgrade needs RDNA 2 or newer (RX 6000 and up). On older cards the option is disabled with a note. It works straight in the driver, without AMD Software. The driver reads the setting when a game starts, so RCU sets it as the game opens and restores it afterwards. If the game was already running, restart it. The game must have FSR 3.1 or newer enabled in its own settings, and the driver only upgrades the titles it supports. Vulkan games are not covered.

## Memory and CPU use

- Sensors only run while the Dashboard is open.
- When minimized to the tray, the sensors stop, the current page is released and the app trims its memory now and every few minutes.
- Background checks (settings drift, game detection) are light and skip work when nothing changed.
- A Debug build with the debugger attached uses 2 to 3 times more memory. Measure a Release build started outside Visual Studio.
- `--apply` and `--launch` never create a window.

## Troubleshooting

**An option is greyed out.** The driver did not report it as supported for your GPU or monitor. Open **Settings → Diagnostics**, press **Copy** and read what the driver answered for that feature.

**A setting says OK but nothing changes in the game.** Check the feature conflicts above. Also check whether the game reads the setting only when it launches, in that case use launcher mode.

**The game does not hold the frame rate I set.** Use Chill with the same minimum and maximum, a little below your refresh rate if V-Sync is on.

**VSR does not appear in Windows.** VSR needs GPU scaling on. RCU enables it, but Windows may need to re-detect the display (Win+Ctrl+Shift+B, or disable and enable the monitor) before the new resolutions show up.

**The GPU clock shows 0 or jumps around.** Radeon cards switch parts of the chip off when idle, so a clock of 0 is normal at rest. Setting min and max clock to the same value is the closest thing to a locked clock.

**The app says ADLX is unavailable.** Usually `amdadlx64.dll` is missing, which means the AMD driver is not properly installed. **Settings → Diagnostics** shows whether the file exists, which backend was used and the startup log.

**Where are the logs?** In `%LOCALAPPDATA%\RedControlUtility\logs`, or press **Open logs** in Settings. When reporting a problem, attach the text from **Settings → Diagnostics**.

**Drag and drop does not work.** Windows blocks dragging from Explorer into an administrator window. Use the **Add app** button instead.

## License and disclaimer

AGPL-3.0, see [LICENSE.txt](LICENSE.txt).

This is an unofficial community project, **not affiliated with, endorsed by or supported by AMD**. "Radeon", "Adrenalin", "FreeSync", "Anti-Lag", "Fluid Motion Frames", "Radeon Boost", "Radeon Chill" and "Radeon Super Resolution" are trademarks of Advanced Micro Devices, Inc. RCU calls AMD's own public driver APIs, like other open-source tools such as GPU-Z. Nothing is reverse engineered and no AMD code is redistributed.

Manual overclocking, undervolting and firmware-level changes are always at your own risk. The authors are not responsible for damage to hardware, data loss or instability.

---

# For developers

Everything above is for users. The rest of this file is for people who want to build, package or understand the code.

## How RCU talks to the driver

In **Automatic** mode the app tries these backends in order:

1. **AMD's own DLL:** `C:\Windows\System32\amdadlx64.dll`, installed with the display driver. The app asks it for the driver's own ADLX version first, like AMD's official `ADLXHelper`.
2. **Helper DLL:** `RedAdlxHelper.dll` next to the executable (optional fallback).
3. **Fallback without ADLX:** GPU details from the registry and sensors through ADL where supported. On a verified Polaris adapter, Overdrive N can also provide core and fan tuning. Controls that need ADLX stay unavailable.

You can force a backend in **Settings → Hardware**.

A self-built ADLX helper must **not** be named `amdadlx64.dll`: it calls `LoadLibrary("amdadlx64.dll")` internally and would load itself instead of AMD's real library. Keep it as `RedAdlxHelper.dll`, built in Release x64 (a Debug build needs `VCRUNTIME140D.dll` and `ucrtbased.dll` from Visual Studio), and put it in the `native` folder. It is optional.

GPU scaling uses `ADL2_DFP_GPUScalingEnable_Set` through `atiadlxx.dll` when ADLX reports it unavailable. FSR upgrade uses the ADLX service when the driver exposes it, otherwise the `FsrOverride` value in the driver's `UMD` key through `ADL2_Adapter_RegValueString_Set`, with support checked through `ADL2_DriverMLSRSupport_Get` and the presence of `amdxcffx64.dll`.

## Building from source

Requirements: Visual Studio 2022 with the .NET desktop workload, the Windows App SDK tooling, the Windows SDK and the .NET 8 SDK.

1. Open `RedControlUtility.sln`.
2. Set the platform to **x64** and the configuration to **Release**. Debug works but uses the Visual Studio debug runtime and much more memory.
3. Build, or run from a terminal:

```
dotnet publish RedControlUtility -c Release -r win-x64 -p:Platform=x64
```

The `locales` and `native` folders are copied into the output automatically.

## Installer and releases

The installer is an Inno Setup script in `installer/RCU.iss`. It installs to `C:\RCU` by default (changeable in the wizard), adds Start menu and optional desktop shortcuts, an optional "start with Windows" entry and a normal uninstaller. When an existing RCU install is found in the folder, its old files are removed before the new version is copied. Settings and logs live in your user profile, so neither updating nor uninstalling touches them.

To build the installer locally:

1. In Visual Studio choose **Release | x64**, right-click the **RedControlUtility** project, choose **Publish** and run the included **FolderProfile**.
2. The publish step trims what the app does not need (unused languages, AI and machine learning components, debug tools) and then compiles `installer/RCU.iss` with **Inno Setup 6.5 or later**. The script also works with Inno Setup 7, and the project looks for version 7 first.
3. The installer reads `Product` and `Version` from the published executable's metadata, which comes from `RedControlUtility.csproj`. Nothing needs to be edited in the `.iss`. The output is `dist/RCU-Setup-<version>.exe`.

The package is self-contained and uses solid LZMA2 compression. It includes only the English, Brazilian Portuguese, Spanish and Simplified Chinese resources. The Simplified Chinese setup messages are from [Inno Setup's translation collection](https://github.com/jrsoftware/issrc/blob/is-6_7_4/Files/Languages/Unofficial/ChineseSimplified.isl).

To publish a GitHub release, set `Version` in the project file, then push a matching tag such as `0.2.8` (without a "v"). The release workflow publishes the app, builds the installer and a portable ZIP, and attaches both to the release. The in-app updater compares the latest release tag with the installed version.

## Project layout

```
RedControlUtility/
  Models/            settings and profile models
  Services/          hardware facade
    Adlx/            ADLX interop (vtable calls), sessions for 3D, display and tuning
    Adl/             ADL fallbacks (Overdrive N, GPU scaling, FSR override, sensors)
    *.cs             profiles, sensors, overlay, tray, updates, stability watchdog
  Views/             WinUI pages (Dashboard, Display, Apps, Tuning, Profiles, Settings)
  Controls/          interactive charts and layout helpers
  locales/           pt-BR.json, en-US.json, es-ES.json, zh-CN.json
  native/            optional RedAdlxHelper.dll
installer/           Inno Setup script and publish trimming
```

---

Built with help from AI.
