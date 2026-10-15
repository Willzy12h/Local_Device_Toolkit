# Select, Review and Run Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans with independent scoped implementations and final review. Track actual evidence below.

**Goal:** Deliver the approved minimal setup/diagnostics layouts and one clear, logged review-before-change journey.
**Architecture:** Existing operations, plans and coordinator retain execution authority. New presentation and companion logging services format typed before/after evidence; WPF composes the select/check/review/run flow.
**Tech Stack:** Existing .NET 10 / WPF / PowerShell 5.1; no new dependencies.
**Spec:** ../specs/2026-10-15-select-review-run-design.md

## Global constraints
- Use this isolated checkout. Current engineer remains the user target; elevate only registered machine operations.
- Retain source provenance, schema/script/catalogue integrity pins, immutable evidence, 10-minute review and fresh before-state checks.
- No auto-selected dependencies, default queued changes, silent bundled UK actions, or rewriting already-correct settings.

## Review focus
- Missing registry value versus unreadable key: preview and logs must distinguish them.
- Selection/edit/navigation during review: reject stale approval, preserve explicit selections without hidden additions.
- Log failure before/after dispatch: do not start without durable log; stop after uncertain logging/result failures.
- Multiple UK settings: each changes only its target, with prerequisites explicit and unrelated preferences preserved.
- Compact table/details/review/log layout: controls remain reachable and no raw technical payload substitutes for clear summaries.

## Tasks
- [x] Atomic UK operations, fixed scripts and catalogue: add failure tests for target isolation/dependencies, implement fixed operation IDs and adapters, update reviewed pins, run setting/catalogue suites.
- [x] Typed review presentation: tests for current/expected/missing/unreadable/unknown values and exact source paths; implement friendly summaries without hiding raw details.
- [x] Per-run append-only text log: tests for unique files, dated append, readback, cancellation, injection/newline handling and failed writes; implement current-user companion log with no authorization role.
- [x] Integrated UI journey: setup task table and diagnostic list/details with independent selection; preflight selected rows; clear review grid; explicit run confirmation; same-file live output and verified results. Test already-correct skips, changed selections/state, no dispatch on cancellation/log failure and preserved evidence.
- [x] Full build/tests, native package/UI smoke and independent final review; update usage/status and create a reviewable PR. Record actual native/manual limits.

## Verification record
- Linux: 295 engine +115 adapter tests passed, no failures/skips. App/Worker build passed with zero warnings/errors.
- Native checkpoint `32c1cb7`: Windows/Linux CI passed 410 tests, PowerShell 5.1 checks, ZIP integrity and 21 extracted-application renders with a real dated check-only log.
- Native layout checkpoint `553be5d`: PR workflow 38707920153 passed the same checks. Actual normal/compact captures were inspected after fixing minimum column widths and single-line table labels. The hosted Windows Server is correctly refused for setting changes.
- The concurrent push run exposed a pre-existing output-cap fixture's 15-second PowerShell startup deadline. The fixture allowance is 45 seconds; the output-cap assertion and production deadlines are unchanged. The current workflow verifies the final revision.
- Independent integration review found no further Critical/Important defects after the before/after display fixes. PR: https://github.com/Willzy12h/Local_Device_Toolkit/pull/2.
- Real Windows 11 mutations, UAC/alternate credentials, vendor installers and physical accessibility remain the workstation acceptance gates in docs/WINDOWS-ACCEPTANCE.md.
