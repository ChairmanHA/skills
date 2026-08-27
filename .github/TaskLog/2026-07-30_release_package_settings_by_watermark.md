# Release Package Settings By Watermark

Date: 2026-07-30

## Scope

- Update the Windows packaging entry `scripts/build.bat`.
- Update the Raspberry Pi native packaging entry `scripts/build_pi.sh`.
- When and only when the script is explicitly invoked with
  `--watermark off` or `--without-watermark`, mutate the staged package's
  `configuration/Settings.ini` before archive creation:
  - remove `FixedLic`;
  - remove `DebugMode`;
  - remove `RecordDeviceHistory`;
  - set `online=true`.
- Leave source templates and build-tree configuration files unchanged.
- Leave `--watermark on`, `--with-watermark`, and omitted watermark arguments
  unchanged.

## Evidence

- Both scripts already parse the watermark argument into
  `WATERMARK_OPTION=ON|OFF`; omitted arguments leave it empty.
- `scripts/build.bat` copies build-tree configuration into
  `%RELEASE_DIR%\configuration` before creating the ZIP.
- `scripts/build_pi.sh` copies build-tree configuration into the staging
  package root before creating the tar.gz.
- Every current `configuration_files/*/Settings.ini` contains all four target
  keys.

## Assumption

"No watermark is a formal release" means an explicit packaging request with
`WATERMARK_OPTION=OFF`. An omitted watermark argument remains source-default
and is not silently classified as a formal release because its effective CMake
cache value is not represented by the script argument state.

## Success Criteria

- A formal Windows package contains no `FixedLic`, `DebugMode`, or
  `RecordDeviceHistory` entries and contains `[Update] online=true`.
- A formal Raspberry Pi package has the same result.
- The mutation applies only to the copied staging file, after configuration
  staging and before archive creation.
- A missing `Settings.ini`, failed rewrite, remaining removed key, or missing
  `online=true` fails packaging instead of producing a mislabeled release.
- Internal/watermarked and source-default package behavior is unchanged.
- The durable packaging/watermark guide documents the new release policy.

## Verification Level

Static.

Per repository policy, no full configure/build/package run is planned unless
the user explicitly requests it. Verification will cover:

1. Windows batch control-flow and PowerShell transformation checks.
2. Bash syntax checking where an available Bash runtime can parse the script.
3. Isolated copies of representative `Settings.ini` files transformed with
   the same commands used by each script.
4. Focused diff and whitespace checks.

## Result

- `scripts/build.bat` now rewrites the copied
  `%RELEASE_DIR%\configuration\Settings.ini` only when
  `WATERMARK_OPTION=OFF`.
- `scripts/build_pi.sh` now applies the same policy to the copied staging file
  only when `WATERMARK_OPTION=OFF`.
- Both transformations fail packaging if `Settings.ini` is missing, an
  internal key remains, or `online=true` is absent.
- The source configuration templates and build-tree files remain unchanged.
- Updated:
  - `.github/KnowledgeBase/build_script_packaging_and_watermark_guide.md`;
  - `.github/KnowledgeBase/standard_cn_remote_auto_update_test_guide.md`.
- The remote update guide records the existing-install boundary:
  maintenance preserves the old `configuration/Settings.ini`, so this change
  makes fresh formal-package installs default to online updates but does not
  migrate an existing `online=false` user setting.

## Static Verification

- Ran the Windows PowerShell transformation against isolated copies of all
  five current configuration variants; every result removed the three target
  keys and contained `online=true`.
- Ran `bash -n scripts/build_pi.sh` using Git Bash successfully.
- Ran the Raspberry Pi `sed`/`grep` transformation against isolated copies of
  all five variants successfully.
- Confirmed both mutation calls are scoped by
  `WATERMARK_OPTION == OFF`.
- `git diff --check -- scripts/build.bat scripts/build_pi.sh` passed.
- Confirmed `git diff -- configuration_files` is empty.
- No configure, compilation, packaging, application run, or remote update was
  performed.
