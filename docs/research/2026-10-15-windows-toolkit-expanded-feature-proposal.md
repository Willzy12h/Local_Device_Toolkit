# Expanded proposal: setup, diagnosis and approved fixes

**Status: proposed, not implemented.** The [written design](../superpowers/specs/2026-10-15-engineer-readiness-diagnostics-design.md) expands the earlier scope in response to the request for more features. It is ready for review before implementation planning.

## More useful setup checks

Expand from twelve broad checks to **24 independent checks**, including improved existing coverage:

- Windows support, work-account context, restart reasons, update history, recovery and drive protection.
- Free space, available disk-health evidence, manufacturer/firmware identity, present device problems, sleep capabilities and battery capacity.
- Defender protection and signatures as separate choices.
- Adapter/route, DNS, TCP, user proxy and system WinHTTP proxy as separate choices.
- Time-service status, Print Spooler state, selected printer queue, incident events and selected application verification.

Laptop, desktop/dock, remote and reassignment scenarios suggest relevant rows without ticking them. Network probes disclose their destination and use. Unknown evidence remains Unknown. New hardware/operational checks do not become invented M365 requirements.

## Six independent approved actions

1. Refresh the local DNS cache after reviewing a supported stale-cache hypothesis.
2. Request a Defender signature update through existing configured/default sources.
3. Set sleep timeout when plugged in.
4. Set sleep timeout on battery.
5. Set display timeout when plugged in.
6. Set display timeout on battery.

Each power value has its own choice and target. Show the current value, expected value, policy/context, account, effect and restoration limit before confirmation. Changing one timeout preserves the others. No automatic Never preset, power-plan switch, DNS-server substitution or Defender policy change.

Power changes use the original engineer's identity, including UAC; alternate-credential elevation cannot apply a scheme to the wrong user. DNS cache and signature updates have no exact undo. Action completed, state verified and symptom resolved are separate results.

## Cleaner diagnosis and handover

- Start with a symptom and incident time. Show up to three expanded relevant findings using **Observed / Why it matters / Next step**.
- Group repeated events while retaining evidence. Keep security findings, missing coverage and unknown required checks visibly signposted; show the rest in counted lists.
- Keep full permitted paths, values, timestamps and source details available. Do not clear logs or export secrets to reduce noise.
- Show deployment status as **Installed / Configured / Signed in / Licensed / Restart / Verified**, alongside package checks.
- Add independent handover rows for display, audio, microphone, camera, keyboard, touchpad, printer, VPN, application sign-in, backup and recovery-key retrieval.
- Provide separate Open settings buttons for supported user preferences; opening a page is not a verified change.
- Add ticket/client context, a concise handover summary and an explicit comparison between compatible runs.

Preserve **choose → check/review → confirm → run/verify**, a new dated log for every review/run, same-file changes/readback, fresh checks before dispatch and original-user/selective-UAC operation. All tasks remain reachable through search and All tasks. No Fix all or hidden bundled actions.

## Foundation and later work

Exact probe selection must replace the current control-based expansion. Version the matching worker/capture contracts, preserve historical evidence and keep protected plans/journals as the execution authority. For multiple selected power values, distinguish measured before-state from the reviewed expected state after earlier selected actions.

OEM firmware/driver installation, WinGet acquisition, temporary keep-awake, time resync, spooler start/restart and DISM/SFC work remain separate later scopes because their prerequisites and interruption behavior differ. No reset/wipe, enrollment changes or generic security/debloat preset is included.

Evidence: [original research](2026-10-15-windows-toolkit-recommendations.md), [78-source manifest](2026-10-15-windows-toolkit-sources.json) and [supplemental Microsoft documentation](2026-10-15-windows-toolkit-expansion-sources.json). Documentation establishes design support, not successful execution on a Windows device. Actual Windows 11/UAC, power/restore, offline/VPN, printing, battery and accessibility acceptance remain required.
