# 2026-08-18 Shared-Chassis A/B Sequential Updater Analysis

## Scope

- Document the current shared-chassis A/B firmware update sequence from target discovery through application restart.
- Separate verified code behavior from hypotheses about intermittent updater failures.
- Identify diagnostic gaps, likely race windows, and a staged validation/remediation direction without changing runtime code.
- Add the durable analysis to `.github/KnowledgeBase/` and register it in the canonical index.

## Evidence Boundary

- Treat `src/plugins/updater/`, `src/plugins/core/`, and `src/maintenance/` as the active implementation because they are included by the current CMake files.
- Treat the external `updater` executable as a black box except for its packaged changelog, command-line contract, exit status, and captured output.
- Treat the reported probabilistic sequential-update failure as field evidence; do not claim a single root cause without target-level logs and timing evidence.

## Plan

1. Reconstruct A/B target selection, ordering, deduplication, and disconnect completion semantics.
2. Reconstruct maintenance argument parsing, process reuse, inter-target timing, failure gating, copy, and restart behavior.
3. Inventory what the application can and cannot observe about the external updater and shared chassis readiness.
4. Rank plausible internal failure points by evidence and explain how to distinguish them.
5. Define a focused logging and test matrix, then outline conservative remediation stages.
6. Create the KnowledgeBase document and update `Index.md`.

## Verification Level

- Static only. No build or runtime/device update is requested.

## Success Criteria

- The document gives an exact sequence for current-device A/B ordering and the maintenance state transitions.
- Every suspected failure point is labeled as inference and tied to a concrete observable or missing guard.
- The document includes enough target-level logging and timing guidance to diagnose the next field failure.
- Recommendations remain narrow and do not prescribe retries or fixed waits without validation evidence.

