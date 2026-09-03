# VectorCore BNC English branding profile

## Profile identity

The VectorCore custom edition uses this exact CMake/profile tuple:

```text
packet=BNC
language=en
SGS_BRANDING_PROFILE=bnc
```

`BNC` is case-sensitive on Linux because `configuration_files/CMakeLists.txt`
resolves the configuration source as `${packet}_${language}`. The only supported
language for this packet is English, backed by `configuration_files/BNC_en/`.

The main CMake target remains `SGStudio` for internal target dependency
compatibility, while its output executable, Qt application name/display name,
updater package name, and default archive root are `VectorCore`.
The internal MiniBar target remains `SGStudioMiniBar`, while BNC builds output
and launch `VectorCoreMiniBar` (`VectorCoreMiniBar.exe` on Windows).

## Updater boundary

VectorCore keeps the updater as a manual menu action; the active updater has no
startup online check. For `packet=BNC`, the existing `Latest Online` radio
option is hidden in the update dialog, and any shared "open online update" entry
falls back to selecting `Local Default` so the hidden online source cannot expose
the `Download` button. The download slots, refresh logic, packet mappings, and
execution paths remain the shared implementation. The existing `Local Default`
and `Local File` choices remain unchanged. Other packet profiles retain the
visible three-mode updater.

## Resources and theme boundary

- `src/app/res_bnc/` owns the title-bar PNG, runtime application PNG, QRC, RC,
  and ICO together, matching the other application resource profiles. The same
  retained PNG source asset is embedded for Windows and Linux/aarch64 builds.
- `src/app/res_bnc/app.ico` is the Windows resource icon. It contains ordered
  PNG-compressed 32-bit entries at 16, 20, 24, 32, 40, 48, 64, 128, and 256 px.
  Each entry comes from the corresponding exact-size BNC PNG source supplied for
  ICO generation; no scaling fallback was needed. The retained
  `BNC_128-128.png` remains the QRC runtime/aarch64 application icon.
- The BNC title-bar logo is `BNC-170-30.png`.
- The custom palette changes are dark-theme-only. `theme_light.css` remains the
  inherited standard English light theme.
- The modulation tile selected color is not controlled by QSS. The custom
  `FancyTabWidget` delegate reads `ThemeManager` colors, so the BNC override is
  selected through `SGS_BRANDING_PROFILE=bnc`.

## Build and package entries

Windows:

```bat
scripts\build.bat BNC en --rebuild --watermark off
```

Cross-compiled Linux/aarch64:

```bash
scripts/build.sh aarch64 BNC en --watermark off
```

Native Raspberry Pi:

```bash
scripts/build_pi.sh aarch64 BNC en --watermark off
```

When no archive name is supplied, all three entries default to `VectorCore`.
The Windows `build_all.bat` matrix intentionally excludes `BNC en`; this special
edition must be built explicitly only when requested. Raspberry Pi BNC packages
contain `launch_pi.sh`, rendered during packaging to launch only
`bin/VectorCore` relative to the package root; no SGStudio executable or install
path fallback remains in that launcher.

## Change checklist

When updating this custom edition, keep the following aligned:

1. `configuration_files/BNC_en/` configuration, language workbook, and themes.
2. `src/app/res_bnc/` title-bar/runtime PNGs plus QRC/RC/ICO resources.
3. The `BNC` branches in root/app/updater CMake files, including the
   `VectorCoreMiniBar` output identity.
4. Windows, cross-Linux, and native-Pi build script branding mappings.
5. `package-info/releasenote_BNC_en.txt`, updater naming, and the BNC-only
   online-option visibility/default-source rule.
