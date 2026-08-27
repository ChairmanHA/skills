# 2026-06-25 package images directory creation

## Scope

- Check the updater preservation contract for runtime/user-owned entries.
- Check Windows and Linux packaging scripts under `scripts/`.
- Add package-time creation of the runtime `images/` directory if missing.

## Evidence

- `updater_firmware_update_mechanism.md` documents the current update flow as whole-package copy followed by selected backup entry merge.
- `CopyThread` currently keeps these backup entries:
  - `configuration/Settings.ini`
  - `QuickWaveFormData`
  - `data`
  - `images`
  - `reports`
  - `bin/**/*.lic`
- `scripts/build.bat` creates the Windows staging root and standard package folders, but did not create `images/`.
- `scripts/build.sh` stages existing build output folders plus optional runtime data, but did not create `images/`.
- `scripts/build_all.bat` delegates Windows package creation to `scripts/build.bat`.

## Design

- Treat `images/` as a runtime package root folder, sibling to `bin/`, `plugin/`, and `configuration/`.
- Create an empty `images/` folder in both Windows and Linux package staging roots.
- Do not change updater copy/merge semantics in this task.
- Do not add broader manifest ownership behavior.

## Verification Level

static

## Success Criteria

- `scripts/build.bat` creates `%RELEASE_DIR%\images`.
- `scripts/build.sh` creates `${STAGING_DIR}/${PACKET_ARCHIVE_FILE_NAME}/images`.
- Static search confirms both scripts contain the new `images` directory creation.
- No build or package run is required for this task.

## Implementation Notes

- Added Windows staging creation for `%RELEASE_DIR%\images`.
- Added Linux tar staging creation for `${STAGING_DIR}/${PACKET_ARCHIVE_FILE_NAME}/images`.
- Updated `build_script_packaging_and_watermark_guide.md` to document that package staging pre-creates `images/`.

## Verification

- `rg -n "images|从 build tree|打包 staging" scripts/build.bat scripts/build.sh .github/KnowledgeBase/build_script_packaging_and_watermark_guide.md`
- `git diff --check -- scripts/build.bat scripts/build.sh .github/KnowledgeBase/build_script_packaging_and_watermark_guide.md .github/TaskLog/2026-06-25_package_images_directory_creation.md`

Result: static checks passed. Git reported only line-ending normalization warnings for the script files.
