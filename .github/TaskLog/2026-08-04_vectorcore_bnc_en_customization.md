# VectorCore BNC English customization

## Scope

- Add `BNC_en` as a supported packaged configuration profile.
- Brand the main application and package as `VectorCore` on Windows and Linux/aarch64.
- Use the user-provided BNC title-bar/runtime PNGs and multi-resolution Windows
  application icon.
- Change only the three requested dark-theme colors; leave `theme_light.css` unchanged.
- Update the Windows, cross-Linux, and Raspberry Pi native packaging entries so the profile can be built and launched.
- Verification level: `static` only. Per user direction, do not configure, compile, package, or run.

## Observations

- `configuration_files/BNC_en/` initially contained the modified English language workbook, settings, device map, dark/light theme templates, and two PNG assets. Per the user's follow-up, the PNG assets belong under `src/app/res_bnc/` with the other application resource profiles.
- `Language.xlsx` exposes only the `English` language column/token.
- `FancyTabWidget` modulation tiles are painted by `ListviewDelegate`; their selected background comes from `ThemeManager::listItemSelectedColor()`, not the QSS item selector.
- Panel title backgrounds come from `Core--Internal--TabWidget/FancyTabWidget QLabel#titleLabel` in the selected theme template.
- `QStatusBar` and `Core--DeviceInfoWidget` share one background rule in the selected theme template.
- `src/app/CMakeLists.txt` selects the executable output name and application QRC/RC by `packet`/`language`.
- The packaged Raspberry Pi launcher currently assumes the executable/process name is `SGStudio`.
- `package-info/CMakeLists.txt` requires a release-note file for every non-neutral packet/language pair.

## Assumptions and evidence

- The requested packet token is exactly `BNC`, because CMake constructs the case-sensitive Linux path `${packet}_${language}` and the user-created directory is named `BNC_en`.
- The screenshot color annotations are authoritative: list selection `RGB(0, 0, 70)` (`#000046`), panel title `RGB(4, 0, 153)` (`#040099`), and device/status bar `RGB(0, 42, 236)` (`#002AEC`).
- Branding assets apply to both themes because the application must remain VectorCore regardless of theme. The three palette changes apply only to dark theme, as requested.
- The initial temporary ICO used only the supplied 128x128 image. The later
  multi-resolution source set supersedes that temporary shape.

## Success criteria

1. `packet=BNC` and `language=en` select `configuration_files/BNC_en` and produce a main executable named `VectorCore`.
2. The Qt application name/display name, Windows executable icon, runtime/aarch64 application icon, and title-bar logo use the VectorCore/BNC identity.
3. The BNC dark modulation selected tile, panel title, and status/device-info backgrounds exactly match the three annotated RGB values.
4. `configuration_files/BNC_en/theme_light.css` has no customization diff introduced by this task.
5. `scripts/build.bat`, `scripts/build.sh`, and `scripts/build_pi.sh` map `BNC` to the BNC branding profile and default archive name `VectorCore`; the Raspberry Pi launcher can find and manage `bin/VectorCore`.
6. The Windows batch matrix includes `BNC en`.
7. Static checks confirm all new resource/config paths exist and the ICO has the
   nine required PNG-compressed, 32-bit entries with valid byte ranges.

## Planned changes

1. Add the BNC application resource set and generate its Windows ICO.
2. Add BNC packet/application/branding selection to CMake and application identity initialization.
3. Add the BNC-only dark delegate color override and edit the two requested dark QSS rules.
4. Add the BNC release note and updater package identity needed for configure/package completeness.
5. Update build/package scripts, the shared Linux launcher, and durable build/profile documentation.
6. Run static diffs, resource-reference checks, script syntax/dry-run checks where they do not configure or build, and binary ICO parsing.

## Adjustment: VectorCore updater UI-only hiding

### Scope

- Rename the BNC custom edition to `VectorCore`; preserve the
  user-edited `releasenote_BNC_en.txt` content.
- For BNC only, hide the existing `Latest Online` radio option in the update
  dialog.
- Do not alter `switchOnline()`, download slots, refresh logic, packet mappings,
  or execution behavior as part of this product UI restriction.
- Keep other branding profiles' updater UI and behavior unchanged.
- Retain only updater changes required for VectorCore naming plus the single
  BNC-specific UI visibility change.

### Current updater observation

- The active updater does not perform an online check during startup.
  `Plugin::extensionsInitialized()` only adopts/cleans local handoff cache data.
- The sole active `PacketSpec::loadUrl()` call is inside the manually triggered
  `UpdateDialog::startOnlineDownload()` slot. `NewVersionNotifiction` is present
  in the source tree but is not instantiated by the active plugin flow.
- Therefore this adjustment does not change startup or updater behavior. It only
  removes the BNC online choice from the visible UI.

### Success criteria

1. BNC builds produce and package `VectorCore` on Windows and Linux/aarch64.
2. The BNC updater dialog hides only the `Latest Online` radio option.
3. `switchOnline()`, online download/refresh code, and the remaining updater
   implementation are unchanged from the common updater behavior.
4. The firmware and local-file selections still resolve to `m_nativelFile` and
   `m_localFile`, respectively, with their existing browse/execute paths intact.
5. Non-BNC builds retain the existing online, firmware, and local-file modes.
6. Verification remains static only; do not configure, compile, package, or run.

## Implementation result

- Added the `BNC` packet and enforced `language=en` at the root CMake entry.
- Added the `bnc` branding profile, `VectorCore` executable/application identity,
  BNC QRC/RC selection, updater package identity, update-process discovery, and
  visible runtime name substitutions.
- Added `src/app/res_bnc/` with the BNC title-bar/runtime PNGs, local QRC
  references, and the multi-resolution Windows ICO.
- Applied the requested dark colors:
  - modulation selected tile: `#000046` through the BNC ThemeManager token;
  - panel title: `#040099` in `BNC_en/theme.css`;
  - status/device-info background: `#002AEC` in `BNC_en/theme.css`.
- Left `BNC_en/theme_light.css` unchanged and identical to `standard_en`.
- Updated the Windows, cross-Linux, native Raspberry Pi, Windows matrix, and
  shared Raspberry Pi launcher paths for `VectorCore`.
- Added `package-info/releasenote_BNC_en.txt` so BNC configure/package selection
  has the required release-note source.
- For BNC only, hid the existing `Latest Online` radio option. No updater slots,
  refresh logic, packet mappings, update-mode labels, or execution paths are
  changed.
- Added durable profile documentation and indexed it in the KnowledgeBase.

## Static verification result

- No configure, compilation, packaging, or runtime launch was performed.
- `scripts/build_all.bat --dry-run --watermark off` listed six targets and emitted
  the expected `BNC en` call without executing `build.bat`.
- Git Bash `bash -n` passed for `build.sh`, `build_pi.sh`, and
  `launch_sgstudio_pi.sh`.
- The BNC QRC and device-info XML parse successfully; every referenced PNG path
  exists.
- `Language.xlsx` is a valid XLSX and its leading workbook tokens confirm the
  single `English` / `en` language column.
- `BNC_en/theme_light.css` is byte-for-byte identical to
  `standard_en/theme_light.css`.
- ICO parsing confirmed reserved/type/count `0/1/9`, ordered entries at 16, 20,
  24, 32, 40, 48, 64, 128, and 256 px, planes/bit depth `1/32`, PNG signatures,
  matching IHDR dimensions, exact final byte boundary, and successful
  `System.Drawing.Icon` loading.
- `git diff --check` passes for modified tracked files outside the pre-existing
  staged `BNC_en` additions. A repository-wide check still reports inherited
  trailing whitespace inside the user-added configuration copies; this task did
  not reformat those files or alter the light theme.
- Static source tracing confirms startup only adopts/cleans local handoff cache;
  the sole active `loadUrl()` call remains in the manual download slot.
- Static updater diff review confirms the BNC behavior adjustment is limited to
  hiding `radioOnline`; common slots, refresh logic, packet mappings, update-mode
  labels, and execution paths have no BNC-specific branches.
- A repository search excluding build/user artifacts finds no stale previous
  branding-name references.

## Adjustment: multi-resolution BNC Windows icon

### Source observation and plan

- Follow `.github/skills/app-icon-ico-generation-workflow/SKILL.md`.
- `C:\Users\jsl\Desktop\ICON BNC` contains exact-size PNG sources for 16, 20,
  24, 32, 40, 48, 64, 128, and 256 px. Each source is square, 8-bit RGBA
  (`colorType=6`), so no generated scaling fallback is needed.
- Before replacement, `src/app/res_bnc/app.ico` contained one 128x128, 32-bit,
  PNG-compressed entry. The user's explicit multi-resolution request supersedes
  that temporary one-entry shape.
- Replace only `src/app/res_bnc/app.ico`; retain all source PNGs and leave
  `app.rc` / `app.qrc` unchanged.
- Verification level remains static; do not configure or compile.

### Success criteria

1. The ICO header is `reserved=0`, `type=1`, `count=9`.
2. Entry order is exactly 16, 20, 24, 32, 40, 48, 64, 128, and 256 px.
3. Every entry is `planes=1`, `bitCount=32`, PNG-compressed, and its embedded
   IHDR dimensions match the ICO directory entry.
4. Each embedded payload is byte-for-byte identical to its corresponding source
   PNG; the last payload ends exactly at the ICO file length.
5. `System.Drawing.Icon` can open the generated file.

### Implementation and verification result

- Replaced only `src/app/res_bnc/app.ico`; `app.rc`, `app.qrc`, and all PNG files
  were left unchanged.
- The final ICO contains the nine entries in the required order, is 15,597 bytes,
  and has SHA-256
  `E30A1E29F5A4C1293F360E88ECA02E8F5CC4D9284D1D681A347B2FFB7137D114`.
- Every embedded image is PNG-compressed, reports `planes=1` / `bitCount=32`,
  has matching IHDR dimensions, and is byte-for-byte identical to its exact-size
  source PNG. The final payload ends at the ICO file boundary.
- `System.Drawing.Icon` opened the result successfully. No application configure,
  compilation, packaging, or runtime launch was performed.
