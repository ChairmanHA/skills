---
name: sgstudio-overdefense-review
description: Use when reviewing or simplifying an SGStudio diff, TaskLog plan, or design for over-defensive code — guards, retries, deferrals, locks, temporary-file protection, change detection, assertions, exception wrappers, or per-stage error texts — or when the user says a change is over-defensive. Gives a per-branch reachability review and SGStudio cases of both over-defense and checks that must stay.
---

# SGStudio Over-Defense Review

The rules live in `AGENTS.md` (Simplicity And Defensive Coding). This skill applies them to a concrete diff or plan. It bounds the fix, never the search: a real, reachable defect is always reported.

## Review procedure

For each new branch, guard, lock, retry, state field, wrapper, check, or error text in the diff or plan:

1. **Trigger.** Name the concrete input or state that reaches it, and how it arrives: UI, SCPI, Minibar IPC, device/API result, worker thread, file or configuration, or a field report.
2. **Reachability.** Check the trigger against current code and the concurrency model in `AGENTS.md`. Does a supported workflow actually produce it here? Reading the call sites decides this; "an API could in principle" does not.
3. **Recovery.** What does the user or caller get differently: a correct result, a visible error, preserved data? If it only logs and continues, or reports through a channel nobody reads, there is no recovery.
4. **Owner.** If it is needed, is it in the component that owns the state, and does it reuse an existing mechanism (result type, request correlation, lease, scope guard)?
5. **Verdict.** Keep it only if steps 1 to 3 have concrete answers. Otherwise delete it and, if the risk is worth remembering, record it as a known limitation in the TaskLog.

Report the verdict per item in one line each: kept (trigger + recovery) or removed (missing answer). Do not produce a scoring table, and do not repeat the review on code that has not changed since it passed.

## Cases

Examples for calibration, not a checklist. A real finding is not dismissed because it resembles one.

### Over-defense: remove these shapes

**SGS-C-001 · Worker thread that only waits for the GUI thread**
SCPI parsing once ran on a worker thread, and each product command called back into the GUI thread with `BlockingQueuedConnection`. The worker added no parallelism, because every handler had to run on the GUI thread. At exit the GUI thread joined the worker in `~Parser()` while the worker waited on the GUI thread, a deterministic deadlock. The fix was to delete the worker and run transport, parser and handlers on the GUI thread in FIFO order. See KnowledgeBase `app_shutdown_exit_flow.md` §3.4.
*Shape:* concurrency machinery added for work that is serialized anyway, which creates the race or deadlock it was meant to manage.

**SGS-C-002 · Change detection across a modal wait**
A save flow recorded parameters, opened a modal confirmation and file dialog, then forced a retry if the parameters or device had changed meanwhile. The modal dialog blocks the UI that edits those parameters, so the retry branch had no supported trigger. A value snapshot taken before the dialog is enough.
*Shape:* defending a window that the modal loop already closes.

**SGS-C-003 · Deferral queue for requests that cannot arrive**
The same flow queued waveform-generation requests that arrived during export and replayed them afterwards. Export was modal and nothing issued generation requests during it, so the queue added a state field, an interception branch and a replay path for no input.
*Shape:* new state and a second execution path built for a hypothetical overlap.

**SGS-E-001 · Temporary-file protection nobody asked for**
Writing through `QSaveFile` to preserve the old file until commit, plus test writes, flush and seek to probe writability before the real write. The user had not asked for overwrite protection. Opening the target file and checking the actual open, write and flush results reports the same failures.
*Shape:* probing for a failure instead of checking the real operation's result.

**SGS-O-001 · Wrappers and texts per stage**
A generic `catch (...)` around the whole export, and a separate translated message for each internal stage (prepare, generate, write, finalize). The user cannot act differently on most of them. One general failure text, plus specific texts for the conditions the user can fix (disk full, out of memory), with details in the log.
*Shape:* error granularity that tracks the code structure instead of what the user can do.

**SGS-O-002 · Guards on established internal state**
Null checks or `Q_ASSERT(ptr)` in getters, UI refresh paths, or after constructor initialization of members that the owner always creates. They have no trigger, and a crash at the real dereference already points at the defect.
*Shape:* a guard whose only justification is "it should exist".

### Required: keep these even though they look defensive

**SGS-K-001 · Reentry through a nested event loop**
SCPI handlers run on the GUI thread, and `ACTION_PRESET` may open a modal confirmation, which runs a nested event loop while a command is still executing. New socket data can arrive inside that loop. The parser's FIFO and `mDraining` flag make nested arrivals queue instead of re-entering libscpi. This is reachable through a supported command, so the guard stays.
*Distinguish from SGS-C-002:* here the nested loop really does deliver input to the guarded state.

**SGS-K-002 · Correlating asynchronous results**
Minibar buttons update optimistically and send IPC requests; replies and device writebacks arrive later and may be stale after a newer click. Per-resource latest-intent request correlation discards stale replies. Rapid clicking is supported use, and the correlation reuses the owner's protocol instead of adding locks. See the `minibar-optimistic-enabled-workflow` skill.

**SGS-K-003 · Checks the requirement names, and real API results**
WAV size limits and free-disk-space checks requested by the feature, and the actual return values of SDK calls and `QFile` open/write/flush. These are the operation's own results, not probes; ignoring them would report success for a failed save. Preserve API error codes as reported (see `AGENTS.md` Ownership And Contracts).

**SGS-K-004 · Shared worker state**
State touched by both an explicit worker (DeviceManager status/IO thread, generation or export worker) and its owner on the GUI thread needs a defined handoff: queued signals, the existing lease or lock, and a shutdown order that stops new work before releasing dependencies. Removing it because "races are rare" is wrong; the threads are real.

## Adding a case

Add a case only when it comes from a real SGStudio change, review, or field report. Give it an ID (`SGS-C` concurrency, `SGS-E` edge case, `SGS-O` overbuild, `SGS-K` keep), and state what was done, why it was or was not reachable, and the proportionate version. Add a `K` case whenever a review nearly removed something that was actually needed.
