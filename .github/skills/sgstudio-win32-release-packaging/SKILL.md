---
name: sgstudio-win32-release-packaging
description: 'Use only for final Win32 Release packaging of the five ordinary SGStudio editions without a watermark, followed by extracted-package startup smoke tests. Excludes BNC/VectorCore and ordinary development builds.'
---

# SGStudio Win32 Final Release Packaging

## Scope

- Use this workflow only for a final Windows Release delivery.
- Build exactly `standard cn`, `standard en`, `standard ru`, `neutral cn`, and
  `neutral en`.
- Never include `BNC en` / VectorCore. It is a special edition with an explicit,
  separate build entry.
- Always produce the formal no-watermark configuration.
- Keep the default product package names: all standard archives are `SGStudio`;
  all neutral archives are `VSG`.

## Execute

Run the bundled entry from PowerShell, passing the SGStudio repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "<this-skill>\scripts\package_and_smoke_test.ps1" -RepositoryRoot "<sgstudio-repository-root>"
```

The entry performs the entire fixed workflow:

1. Rejects the run if an SGStudio, VSG, or Russian-edition process is already
   active, because single-instance behavior would invalidate the smoke test.
2. Calls `scripts/build_all.bat --watermark off`; `build_all.bat` supplies
   `--rebuild` to every target.
3. Requires all five expected zip files and extracts each into its own temporary
   directory.
4. Starts the edition's real executable from the extracted `bin/` directory:
   `SGStudio.exe`, `СПО ГСРВ.exe`, or `VSG.exe` as applicable.
5. Waits six seconds. The launch passes only when the captured process is still
   alive, after which the entry terminates that process tree and continues.
6. Prints one final summary and returns nonzero if compilation/packaging or any
   launch check failed.

Do not replace this entry with ad hoc archive discovery or build-tree execution:
the validation target is the contents of each newly generated zip.

## Report

Relay the final result in Chinese and include both outcomes:

- 编译打包：成功或失败。
- 启动检查：成功数量/5，并列出任何失败的 packet/language 与原因。

If the entry fails before building because a product process is already active,
state that compilation/packaging and launch validation were not run and ask the
user to close the reported process before retrying.
