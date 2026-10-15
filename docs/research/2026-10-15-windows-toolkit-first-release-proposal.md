# Proposed first release: setup readiness and quiet diagnostics

**Historical scope:** the user's subsequent request for more features is addressed in the [expanded proposal](2026-10-15-windows-toolkit-expanded-feature-proposal.md) and its linked written design. Both remain proposals awaiting design review.

**Status:** for scope review; no new product features or repairs have been implemented. Based on the [full research](2026-10-15-windows-toolkit-recommendations.md) and [78-source evidence manifest](2026-10-15-windows-toolkit-sources.json).

## Recommended direction

Add useful setup/handover evidence and symptom-focused diagnostics to the existing select → check → review → confirm → run flow. Each check and action remains independently selectable. Laptop/desktop/remote/redeployment scenarios only suggest relevant tasks; they never tick or run a bundle. The same underlying probes serve both workspaces.

Alternatives: **read-only first** is simpler but leaves all correction manual; **a broad repair suite** adds disruption, recovery and long-running process requirements. The focused release below is recommended.

## Twelve proposed checks

| Setup and handover | Diagnosis and applicability |
| --- | --- |
| Windows edition, release and dated support status | Storage capacity |
| Management and original-user context | Available disk-health evidence |
| Locally observed update/restart state | Power, battery and sleep capabilities |
| WinRE recovery readiness | DNS for the affected hostname |
| BitLocker protection/suspension and recovery-verification state | TCP to the explicitly selected service/port |
| Defender mode, protection and signature freshness | Incident-related events in a selected time window |

Support status is separate from the client's approved build. Local policy, MDM URLs and encryption status do not prove tenant compliance or recovery-key retrieval. Missing data stays unknown. Probes that make network requests must say so before selection/run; update history is not an implicit online update scan.

## Two proposed approved actions

- **Refresh this device's DNS cache:** only after review of a supported stale-cache hypothesis. Disclose that it clears the whole local client cache and has no exact undo. Preserve resolver/VPN configuration; repeat the affected query and endpoint test afterward.
- **Request Defender signature update:** only when Defender is applicable/active and source policy is understood. Use its configured/default source order, disclose network use and do not override policy. Read fresh signature/protection state; unchanged version may already be current.

Show scope, account/UAC, expected impact, network use and rollback limits before confirmation. Preserve fresh-state checks, expiry, replay guards, protected journals and the same dated text log. Separate **action completed**, **expected state verified**, and **symptom resolved**. No automatic retry or green “Fixed” based only on process completion.

## Quiet engineer view

Show up to three expanded findings with **Observed / Why it matters / Next step**. Keep all remaining findings in a counted list, with required unknowns and security/incomplete checks visibly signposted. Group repeated events by provider, relevant signature, resource and time; preserve original record IDs and evidence.

Expected DCOM patterns belong in explained background details only when the documented identifiers match. Event 41 alone does not identify root cause. Full source paths, timestamps and raw details remain copyable; exports minimize sensitive data.

## Later stages

OEM firmware/driver installation, optional WinGet acquisition, temporary setup keep-awake, independent power/preferences, guided default-app/startup/Storage Sense choices, time resync, spooler actions and evidence-led DISM/SFC repairs. Firmware, reset/wipe and policy/security weakening are outside the first release.

The implementation needs exact probe selection: today's control-based capture can expand one selected row into multiple checks. It also needs deterministic diagnostic interpretations, dated reference profiles and nonsecret typed observations. Existing M365 controls and execution authority remain intact.

Scope approval would allow a written implementation specification to be prepared. It would not authorize device changes or silently accept later-stage repairs. Real Windows 11/UAC, offline/VPN, Defender/third-party AV, laptop/desktop and native compact/accessibility acceptance remain necessary before release.
