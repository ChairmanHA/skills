# Updater Packet Extract Writable Temp Fix

Date: 2026-07-01

## Scope

- Fix `PacketSpec` package extraction path selection for Raspberry Pi / Linux local update parsing.
- Keep package structure parsing, update handoff arguments, and decompression behavior unchanged.
- Add focused diagnostics around directory creation and decompression failure.

## Observations

- `PacketSpec::run()` currently builds `m_folderPath` from `Updater::Internal::Plugin::appLocalPath()`.
- The code calls `QDir().mkpath(m_folderPath)` without checking the return value.
- Raspberry Pi field debugging shows failure at `mkpath(m_folderPath)`, before package metadata can be parsed.
- `UpdateDialog` already uses `QStandardPaths::TempLocation` for maintenance ready-marker files, which is a better fit for short-lived updater scratch data.

## Assumption

On the Raspberry Pi deployment, the current AppLocal path may be empty, redirected, root-owned, or otherwise not writable for the running GUI user. Package extraction is temporary scratch data, so a verified writable temp directory is the correct primary target.

## Plan

1. Add a local helper in `packetspec.cpp` to select a writable updater scratch root. Done.
2. Prefer `QStandardPaths::TempLocation`, fall back to `QDir::tempPath()`, and only then the existing app-local path. Done.
3. Verify each candidate by creating the directory and writing/removing a probe file. Done.
4. Check `mkpath(m_folderPath)` and `Decompressor::decompress(...)` return values before continuing. Done.
5. Run static diff checks only; no build/run unless requested. Done.

## Success Criteria

- `PacketSpec` extracts into a per-run directory under a verified writable temp root.
- Directory creation failures produce a clear warning and do not continue into decompression/parsing.
- Decompression failures stop parsing instead of producing misleading package-structure errors.
- Static diff checks pass for the changed files.

## Verification

- Static inclusion check: `packetspec.cpp` is part of the `Updater` target in `src/plugins/updater/CMakeLists.txt`.
- Static diff hygiene: `git -c safe.directory=D:/development/vsg2.0 diff --check -- src/plugins/updater/packetspec.cpp` passed.
- Build/run: not run, per the repository default static-analysis workflow.
