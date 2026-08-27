# 3rdParty build hygiene and incremental packaging

## Goal

- Keep Updater-related third-party dependencies minimal when building from source.
- Stop forcing full third-party rebuilds during normal package builds.

## Findings

- `scripts/build.bat` unconditionally deletes `build/build_msvc_<packet>_<language>` before every configure/build. That guarantees a full third-party rebuild on every package run.
- `build/build_msvc_standard_cn/CMakeCache.txt` shows curl and libzip still enable optional features by default:
  - curl: `BUILD_LIBCURL_DOCS=ON`, `BUILD_MISC_DOCS=ON`, `BUILD_OSSFUZZ=ON`, `BUILD_TESTING=ON`, `CURL_BROTLI=AUTO`, `CURL_ZSTD=AUTO`, `CURL_DISABLE_LDAP=OFF`, `CURL_DISABLE_LDAPS=OFF`
  - libzip: `ENABLE_BZIP2=ON`, `ENABLE_LZMA=ON`, `ENABLE_ZSTD=ON`
- The same cache resolves Brotli, BZip2 and Zstd from `D:/vcpkg/installed/x64-windows`, so those optional features become real link/runtime dependencies.
- Top-level `add_compile_definitions(...)` in the repo root `CMakeLists.txt` applies SGStudio git/version/branding macros to child directories, including `3rdParty`. That means metadata changes can invalidate third-party compile commands even when third-party sources do not change.

## Planned fix

- In `3rdParty/CMakeLists.txt`, explicitly disable curl docs/tests/fuzzing, curl Brotli/Zstd/LDAP, libzip BZip2/LZMA/Zstd, and zlib tests.
- In the repo root `CMakeLists.txt`, move SGStudio-specific compile definitions and the project C++ standard setup to after `add_subdirectory(3rdParty)` so third-party targets stop inheriting them.
- In `scripts/build.bat`, remove the unconditional build-dir deletion and make incremental builds the default; keep an explicit `--rebuild` escape hatch for clean builds.

## Validation

- Reconfigure `build/build_msvc_standard_cn` and verify the affected cache options switch to `OFF`.
- Build a narrow target (`Updater`) from the existing build tree to ensure the adjusted dependency graph still builds.
- Run the batch script again without `--rebuild` and confirm it reuses the existing build tree.

## Follow-up validation and docs

- Rebuilt `standard/cn` through `scripts/build.bat` and extracted `build/windows_x86_64/standard/cn/SGStudio.zip` for package-level validation.
- Verified packaged `plugin/Updater.dll` now imports only `Core.dll`, `ExtensionSystem.dll`, `Controls.dll`, `Utils.dll`, Qt runtime DLLs, `z.dll`, and system/MSVC runtime DLLs; previous `bz2.dll`, `zstd.dll`, `brotlidec.dll`, and `brotlicommon.dll` imports are gone.
- Launched the extracted packaged app and confirmed `build/windows_x86_64/standard/cn/_copilot_inspect/SGStudio/bin/debug.log` records `Load Updater OK`, `Initialize Updater OK`, and `Start Updater OK`.
- Planned knowledge base update: add a deployment/build-chain document that explains the new third-party feature gating, incremental `build.bat` behavior, validation workflow, and the exact files future maintainers should inspect when changing updater packaging.