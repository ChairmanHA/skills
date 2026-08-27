# Raspberry Pi launcher brand-neutral packaging

## Scope

- Analyze and update the launcher staging performed by `scripts/build_pi.sh`.
- Keep cross-compiled `scripts/build.sh` aligned because it stages the same
  launcher into the same Linux package layout.
- Package `launch_pi.sh` for neutral and BNC editions so their launcher filename
  and content contain no `SGStudio` branding.
- Preserve the standard edition's packaged launcher filename
  `launch_sgstudio_pi.sh` to avoid an unrelated standard-package compatibility
  break.
- Remove launcher package-root and executable-name fallback probing. Launch only
  `<script-directory>/bin/<packet application name>`.
- Verification level: `static`; do not configure, compile, package, or run.

## Observations

- Both Linux packaging scripts create a staging root at
  `${STAGING_DIR}/${PACKET_ARCHIVE_FILE_NAME}`, copy runtime directories under
  it, then copy `scripts/launch_sgstudio_pi.sh` unchanged to that root and mark it
  executable before creating the tarball.
- The current launcher can take `--app-root`, reads `SGSTUDIO_ROOT`, and probes
  Desktop, script-parent, `/software/VectorCore`, and `/software/SGStudio`
  candidates. It then probes both `bin/VectorCore` and `bin/SGStudio`.
- This fallback model is unnecessary for a launcher shipped at the package root:
  its application directory is deterministically `<launcher-dir>/bin`.
- Main executable names are already packet/language-defined: standard cn/en use
  `SGStudio`, standard ru uses `СПО ГСРВ`, neutral uses `VSG`, and BNC uses
  `VectorCore`.

## Design

- Rename the tracked source launcher to `scripts/launch_pi.sh` using `git mv`.
- Make it a package template containing `@APPLICATION_EXECUTABLE@`; it has no
  product-name or installation-path fallback.
- In both Linux packaging scripts, derive:
  - `APPLICATION_EXECUTABLE_NAME`: `SGStudio`, `СПО ГСРВ`, `VSG`, or
    `VectorCore`;
  - `PACKAGE_LAUNCHER_FILE_NAME`: legacy `launch_sgstudio_pi.sh` only for
    standard/other packets, otherwise `launch_pi.sh`.
- Render the template into the staging root with `sed`, then apply mode `755`.
- Retain normal-user enforcement, existing-process cleanup, Wayland/session
  environment setup, root-process/semaphore warnings, detach mode, and argument
  forwarding.

## Success criteria

1. Native-Pi and cross-Linux BNC packages contain `launch_pi.sh` launching only
   `bin/VectorCore`.
2. Native-Pi and cross-Linux neutral packages contain `launch_pi.sh` launching
   only `bin/VSG`.
3. Standard packages retain `launch_sgstudio_pi.sh`; cn/en launch only
   `bin/SGStudio`, while ru launches only `bin/СПО ГСРВ`.
4. The generic source template contains no case-insensitive `sgstudio` text and
   no Desktop, `/software`, parent-directory, `--app-root`, or executable probing.
5. The staged launcher always derives the package root from its own directory.
6. Bash syntax and static render simulations pass without configuring, building,
   packaging, or launching the application.

## Implementation result

- Renamed the tracked source launcher from `scripts/launch_sgstudio_pi.sh` to
  the brand-neutral template `scripts/launch_pi.sh`.
- Reduced launcher root resolution to its own directory and the single path
  `bin/@APPLICATION_EXECUTABLE@`; removed `--app-root`, `SGSTUDIO_ROOT`, Desktop,
  parent-directory, `/software`, and alternate-executable probing.
- Kept the existing user-mode guard, process cleanup, log permission checks,
  Wayland/session environment, root process/semaphore warnings, detach mode, and
  argument forwarding. The template no longer reassigns `HOME`.
- Updated `scripts/build_pi.sh` and `scripts/build.sh` to render the template with
  these mappings:
  - standard cn/en: `launch_sgstudio_pi.sh` -> `bin/SGStudio`;
  - standard ru: `launch_sgstudio_pi.sh` -> `bin/СПО ГСРВ`;
  - neutral: `launch_pi.sh` -> `bin/VSG`;
  - BNC: `launch_pi.sh` -> `bin/VectorCore`.
- Updated the durable packaging and BNC profile documentation to describe the
  exact launcher filenames and direct package-relative resolution.

## Static verification result

- Git for Windows Bash `-n` passed for `scripts/build.sh`,
  `scripts/build_pi.sh`, and the source `scripts/launch_pi.sh` template.
- Rendered standard cn/en, standard ru, neutral, and BNC launcher content
  independently; Bash `-n` passed for all rendered forms and each contained its
  expected executable.
- The neutral and BNC rendered launchers contained no case-insensitive
  `SGStudio` text.
- The source template contained none of `SGStudio`, Desktop, `/software`,
  `--app-root`, `SGSTUDIO_ROOT`, or the former candidate-probing loop.
- Active packaging scripts and KnowledgeBase documents contain no reference to
  the removed source path `scripts/launch_sgstudio_pi.sh`.
- `git diff --check` passed. No configure, build, package, or application launch
  was performed.
