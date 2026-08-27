# Raspberry Pi native packaging script

Date: 2026-07-02

## Scope

- Add a packaging script for the Raspberry Pi development environment at `192.168.3.179`.
- Preserve the old Linux packaging script's external parameter contract and package layout:
  - Usage shape: `<env> <packet> <language> [archive_name] [--watermark <on|off>]`
  - Supported watermark aliases: `--with-watermark`, `--without-watermark`
  - Build tree: `build/build_<env>_<packet>_<language>`
  - Package output: `build/linux_<arch>/<packet>/<language>/<archive_name>.tar.gz`
  - Archive root folder: `<archive_name>/`
  - Staged directories: `bin`, `lib`, `plugin`, `configuration`, `updater`, `tile_cache` when present
  - Pre-created `images/`
  - `QuickWaveFormData/*.wav`, `releasenote.txt`, and `version.json` copied when present
- Keep this script focused on the Raspberry Pi/native Linux environment; do not change `scripts/build.sh` behavior.

## Evidence

- `scripts/build.sh` currently implements Linux cross/native package staging and tar creation.
- `.github/KnowledgeBase/build_script_packaging_and_watermark_guide.md` documents the expected `scripts/build.sh` parameters and output layout.
- `.github/KnowledgeBase/minibar_wayland_layer_shell_qt_integration.md` documents the Raspberry Pi QtWayland/layer-shell dependencies and the need to avoid Qt runtime/plugin mixing.
- Previous remote packaging on `192.168.3.250` succeeded only after explicitly controlling Qt and `pkg-config` paths, so the new script should make the intended native Pi environment explicit.

## Assumptions

- The script will live under `scripts/` and be run from the Raspberry Pi checkout, typically `~/Desktop/SGSProject/scripts`.
- The Raspberry Pi build should be native aarch64, not cross-compiled from x86_64.
- The script should accept `aarch64` for compatibility with old commands, but treat it as a native Raspberry Pi build on an aarch64 host.
- Credentials and remote login details are session context only and should not be written into the script.

## Success Criteria

- The new script parses the same user-facing arguments as `scripts/build.sh`.
- It preserves the package directory structure and tar layout produced by `scripts/build.sh`.
- It avoids hard-coded `/opt/Qt/5.15.18/gcc_aarch64` cross-Qt assumptions and uses the active/native Qt discovered from `qmake` or an explicit `QT_PREFIX`.
- It exports a native `pkg-config` environment suitable for Raspberry Pi aarch64 libraries.
- Static shell parse checks pass.
- Remote probe verifies the intended paths/tool assumptions on `192.168.3.179`; full packaging run is optional unless explicitly requested.

## Verification Plan

1. Probe `192.168.3.179` for OS, architecture, project path, Qt, CMake, `qtwaylandscanner`, and package output expectations.
2. Add the new script after reading existing `scripts/build.sh`.
3. Run local static shell checks where possible.
4. Optionally copy/probe the script remotely without running a long package build unless requested.

## Remote Packaging Run

Date: 2026-07-02

- Host: `192.168.3.179`
- Checkout: `~/Desktop/SGSProject`
- Branch observed before build: `minibar`
- Script: `~/Desktop/SGSProject/scripts/build_pi.sh`
- Command: `bash ./build_pi.sh aarch64 standard cn SGStudio --watermark on`
- Build log: `/home/htra/Desktop/SGSProject/scripts/build_pi_aarch64_standard_cn_SGStudio_20260702_192527.log`
- Result: `BUILD_EXIT=0`
- Archive: `/home/htra/Desktop/SGSProject/build/linux_aarch64/standard/cn/SGStudio.tar.gz`
- Archive size: `106M`

Spot-checks:

- Archive root/layout uses `SGStudio/`.
- LayerShellQt runtime files are present:
  - `SGStudio/lib/libLayerShellQtInterface.so`
  - `SGStudio/lib/libLayerShellQtInterface.so.5`
  - `SGStudio/lib/libLayerShellQtInterface.so.5.27.12`
- Wayland shell integration plugin is present:
  - `SGStudio/bin/wayland-shell-integration/liblayer-shell.so`
- Qt Wayland platform plugins are present:
  - `SGStudio/bin/platforms/libqwayland-egl.so`
  - `SGStudio/bin/platforms/libqwayland-generic.so`
  - `SGStudio/bin/platforms/libqwayland-xcomposite-egl.so`
  - `SGStudio/bin/platforms/libqwayland-xcomposite-glx.so`
- `file` reports the checked LayerShellQt and Qt Wayland binaries as `ELF 64-bit LSB shared object, ARM aarch64`.

Notes:

- The build emitted non-fatal Qt deprecation/GCC warnings, but completed packaging and tar creation successfully.
