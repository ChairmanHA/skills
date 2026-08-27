# QuickWaveFormData Recursive Packaging And Linux Launcher

## Scope

- Update `scripts/build.bat`, `scripts/build.sh`, and `scripts/build_pi.sh` package staging.
- Include the complete source `QuickWaveFormData/` directory tree in every package, preserving its modulation subdirectories and waveform files.
- Put `launch_sgstudio_pi.sh` at the archive root of both Linux packages, alongside `bin/`, so it can launch SGStudio when desktop file activation is unavailable.
- Update the existing build-script packaging guide to replace the previous empty-directory policy.

## Design

- Copy `QuickWaveFormData/` recursively instead of discovering only root-level `*.wav` files or creating an empty destination.
- Preserve the source directory hierarchy in the package as `<archive-root>/QuickWaveFormData/**`.
- Keep the Linux launcher at `<archive-root>/launch_sgstudio_pi.sh` and set mode `755`; this location lets the launcher's existing script-directory detection find `<archive-root>/bin/SGStudio`.
- Do not change configure, build, watermark, archive naming, or other runtime staging behavior.

## Success Criteria

- Windows zip staging contains the full `QuickWaveFormData/` tree, including nested waveform files.
- `scripts/build.sh` and `scripts/build_pi.sh` tar staging contain the same complete tree.
- Both Linux archive roots contain executable `launch_sgstudio_pi.sh` next to `bin/`.
- The durable packaging guide no longer says that waveform data is intentionally excluded.
- Verification level: static script review plus `bash -n` syntax checks when Bash is available; no build/package run requested.

## Verification

- `git diff --check -- scripts/build_pi.sh scripts/build.sh scripts/build.bat`: passed.
- Git Bash `bash -n scripts/build.sh scripts/build_pi.sh scripts/launch_sgstudio_pi.sh`: passed.
- Static path review: both Linux scripts stage `launch_sgstudio_pi.sh` at `<archive-root>/` and apply mode `755`; the launcher's existing `$SCRIPT_DIR/bin/SGStudio` detection matches that layout.
- Static copy review: Linux uses `cp -a` on the source directory and Windows uses recursive `xcopy /E`, so the seven current modulation subdirectories and their 18 waveform files remain nested in the package.
