# Engineer readiness, quiet diagnostics and approved fixes

**Status: written design for review; not implemented.** This expands the earlier twelve-check/two-action proposal following the request to improve the toolkit and add features. Design approval permits implementation planning; it does not authorize a change to any engineer's device.

Baseline reviewed: `2fe1e4d`. Supporting material: [original research](../../research/2026-10-15-windows-toolkit-recommendations.md), [78-source manifest](../../research/2026-10-15-windows-toolkit-sources.json), and [supplemental Microsoft sources](../../research/2026-10-15-windows-toolkit-expansion-sources.json). Release scope, UI choices and thresholds below are engineering decisions, not Microsoft requirements.

## 1. Outcome and scope

Engineers need one native Windows tool to prepare a device, verify deployments, find relevant problems, review a proposed change and keep a readable record. The user requires independent options, concise language, current/expected values before changes, selective administrator elevation and one dated log for each review/run. Setup and diagnostics must use the same code.

Keep the approved setup table and diagnostic list/details layouts. Expand the proposal to **24 separately selectable checks**, **six separately selectable actions**, guided Windows Settings links, manual handover checks and clearer deployment/reporting results. Some checks enrich existing coverage; these are not 24 entirely new capabilities. Existing M365 checks and operations remain available.

The recommended approach is a shared readiness/diagnostic extension. A read-only extension leaves every correction manual. A universal repair suite brings substantially different servicing, recovery and interruption requirements; those operations stay in separate later designs. This remains architectural work because exact capture selection, evidence interpretation and the worker contract change.

Assumptions: Windows 11 x64 remains the writable target; the existing build-standard provenance remains authoritative; customer-specific targets are supplied locally. Device role suggestions never select tasks. Unknown customer requirements remain unknown rather than acquiring an invented default.

## 2. Engineer workflow and language

1. **Choose tasks.** Setup suggests tasks for laptop, desktop/dock, remote worker or reassignment. Diagnostics suggests tasks from a symptom and incident time. Suggestions only reorder/highlight rows. All tasks remain discoverable through search and an All tasks view. No group checkbox, auto-selection, hidden dependency installation or Fix all.
2. **Check and review.** Each checked row is either Check only or Review change. Show Task, Current, Expected, Status and Account. Selected changes disclose their specific preflight reads, network use and effect. The bottom bar shows selections outside the current filter. Changing a target, parameter, profile or selection invalidates the review.
3. **Confirm selected changes.** Display a compact summary and expandable exact evidence before confirmation. Already-correct settings are skipped. Unreadable or policy-blocked changes cannot run; remove them and create a fresh review. Read-only checks never become actions through navigation.
4. **Run and verify.** Append actions, actual actor and readback to the same log. Show each result separately, stop on uncertainty, and offer a symptom retest without selecting it automatically.

Use these words consistently:

| Status | Meaning |
| --- | --- |
| Not checked | No evidence collected for this task and target. |
| Current | Observed value matches the selected expected value. |
| Different | A known value differs; display the actual value in plain language. |
| Missing | A successful lookup establishes that this exact value/resource is absent. |
| Unknown | Evidence, applicability or the expected value could not be established. |
| Blocked | A specific prerequisite prevents this proposed change. |
| Not applicable | Evidence establishes that this check does not apply. |
| Needs recheck | Evidence is old or its device/user/target context changed. |

For an unrecognized registry value, show `Value 3 — meaning not recognized`, not Missing. Details retain full path, value name, registry view, type, raw value, source and capture time. Unknown never becomes an empty string, zero, false or success. Diagnostic observations without a defined target use neutral Observed wording, not Current.

## 3. Check catalogue

Every numbered row is independently selectable. A logical check may return several related fields but cannot perform another listed check as a side effect. Shared API responses can be projected into separately requested fields. Required action preflight is listed during review; it cannot conceal unrelated/network probes.

| # | Check | Concise result and limit |
| --- | --- | --- |
| 1 | Windows release and support | Edition, release/build, architecture, dated support reference and client target. Support and client approval are distinct; no implied upgrade eligibility. |
| 2 | Local work-account context | Original engineer, local join state and management indicators. No tenant-compliance claim or automatic online device authentication. |
| 3 | Restart reasons | Individually identified local update/installer reasons and boot identity. No single flag claims to cover every restart requirement. |
| 4 | Update history | Recent local update result, code and time. Use bounded local history; no update search/download/install. |
| 5 | Windows recovery | WinRE enabled/disabled/unknown and registered recovery location. Registration does not prove successful recovery. |
| 6 | Drive protection | Selected volume's BitLocker conversion, protection/suspension and nonsecret protector identifiers. Recovery-key retrieval remains a separate external attestation. |
| 7 | Free space | Selected volume's free/total space against an explicit deployment/client requirement. No universal percentage pass. |
| 8 | Disk-health evidence | Available reliability/health observations and provider limits. Unsupported USB/RAID/VM evidence remains Unknown. No stress or repair scan. |
| 9 | Manufacturer and firmware identity | Manufacturer, exact model, BIOS version/date and relevant device identity. Inventory does not establish that firmware is latest or compatible. |
| 10 | Present device problems | Present PnP devices with reported problems, problem code and class. A deliberately disabled device is explained, not automatically enabled. |
| 11 | Sleep capabilities | Supported sleep states and platform limitations. No forced S3, hibernation change or continuous energy trace. |
| 12 | Battery capacity | Per-battery design/full-charge capacity and reported cycle data when available. Ratio is descriptive; no universal replacement threshold or fabricated runtime estimate. |
| 13 | Defender protection | Active/passive/unavailable mode and protection observations. A third-party product name does not establish health. |
| 14 | Defender signatures | Installed signature version/time and source configuration. Age alone does not prove an update exists. |
| 15 | Adapter and route | Explicitly selected adapter, link, addressing, resolver and route context. Internet access is not a corporate application test. |
| 16 | DNS lookup | Selected hostname/query type with the system resolver and captured adapter/VPN context. Disclose network use and split-DNS limits. |
| 17 | TCP connection | Explicit target and port; disclose name resolution if the target is a hostname. TCP success is not TLS, authentication or application success. |
| 18 | User proxy configuration | Original engineer's connection-specific proxy/PAC configuration. Configuration only: do not fetch/evaluate PAC or claim all apps use it. |
| 19 | System WinHTTP proxy | Local WinHTTP configuration, separately from the user's proxy. No PAC evaluation or proxy reset. |
| 20 | Windows time status | Local time-service state, configured/observed source and last-sync information. Local status is not an independent accuracy measurement; no resync. |
| 21 | Print Spooler state | Local service status and startup mode. Read only; no start or restart. |
| 22 | Selected printer queue | Selected queue, bounded job states/counts and access limits. Omit document names and owners; no test print or queue deletion. Network/shared queue access is disclosed. |
| 23 | Incident events | Selected symptom/provider set and explicit time window, grouped findings and coverage. No blanket collection or event-log clearing. |
| 24 | Selected application verification | Existing approved deployment's identity/version/scope, pending/restart guard and verification evidence. Configuration, sign-in, licensing and enrollment remain distinct. |

Windows Update history, PnP and user-proxy choices are supported by supplemental sources E05, E06, E08 and E10. The existing research supports the other diagnostic constraints. Battery collection uses the documented `powercfg /batteryreport /xml` when supported (E09), with an explicitly disclosed short local collection, a private temporary output and a bounded parser with external entities disabled. Persist selected capacity fields only; remove raw temporary output and report cleanup failures without echoing its contents. No HTML scraping or collection of serial/history data into the run log.

No setting changes happen in these checks. Local report generation is disclosed as diagnostic output, not a configuration write. If a Windows API/cmdlet is unavailable or its output cannot be interpreted safely, show Unknown and guidance.

## 4. Six individual actions

The action family is a closed registry, not a script/registry editor. Each action uses existing reviewed plans, original-user identity, per-operation UAC, protected execution and evidence journals. Nothing runs merely because a finding recommends it.

| Action | Reviewed change | Verification and restoration |
| --- | --- | --- |
| Refresh local DNS cache | Clear the whole local client cache once after an engineer-reviewed stale-cache hypothesis. Preserve resolvers/VPN; no server cache change. | Requery the affected name and retest the selected endpoint only when included in the reviewed verification scope. Cache rebuilds; no exact undo. |
| Update Defender signatures | Bare `Update-MpSignature` with the existing configured/default source order. Active/applicable Defender required; disclose network sources and Windows fallback defaults. | Fresh mode/version/time. Unchanged version can already be current; never invent an upgrade. No exact undo or automatic retry. |
| Sleep when plugged in | One AC sleep timeout on the reviewed active scheme. | Read back that index and all four timeout values; restore its recorded value only through a fresh review. |
| Sleep on battery | One DC sleep timeout when a battery/platform supports it. | Same guard and restoration; no change to the AC timeout. |
| Turn off display when plugged in | One AC display idle timeout. | Same guard and restoration; screen lock is a separate setting and remains unchanged. |
| Turn off display on battery | One DC display idle timeout when supported. | Same guard and restoration; no sleep, lid or hibernation change. |

### Power semantics

Each power row shows its own current value and editable target. Choices are Never or a whole number of minutes from 1 to 1,440. This UI range is a product choice, not a Windows maximum. Existing values outside the range or not divisible by 60 remain precisely readable/restorable. No target is silently chosen; a client profile may supply one with its source shown.

Persist raw seconds, active scheme GUID, fixed subgroup/setting GUID and AC/DC identity. Use the supported native power API for the individual value (E12/E13), apply the same scheme (E14), and verify all four reviewed timeout values. E14 applies the scheme for the current user: both new power operations and the existing AC-sleep path must require the original engineer's SID before any write/activation, including after UAC. Same-account administrator consent is supported; alternate-credential elevation is blocked for these operations before the first write. Do not activate a scheme for the alternate administrator and report it as the engineer's result. Other machine operations retain their existing identity rules.

Recognized effective policy or unreadable policy blocks the change (E04). Confirm external management review as the existing fixed-setting path does; absence of a checked registry policy is not proof of an unmanaged device. Modern Standby capability/behavior is disclosed; an index readback does not prove every sleep transition follows it.

Fresh dispatch must match scheme, reviewed values, policy and supported platform. Recheck scheme identity immediately before activation; if an external actor changes the scheme during execution, report an unverified/uncertain result and stop. Do not silently retry or switch back to a previously reviewed scheme after observing drift. Windows power APIs are not a transaction; document this residual race.

Multiple selected power rows in one plan use an ordered expected state. Preserve the actually observed before-state; store a separately named and hashed ExpectedAtDispatch projection that includes only earlier, explicitly reviewed toolkit changes. Never describe that projected future value as an observation. Before dispatch, the coordinator and protected worker validate the projection against the reviewed plan, successful prior journal results and fresh actual state. Missing/uncertain prior results block the sequence. Each row changes one index; the queue does not falsely reject the second row because the first succeeded. Restoration has the same ordering and compares the current target with the last recorded toolkit write.

The existing `settings.no-ac-sleep` operation and the new AC timeout operation must share a logical resource key so contradictory writes cannot queue together. The legacy fixed Never choice remains available for its sourced M365 control. A user-selected different timeout is labelled a local preference; it does not redefine `CFG-WIN-020` or claim that the build standard passes.

### Volatile diagnostic actions

DNS TTLs, query timestamps and signature-age counters are evidence, not stable authorization keys. Bind review to the device/user, selected target, adapter/resolver/VPN identity, policy, operation and intended verification. Freshly re-evaluate the action-specific preconditions immediately before dispatch; changed semantic context requires a new review. If the suspected DNS issue is no longer present, do not flush solely to fulfill an old suggestion. Different round-robin responses alone do not establish stale cache.

These actions must not weaken the exact-state checks for settings/deployments. Their stable authorization projection and full observed evidence are separate, versioned records. A completed cache operation can be verified as requested while symptom resolution remains Unknown or No; it must never be labelled Fixed from an exit code. An uncertain signature-update process stops the queue and is reconciled before retry; the UI timeout is not evidence that Windows stopped updating.

## 5. Quiet diagnostics

Symptoms: network/VPN, slow device, battery/sleep, printing, update failure, application/deployment and crash/restart. Choosing one only suggests relevant checks. Until an incident time is entered, offer Last hour and Custom range as explicit choices; do not silently turn an old event into a current problem.

Show up to three expanded relevant findings, each with **Observed / Why it matters / Next step** and capture time. Display counted lists for the rest, with always-visible signposts for security findings and incomplete/unknown required checks. Background findings remain accessible. An engineer can expand all, search, copy a concise ticket summary or inspect exact evidence.

Priority is deterministic: security/action-blocking evidence, direct symptom/time/target match, then supporting observations. A versioned rule records its inputs and reason for ranking. Use Confirmed observation, Possible explanation or Not established. Do not generate numerical confidence scores or call temporal correlation a cause.

Deduplicate events by provider, event ID, relevant event-data signature, resource and time window; retain channel, record IDs, first/last occurrence and count. Recognized benign DCOM 10016 patterns require the documented identifiers; other 10016 events remain visible. Kernel-Power 41 establishes an unclean shutdown, not a faulty power supply.

Collect server-side bounded events, newest first: a maximum seven-day selected window, 200 records per explicit page, 256 KiB projected payload per probe and a 30-second default read deadline. These are visible product limits. A details click only displays collected evidence; Collect next page is a new explicit read with its own logged request. Preserve all returned record IDs; if size limits omit message bodies, show that fact. Do not increase the existing 1 MiB worker frame limit to accommodate raw logs. Report retention gaps, inaccessible/disabled channels, failed providers, uncollected pages and truncation. `No matching evidence in the available records` never means Healthy.

## 6. Deployment, preferences and handover improvements

Improve the existing deployment workspace with distinct columns for **Package checked / Installed / Configured / Signed in / Licensed / Restart / Verified**. Unknown or manual items stay visible. Reuse the current MSI, approved EXE, current-user MSIX and Office ODT backends and package hash/signer approvals. Dependencies and offline readiness are shown individually; no automatic acquisition or extra installation.

Add an optional client label and ticket reference to a local run. Profiles supply versioned expected values and required handover items; they never load a previously approved plan or tick a task bundle. Plain local profiles cannot grant permission to execute anything. Keep the approved deployment-profile authority separate from presentation/expected-value profiles.

Guided buttons open separate supported Windows Settings pages for display/scaling, audio, accessibility, default apps, startup apps, notifications, Storage Sense and work account. These are labelled Open settings, not Apply or Fixed. Validate a fixed URI allowlist. Record launch and later explicit verification separately. Default browser/PDF remain supported actual-user choices; no protected UserChoice changes. No cleanup/dehydration, notification suppression or removal of applications is hidden in these links.

Handover offers separate manual rows for external display, audio output, microphone, camera, keyboard, touchpad, selected printer, VPN connectivity, application sign-in, backup verification and recovery-key retrieval. They start Unverified; each records who tested what, when and its scope. There is no automatic camera/microphone activation or test print. Recovery attestation records the nonsecret matching protector reference and retrieval result, never the key. Engineer-profile verification does not certify the eventual end user's profile.

The handover summary separates verified local checks, manual attestations, approved exceptions and outstanding work. Unknown required checks, unresolved/uncertain actions and missing manual requirements prevent Ready for handover. Exceptions stay labelled and cannot become a green automated pass; readiness with exceptions is a separate outcome only when the reviewed client checklist explicitly allows them.

Reports add an explicit two-run comparison for the same device, target, context and compatible schemas/rules. Show changed values, new findings, resolved observations and no-longer-comparable items. Old snapshots do not authorize current changes. If a target/schema differs, display Not comparable rather than a false improvement.

## 7. Architecture and evidence contracts

The current `WorkbenchSession.CaptureAtOriginAsync` maps selected tasks to control IDs; `WindowsDeviceInspector` expands all checks for those controls. This must change before adding more probes. Otherwise a selected row can read unrelated checks.

| Component | Responsibility and boundary |
| --- | --- |
| Core capture contracts | Version 2 capture request includes exact registered task/check IDs, typed target parameters, origin and context. Reject unknown IDs/fields, duplicate targets and mismatched task/check mappings. |
| Probe catalogue and Windows providers | Declare read scope, privilege, network/cost, prerequisites, payload limits and applicability. Issue one bounded probe capture at a time; read in original-user context where required. No arbitrary command/path interpolation. |
| Workbench session | Resolve exact requested checks; share canonical results across setup/diagnostics without broadening selection. A requested subset cannot mark the entire composite M365 control complete. |
| Diagnostic interpretation | Pure, deterministic rules over observations, incident scope and versioned reference data. Output findings with evidence references, limitation and optional registered action suggestion. Never dispatch actions. |
| Operations and review/run | Retain `ILocalOperation`, plan validation, UAC, protected journals and run coordinator. Add typed DNS/Defender/power operations and explicit ordered expected states for selected writes to the same power scheme. |
| Reporting | Aggregate immutable captures, action outcomes, manually recorded checks, comparisons and summaries. An export is not execution authority. |

Ship a matching schema-2 app/worker pair; reject mixed versions before capture or dispatch. Historical schema-1 evidence remains readable via its existing validator without rewriting its meaning. Historical broad captures are labelled as such; old plans are never silently upgraded/replayed. Version new observation/diagnostic/outcome payloads and update strict validators, registered script hashes and portable-package integrity together.

Keep the 30 pinned M365 source controls unchanged. Local operational checks have a separate `local.*` namespace and separate provenance. Support-reference data has publication/retrieval dates and an explicit stale/unknown state; offline operation uses the bundled dated reference. Updating it is a separate, explicitly selected download after review, not an automatic startup request; remote reference refresh itself is outside this first implementation scope.

Power APIs and diagnostic providers should live in focused files rather than further expanding the single settings backend or facts script. Shared facts feed check-specific projections; full objects from BitLocker, proxies, printers and event providers must never reach logging through an unrestricted serializer.

## 8. Logging, failure and disclosure

Create a new UTC-dated, uniquely named companion file at Check and review. Append selected tasks, disclosed probe scope, before values, expected target/source, confirmation, execution, actual identity and verification to that same file. Keep the existing immutable/protected journal as authority. A later review or additional explicit collection creates a new file with a link to its parent run when relevant; files are never overwritten.

Retain the ten-minute review expiry and add a same-process monotonic elapsed-time limit so wall-clock rollback cannot extend approval. A restart of the app requires a fresh review. A fresh semantic preflight is still required immediately before each action; an unexpired plan is not proof of unchanged state.

Exclude secrets before persistence: BitLocker recovery passwords, credentials, tokens, Wi-Fi keys, printer document names and full dumps. For free-text events, preserve provider/IDs and allowlisted structured fields; do not promise arbitrary raw messages are secret-free. Full technical detail means full permitted evidence, not unfiltered sensitive output. Parse/redact proxy userinfo and sensitive PAC query values before any log, exception or export. Reports default to minimized identifying fields, with an explicit export preview for permitted additional identifiers.

Record **Action completed / Expected state verified / Original symptom resolved** separately. Power settings can be read back while sleep behavior still requires a manual test. A completed signature request with an unchanged version does not prove a newer version was available. Reboot verification gets a new run linked to the previous one; no installer/action replay after restart.

Pre-dispatch log/evidence failure blocks execution. Post-dispatch persistence failure preserves the actual/uncertain result through the existing protected path and stops remaining work. Cancellation before dispatch changes nothing; cancellation after a dispatched operation is not automatically labelled Cancelled. No retry loops. Process lifetime/outcome uncertainty must be reconciled before a later attempt.

## 9. Acceptance and release boundaries

Required automated coverage:

- A single selected task executes only its registered probe, including when several tasks share an M365 control. Network, protected reads and event paging require disclosed explicit selection.
- Shared-task selection survives navigation/search; suggested tasks stay unticked; changed inputs invalidate review. All tasks remain reachable at compact window sizes and by keyboard.
- Missing, inaccessible, unsupported, malformed, stale and unknown expected values remain distinct. Registry types and unrecognized values retain exact permitted evidence.
- Original-user proxy/current-user facts cannot come from an alternate UAC identity. Secret-bearing fixture values cannot reach persisted evidence, companion logs or exports.
- Deterministic event grouping, incomplete coverage, exact benign-pattern matching and Event 41 interpretation; security/unknown findings stay signposted beyond the first three.
- DNS context changes/round-robin/expired cache and Defender active/passive/no-new-version/unknown-outcome behavior; verification does not manufacture symptom resolution.
- Four independent power indices, plan drift, policy denial, multiple selected rows, legacy AC-sleep conflict, readback and guarded restoration; no unselected index change.
- Mixed worker versions rejected, old evidence readable, no automatic plan upgrade, one dated log per review and failure-before/after-write behavior retained.
- Deployment installed/configured/licensed states separated; handover requirements/exceptions and cross-run comparability cannot produce a false pass.

Required native acceptance: Windows 11 x64 laptop/desktop/VM; supported S0/S3 hardware differences; battery unavailable/multiple batteries; managed/unmanaged and offline/VPN cases; active Defender/third-party AV; local/shared printer access; alternate-admin/UAC cancellation; power changes/restoration on a disposable target; normal/compact/high-contrast/keyboard/Narrator review. Linux builds and hosted Server UI captures cannot replace these checks.

Implementation should deliver the exact-capture/evidence foundation first, then checks and diagnostic presentation, then reviewed actions and handover/reporting. Each delivered increment must work end to end; do not expose nonfunctional action buttons. This is sequencing guidance, not the implementation plan.

## 10. Separate later scopes

| Follow-on | Design needed before it becomes an action |
| --- | --- |
| OEM drivers, dock firmware and BIOS | Exact supported vendor/model/tool, scan versus install, recovery, power, signing and reboot lifecycle. |
| WinGet discovery/download | Exact source/package/version/scope, individual dependencies/licenses and transfer into existing approved media workflow. |
| Temporary setup keep-awake | Scoped lifetime, expiry, cancellation, power source and guaranteed request cleanup. |
| Time resync | Configured source only, clock-step consequences and audit/plan timing behavior. |
| Spooler start or restart | Separate actions, queued-job/whole-device impact, inaccessible queue state and actual printing verification. |
| DISM/SFC diagnostics and repair | Separately selected read/scan/repair, source/policy, long-running servicing, cancellation and post-repair symptom evidence. |
| Storage Sense and user preferences | Each retention/deletion/dehydration option separately specified; no generic debloat or cleanup preset. |

Reset/wipe, enrollment changes, security weakening, forced sleep-model changes and automatic firmware flashing are not included in this design. No new device change has been executed while preparing it.
