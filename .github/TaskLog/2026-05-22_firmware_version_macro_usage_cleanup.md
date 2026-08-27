# 2026-05-22 firmware version macro usage cleanup

## Goal
- Check whether FPGA_VERSION / MCU_VERSION / BUS_VERSION / GNSS_VERSION are still used.
- Delete only redundant propagation if the root variables are still required.
- Keep Updater PACKET_VERSION synced with source defaults instead of sticking to old build-tree cache values.

## Findings
- Root CMake defines the four firmware version variables.
- src/CMakeLists.txt forwards them as global compile definitions to all first-party targets.
- Current source search shows only updater consumes these macros in code: src/plugins/updater/packetspec.cpp.
- src/plugins/updater/CMakeLists.txt already defines the same four macros locally for the Updater target.
- Updater dialog Version comes from compile-time PACKET_VERSION in updatedialog.cpp / PacketSpec::createNativeInfoPacket(), not from runtime package parsing.
- Top-level `set(PACKET_VERSION ... CACHE STRING ...)` lets existing build trees keep an old cached package version after the source default changes.
- Current repo scripts do not pass `-DPACKET_VERSION=...`, so the cache-backed default can be safely replaced with a source default plus an explicit override variable.

## Plan
- Keep the root variables because updater still needs them.
- Remove the redundant global forwarding from src/CMakeLists.txt.
- Validate by checking diagnostics for the touched CMake file.
- Replace the cache-backed PACKET_VERSION default with a non-cache source default and an explicit override cache variable.
- Remove any legacy PACKET_VERSION cache entry during configure so existing build trees resync automatically.
- Re-run Debug configure and verify the generated updater definitions use the source default value.
