# Stop Channel Before HTRA Business Configuration

> Superseded on 2026-08-28 by `2026-08-28_revert-channel-stop-and-ffm-dedup.md`; the implementation described here was reverted for the new requirement.

## Scope

- Add one shared `FancyDevice` guard that calls `channel_stop()` and validates the H2 status.
- Invoke the guard before every HTRA TX business configuration transaction: generic mode configuration, CW/Playback/Streaming sweep configuration, and Playback trigger configuration.
- Do not stop the channel for data-plane operations such as waveform upload, realtime IQ transfer, queries, or device-only settings.
- Preserve the existing serialized `FancyDevice::m_mutex` ownership and existing error propagation.

## Success Criteria

- No TX business configuration reaches `tx_config_*`, stream/trigger/output configuration, or `channel_start()` before a successful `channel_stop()` in the same locked transaction.
- A negative `channel_stop()` result aborts that configuration and reaches the existing caller error path.
- H2 warning return codes retain the existing warning-as-success behavior.
- `SweepCw -> FixedCw` explicitly stops the active sweep before configuring CW.

## Plan

1. Add a private locked stop helper to `FancyDevice`.
2. Apply it at the configuration transaction roots, avoiding duplicate calls inside nested helpers.
3. Update the H2 API usage document so its `channel_stop()` contract matches the implementation.
4. Perform static call-path and diff checks; do not build or run unless explicitly requested.

## Verification Level

`static`

## Result

- Added the shared locked stop guard and applied it to all TX business configuration roots.
- Added complete stop/reconfigure/rearm handling to the realtime stream sample-rate fallback.
- Updated the durable H2 API usage contract.
- Static call-path audit and `git diff --check` passed; build/runtime verification was not requested.
