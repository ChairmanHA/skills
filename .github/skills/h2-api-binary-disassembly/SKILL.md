---
name: h2-api-binary-disassembly
description: "Use when an SGStudio bug may originate inside the closed-source HAROGIC H2 API (h2_api.dll / libh2api.so) and the header alone cannot explain the behavior: e.g. a call works only under a debugger breakpoint or with an added sleep, a setting silently does not take effect, a query returns stale values, or you must prove to the API vendor what a function really sends. Covers why to disassemble the symbol-bearing gcc_x86_64 .so, extracting call graphs, device opcodes, waits, cache-vs-device queries, transport vtables, cross-checking the Windows DLL, and writing an evidence-based vendor report."
---

# H2 API Binary Disassembly

## Goal

Determine what an H2 API function **actually does inside the library** — which device instructions it sends, whether it waits for an acknowledgement, whether a query reads the device or a host cache, and what hidden limits or delays exist — so a timing or "no effect" bug can be attributed to SGStudio code or to the API with concrete evidence.

This is static analysis of the vendor binary. It does not replace measurements on the device; it tells you which experiment to run and what to ask the vendor.

## When To Use

- Behavior changes when a breakpoint, `qDebug`, or `QThread::msleep` is added near an H2 call (classic sign of a missing hardware handshake).
- An H2 `config` call returns `0` but the RF output does not change.
- You need to know if `*_query_*` returns device state or only what the host last sent.
- You need to know whether an API internally stops the channel, clamps parameters, or truncates arrays.
- You must write a defensible problem report to the API developers.

Do not use it for bugs already explained by SGStudio source, or for anything requiring firmware internals (the firmware is not in these binaries).

## Why Disassemble `gcc_x86_64/libh2api.so.*`

The repository ships the same API source built for several targets under `3rdParty\h2_api\lib\`:

| Binary | Internal symbols | Usefulness |
|---|---|---|
| `msvc_x86_64\h2_api.dll` (what Windows SGStudio loads) | Only ~100 exported C functions. Internal C++ helpers appear as bare addresses such as `call 0x18000f400`; the PDB is not shipped. | Ground truth for Windows, but unreadable on its own. |
| `msvc_x86_32\h2_api.dll` | Same as above, 32-bit. | Only if a Win32 build is in question. |
| `gcc_x86_64\libh2api.so.*` | Full `.symtab`, ~780 functions with C++ mangled names (`ahost::send_instr`, `bus_usb::send_inst`, `ensure_channel_stop`, …) and vtables. | **Primary analysis target.** |
| `gcc_aarch64\libh2api.so.*` (Raspberry Pi) | Also has `.symtab`. | Use when the bug is Pi-only; needs `CS_ARCH_ARM64`, which the bundled script does not implement. |

Choose `gcc_x86_64` because:

1. **Names.** Internal function and class names turn an anonymous call graph into readable intent (`load_ampcomp`, `download_ampcomp_ext`, `query_channelSetting`). The demangled signature also reveals parameter types.
2. **Same ISA as the Windows DLL.** Constants, struct offsets, loop bounds and opcode bytes found in the `.so` can be matched instruction-for-instruction in the x64 DLL, which proves the Windows build has the same logic.
3. **Simple calling convention.** SysV x86-64 passes arguments in `rdi, rsi, rdx, rcx, r8, r9` (floats in `xmm0..`), which makes argument tracking easy.
4. **Vtables are recoverable.** `_ZTV*` symbols plus `R_X86_64_RELATIVE` relocations resolve virtual dispatch (`call qword ptr [rax + 0x38]`) to concrete transport methods.

Always confirm the `.so` version matches the DLL in use (same `changelog.md` release) before trusting the mapping, then do the DLL cross-check in step 6.

## Setup

Use a throwaway dependency directory; do not install into the project or global Python.

```powershell
$d = Join-Path $env:TEMP 'h2dis'
New-Item -ItemType Directory -Force $d | Out-Null
python -m pip install --target $d pefile capstone pyelftools -q
$env:PYTHONPATH = $d
$s   = 'D:\development\skills\.github\skills\h2-api-binary-disassembly\scripts\h2_disasm.py'
$so  = 'D:\development\vsg2.0\3rdParty\h2_api\lib\gcc_x86_64\libh2api.so.2.1.42'   # adjust version
$dll = 'D:\development\vsg2.0\3rdParty\h2_api\lib\msvc_x86_64\h2_api.dll'
```

`$env:PYTHONPATH` does not persist between separate PowerShell tool calls; set it in every command.

Script: [h2_disasm.py](./scripts/h2_disasm.py)

| Command | Purpose |
|---|---|
| `python $s syms $so <substr>...` | Find exported and internal symbols (address, size, mangled name). |
| `python $s summary $so <sym>...` | Callees only, with the opcode byte of each `send_instr` packet. Start here. |
| `python $s dis $so <sym>...` | Full disassembly with call/jump targets named. |
| `python $s vtable $so <class>` | Resolve virtual slots, e.g. `bus_usb`, `bus_eth`. |
| `python $s pe $dll <export> [n]` | Disassemble a Windows export for cross-checking. |

## Procedure

1. **List the exact SGStudio call sequence first.** From source (for example `FancyDevice::configuration` → `applyCommonDeviceSettingsLocked` → `applyModeConfigurationLocked` → `fillWritebackProfileLocked`), write down every H2 call in order for the failing scenario, noting which calls are skipped by local caches such as `m_fixedCarrierPlanConfigured`. The bug usually lives between two of these calls.

2. **Summarize each H2 function involved.**
   ```powershell
   python $s summary $so channel_config_amplitude_compensation tx_config_output channel_start
   ```
   C exports keep their plain names. Internal helpers are mangled; find them with `syms`, e.g. `_Z19ensure_channel_stopPK7channel`, `_Z12channel_stopPK7channel`.

3. **Classify every function** into one of three kinds:

   | Kind | Signature in disassembly | Meaning |
   |---|---|---|
   | Send-only | Ends in `ahost::send_instr` → `abus::send_inst` → `bus_usb/bus_eth::send_inst`, no read afterwards | Return `0` means "bytes written", **not** "device applied". |
   | Host-cache query | Only `query_channelSetting` / `memcpy`, no bus call | Returns what the host last sent; cannot detect device-side failure. |
   | Round trip | Reaches `get_info_from_slavedevice` → `loop_inst` (send + receive) | Actually talks to the device and waits for a reply. |

   Confirm transport behavior with `vtable bus_usb` / `vtable bus_eth`, then `summary` the slot targets (`send_inst` vs `loop_inst` vs `get_inst`).

4. **Find hidden waits, stops and limits** with `dis`:
   - Busy waits: `ahost::delay_us` (spins on `system_clock::now`) or inline `steady_clock::now` loops; the immediate compared against the start time is the duration (e.g. `0x1e8480` ns = 2 ms).
   - `ensure_channel_stop`: queries the cached channel state and calls `channel_stop` (opcodes `0x11`, `0x1a`, 2 ms wait, `0x1b`) if running. Many config calls do this implicitly.
   - Clamps and truncation: `cmp r8d, 0x3e8` / `mov eax, 0x62` means "limit 1000, return 98 (`H2_WARNING_PARAMOUTRANGE`)".
   - Packetization: loop counters and chunk sizes (for example 43 packets × `0xee` bytes).

5. **Decode values with these rules:**
   - The device opcode is the byte stored at `[rsp]` (`mov byte ptr [rsp], 0x4e`) just before `send_instr`; `summary` prints it.
   - Return codes: `0x62` = 98 `H2_WARNING_PARAMOUTRANGE`, `0x63` = 99 `H2_WARNING_UNLEVEL`, `0xfffffff8` = -8 (bus/transport failure). Map other codes through `h2_api.h`.
   - `cmovne`/`sete` after a call show how internal return codes are merged; a warning can be overwritten by a later success.
   - Large `lea reg, [rdi + 0x18...]` offsets are fields in the `ahost` object (host-side caches and tables).

6. **Cross-check the Windows DLL.** Run `python $s pe $dll <export>` and confirm the same structure: the same first internal call (stop check), the same distinctive constants (opcode `0x5c`, chunk `0xee`, limit `0x3e8`, `ahost` field offset `0x180c010`), and the same loop shape. MSVC may inline helpers (for example `download_ampcomp_ext` is inlined into the export and uses QPC-based waits), so match constants and control flow, not call counts.

7. **Turn the findings into an experiment.** Static analysis only proves what the host sends and when. Validate on hardware with a single-variable test: add `QThread::msleep(N)` after the suspect call, bisect N, move the sleep before each later call to find the conflicting instruction, and try USB and ETH. Remove the temporary sleep afterwards.

8. **Write the report** (TaskLog first, then a vendor-facing message), keeping **observation** and **inference** separate:
   - Observed: SGStudio call sequence; per-call opcodes, waits and ACK/no-ACK, from disassembly; the device experiment result.
   - Inferred: what the firmware probably does (it is not visible in these binaries).
   - Questions for the vendor: completion and ACK semantics, effective time, required follow-up calls (for example `tx_config_ffm`), parameter limits, sign conventions, and `NULL`/zero-length handling.

9. **Clean up** the temporary dependency directory and any saved large outputs:
   ```powershell
   Remove-Item -Recurse -Force (Join-Path $env:TEMP 'h2dis')
   ```

## Known Findings (libh2api 2.1.42 / matching DLL)

Re-verify these against the current binary after any API upgrade.

| API | Internals | Implication |
|---|---|---|
| `ahost::send_instr` | CRC, then `bus->send_inst`; USB control/bulk write, ETH `asio::write`; no response read | All config calls are fire-and-forget. |
| `channel_config_amplitude_compensation` | `ensure_channel_stop` → `load_ampcomp` (max 1000 points, else truncates and returns 98) → 43 × `0x5c` packets, 1 ms busy wait **before** each packet, none after the last | Returns before the firmware applies the table; following instructions can race it. A 200 ms sleep after the call made User Flatness work. |
| `tx_config_ffm` | `ensure_channel_stop` → `0x4a` → `tx_query_ffm` (cache) → `device_check_power` | |
| `tx_query_ffm`, `tx_query_stream`, `channel_query_port/lo_mode/trigger` | Host cache only | Cannot confirm device state; "skip if equal" logic based on them never resends. |
| `device_query_clock` | Round trip via `get_info_from_slavedevice` | The first real device read in a full `configuration()`. |
| `channel_config_lo_mode` `0x3b`, `channel_config_port` `0x3e`, `tx_config_output` `0x4e`, `channel_config_trigger` `0x49`, `tx_config_stream` `0x4f`/`0x50` | Each starts with `ensure_channel_stop` | Reconfiguring a running channel implicitly stops it. |
| `device_config_clock` `0x37`, `device_config_trigger_out` `0x3d`, `channel_start` `0x12`, `channel_bus_trigger` `0x19` | Plain send | |

## Pitfalls

- Never name the script `dis.py`: it shadows the standard library `dis` module and breaks the `pyelftools` import with a circular import error.
- In Windows PowerShell 5.1, `Set-Content -Encoding utf8` writes a BOM, which Python rejects as `\ufeff` at line 1. Prefer piping a single-quoted here-string to `python -` or writing with `-Encoding ascii`.
- Large disassembly output may be offloaded to a temp file; use `summary` or `grep` instead of dumping whole functions.
- Do not claim firmware behavior from host disassembly. Say "the host sends X with no wait or ACK" and "therefore the firmware may still be processing", and label the latter as inference.
- Do not commit disassembly dumps or redistribute vendor binary internals outside the team; keep excerpts minimal in TaskLogs and vendor reports.
