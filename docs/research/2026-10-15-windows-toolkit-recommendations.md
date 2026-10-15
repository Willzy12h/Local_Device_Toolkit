# Windows setup, deployment and quiet diagnostics: recommendations

Research date: 15 October 2026. Repository reviewed: `2fe1e4d`. This is a research and scope proposal, not an approved implementation specification. The new checks and repairs below are **not yet implemented**. Existing toolkit behavior is unchanged.

**Expanded proposal:** the subsequent request for more features is covered by the [written readiness and diagnostics design](../superpowers/specs/2026-10-15-engineer-readiness-diagnostics-design.md) and [expanded feature summary](2026-10-15-windows-toolkit-expanded-feature-proposal.md). Their 24-check/six-action proposal replaces the initial twelve-check/two-action release recommendation below, subject to design review. This document retains the original research and its distinctions between initial and advanced work.

**Recommendation:** extend the shared task engine with capability-based setup checks and symptom-led diagnostics. Ship a focused first release with two individually approved diagnostic actions. Keep disruptive repairs in a separate advanced stage.

The research draws on **78 successfully fetched official Microsoft and OEM sources**. The [source manifest](2026-10-15-windows-toolkit-sources.json) records URLs, titles, retrieved dates, published date fields, extracted-text hashes, checked short excerpts and unsuccessful candidate URLs. Microsoft/OEM statements are cited; priority, layout, ranking and release scope are engineering recommendations. No target Windows device or tenant was tested during this research.

## 1. Setup scenarios worth supporting

A scenario should suggest relevant rows, **never tick options or apply a bundle**. Setup and diagnostics must reference the same canonical probes and actions. Preserve selections visibly when changing views; do not convert a diagnostic check into a change.

| Scenario | Checks and decisions to add | Boundary |
| --- | --- | --- |
| New managed laptop | Approved Windows edition/release, battery and sleep capabilities, OEM/dock drivers, recovery readiness, encryption/protection, update/restart state, camera/microphone/audio and handover tests. | Use the organization-approved Autopilot/device preparation/manual route. Local success does not establish Intune compliance or escrow. |
| New desktop or docking workstation | Display arrangement/scaling, wired network, audio, peripherals, OEM/dock firmware and role-specific AC power. | A connected charger does not make a laptop a desktop. Continuous operation must be an explicit role requirement. |
| Existing device or feature upgrade | Support lifetime, compatibility/safeguard guidance, storage, failed update, recent changes and a fresh post-restart check. | Supported by Microsoft and approved by the client are separate questions. Do not force the highest build number. |
| Remote worker or intermittently offline device | VPN/internal service tests, offline media completeness, update-source availability, recovery access and offline-files expectations. | Successful public internet access does not prove corporate connectivity. Avoid public-DNS substitutions. |
| Reassignment or redeployment | Backup attestation, WinRE status, current join/management evidence, old-owner/primary-user follow-up, approved reset route. | Autopilot Reset retains join/management while removing personal files/apps/settings; it is not a general wipe or proof of sanitization. |
| Shared, kiosk or restricted device | Role, management owner, sign-in model, permitted apps and recovery requirements. | Use a separately approved provisioning design. Current-engineer preferences are not settings for every future user. |

Windows documents distinguish classic Autopilot, device preparation and reset behavior. Capability inventory can use chassis information alongside actual battery/sleep/device capabilities; allow the engineer to correct an uncertain classification. The existing Windows 11 x64 execution boundary should remain; Server, ARM64 and other unsupported targets must not become writable simply because they can be inventoried. [Provisioning comparison][autocompare] · [Reset behavior][autoreset] · [Chassis data][chassis]

## 2. Highest-value setup and deployment additions

**P0:** establishes whether results and subsequent actions can be trusted. **P1:** improves coverage after that foundation. **P2:** useful specialist work.

| Priority | Addition | What the engineer should see | Initial automation |
| --- | --- | --- | --- |
| P0 | Edition-aware Windows support | Edition, release, build/UBR, architecture, approved client target, dated Microsoft support end and reference date. | Read and compare against a versioned reference. Refresh online only when selected. A numeric build floor alone is not lifecycle or upgrade eligibility. [Source][release] |
| P0 | Management and user context | Original user, locally observed join state, policy evidence, expected management route and external checks still needed. | Context-correct read; stop conflicting local changes. `MdmUrl` alone does not prove MDM enrollment; elevated user/SSO evidence can describe the wrong account. [Source][dsreg] |
| P0 | Update/restart handover | Observed reason, affected update or installer, evidence time, restart decision and post-boot verification still outstanding. | Collect evidence and open Windows Update. Preserve a run for later reconciliation; do not restart or install updates implicitly. A single registry flag is not a universal restart detector. [Restart guidance][updates] · [Safeguards][safeguard] |
| P0 | Recovery and protection readiness | WinRE status; BitLocker conversion, protection and suspension separately; protector type/ID; escrow verification state. | Nonsecret reads. Record external recovery verification separately. Encryption or successful backup-command execution does not prove the matching key can be retrieved. [BitLocker][bitlocker] · [Recovery][bitrecovery] · [WinRE][winre] |
| P0 | Storage capacity and health limits | System-volume free space, trend when available, declared deployment requirement and hardware-health observation/availability. | Read only. No universal free-space percentage or missing SMART data should produce a false pass. Hardware concerns take priority over repair/stress operations. [Storage counters][storagereliability] |
| P1 | OEM and dock readiness | Exact manufacturer/model, current BIOS/driver versions, supported vendor tool and selected recommendations. | Begin with inventory/guidance. Dell Command Update, HP Image Assistant and Lenovo tools have different platform/tool requirements. A vendor recommendation is not approval to install everything. [Dell][oem-dell] · [HP][oem-hp] · [Lenovo][oem-lenovo] |
| P1 | Temporary setup keep-awake | Reason, duration, AC/battery applicability, request state and expiry. | Prefer a scoped power request during an explicitly selected setup activity over permanently setting AC sleep to Never. Clear it on completion/cancellation; respect deliberate Sleep and platform limits. Requires its own lifecycle design. [Power requests][powerrequest] |
| P1 | Reviewed app acquisition | Exact package/source/version/scope, dependencies, licensing, offline suitability and installer identity. | Optional WinGet discovery/download feeding existing hash/signer/media approval. No `upgrade --all`, arbitrary overrides, hash bypass or hidden dependency installation. Downloaded bootstrapper does not imply offline capability. [Install][wingetinstall] · [Download][wingetdownload] |
| P1 | Application deployment cross-check | Installed identity/version, actual user/machine scope, restart/guard state, configuration, sign-in, activation and enrollment as separate results. | Extend existing verification. Installed is not configured, licensed or enrolled. Check App Installer/dependencies/offline-license prerequisites without assuming Store or WinGet availability. [App Installer][appinstallerupdate] · [MSIX][msixsideload] |
| P1 | Clear user handover | Open items, verified items, manual tests, affected account, client exceptions and next owner. | Generate a short report with separate “Local checks complete” and “Ready for handover” states. Required unknown/manual items prevent the latter. |
| P2 | Recovery, firmware and redeployment operations | Backup, power, recovery-key route, supported vendor process, interruption and reboot consequences. | Guided initially. Do not add generic firmware flashing, reset/wipe or automatic BitLocker suspension to the first release. |

Firmware integrations must use approved, versioned vendor tooling and documented scan/install behavior. HP explicitly distinguishes recommendations that are automatically installable; generic or unsupported-platform recommendations must not be treated as exact compatibility proof. Vendor minimum power and recovery prerequisites override any toolkit heuristic.

## 3. Windows changes users commonly need

Expose these as separate choices, with current/expected values where reliable. When Windows requires interactive user choice, open its supported Settings page and record that verification is still required. Navigation is not a completed fix. [Settings URIs][settings]

| Area | Recommended options | Important distinction |
| --- | --- | --- |
| Power | AC sleep, battery sleep, AC display timeout, battery display timeout; lid/docked behavior as supported choices. | Each value independent. Preserve the active power plan and unrelated values. Role-approved settings, not a universal performance preset. |
| Display and accessibility | Monitor arrangement, scaling, text size, pointer, touchpad, keyboard accessibility and audio-device checks. | Prefer interactive Settings and a user test; copying the engineer's preference to an absent end user's profile is not valid handover. |
| Default apps | Browser and PDF defaults, verified by the actual user. | Use supported user choice or a separately designed provisioning policy. Do not edit protected `UserChoice` associations. Image-imported defaults have a different scope. [Association guidance][defaultapps] |
| Startup and notifications | Review one startup app or notification source at a time; welcome tips and Spotlight as distinct policy-aware preferences. | Start with Settings/guidance. Do not indiscriminately disable VPN, protection, accessibility or OEM services. [Settings][settings] · [Spotlight policy][spotlight] |
| Storage Sense | Enabled state, cadence, temporary files, Downloads retention, Recycle Bin retention and cloud-file dehydration separately. | Read values, not key presence. Downloads deletion and cloud dehydration must never be hidden consequences of “Enable cleanup.” Storage policy documents explicit never/zero choices. [Storage policy][storage-policy] |
| Region and sign-in | Keep the existing independent language/keyboard/format actions; show timezone versus actual clock synchronization separately. | Client/location expectations may differ. Do not silently change the existing M365 baseline or another user's profile. |
| Privacy and communications | Camera/microphone permission review, camera/audio test, approved printer and sharing needs. | User- and role-specific guidance. Do not enable broad network discovery, legacy protocols or remote access to make a device appear ready. |

Reconsider three existing labels/default assumptions without silently changing their approved baseline: AC sleep Never is not a universal laptop recommendation; disabling Fast Startup trades startup behavior for troubleshooting predictability; enabling long paths only helps participating applications. Consumer Copilot package removal is not equivalent to blocking every Copilot service, Edge entry point or Microsoft 365 feature. [Fast Startup][faststartup] · [Long paths][longpath] · [Current Copilot policy][copilotpolicy] · [Microsoft baselines][baseline]

## 4. Diagnostics: quiet, but never incomplete

**Start with the symptom and when it happened.** Offer network/VPN, updates, slow startup/performance, battery/sleep, printing and crashes. Scenario selection suggests checks; the engineer still ticks each check. Label slow scans and outbound tests before running them.

Recommended ordinary view:

> **Internal site unavailable** — name resolution needs review.
>
> **Observed:** the selected name did not resolve through the current VPN/DNS context.
>
> **Next step:** review the resolver and route evidence. Cache refresh is available only if the stale-cache hypothesis is supported.
>
> **More evidence:** exact name, adapter, source, query result and timestamps.

This is an illustrative layout, not a measured device finding. One failed query is not proof of a cache fault.

Use these presentation rules:

1. Show up to three expanded, symptom-relevant findings by default. Keep all additional findings in a counted list. **Required unknowns, incomplete checks and security-relevant failures remain visibly signposted**, regardless of ranking.
2. Each finding answers: **what was observed; why it matters; what to do next**. Show capture time, context and confidence as *confirmed observation*, *possible explanation* or *not established*. Do not invent numeric confidence percentages.
3. Group repeats by provider, event ID, relevant event-data signature, affected resource and selected time window. Show first/last occurrence and count; retain channel and original record IDs. Never merge different causes because their event numbers match.
4. Put unrelated historical events and documented expected patterns in **Background details (N)**. Explain why each was classified. Do not clear Windows logs or discard original evidence.
5. A DCOM 10016 event is only a known expected pattern when its documented event-data identifiers match. Do not suppress all 10016 events or rewrite DCOM permissions. Event 41 means an unexpected shutdown occurred; it is not a diagnosis of the power supply or another root cause. [DCOM guidance][dcom10016] · [Event 41][event41]
6. Use a selected incident window, with visible presets such as last hour/day/week and a custom range. These are design choices, not Microsoft diagnostic thresholds. Show log retention, disabled channels, missing intervals, access denial, pagination and any collection limit. “No matching evidence in available coverage” is not “device healthy.” [Bounded event queries][getevent]
7. Share one capture between setup and diagnostics when context/freshness match. A newer observation invalidates a stale conclusion. Preserve all original before/after evidence and one dated log for each check/review/run.
8. Keep raw details searchable/copyable and available in reports. Default exports minimize account, tenant, network, document and crash data. Never collect recovery passwords, Wi-Fi keys, tokens or credentials. Sensitive MDM/dump reports require separate selection and export review. [MDM data][mdm] · [WER][wer]

### Recommended diagnostic coverage

| Symptom | Focused checks | Recommendation boundaries |
| --- | --- | --- |
| Slow startup/performance | Uptime/restart context, short CPU/memory/disk sample, startup entries and incident-correlated app errors. | One high sample is not a bottleneck diagnosis. WPR is a separately selected advanced capture with duration/data scope. No registry “optimizer” or blanket startup disable. [Counters][perf] · [WPR][performancewpr] |
| Low space/storage errors | Affected volume, actual free space, supported disk-health data and relevant recent storage events. | Missing counters/RAID/USB limitations stay unknown. Backup/escalation takes priority when hardware failure is plausible. No automatic file cleanup or offline repair. [Storage evidence][storagereliability] |
| Network/DNS/VPN | Selected adapter, address/route/resolver, proxy/VPN context, affected hostname and explicit destination/port test. | Separate DNS, TCP, application and authentication findings. ICMP failure alone is insufficient; TCP success is not an application sign-in test. Preserve split DNS and third-party VPN limitations. [DNS][resolve] · [TCP][nettest] · [VPN][vpnconnection] |
| Updates | Specific failed update/error/time, update policy context, servicing state and observed restart reasons. | No blanket update-service/cache reset. Generated Windows Update logs are snapshots, not a live source. [Update diagnostics][update] |
| Battery/sleep/wake | Battery capacity/history, supported sleep states, last wake, active requests and selected SleepStudy where supported. | No invented universal battery-health pass threshold. SleepStudy applies to Modern Standby; powercfg energy analysis needs appropriate idle conditions. Do not force S3 or disable hibernation as a generic fix. [Powercfg][powercfg] · [SleepStudy][modernstandby] |
| Printing | Selected printer/queue visibility, local spooler, job progress and relevant errors. | A local service restart affects all local printing. Do not delete jobs, drivers or spool files by default. Inaccessible job state is not an empty queue. [Printers][getprinter] · [Jobs][printjob] |
| Crashes/app failures | Incident timeline, application/signature, bug-check and dump metadata where available. | Metadata is not stack analysis; Event 41 is not root cause. Do not upload dumps automatically. [Event 41][event41] · [WER][wer] |
| Work account/policy | User-context join/SSO observations; distinct elevated device facts; relevant policy evidence. | MDM URLs and local settings do not prove enrollment, compliance or current tenant assignment. `gpupdate` changes state and is not a read-only diagnostic. `dsregcmd` DeviceAuthStatus can perform an online check: disclose that before running it, or use local APIs for an explicitly offline-only capture. [Join evidence][dsreg] · [Policy reports][gpresult] |
| Windows corruption | Separate DISM CheckHealth, ScanHealth and SFC verify-only options, each with clear cost and privilege. | CheckHealth reads recorded corruption; ScanHealth scans; RestoreHealth and SFC scannow repair. Do not label repair commands as checks. [DISM][dism] · [SFC][sfccommand] |

## 5. Automated actions: proposed approval contract

Automation means **recommend, select, review, confirm, act once, verify**. It does not mean automatic remediation during a check. No “Fix all.” Changes to selection, target, network context, policy or relevant evidence require a fresh review.

| Stage | Individually selectable action | Required evidence, impact and verification |
| --- | --- | --- |
| First release | **Refresh this device's DNS cache** using the fixed `Clear-DnsClientCache` operation. | Require an engineer-reviewed stale-cache hypothesis and captured affected-name/resolver/VPN context; differing round-robin answers alone are insufficient. Explicitly disclose that the **whole local client cache** is cleared. It cannot be restored exactly. Do not change DNS servers or server caches. Requery the same name/context and retest the original endpoint; success of the command alone does not establish a fix. [Cmdlet][flush] |
| First release | **Request Defender signature update from configured sources** using bare `Update-MpSignature`. | Active Defender and applicable policy/source checks required; passive/third-party-only cases get guidance. Disclose network use. Do not force `-UpdateSource` or change fallback policy. When no fallback order is configured, Windows can use its documented Microsoft defaults; show that distinction and require authorization for the resulting network use. Read fresh mode, protection and signature time/version afterward. An unchanged version may already be current; report the observed result, not an invented upgrade. No exact rollback. [Update][mpsignature] · [Readback][mpstatus] |
| Advanced stage | **Resync with the configured Windows time source** using local `w32tm /resync`. | Source/service/policy and clock-impact review required. It can step the clock; no exact rollback. Do not configure a new source, use rediscovery or claim an NTP probe proves service sync. Time changes also affect review expiry and timestamp ordering, requiring explicit engine design. [Time service][w32time] |
| Advanced stage | **Start local Print Spooler**, or a separately selected **restart**. | Expected service role, policy and queue/job impact must be established. Starting can resume queued printing; restart disrupts all local printers. Unknown activity blocks the default path. No startup-type change, `-Force`, queue purge or repeated restart loop. Verify printer/job progress, not just Running. [Printing][printing] |
| Advanced stage | **Repair the Windows component store** using approved DISM parameters/source. | Confirm repairable corruption; review administrator rights, servicing state, disk/power and repair-source/network use. `/LimitAccess` blocks Windows Update fallback, not all network access to a selected share. Source language/servicing mismatch can fail repair. No generic undo or silent restart. [Repair][dism] · [Source requirements][dismsource] |
| Advanced stage | **Repair protected system files** using `sfc /scannow`. | Confirm violations and address component-store issues first where indicated. This writes protected files, consumes I/O and has no generic undo. Preserve CBS outcome, reverify when appropriate and retest the original symptom. Do not kill servicing because the UI timer expired. [SFC][sfccommand] |

For every action, show account/UAC, exact scope, current evidence, expected result, policy/availability blockers, disruption, network use and rollback limits before confirmation. Write pre-change evidence durably, append actual actor/result/readback to the same dated log, retain the protected journals and stop further actions on uncertainty or logging failure. Reversible settings restore their exact recorded value only if unchanged since the toolkit wrote it; cache, update and repair actions must not advertise an undo they cannot provide.

Use separate result fields: **Action completed?**, **Expected state verified?**, **Original symptom resolved?** A command can complete while the symptom persists, or require a later user test. Never collapse those into a green “Fixed.”

## 6. Proposed first release and implementation implications

The recommended scope is a **focused balanced release** with twelve independently selectable checks: Windows support; management context; locally observed update/restart state; WinRE; BitLocker; storage capacity; available disk health; power/battery capabilities; target DNS; target TCP; Defender mode/signatures; and incident-related events. Include quiet presentation, handover reporting and the two first-release actions above. Each check/action remains independently selectable. OEM installation, WinGet execution, time/service/servicing repairs, reset/wipe and a general optimization engine are outside this first release.

Alternatives: a **read-only release** is simpler but leaves every corrective step manual; a **broad repair release** offers more interventions but substantially expands disruption, long-running process, recovery and acceptance requirements. The balanced release gives useful action without presenting a universal repair button.

Repository changes needed after scope/design approval:

- Reuse existing `ILocalOperation`, reviewed plans, original-user/UAC dispatch, immutable evidence and `ReviewRunService`. Add only closed, typed operations; no arbitrary commands or registry editor.
- Add a deterministic diagnostic interpretation layer that cites observation IDs, incident window, rule version and limitations. Keep symptom explanations separate from M365 compliance assessment. No cloud AI/log upload is necessary.
- Add exact probe targeting. Today `WorkbenchSession.CaptureAtOriginAsync` selects control IDs and `WindowsDeviceInspector` expands all checks for those controls. One selected readiness row can therefore read other checks. The expansion needs an explicitly versioned request/worker contract so selected diagnostic probes stay bounded and costly/network checks are never hidden.
- Preserve the 30 sourced M365 controls. Supplement them with clearly identified local operational checks; do not label an added hardware check as a new M365 requirement.
- Replace presence-only observations for Storage Sense/LAPS/OneDrive/Edge with useful typed values where supported, retaining manual verification limits. Enrich BitLocker beyond its protection Boolean. Retain honest unavailable results while improving third-party AV guidance; do not infer protection from a display name or undocumented status bits.
- Add an approved, dated release/reference profile and capability applicability. Separate client preferences, Microsoft support, local observations and external attestations. Missing reference/unsupported context disables a recommendation rather than inventing an expected value.
- Keep existing short action outcomes from being reused for unbounded DISM, firmware, reboot or long-lived keep-awake activities without designing their lifecycle, cancellation and reconciliation first.

Acceptance must cover known/unknown/stale values, one task/one selection, no hidden reads or repairs, locale parsing, bounded/paged events, exact expected-pattern matching, security failures outside the top three, secrets excluded before logging, UAC cancel/alternate credentials, VPN/context drift, source/network restrictions, before-write log failure, uncertain outcomes, native normal/compact UI, and real Windows 11 hardware. Test active Defender and third-party AV separately; laptop S0/S3/desktop/VM, dock, offline and centrally managed cases must not share a fabricated universal expected state.

## 7. Research limits and documentation conflicts

All new features here are proposals. Linux documentation research does not establish cmdlet availability, privilege or behavior on a supported Windows 11 workstation; some PowerShell references use Windows Server module views. No new automated fix or target-device mutation was run.

Release data is deliberately not hardcoded into this report's proposed logic. Current Microsoft pages distinguish edition support and hardware-specific releases; approved client target and lifecycle remain separate. The fetched Autopilot comparison page contains both a 25-essential-app capability statement and a decision row referring to more than 10 apps. Do not encode that disputed limit. Older Copilot descriptions also differ from newer policy/service scope. Displayed update dates and metadata dates sometimes differ; the source manifest preserves that distinction. Failed URLs are recorded and not cited as supporting evidence.

The selected source documents support capabilities and constraints; the prioritization, top-three layout, proposed time presets, first-release boundaries and interpretation rules are recommendations to review, not Microsoft mandates.

[release]: https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information
[homepro]: https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro
[enterprise]: https://learn.microsoft.com/en-us/lifecycle/products/windows-11-enterprise-and-education
[updates]: https://learn.microsoft.com/en-us/windows/deployment/update/waas-restart
[safeguard]: https://learn.microsoft.com/en-us/windows/deployment/update/safeguard-holds
[autoprep]: https://learn.microsoft.com/en-us/autopilot/device-preparation/overview
[autocompare]: https://learn.microsoft.com/en-us/autopilot/device-preparation/compare
[autoreq]: https://learn.microsoft.com/en-us/autopilot/device-preparation/requirements
[winget]: https://learn.microsoft.com/en-us/windows/package-manager/winget/
[wingetinstall]: https://learn.microsoft.com/en-us/windows/package-manager/winget/install
[wingetdownload]: https://learn.microsoft.com/en-us/windows/package-manager/winget/download
[win32]: https://learn.microsoft.com/en-us/intune/app-management/deployment/win32
[msix]: https://learn.microsoft.com/en-us/windows/msix/app-installer/install-update-app-installer
[bitlocker]: https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/
[bitrecovery]: https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/recovery-overview
[bitfaq]: https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/faq
[bitops]: https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/operations-guide
[faststartup]: https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/distinguishing-fast-startup-from-wake-from-hibernation
[power]: https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/sleep-settings
[longpath]: https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation
[copilot]: https://learn.microsoft.com/en-us/copilot/manage
[copilotwindows]: https://learn.microsoft.com/en-us/windows/client-management/manage-windows-copilot
[copilotpolicy]: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai
[powercfg]: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options
[modernstandby]: https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/modern-standby-sleepstudy
[restartfull]: https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/updates-not-install-with-fast-startup
[autoreset]: https://learn.microsoft.com/en-us/autopilot/windows-autopilot-reset
[winre]: https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/reagentc-command-line-options?view=windows-11
[dsreg]: https://learn.microsoft.com/en-us/entra/identity/devices/troubleshoot-device-dsregcmd
[powerrequest]: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-powersetrequest
[executionstate]: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate
[appinstallerupdate]: https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview
[msixsideload]: https://learn.microsoft.com/en-us/windows/msix/app-installer/installing-windows10-apps-web
[event41]: https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/event-id-41-restart
[dcom10016]: https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/event-10016-logged-when-accessing-dcom
[dsregcmd]: https://learn.microsoft.com/en-us/entra/identity/devices/troubleshoot-device-dsregcmd
[dism]: https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image?view=windows-11
[dismsource]: https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/configure-a-windows-repair-source?view=windows-11
[update]: https://learn.microsoft.com/en-us/windows/deployment/update/windows-update-logs
[dns]: https://learn.microsoft.com/en-us/troubleshoot/windows-client/networking/troubleshoot-dns-client-resolution-issues
[resolve]: https://learn.microsoft.com/en-us/powershell/module/dnsclient/resolve-dnsname?view=windowsserver2025-ps
[flush]: https://learn.microsoft.com/en-us/powershell/module/dnsclient/clear-dnsclientcache?view=windowsserver2025-ps
[nettest]: https://learn.microsoft.com/en-us/powershell/module/nettcpip/test-netconnection?view=windowsserver2025-ps
[repairvolume]: https://learn.microsoft.com/en-us/powershell/module/storage/repair-volume?view=windowsserver2025-ps
[mdm]: https://learn.microsoft.com/en-us/windows/client-management/mdm-collect-logs
[gpresult]: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/gpresult
[gpupdate]: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/gpupdate
[printing]: https://learn.microsoft.com/en-us/troubleshoot/windows-server/printing/print-spooler-service-not-running
[getevent]: https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.diagnostics/get-winevent?view=powershell-7.6
[wer]: https://learn.microsoft.com/en-us/windows/win32/wer/windows-error-reporting
[perf]: https://support.microsoft.com/en-us/windows/experience/performance-optimization/tips-to-improve-pc-performance-in-windows
[sfc]: https://support.microsoft.com/en-us/windows/experience/backup-recovery/using-system-file-checker-in-windows
[networksupport]: https://support.microsoft.com/en-us/windows/experience/connectivity-networking/fix-wi-fi-connection-issues-in-windows
[updatesupport]: https://support.microsoft.com/en-us/windows/deployment/updates-lifecycle/troubleshoot-problems-updating-windows
[printsupport]: https://support.microsoft.com/en-us/windows/hardware/printer/fix-printer-connection-and-printing-problems-in-windows
[free]: https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/free-up-drive-space-in-windows
[vpn]: https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/vpn/vpn-guide
[vpntroubleshoot]: https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/troubleshoot-always-on-vpn
[reliability]: https://learn.microsoft.com/en-us/previous-versions/windows/desktop/racwmiprov/win32-reliabilityrecords
[sfccommand]: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc
[storagereliability]: https://learn.microsoft.com/en-us/powershell/module/storage/get-storagereliabilitycounter?view=windowsserver2025-ps
[performancewpr]: https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder
[netadapter]: https://learn.microsoft.com/en-us/powershell/module/netadapter/get-netadapter?view=windowsserver2025-ps
[vpnconnection]: https://learn.microsoft.com/en-us/powershell/module/vpnclient/get-vpnconnection?view=windowsserver2025-ps
[getprinter]: https://learn.microsoft.com/en-us/powershell/module/printmanagement/get-printer?view=windowsserver2025-ps
[printjob]: https://learn.microsoft.com/en-us/powershell/module/printmanagement/get-printjob?view=windowsserver2025-ps
[w32time]: https://learn.microsoft.com/en-us/windows-server/networking/windows-time-service/windows-time-service-tools-and-settings
[mpsignature]: https://learn.microsoft.com/en-us/powershell/module/defender/update-mpsignature?view=windowsserver2025-ps
[mpstatus]: https://learn.microsoft.com/en-us/powershell/module/defender/get-mpcomputerstatus?view=windowsserver2025-ps
[defenderupdates]: https://learn.microsoft.com/en-us/defender-endpoint/manage-protection-updates-microsoft-defender-antivirus
[storage-policy]: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-storage
[settings]: https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings
[oem-dell]: https://www.dell.com/support/kbdoc/en-us/000177325/dell-command-update
[oem-hp]: https://ftp.ext.hp.com/pub/caps-softpaq/cmit/HPIA.html
[oem-lenovo]: https://support.lenovo.com/us/en/solutions/ht037099
[baseline]: https://learn.microsoft.com/en-us/windows/security/operating-system-security/device-management/windows-security-configuration-framework/windows-security-baselines
[spotlight]: https://learn.microsoft.com/en-us/windows/configuration/windows-spotlight/
[chassis]: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-systemenclosure
[defaultapps]: https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/export-or-import-default-application-associations?view=windows-11
