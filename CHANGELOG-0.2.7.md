# 0.2.7

Changes since 0.2.6.

## New

- History charts on the Dashboard: usage, hotspot temperature, power and fan speed for the last two minutes. They only collect data while the Dashboard is open, so they cost nothing while the app is in the tray.
- Run a program when a game starts and another when it closes, set per game. It runs hidden, without a shell. Commands are removed from imported profiles.
- More hardware info on the Dashboard: PCIe link (current and maximum) and subsystem ID.
- Fan duty tile, shown only when the driver reports it.

## Improved

- Tuning now applies only what you changed instead of resending every value to the driver. If nothing changed, it says so and skips the confirmation.
- The profile watcher checks running games once per second with a single lightweight process snapshot, instead of every 400 ms once per game.
- Sensor values are validated before being shown (usage, fan speed and duty).
- GPU and VRAM clock readouts hold the last value for up to 4 samples when the driver reports 0, so the tiles and charts stop flickering.
- The scrollbar no longer sits on top of the cards, and button rows wrap instead of being cut off at the edge.
- The navigation pane stays open beside the content.
- Smaller install: unused AI, machine learning, widget, push notification and debugging components are removed from the published app, and the installer files list is updated to match.
- Fatal errors on background threads and failed background tasks are now written to the log.
- README opens with a short summary and tells users to install through the setup.

## Fixed

- The release workflow now also triggers on tags without a "v" (such as 0.2.7).
- Release tag examples in the README use the format without "v".

## Known notes

- GPU clock can still show low values or 0 when the core idles. This is how Radeon power states work, not a locked clock like NVIDIA's high performance mode. Setting minimum and maximum to the same value is the closest option.
- Fan hysteresis is not included, because the driver runs the fan curve and ADLX has no option for it.
- GPU usage and fan reporting were checked in code but not on hardware. The log records the first sensor values so they can be compared with other tools.
- Polaris tuning has not been verified on real hardware yet.
