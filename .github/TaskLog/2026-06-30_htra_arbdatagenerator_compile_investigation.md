# HTRA ArbDataGenerator Compile Investigation

## Scope

- Investigate compile failures reported from `src/plugins/htra/arbdatagenerator.cpp`.
- Build the owning CMake target `HTRA` using the existing Debug build tree.
- Keep any source changes limited to the smallest fix required by the compile evidence.

## Verification Level

- `debug-build`: `cmake --build build/cmake-win-debug --config Debug --target HTRA` with MSVC parallelism reduced for clearer diagnostics.

## Observations

- `src/plugins/htra/CMakeLists.txt` includes `arbdatagenerator.cpp` in the `HTRA` shared library target.
- The user reports MSVC diagnostics such as `arbdatagenerator.cpp(188): error C2065: 'profile': undeclared identifier`, while line 188 is only a closing brace in the current file.
- The reproduced MSVC diagnostic reports `arbdatagenerator.cpp(188,28): error C2065: 'profile': undeclared identifier`; column 28 corresponds to `handleData(profile);` in the editor view, but the editor line is 193.
- The same compile emits `C4819` for `arbdatagenerator.cpp` and `arbdatagenerator.h`, showing MSVC is not reading the current UTF-8 source as UTF-8.
- `src/app/CMakeLists.txt` and `src/maintenance/CMakeLists.txt` already use `$<$<CXX_COMPILER_ID:MSVC>:/utf-8>`.

## Assumptions

- The line mismatch is most likely caused by parser recovery after an earlier syntax or scope error, or by the compiler consuming a different generated/preprocessed source state than the editor view.
- The first real compiler error in a narrowed `HTRA` Debug build is the best evidence for the root cause.

## Success Criteria

- Reproduce the current compile failure for target `HTRA`, or report that the target already compiles.
- If failures reproduce, identify the earliest actionable diagnostic and whether the reported line drift is due to parser recovery, stale build state, encoding, or a concrete source issue.
- If a narrow source fix is made, rerun the same `HTRA` Debug build and report the result.

## Result

- Added the existing project-style MSVC UTF-8 compile option to the `HTRA` target.
- Rebuilt with:

  ```powershell
  $env:CL='/FS'; cmake --build build/cmake-win-debug --config Debug --target HTRA -- /m:1 /p:CL_MPCount=1 /p:UseMultiToolTask=false
  ```

- Result: `EXITCODE=0`, no `error C...` matches, no `C4819` matches, and `HTRA.vcxproj -> D:\development\vsg2.0\build\cmake-win-debug\plugin-runtime\HTRA.dll`.
