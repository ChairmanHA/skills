# Linux build environment parity (192.168.3.132 / 192.168.3.250)

Date: 2026-08-20

## Scope

- Compare the known-good Raspberry Pi build host `192.168.3.132` with the
  x86_64 build host `192.168.3.250`.
- Provide an x86_64-hosted aarch64 build path whose target OS, compiler, Qt,
  QtWayland private ABI, and packaging behavior match the Raspberry Pi native
  build closely enough for UI/runtime validation.
- Define the complete `gcc_x86_64` build boundary and a suitable Linux UI test
  environment.
- Keep `scripts/build_pi.sh` as the native build implementation. Do not bypass
  its architecture guard with a host cross compiler and a different Qt tree.

## Observations

- Both remote checkouts are at commit
  `44afde79bc5ff69745626d48746c0050fd193670`; their tracked
  `scripts/build_pi.sh` contents match.
- The known-good host is Debian 12 / aarch64 with GCC 12.2, glibc 2.36, and
  Debian Qt 5.15.8. Its desktop is labwc/Wayland with Xwayland available.
- The x86_64 host is Ubuntu 18.04 with GCC 7.5, glibc 2.27, and system Qt 5.9.5.
  Its historical aarch64 build used GCC 7.5 and
  `/opt/Qt/5.15.18/gcc_aarch64`.
- The known-good aarch64 package contains Debian Qt 5.15.8 libraries/plugins.
  The historical x86-hosted aarch64 package contains Qt 5.15.18
  libraries/plugins. The packages differ substantially in file count and Qt
  platform-plugin binaries.
- Vendored layer-shell-qt links QtWayland private APIs. Mixing or substituting
  Qt builds is therefore an ABI and behavior risk, not only a missing-package
  problem.
- The repository contains the H2 API for `gcc_x86_64`, but it does not contain
  `3rdParty/modulation/lib/gcc_x86_64/libgenSignalWave.so`. The modulation
  source is not present, so a complete x86_64 product build cannot manufacture
  this binary from the repository.
- `192.168.3.250` currently has neither Docker nor qemu-user-static/binfmt for
  aarch64 containers. Sudo is available and the machine has sufficient CPU,
  memory, and disk capacity.

## Inference

- Installing more Ubuntu 18.04 arm64 packages into the historical cross-build
  environment cannot make it equivalent to the Raspberry Pi build because the
  compiler, libc, Qt, platform plugins, and QtWayland private ABI still differ.
- A Debian 12 container running the target architecture is the narrowest
  reproducible way to reuse the native `build_pi.sh` logic on the x86_64 host.
  It targets functional/runtime parity, not a byte-for-byte reproducible tarball;
  timestamps, build paths, build IDs, and package patch revisions can still
  differ.
- The same Debian 12 container definition can provide an x86_64 package with
  Qt 5.15.8 for cross-architecture UI comparison, once the missing vendor
  `gcc_x86_64` modulation library is supplied.

## Planned Changes

1. Add a Debian 12 container image definition containing the native SGStudio
   Qt5/QtWayland/OpenSSL build dependencies for either arm64 or amd64.
2. Add a host setup/check script for Docker and arm64 binfmt support.
3. Add a container build wrapper accepting the same arguments as
   `build_pi.sh`; it runs that native script inside the selected architecture.
4. Improve `build_pi.sh` architecture errors so an x86_64 user is directed to
   the container wrapper instead of being encouraged to disable the guard.
5. Add early checks for architecture-specific closed-source vendor libraries,
   so the x86_64 blocker is reported before a long configure/build.
6. Add a package audit script that checks ELF architecture, bundled Qt version
   consistency, Wayland platform/shell plugins, and optional comparison with a
   known-good reference package.
7. Update the durable build guide with the native/container distinction and
   x86_64 test recommendation.

## Success Criteria

- `build_pi.sh aarch64 ...` remains a true native-aarch64 entry.
- On an x86_64 host, the new wrapper can select a Debian 12 arm64 container and
  invoke the unmodified native build flow with Qt 5.15.8.
- The container image exposes GCC 12, Qt 5.15.8, QtWayland private development
  files, Wayland scanners/protocols, xkbcommon, and OpenSSL 3 through the paths
  expected by the repository.
- The package audit rejects mixed Qt library/plugin versions and wrong ELF
  architectures and confirms the layer-shell runtime artifacts are present.
- The x86_64 path fails immediately with the exact missing vendor library until
  `libgenSignalWave.so` for `gcc_x86_64` is supplied.
- Static shell parsing and whitespace checks pass. Remote container/configure or
  build verification is performed if host setup completes safely.

## Verification Level

- Local: static.
- Remote `192.168.3.250`: dependency/container probe, then configure/build when
  the required architecture-specific vendor binaries are available.
- UI acceptance: Debian 12 x86_64 with labwc/Wayland at 1280x800 for geometry
  parity; the aarch64 release remains accepted only on the target Raspberry Pi
  labwc/Wayland environment.

## Implementation And Verification Status

- Added `scripts/docker/linux-bookworm.Dockerfile` with Debian 12 Qt5/QtWayland,
  private headers, Wayland, xkbcommon, OpenSSL 3, compiler, and multimedia
  development dependencies. The image creates the `/opt/openssl` and
  `/opt/aarch64-openssl` paths expected by the repository CMake logic without
  glob-copying system libraries.
- Added `scripts/build_linux_bookworm_container.sh` and
  `scripts/install_linux_container_build_deps.sh`. The wrapper selects an
  explicit arm64 or amd64 Debian 12 image and invokes the native
  `scripts/build_pi.sh` implementation; the setup script installs Docker,
  qemu-user-static, and binfmt-support on an x86_64 Debian/Ubuntu host.
- Added `scripts/audit_linux_package_ui_runtime.sh`. It was run against both
  existing remote archives and passed architecture, Qt-version, Wayland plugin,
  and layer-shell artifact checks. It reported Qt `5.15.8` for 132 and Qt
  `5.15.18` for 250, providing direct evidence of the runtime difference.
- Added early missing-vendor checks to `scripts/build.sh` and
  `scripts/build_pi.sh`. The x86_64 path now reports the absent
  `3rdParty/modulation/lib/gcc_x86_64/libgenSignalWave.so` before configuring.
- Local Bash syntax checks and `git diff --check` pass.
- 250 dependency installation was attempted with the new setup script but was
  blocked before package changes by an existing apt update process holding
  `/var/lib/apt/lists/lock` for approximately 28 hours. The stale apt process
  remains untouched; rerun the setup script after the host administrator
  resolves that lock.

## Follow-up: enable the supplied x86_64 modulation runtime

- The user has now synchronized the repository to 250 and supplied
  `3rdParty/modulation/lib/gcc_x86_64/libgenSignalWave.so`.
- `scripts/build.sh` will accept `gcc` as the canonical x86_64 environment and
  additionally accept `gcc_x86_64`/`x86_64` aliases, while keeping the existing
  aarch64 path unchanged.
- The x86_64 branch will reject a non-x86_64 host or compiler before CMake and
  will continue to require the supplied vendor runtime.
- Verification target: Bash syntax/static checks only in this turn; the user
  will run the remote build and UI test.

Implementation completed: `scripts/build.sh` now accepts `gcc`,
`gcc_x86_64`, and `x86_64` (all use the canonical `gcc` build tree), validates
the host/compiler target, and reports the requested alias in build information.

## Follow-up: x86 QtWayland configuration and Linux package size

### Scope

- Fix the reported x86_64 configure failure when the Qt SDK cannot resolve
  `Qt5WaylandClientConfig.cmake` for vendored `layer-shell-qt`.
- Inspect the `layer-shell-qt` install/staging path and make `build_pi.sh`
  collect only runtime artifacts needed by SGStudio, excluding source, Python,
  CMake development trees, and other host-side files.
- Preserve the layer-shell runtime plugin and all Qt Wayland platform plugins
  required by the helper on Raspberry Pi.

### Success criteria

- On the 132 Raspberry Pi host, `build_pi.sh aarch64 standard cn SGStudio`
  configures without the x86-only Qt SDK error and produces a package.
- The staged archive contains `liblayer-shell.so` plus the required Qt
  Wayland/platform runtime files, but no vendored layer-shell source tree,
  Python interpreter/modules, or CMake package metadata copied solely from a
  development prefix.
- The package audit/static checks continue to pass; runtime UI acceptance is
  performed by the user on 132.

### Verification level

- Static inspection plus user-requested remote Raspberry Pi package build.
- Do not claim UI parity or byte identity until the resulting package is run
  on 132 and its size/content are compared with the previous archive.

### Observed package evidence

- The existing 132 archive is `158,488,823` bytes and contains 349 entries.
- Its uncompressed `lib/` content totals about 269 MB. The unrelated
  `libQt5WebEngineCore.so.5.15.13` (about 125 MB) and
  `libQt5WebKit.so.5.212.0` (about 42 MB) dominate the excess.
- The archive has no Python interpreter or `.py` files, but the blanket Qt
  plugin copy stages a `PyQt5` plugin group. It also stages client and server
  Wayland plugin groups even though SGStudio is a Wayland client.
- The old copy command places the system `wayland-shell-integration` directory
  underneath the existing SGStudio output directory, producing an unnecessary
  nested `wayland-shell-integration/wayland-shell-integration/` tree.

### Implemented package boundary

- `build_pi.sh` now copies only selected Qt client/runtime plugin groups and
  explicitly excludes development/Python and compositor-server groups.
- It merges the standard Qt shell integration plugins beside
  `liblayer-shell.so`, preserving normal xdg-shell for the main process and the
  custom layer-shell integration for the minibar helper without nesting.
- It discovers the Qt library module closure with `ldd` over every packaged ELF
  root and copies only those Qt module families from the native qmake prefix.
- It validates the Wayland platform plugin, layer-shell plugin/interface, and
  essential Qt UI libraries before staging the archive.
- Local Bash syntax and whitespace verification are required before handoff;
  the user will perform the full build and UI run on 132.

### ICU correction

- The first 132 test exposed a classifier bug: `ldd` reports versioned ICU
  sonames such as `libicudata.so.72`, while the collector only matched the
  unversioned `libicu*.so` spelling. This produced zero collected runtime
  modules and a misleading hard failure.
- The collector now matches `libicu*.so*` and copies ICU families only when an
  actual packaged ELF depends on them. ICU is no longer unconditionally
  required by the package verifier.

## Follow-up: isolate packaged Qt 5.15.8 from target Qt 5.15.15

### Observation

- The reduced package can still start with bundled Qt 5.15.8 libraries and then
  discover a target-machine Qt 5.15.15 plugin through Qt's compiled-in plugin
  prefix. Removing inherited `QT_PLUGIN_PATH` does not remove that compiled-in
  fallback.
- The official launcher currently appends inherited `LD_LIBRARY_PATH`, which
  also permits a target-specific Qt directory to participate after the package
  directories.

### Plan and success criteria

- Make the packaged runtime layout explicit through `qt.conf`: executable in
  `bin/`, Qt plugins in `bin/`, and Qt libraries in `lib/`.
- Make the official launcher set package-owned Qt plugin paths and a
  package-only `LD_LIBRARY_PATH`; do not inherit arbitrary target Qt paths.
- Preserve the same paths across the application's detached restart.
- During `build_pi.sh`, verify that every `libQt5*.so` resolved by every staged
  ELF comes from the current build's `lib/` directory.
- Success requires the official launcher to run the 5.15.8 package without
  loading any target-machine Qt 5.15.15 library or plugin. Non-Qt OS runtime
  libraries remain target dependencies.

### Archive evidence and implementation

- Inspected `D:/SGStudio.tar.gz` (`92,629,141` bytes, 249 entries). Its 15
  versioned Qt libraries are consistently Qt 5.15.8; the archive contains no
  Qt 5.15.15 file. ICU 72 libraries and links are also present.
- The archive does not contain `bin/qt.conf`. Its launcher unsets the package
  Qt plugin variables and appends the inherited target `LD_LIBRARY_PATH`, so a
  target Qt 5.15.15 plugin/library can still enter the process.
- `src/app/etc/qt.conf` now fixes the relative runtime layout. Both Linux build
  scripts copy it beside the packaged executable.
- `launch_pi.sh` now sets package-owned Qt plugin paths and replaces rather than
  extends inherited `LD_LIBRARY_PATH`.
- The packaged Wayland restart path retains those same package plugin paths.
- `build_pi.sh` now checks every staged ELF under a package-only loader path and
  fails if any resolved `libQt5*.so` comes from outside `build/lib`.

## Follow-up: packaged indirect ICU dependency lookup

### Scope

- Diagnose why the package contains `libicui18n.so.72` and its target but the
  Raspberry Pi dynamic loader still reports it missing.
- Preserve the package's existing relative layout while making the official
  launcher export a package-local library search path for indirect dependencies.

### Success criteria

- The launcher resolves direct and transitive dependencies from
  `<package>/lib`, including ICU dependencies of bundled Qt libraries.
- Existing user/system `LD_LIBRARY_PATH` entries remain available after the
  package paths, and the launcher does not require root or system installation.
- The diagnosis is backed by ELF `NEEDED`/`RUNPATH` inspection of the generated
  package and a local shell syntax check.

### Diagnosis and implementation

- The archive contains the valid relative link
  `lib/libicui18n.so.72 -> libicui18n.so.72.1` and the target file.
- `bin/SGStudio` has `RUNPATH=$ORIGIN/../lib:$ORIGIN/../plugin`, while the
  bundled `libQt5Core.so.5.15.8` has no RPATH/RUNPATH and directly declares
  `libicui18n.so.72`, `libicuuc.so.72`, and `libicudata.so.72` as NEEDED.
- Because ELF RUNPATH is not inherited for a library's transitive dependencies,
  the loader can find QtCore but still searches system paths for ICU. A target
  Raspberry Pi without ICU 72 therefore reports the bundled ICU as missing.
- `scripts/launch_pi.sh` now prepends the package `lib`, `bin`, `plugin`, and
  `updater` directories to `LD_LIBRARY_PATH`, preserving any existing entries.
  This makes direct and transitive package-owned dependencies resolve from the
  extracted archive.

### Follow-up: missing ICU runtime

- The first reduced package failed on a clean runtime host with
  `libicui18n.72.so` missing. This is a non-Qt dependency of the Debian Qt
  build, not a layer-shell source dependency.
- `build_pi.sh` now records resolved `libicu*.<version>.so` dependencies from
  the same ELF scan and bundles their versioned file/symlink families from the
  resolved host directory.
- `verify_packaged_ui_runtime` requires the ICU data, common, and international
  families before staging; the package audit checks the same three families.
- This keeps the package independent of a target Qt development installation;
  the user must rebuild on 132 and rerun the clean-machine startup test.
