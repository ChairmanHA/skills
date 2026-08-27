# 2026-05-28 HLC PGA Probe Results

## Scope
- Host: PGA IQ at `192.168.3.239:22`
- Login: `htra`
- Probe method: read-only `hlc` queries over SSH using PuTTY `plink`
- Goal: identify fields useful for current PGA status-bar UI and record the actual output format on the device

## Actual `hlc --help`
```text
HAROGIC Local Config

hlc [OPTIONS]

OPTIONS:
  -h,     --help              Print this help message and exit
  -v,     --version           Query HLC Version
  -b,     --battery           Display battery information
          --bright            Query Screen Brightness (0 - 1)
          --set-bright FLOAT  Set Screen Brightness (0 - 1)
          --trigger-feedback  Trigger Haptic feedback
          --feedback-strength Query Haptic feedback strength
          --feedback-duration Query Haptic feedback duration
          --set-feedback-strength FLOAT
                              Set Haptic feedback strength
          --set-feedback-duration FLOAT
                              Set Haptic feedback duration
          --fan-rpm           Query fan rpm
          --set-fan-rpm FLOAT Set fan rpm
  -i,     --info              Query Device info
          --get-cpu-temp      Check CPU temperature
          --set-device-power TEXT
                              Set the device to power on or off
          --set-fan-mode INT  Set fan mode (0: Quiet, 1: standard)
          --fan-temp-apply FLOAT
                              Set the fan speed level according to the temperature.
          --get-fan-level     Show current wind speed rating
          --device-kernel-info TEXT
                              Show kernel info of the device, You can obtain a single version
                              using `hlc --device-kernel-info <string>`.
          --device-kernel-info-all
                              Show all kernel info of the device.
```

## Probed Outputs

### `hlc -v`
```text
0.0.14a
```

### `hlc -b`
```text
AC Line Statue: Unknown
Battery Charging Status: Unknown
Battery percent: Unknown
Battery lifetime: Unknown
Battery Life CapRep: Unknown
Battery FullCapRep: Unknown
Battery Voltage: Unknown
Battery Current: Unknown
```

### `hlc -i`
```text
Model: PX_P5_PGA
```

### `hlc --get-cpu-temp`
```text
49.05
```

### `hlc --bright`
```text
1
```

### `hlc --fan-rpm`
```text
60
```

### `hlc --feedback-strength`
```text
0.2
```

### `hlc --feedback-duration`
```text
120
```

### `hlc --get-fan-level`
```text
Mode : 1
Level : 6
```

### `hlc --device-kernel-info-all`
```text
AGUVersion: 0
BusBandwidth_MBps: 625
BusVersion: 131073
EIOVersion: 0
Error: 0
FFWVersion: 131084
HardwareVersion: 0
MFWVersion: 131091
Model: 132
PGA_PowerSourceType: 1
PMUVersion: 0
PowerSourceType: 0
UID_H32: 388051011
UID_L64: 6315474085026870325
```

## Current UI-Relevant Fields

### Must use now
- System temperature: `hlc --get-cpu-temp`
- Battery block: `hlc -b`
- Device model text: `hlc -i`
- Device kernel/device firmware info if later needed in compact/full variants: `hlc --device-kernel-info-all`

### Useful but not in current status-bar UI
- HLC version: `hlc -v`
- Brightness value: `hlc --bright`
- Fan state: `hlc --fan-rpm`, `hlc --get-fan-level`
- Haptic defaults: `hlc --feedback-strength`, `hlc --feedback-duration`

## Important Observations
- Battery information is currently not provisioned on this device. All fields return `Unknown` and this is normal for now.
- Battery parser must treat `Unknown` as empty/uninitialized, not as `0%`.
- The real output uses `Battery percent` and `Battery lifetime` with lowercase words, and `AC Line Statue` contains a typo. Parsing should be case-insensitive and prefix-based.
- The installed `hlc` binary exposes more options than the older plan document, especially `--get-cpu-temp`, `--get-fan-level`, and `--device-kernel-info*`.
- `hlc --fan-rpm` currently returns `60`; this looks more like a coarse level or percentage than a literal RPM value, so UI should avoid labeling it as exact RPM without confirmation.

## Not Executed
- Skipped all mutating commands during probe:
  - `--set-bright`
  - `--trigger-feedback`
  - `--set-feedback-strength`
  - `--set-feedback-duration`
  - `--set-fan-rpm`
  - `--set-device-power`
  - `--set-fan-mode`
  - `--fan-temp-apply`

## Follow-up
- PGA runtime code should use `hlc --get-cpu-temp` as the system-temperature source.
- PGA battery UI should render empty state when `hlc -b` reports `Unknown` instead of showing `0%`.