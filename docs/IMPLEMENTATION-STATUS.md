# Implementation status

Reviewed on 14 October 2026; execution update 15 October 2026. This records inspected repository contents, not intended future behaviour.

## Created

| Deliverable | Evidence |
| --- | --- |
| Native WPF design proposal | [Design](superpowers/specs/2026-10-14-local-device-toolkit-design.md): source selection, five UI areas, current-user execution, per-action elevation, local controls, deployments and verification. |
| Versioned source reference | [30 Windows controls](reference/m365-windows-controls.json), compared with the exact upstream definitions and source SHA-256. |
| Review corrections | The design now defines IPC peer validation, protected staging, required-policy inspection failures, durable intent before dispatch, separate installer supervision and bounded history handling. |
| Implementation plan | [12 tasks](superpowers/plans/2026-10-14-local-device-toolkit.md) with file boundaries, interfaces, failure cases and verification commands. T1–T3 are implemented and tested; T4–T12 remain in progress or pending. |
| Repository entry point | [README](../README.md) identifies the current status and links the working documents. |

The initial design/reference commit is cbfa3ae. Review and planning changes are recorded in subsequent Git history. No remote push, pull request, merge, release or publication is claimed.

## Review findings and disposition

| Finding | Improvement made | Remaining proof |
| --- | --- | --- |
| The native application has not yet been built. | Added explicit repository status and a complete implementation task map. | Build the application and run its checks. |
| The elevation boundary did not specify how the real caller and worker are verified. | Added OS-observed peer PID/creation-time checks, original-SID derivation, local-only pipes, explicit ACLs, bounded messages and refusal rules. | Implement negative tests and native Windows/UAC validation. |
| Reusing the M365 read runner could terminate an installer mid-change. | Required separate mutation supervision, durable pre-dispatch intent and reconciliation for unknown outcomes. | Installer fixtures and authorised Windows acceptance. |
| An unavailable policy probe could be confused with an unmanaged device. | Required explicit unavailable/conflict states that block dependent changes. | Adapter and planner refusal tests. |
| Package inputs could change between review and privileged execution. | Required protected staging and revalidation of the entire approved file set. | Substitution, dependency, ACL and path-escape tests. |
| Damaged history could disappear or be interpreted as successful empty data. | Required strict schemas, per-record diagnostics, bounded paging and preserved healthy entries. | Malformed/unknown-version/access-denied history tests. |

## Pending implementation

| Capability | Plan tasks | Current state |
| --- | --- | --- |
| Solution, source imports and executable catalogue | T1 | Implemented; portable tests passed |
| Evidence persistence and corruption handling | T2 | Implemented; portable tests passed |
| Assessment, planning, expiry and sequencing | T3 | Implemented; portable tests passed |
| Local Windows inventory and probes | T4 | Not started |
| Authenticated UAC worker and protected journals | T5 | Not started |
| Current-user settings | T6 | Not started |
| Machine settings and selective restoration | T7 | Not started |
| Local application inventory/deployment | T8 | Not started |
| Native five-area WPF interface | T9 | Not started |
| Integrated local scripts and tools | T10 | Not started |
| History, reports and reconciliation | T11 | Not started |
| Portable packaging, CI and acceptance evidence | T12 | Not started |

## Validation actually performed

- Verified that the reference contains exactly CMP-WIN-001, CFG-WIN-001 through CFG-WIN-021 and APP-WIN-001 through APP-WIN-008.
- Compared all 30 complete control definitions with M365 commit 80f01a9482c0ae20e995f9df357c9fca9217f623, standards/2026.09.30.json.
- Verified that upstream file's SHA-256 is 124a033454385b262960db2a6f61392ee3e64ad8abb1a6f0d8276db29e21302f.
- Reviewed the design and plan against the requested native UI, current-user execution, selective administration, local deployment and cross-checking workflow.

The portable product suite now has 24 passing tests (zero failures/skips), covering catalogue, evidence, planning, shared queue and result guards. Engine warning-as-error builds pass. Native UI, UAC, package extraction and real-device checks have not run; this cloud host is Linux.

## Next step

The user’s pasted request authorises direct implementation and testing of the full solution. Continue with T4–T12 using the execution ledger; the completed branch requires an independent review.

Implementation is under way with the executing-plans and test-driven-development skills. Setup and diagnostics use shared task definitions, a shared queue and common evidence services. No further plan-approval pause is pending.
