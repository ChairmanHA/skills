# Standard CN Remote Auto-Update Test Guide

Date: 2026-07-29

## Scope

- Document the current remote auto-update contract for the active
  `src/plugins/updater` implementation.
- Cover the two requested Standard CN targets:
  - Windows x86_64 (called Win32 in the product/testing context)
  - Linux aarch64
- Give website operations the exact public object paths, filenames, package
  layout, publishing requirements, and pre-release checks.
- Give the tester the client-side prerequisites and a minimal acceptance
  procedure.

## Evidence And Assumptions

- `src/plugins/CMakeLists.txt` includes `src/plugins/updater`; the
  `src/plugins/updater_bak` tree is not active.
- `PacketSpec::loadUrl()` ignores the URL value read from `Settings.ini` and
  constructs the final URL from the hard-coded Harogic prefix, target platform,
  compiled language, and compiled package filename.
- Standard packages map to the remote basename `SGStudio`.
- Windows builds request `.zip`; Linux aarch64 builds request `.tar.gz`.
- The request paths use hyphenated platform names, while local build output
  directories use underscored platform names.
- "Win32" is interpreted as the current Windows x86_64 product target. There
  is no active 32-bit x86 remote package branch.
- Documentation is based on current source and repository documentation. No
  production upload, remote download, build, or real update is authorized by
  this task.

## Success Criteria

- The document gives the exact two URLs requested by the current binaries.
- The document names the exact two artifacts that website operations must
  publish and shows their single-root-directory archive layout.
- The document distinguishes the software prompt version (`Software`) from
  the package display version (`Version`).
- The document states that `[Update] online=true` is required on the installed
  test client and that the runtime `url` setting does not redirect the current
  implementation.
- The document includes safe publication guidance for stable, overwritten
  URLs and a practical package/server/client verification checklist.
- The KnowledgeBase index links to the new durable guide.

## Verification Level

Static.

## Planned Static Checks

1. Re-read all modified documentation files before editing.
2. Cross-check URLs against `currentRemotePackageUrl()`.
3. Cross-check package names against `src/plugins/updater/CMakeLists.txt`.
4. Cross-check archive layout and output paths against `scripts/build.bat`,
   `scripts/build.sh`, and `scripts/build_pi.sh`.
5. Cross-check required parsed files against `PacketSpec::parseScratchWorkspace()`,
   `findMaintenance()`, and `findUpdater()`.
6. Run focused content searches and `git diff --check` for the new documents.

## Result

- Added
  `.github/KnowledgeBase/standard_cn_remote_auto_update_test_guide.md`.
- Added the guide to `.github/KnowledgeBase/Index.md`.
- Confirmed from active source that Standard CN resolves to:
  - `https://www.harogic.cn/windows-x86_64/cn/SGStudio.zip`
  - `https://www.harogic.cn/linux-aarch64/cn/SGStudio.tar.gz`
- Confirmed the installed Standard CN settings template currently has
  `[Update] online=false`.
- Confirmed the active Linux parser requires the exact lowercase
  `updater/updater` name.
- Inspected the existing local package outputs:
  - the Windows archive contains the current key package entries;
  - the existing 2026-05-22 aarch64 archive still contains
    `Updater_Linux-2.0.19`, so the guide explicitly rejects it and requires a
    rebuild before the requested test.
- Checked the new guide and TaskLog for trailing whitespace, balanced Markdown
  fences, and a resolvable KnowledgeBase index entry.
- Ran `git diff --check` for the intended documentation paths. This workspace's
  `.gitignore` ignores `.github/`, so Git does not report those files in the
  worktree diff; the explicit content/hygiene checks above were used to verify
  the generated documents.
- No build, production upload, network download, application run, or real
  software/firmware update was performed.

## 2026-07-30 URL And Release-Note Follow-up

### User Changes Observed

- `currentRemotePackageUrl()` now uses
  `https://www.harogic.cn/` as its hard-coded prefix; the intermediate `sg/`
  path component has been removed.
- Top-level `CMakeLists.txt` now declares:
  - software version `2.7.2`;
  - package version `26.07.30`.
- `package-info/version.json` has already been updated consistently to
  `Software: 2.7.2`, `Version: 26.07.30`, and `Date: 2026-07-30`.

### Follow-up Scope

- Replace every operative `/sg/` URL and web-root path in the Standard CN
  remote auto-update guide.
- Update all five package release-note headings from `2.7.1` to `2.7.2`.
- Add online-update support to each release note:
  - use the user's exact sentence `支持在线更新功能。` in Chinese files;
  - use equivalent localized wording in English and Russian files.
- Preserve all existing release-note content and ordering outside these
  requested changes.

### Success Criteria

- The guide contains only the two current URLs:
  - `https://www.harogic.cn/windows-x86_64/cn/SGStudio.zip`
  - `https://www.harogic.cn/linux-aarch64/cn/SGStudio.tar.gz`
- No operative `/sg/` path remains in the guide.
- Every `package-info/releasenote_*.txt` file starts with version `2.7.2` and
  contains one localized online-update feature entry.
- `CMakeLists.txt` and `package-info/version.json` remain untouched because
  the user's existing changes are already consistent.

### Verification Level

Static. No configure, build, package, download, or update run is planned.

### Follow-up Result

- Updated all exact download URLs and web-root paths in the KnowledgeBase guide
  to remove the former intermediate `sg/` component.
- Updated all five package release-note headings to `2.7.2`.
- Added one online-update feature entry to every package release note, localized
  for Chinese, English, and Russian.
- Verified the existing CMake and package metadata values remain aligned:
  software `2.7.2`, package `26.07.30`, date `2026-07-30`.
- No source code, CMake value, `package-info/version.json`, build output, remote
  server, or installed application was modified by this follow-up.
