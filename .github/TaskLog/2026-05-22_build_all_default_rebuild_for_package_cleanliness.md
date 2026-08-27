# 2026-05-22 build_all default rebuild for package cleanliness

## Goal
- Make batch packaging via scripts/build_all.bat default to full rebuilds.
- Avoid stale files in build outputs being carried into package assembly.

## Findings
- scripts/build.bat packages updater payload from build/build_msvc_<packet>_<language>/updater, not directly from updater_files/windows.
- scripts/build.bat only removes the build directory when --rebuild is passed.
- build_all.bat currently invokes build.bat without --rebuild, so stale updater artifacts can survive across package runs.

## Decision
- Keep the fix local to scripts/build_all.bat for this request.
- Append --rebuild to every build.bat invocation, including dry-run output.

## Validation
- Dry-run output should show each generated command ending with --rebuild.