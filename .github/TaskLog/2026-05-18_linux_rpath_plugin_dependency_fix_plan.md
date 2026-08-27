# 2026-05-18 Linux RPATH plugin dependency fix plan

## Context
- `scripts/build.sh` packages Linux runtime with `bin/`, `lib/`, `plugin/`, `configuration/`.
- Global Linux RPATH in `src/CMakeLists.txt` already points plugins to `$ORIGIN/../lib`.
- Remote `readelf -d build/build_aarch64_standard_cn/plugin/libAnalogModulation.so` shows:
  - `RUNPATH = $ORIGIN:$ORIGIN/../lib:$ORIGIN/../bin:$ORIGIN/../plugin`
  - `NEEDED = /home/jiashilin/vsg2.0/sgstudio/3rdParty/modulation/lib/gcc_aarch64/libgenSignalWave.so`
- Remote `readelf -d 3rdParty/modulation/lib/gcc_aarch64/libgenSignalWave.so` shows no `SONAME` entry.

## Hypothesis
- The packaging failure is not caused by plugin RUNPATH.
- `libgenSignalWave.so` lacks `SONAME`, and current CMake links Analog against the absolute `.so` path, so the linker records that absolute path in `DT_NEEDED`.
- After the absolute-path fix, Linux package scan may still reveal transitive third-party runtime gaps unrelated to RPATH; those need separate packaging treatment.

## Plan
1. Update `3rdParty/modulation/CMakeLists.txt` on Linux to mark the imported library as `IMPORTED_NO_SONAME TRUE` so consumers link by search name instead of embedding the absolute file path.
2. Rebuild `aarch64` on the remote Linux host with `scripts/build.sh`.
3. Verify `libAnalogModulation.so` no longer contains any absolute-path `NEEDED` entry.
4. Scan all built ELF files under the packaged aarch64 build tree for absolute-path `NEEDED` entries.
5. If the repo doc still says `libs`, align it with this repo's actual `lib` runtime layout.

## Validation update
- Packaged aarch64 tarball no longer contains absolute-path `DT_NEEDED` entries for app plugins; `libAnalogModulation.so` now depends on `libgenSignalWave.so` by basename.
- Package scan shows business plugins have no unpackaged direct dependencies, but `libh2api.so.2` and `Updater_Linux-2.0.17` still require system `libgomp.so.1`.
- Qt's optional graphics/platform sub-plugins still depend on host graphics stack libraries such as `libGL.so.1`, `libEGL.so.1`, `libdrm.so.2`, and Wayland libs; these are separate deployment assumptions, not plugin-specific absolute-path issues.