# Red Control Utility (RCU), An open-source, lightweight control utility for AMD drivers. (Unofficial community project, not affiliated with AMD).

![version](https://img.shields.io/badge/version-0.2.5-blue)

RCU is a small WinUI 3 / .NET 8 app that replaces AMD Software (Adrenalin) for everyday control of a Radeon GPU. It talks straight to the AMD display driver through ADLX (`amdadlx64.dll`) and ADL (`atiadlxx.dll`), which ship with the driver, so a **driver-only install is enough**, Adrenalin does not need to be installed and does not need to be running.

It covers GPU clocks, undervolt, power limit, fan curve, VRAM, per-monitor display options (VSR, FreeSync, GPU scaling, integer scaling, custom color), the 3D feature set (Anti-Lag, AFMF, FSR upgrade, RSR, Image Sharpening, Radeon Boost, Chill, FRTC, Enhanced Sync, V-Sync, anisotropic filtering, tessellation) and per-application profiles, because the driver keeps forgetting everything when the game closes or the PC restarts.

> **Status:** version 0.2.5, early release. Not every driver/GPU combination behaves the same; if an option does not respond, **Settings → Diagnostics** tells you exactly what the driver answered. See [Troubleshooting](#troubleshooting).

---

## Contents

- [What it can control](#what-it-can-control)
- [Requirements](#requirements)
- [Download and install](#download-and-install)
- [Building from source](#building-from-source)
- [How the app talks to the driver](#how-the-app-talks-to-the-driver)
- [Using the app](#using-the-app)
- [Per application profiles: three ways to use them](#per-application-profiles-three-ways-to-use-them)
- [Command line](#command-line)
- [Simple and Advanced mode](#simple-and-advanced-mode)
- [Quick presets](#quick-presets)
- [Safe apply (auto revert)](#safe-apply-auto-revert)
- [GPU support: Polaris to RDNA 4](#gpu-support-polaris-to-rdna-4)
- [FRTC and GPU scaling](#frtc-and-gpu-scaling)
- [Feature conflicts the driver enforces](#feature-conflicts-the-driver-enforces)
- [FSR upgrade (AMD FSR Upscaling)](#fsr-upgrade-amd-fsr-upscaling)
- [Overlay when a game profile starts](#overlay-when-a-game-profile-starts)
- [Stability watchdog](#stability-watchdog)
- [Interactive charts](#interactive-charts)
- [Feedback after applying](#feedback-after-applying)
- [Updates](#updates)
- [Window size](#window-size)
- [Troubleshooting](#troubleshooting)
- [Memory use](#memory-use)
- [Project layout](#project-layout)
- [License and disclaimer](#license-and-disclaimer)

---

## What it can control

| Area | Options |
| --- | --- |
| GPU tuning | min/max clock, max voltage (undervolt), power limit, VRAM clock and timing, fan curve, Zero RPM, voltage/frequency state list on older cards |
| Display (per monitor) | FreeSync, Virtual Super Resolution, GPU scaling, integer scaling, scaling mode, custom color (hue, saturation, brightness, contrast, temperature) |
| 3D features | Radeon Anti-Lag, AMD Fluid Motion Frames (AFMF), FSR upgrade, Radeon Super Resolution, Image Sharpening, Radeon Boost, Radeon Chill, FRTC (frame rate target), Enhanced Sync, V-Sync, anisotropic filtering, tessellation |
| Per app profiles | any combination of the options above, applied only while the game runs, restored when it closes |
| Extras | live sensors, colour profiles through the Windows gamma ramp, game overlay, tray icon, crash recovery, shader cache cleanup, diagnostics |

## Requirements

| Item | Requirement |
| --- | --- |
| OS | Windows 10 19041 (20H1) or newer, 64 bit |
| GPU | AMD Radeon, Polaris (RX 400/500) up to RDNA 4, with the AMD display driver installed |
| Runtime | None needed (the build is self-contained: .NET and Windows App Runtime are bundled) |
| Build tools | Visual Studio 2022 with the .NET desktop workload and the Windows App SDK |

AMD Software / Adrenalin is **not** required. Everything goes through the driver libraries that the display driver installs into `System32`.

## Download and install

1. Take the release zip from [Releases](https://github.com/Khotyz/RCU/releases).
2. Extract it anywhere (for example `C:\Tools\RedControlUtility`).
3. Run `RedControlUtility.exe`. No installer, no service, no background driver.

The app stores its settings in `%APPDATA%\RedControlUtility` and its logs in `%LOCALAPPDATA%\RedControlUtility\logs`. Delete those folders to reset everything.

## Building from source

1. Open `RedControlUtility.sln` in Visual Studio 2022.
2. Set the platform to **x64**.
3. Build **Release** (Debug works but uses the Visual Studio debug CRT and much more memory).

```
dotnet publish RedControlUtility -c Release -r win-x64 -p:Platform=x64
```

The `locales` and `native` folders are copied into the output automatically.

## How the app talks to the driver

In **Automatic** mode the app tries these backends in order:

1. **AMD's own DLL**: `C:\Windows\System32\amdadlx64.dll`, installed with the display driver (no Adrenalin needed). The app asks it for the *driver's own* ADLX version first, exactly like AMD's official `ADLXHelper`.
2. **Helper DLL**: `RedAdlxHelper.dll` next to the executable (optional fallback).
3. **Fallback without ADLX**: the app shows GPU details from the registry and reads sensors through ADL where supported. On a verified Polaris adapter, Overdrive N can also provide core and fan tuning. Controls that require ADLX remain unavailable.

You can force a backend in **Settings → Hardware**.

### About the helper DLL

A self-built ADLX helper must **not** be named `amdadlx64.dll`: it calls `LoadLibrary("amdadlx64.dll")` internally and would load itself instead of AMD's real library, so ADLX would never initialise. Keep it as `RedAdlxHelper.dll`, built in **Release x64** (a Debug build needs `VCRUNTIME140D.dll` / `ucrtbased.dll` from Visual Studio and fails on any other machine), and drop it in the `native` folder. It is optional, the app uses the driver's own DLL by default.

## Using the app

### Dashboard

GPU model, architecture, memory, driver version and live statistics (usage, clocks, temperatures, power, fan speed, VRAM). It also shows which per-application profile is active and offers the [quick presets](#quick-presets).

### Display

Per-monitor driver options: FreeSync, Virtual Super Resolution, GPU scaling, integer scaling, scaling mode and custom color, plus saved colour profiles applied through the Windows gamma ramp. The app remembers what you apply and puts it back at startup, after sleep and whenever the driver resets it.

VSR and integer scaling only work with **GPU scaling on**: when you turn VSR on, the app enables GPU scaling first and gives the driver time to rebuild the mode list, then applies VSR and reads the value back.

### Apps (per application profiles)

1. Click **Add app** (or drag a `.exe` / `.lnk` anywhere on the page) and pick the game.
2. Select it and set each option to **Driver default**, **On** or **Off**, or give it a value. Anything left on *Driver default* is not touched.
3. Options with a value (frame cap, Chill range, sharpening, Boost resolution, power limit) show a slider and the usual presets (30, 60, 120 fps…) once you turn them on.
4. **Quick setup** fills the whole profile in one click: Competitive, 60 FPS cap, Power saver, Image quality, Clear all. Only options your GPU supports are touched.

5. **Search games** scans your Steam, Epic Games and GOG libraries and lets you add several games at once, shown as a grid or a list. The program chosen for each game is the largest executable in its folder.
6. **Add running process** lists the programs open right now, so you can add a game that is already running. Settings are only applied when the game starts, so close it and open it again after adding.
7. The **Global profile** entry at the top of the list holds one set of options shared by every game that has *Use the global profile* turned on, plus the global FPS limit (FRTC), which affects all games. Games are added with their own settings and only follow the global profile when you tick that box.

The app watches processes every 0.4 s so options such as FRTC, AFMF and FSR are in place as early as possible.

### Tuning

Manual GPU tuning: min/max clock, voltage (undervolt), power limit, VRAM clock/timing, fan curve or direct RPM settings, and Zero RPM, depending on driver support. Cards that expose a voltage/frequency state list get an editable list with a chart.

* Moving a slider only updates the label; nothing goes to the driver until you press **Apply**, unless *live apply* is enabled.
* **Uniform offset** (-10, -25, -50 mV) lowers the voltage linearly from the factory value, like MSI Afterburner: -25 after -10 means -25, not -35. **Reset** returns to the baseline.
* **Keep min clock 100 MHz below max** makes the minimum follow the maximum, keeping the GPU out of idle clocks.
* **Reset to factory** restores the default values.
* Tuning is normally lost on reboot or driver restart, save a profile (or enable *Apply profile on startup*) to reapply it.

> **Warning:** manual tuning can crash or destabilise the system. Change values in small steps; the [safe apply](#safe-apply-auto-revert) dialog and the [watchdog](#stability-watchdog) are there for that.

### Profiles

Save the current driver state into a profile, apply it, export it to JSON or import one. The current profile is saved automatically.

### Settings

Language (Brazilian Portuguese, English, Spanish and Simplified Chinese), apply mode, advanced mode, confirmation before tuning, live undervolt, auto revert, apply on startup, per-app profiles, overlay on/off/corner/duration, start with Windows, start minimized, minimize to tray (minimize and close send the window to the tray), backend, GPU selection, sensor interval, shader cache cleanup, update check and diagnostics.

**Apply mode:** *Instant* sends every change to the driver as you make it (with a short debounce); *Manual* waits for the Apply button. Fine tuning always uses Apply.

## Per application profiles: three ways to use them

ADLX has no public per-game API, so RCU applies the profile itself while the game runs.

### 1. Launcher mode (no background process)

```
RedControlUtility.exe --launch "C:\Games\Game\game.exe"
```

The app applies the profile for that game, starts it, waits for it to exit, restores your previous values and quits. Extra arguments after the path are forwarded to the game. Nothing stays in memory while you are not playing.

### 2. Watcher mode

Keep **Enable per app profiles** on. The app checks running processes and applies the matching profile when a game starts, restoring your values when it closes. RCU has to be running, but it can sit in the tray, enable **Start with Windows** + **Start minimized** to make it automatic.

If a game only honours an option that was set *before* it started (common with FRTC, AFMF and FSR), start it through launcher mode so the profile is in place before the game opens. When a game closes, your previous values are always restored.

### 3. Apply once and exit

Display, colour and 3D values stay in the driver after the app closes. To apply a saved profile without opening a window:

```
RedControlUtility.exe --apply "profile.json"
```

That is handy in a scheduled task at logon to reapply tuning after a reboot.

## Command line

| Argument | What it does |
| --- | --- |
| `--launch "<exe>" [args…]` | applies the profile of that app, runs it, restores on exit, then quits |
| `--apply "<profile.json>"` | applies the profile and quits (no window) |
| `--startup` | used by the "start with Windows" entry |
| `--minimized` | starts hidden in the tray |

## Simple and Advanced mode

RCU starts in **Simple mode**: Dashboard, Apps, Tuning and Settings. Turn on **Advanced mode** to also show Display and Profiles, image quality options, VRAM tuning, the fan curve and the voltage state editor. Fields that belong to Advanced mode stay in place and are dimmed instead of disappearing, so the window never jumps around.

## Quick presets

One-click presets on the Dashboard: **Safe undervolt (-25 mV)**, **Quiet**, **Balanced (factory)** and **Performance**. Values come from the ranges your driver reports, so they stay inside what the GPU accepts. Balanced simply resets tuning to factory values.

## Safe apply (auto revert)

When you apply tuning or a preset, a dialog gives you 15 seconds to confirm. If you do not confirm, the previous values are restored automatically. It can be turned off in Settings.

If the PC freezes or reboots before you confirm, the app notices on the next start, does **not** reapply anything automatically and tells you what happened.

## GPU support: Polaris to RDNA 4

The GPU model and architecture are detected from the PCI device ID and name, covering Polaris, Vega, RDNA 1, RDNA 2, RDNA 3 and RDNA 4. What you can control depends on what your driver reports; the app disables what it cannot confirm (and where the driver lies about support, it tries anyway and reports the real answer).

* **AMD RDNA 1 to AMD RDNA 4:** tuning through ADLX for the features reported by the driver.
* **AMD Polaris (Radeon RX 400/500):** frequency and voltage per performance state for undervolting and overclocking. The app prefers the [ADLX manual graphics and fan tuning interfaces](https://gpuopen.com/manuals/adlx/programming-with-adlx/adlx-samples/cplus-samples/gpu-tuning/manualgraphicstuning/). If ADLX does not expose core tuning or fan control, the app probes [AMD Display Library Overdrive N](https://gpuopen-librariesandsdks.github.io/adl/group__OVERDRIVENAPI.html) for the missing controls. An unambiguous PCI adapter match is required to write through ADL. Rather than simulating a fan curve, the fallback exposes minimum and target fan speed in RPM where supported. **Reset to factory** restores the available tuning controls, including automatic fan control on Overdrive N.
* **Other pre-RDNA GPUs:** existing ADLX support where available; the ADL sensor fallback remains read-only for these GPUs.

## FRTC and GPU scaling

FRTC goes through ADLX (`IADLX3DFrameRateControl`): the feature is enabled first and only then the limit is written, because the driver ignores the limit while the feature is off. The value is read back; if it did not stick, the app disables the conflicting feature and tries once more.

The older ADL calls (`ADL2_FPS_Settings_Set` with the full 40-byte `ADLFPSSettingsInput`, and `ADL2_DFP_GPUScalingEnable_Set` through `atiadlxx.dll`) are kept as a fallback. FRTC is written through **one** path only: writing the same cap through ADLX and ADL at the same time capped the game twice and made it drop to about 5 fps instead of the requested 30. GPU scaling has no ADLX equivalent in some drivers, so ADL is used when ADLX reports it unavailable, several drivers only report VSR and integer scaling as available after GPU scaling is on.

## Feature conflicts the driver enforces

The driver returns `OK` and still ignores some combinations, so the app resolves them before writing and lists every decision in the log:

- AFMF does not run together with Chill, FRTC, RIS, RSR or Boost, AFMF wins, the others are reported as not supported for that apply.
- Chill and FRTC are two ways of capping the frame rate, Chill wins.
- Enhanced Sync needs a FreeSync-capable display; without it the app enables FreeSync first, otherwise the setting is accepted and does nothing.
- VSR and integer scaling need GPU scaling on.

Every value is read back from the driver. A setting the driver silently ignored is reported as a failure (with what the driver answered) instead of a green "Ok".

## FSR upgrade (AMD FSR Upscaling)

FSR upgrade needs RDNA 2 or newer (RX 6000 and up); on older GPUs the option is disabled with a note. It is applied straight in the driver, without AMD Software:

1. If the driver exposes the FSR service through ADLX, that is used.
2. Otherwise the app uses the same switch AMD Software uses: `FsrOverride` (`1` on, `0` off) in the driver's `UMD` key, written through `ADL2_Adapter_RegValueString_Set`. Support is checked with `ADL2_DriverMLSRSupport_Get` and the presence of `amdxcffx64.dll` in the driver folder.

The driver reads that value when a game starts, so the app sets it as the game opens and restores it afterwards. If the game was already running, restart it. The game must have FSR 3.1+ enabled in its own settings, and the driver only upgrades the titles it supports. Vulkan games are not covered by the driver upgrade.

## Overlay when a game profile starts

When a game with a profile starts, a small overlay appears in the corner of its screen listing the technologies the profile turned on or off (green dot = on). It is a click-through window drawn with plain Win32, never takes focus and disappears by itself (default 3 s). Configure it in Settings: on/off, corner, duration, preview. Games in exclusive fullscreen may hide it, because Windows does not let other windows draw above them.

## Stability watchdog

If the video driver stops responding and recovers (Windows event *Display 4101* or a critical AMD driver event) while an undervolt or overclock is active, the app returns the GPU to factory values, clears the saved tuning and shows a notice. It listens to the Windows event log through the native API, so it costs almost nothing. It can be turned off in Settings.

## Interactive charts

* **Manual editing:** click a point and drag it. On cards with a voltage/frequency state list each point moves freely; on cards that take min clock, max clock and one max voltage (such as RDNA 2) the top point of the curve is your max clock and max voltage.
* **Linear change:** select several points (click, drag a box, Ctrl to add, Ctrl+A for all, Esc to clear), then drag, use the mouse wheel or the arrow keys. Clock drops in proportion to the voltage, like MSI Afterburner. The dashed curve is a ghost of the last applied values.
* The fan curve keeps points coherent: setting a point to 100% raises every later point to 100%.

## Feedback after applying

After applying, the app reads everything back from the driver. The message lists each option with the code the driver returned and flags anything the driver accepted but did not actually change. Options your GPU or driver does not expose stay disabled in the editor.

## Updates

RCU updates itself from this repository's GitHub releases, no store, no installer service.

* **Settings → Updates** shows the version you are running (currently **0.2.5**) and has a **Check now** button.
* **Check for updates automatically** (on by default) asks `api.github.com/repos/Khotyz/RCU/releases/latest` once at startup. Nothing is downloaded without your confirmation.
* When a newer release exists, an information bar appears at the bottom of the window with a **Download** button. RCU downloads the release asset (`.exe`, `.msi` or `.zip`) to `%TEMP%\RedControlUtility\update-<tag>` and opens it; if the release has no asset, it opens the release page in your browser.
* The comparison uses the tag (`v0.3.0` > `0.2.5`). Turn the option off in Settings if you prefer to update manually.

## Window size

The window opens at its final size (92% of the work area, at least 1280×820 effective pixels) and stays there: a size change that does not come from you is undone. Pages never add or remove controls when you toggle an option, advanced fields are dimmed in place, so nothing moves or animates while you change settings.

## Troubleshooting

**An option is greyed out.** The driver did not report it as supported for your GPU. Open **Settings → Diagnostics**, press **Copy** and read what the driver answered for that feature.

**A setting says "Ok" but nothing changes in the game.** Check *Feature conflicts* above (AFMF and Chill/FRTC/RIS/RSR/Boost are mutually exclusive), and check whether the game reads the setting only at launch, in that case use launcher mode.

**FRTC does not cap the way I expect.** Use a cap a little below your refresh rate (30 for a 60 Hz panel is fine, 60 for 60 Hz is not, it fights V-Sync). Make sure Chill is off: the two cap the frame rate in different ways.

**VSR does not appear in Windows.** VSR needs GPU scaling on; the app enables it automatically, but Windows may need the display to be re-detected (Win+Ctrl+Shift+B or disable/enable the monitor) before the new resolutions show up.

**The app says ADLX is unavailable.** Usually `amdadlx64.dll` is missing, which means the AMD driver is not properly installed. **Settings → Diagnostics** shows whether the DLL exists, which backend was used and the initialisation log.

**Where are the logs?** `%LOCALAPPDATA%\RedControlUtility\logs`, or press **Open logs** in Settings. When reporting a problem, attach the diagnostics text from **Settings → Diagnostics**.

**Windows blocks drag and drop.** Drag and drop from Explorer into an elevated (administrator) window is blocked by Windows; use the **Add app** button instead.

## Memory use

* Sensors only run while the Dashboard is open.
* Minimised, the sensors stop, the current page is released and the app trims its memory.
* A Debug build with the debugger attached uses 2-3× more memory than Release. Measure a Release build started outside Visual Studio.
* `--apply` and `--launch` never create a window.

## Project layout

```
RedControlUtility/
  Models/            settings and profile models
  Services/          hardware facade
    Adlx/            ADLX interop (vtable calls), sessions for 3D, display and tuning
    Adl/             ADL fallbacks (Overdrive N, FRTC, GPU scaling, FSR override, sensors)
    *.cs             profiles, sensors, overlay, tray, updates, stability watchdog
  Views/             WinUI pages (Dashboard, Display, Apps, Tuning, Profiles, Settings)
  Controls/          interactive charts
  locales/           pt-BR.json, en-US.json, es-ES.json, zh-CN.json
  native/            optional RedAdlxHelper.dll
```

## License and disclaimer

AGPL-3.0, see [LICENSE.txt](LICENSE.txt).

This is an unofficial community project, **not affiliated with, endorsed by or supported by AMD**. "Radeon", "Adrenalin", "FreeSync", "Anti-Lag", "Fluid Motion Frames", "Radeon Boost", "Radeon Chill" and "Radeon Super Resolution" are trademarks of Advanced Micro Devices, Inc. The app calls AMD's own public driver APIs, exactly like open-source tools such as GPU-Z; nothing is reverse engineered, and no AMD code is redistributed.

Manual overclocking, undervolting and firmware-level changes are always at your own risk. The authors are not responsible for damage to hardware, data loss or instability.

## Installer and releases

The installer is an Inno Setup script in `installer/RCU.iss`. It installs to `C:\RCU` by default (the folder can be changed in the wizard), adds Start menu and optional desktop shortcuts, an optional "start with Windows" entry, and a normal uninstaller. Settings and logs stay in your user profile, so uninstalling does not touch them.

To build locally with the full Microsoft Visual Studio:

1. Open `RedControlUtility.sln` in Visual Studio with the WinUI project tooling, Windows SDK and .NET 8 SDK installed.
2. Choose **Release | x64**, right-click the **RedControlUtility** project, choose **Publish** and run the included **FolderProfile**. Publish to the profile's default folder before compiling `installer/RCU.iss` in **Inno Setup 6.5 or later**.
3. The installer reads `Product` and `Version` from the published executable's metadata, generated from `RedControlUtility.csproj`. No duplicate version or product name needs to be edited in the `.iss`. For this release, the output is `dist/RCU-Setup-0.2.5.exe`.

The package is self-contained and uses solid LZMA2 compression. It includes only the English, Brazilian Portuguese, Spanish and Simplified Chinese UI resources. The Simplified Chinese setup messages are from [Inno Setup's translation collection](https://github.com/jrsoftware/issrc/blob/is-6_7_4/Files/Languages/Unofficial/ChineseSimplified.isl).

To publish a GitHub release, set `Version` in the project file, then push a matching tag such as `v0.2.5`. The release workflow publishes the app, builds the installer and a portable ZIP, and attaches both to the release. The updater compares the latest release tag with the installed version.

---

> Built with help from AI.
