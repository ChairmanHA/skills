# Updater Maintenance Cleanup And Docs

## Scope

- Inspect current updater / maintenance handoff behavior, with focus on Linux forced shutdown fallback.
- Keep forced kill as a timeout fallback, but remove unnecessary second-stage waiting and duplicated defensive code in `ProgressDialog`.
- Refresh updater KnowledgeBase docs so they describe current behavior first and keep old mechanism discussion concise.

## Verification Level

Static.

No build or runtime update rehearsal was requested.

## Plan

1. Read active updater and maintenance source.
2. Simplify `src/maintenance/progressdialog.cpp` around parent-process shutdown, app restart lookup, and dead/repeated code.
3. Update `updater_firmware_update_mechanism.md` and `updater_mechanism_gap_and_remediation.md`.
4. Run targeted searches and `git diff --check`.

## Outcome

- Kept forced termination as the timeout fallback, but removed the extra post-kill wait.
- Removed duplicated wrapper/dead code in `ProgressDialog`, including the unreachable restart prompt branch and repeated application filename filters.
- Kept current Linux behavior as: wait for normal parent exit, then one `SIGKILL` fallback, then continue updater when the kill call succeeds or the process is already gone.
- Refreshed both updater KnowledgeBase documents to describe the current maintenance handoff, updater result gate, install-root preservation, and `.lic`-level restore behavior.

## Verification

- Targeted search confirmed removed symbols and messages no longer exist in `progressdialog.cpp` / `.h`.
- `git diff --check` passed.
- No build or runtime update rehearsal was run.
