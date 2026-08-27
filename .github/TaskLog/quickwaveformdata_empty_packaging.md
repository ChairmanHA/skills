# QuickWaveFormData Empty Packaging

## Scope

- Update `scripts/build.bat` and `scripts/build_pi.sh` package staging logic.
- Package output should contain a `QuickWaveFormData` directory.
- Waveform files from source `QuickWaveFormData` should not be copied into the package.

## Design

- Keep the existing package layout stable by creating the destination directory unconditionally with the rest of the staged runtime folders.
- Remove the `*.wav` discovery/copy step from both scripts.
- Do not change build, archive naming, watermark, or other staging behavior.

## Success Criteria

- Windows package staging creates `%RELEASE_DIR%\QuickWaveFormData` and never runs `xcopy` for source waveform files.
- Raspberry Pi staging creates `${archive_root}/QuickWaveFormData` before archiving and never runs `find/cp` for source waveform files.
- Verification level: static review only; no build/package run requested.
