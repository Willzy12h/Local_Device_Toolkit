# Engineer Readiness and Diagnostics Implementation Plan

Historical implementation plan. Source execution, deliberate deviations and measured checks are recorded in the [execution record](../2026-10-16-engineer-readiness-execution.md). Unchecked drafting steps below do not establish missing source features; current remaining work is tracked in [implementation status](../../IMPLEMENTATION-STATUS.md) and [Windows acceptance](../../WINDOWS-ACCEPTANCE.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved 24-check/six-action expansion with exact selection, quiet diagnostics, verified changes and clear handover evidence.

**Architecture:** Extend the existing native workbench, operation registry and protected execution path. Add exact, versioned capture requests and local operational evidence alongside the unchanged 30 M365 controls. Deterministic diagnostics and reports consume evidence; they never authorize execution.

**Tech Stack:** .NET SDK 10.0.401, C# 12, WPF, Windows APIs, Windows PowerShell 5.1-compatible registered scripts, xUnit; no new package dependencies.

**Spec:** [Approved engineer readiness and diagnostics design](../specs/2026-10-15-engineer-readiness-diagnostics-design.md). The user approved this written design in chat. The user approved this implementation plan and selected Native execution in chat. Source baseline: `863b5f1`; application baseline: `2fe1e4d`.

## Global Constraints

- Windows 11 x64 remains the writable target; native app runs as the original engineer with selective per-operation UAC.
- Keep the 30 pinned M365 source controls unchanged. Local operational checks have a separate `local.*` namespace and separate provenance.
- No group checkbox, auto-selection, hidden dependency installation or Fix all.
- Preserve choose → check/review → confirm → run/verify and a new dated companion log per review/run; append changes/readback to that same file.
- Retain the ten-minute review expiry and add a same-process monotonic elapsed-time limit so wall-clock rollback cannot extend approval.
- Power choices are Never or a whole number of minutes from 1 to 1,440. Existing raw values remain precisely readable/restorable.
- Events: a maximum seven-day selected window, 200 records per explicit page, 256 KiB projected payload per probe and a 30-second default read deadline.
- Do not increase the existing 1 MiB worker frame limit. Preserve the existing 8 MiB evidence-record limit.
- Exclude secrets before persistence; raw provider objects and free-text messages cannot bypass projection through exceptions/logging.
- Original engineer SID is required before any new or legacy power write/activation, including after UAC. Alternate-credential power writes stop before the first write.
- No remote support-reference refresh, OEM installation, WinGet execution, keep-awake, time resync, spooler actions, DISM/SFC, reset/wipe or enrollment change in this implementation.
- Use the current cloud checkout; do not create a worktree. Do not push the unrelated backup branch or change the saved environment configuration for these features.

## Review Focus

1. Same check with a different hostname, volume, printer or original user must not reuse an earlier result or approval: Tasks 1, 5, 14.
2. One denied/failed provider in an otherwise successful collection must remain incomplete, including an unknown required handover item: Tasks 3, 6, 15.
3. Wall-clock rollback, elapsed time during sleep and application restart must not extend a review's lifetime: Task 9; Task 16 adds native sleep/resume acceptance.
4. Several selected power values, a concurrent plan switch or alternate-admin UAC must not overwrite an unrelated value or activate another user's scheme: Tasks 9–10.
5. Event pagination, reused record IDs after log clearing and secret-bearing messages must not manufacture complete coverage or leak content: Tasks 7–8, 15.

## File structure and dependency map

| Boundary | Files/responsibility |
| --- | --- |
| Capture and values | `src/LocalDeviceToolkit.Core/InspectionContracts.cs`, `LocalProbeValues.cs`; exact requests, coverage and approved value shapes. Existing `Models.cs` keeps historical records stable. |
| Local catalogue | `src/LocalDeviceToolkit.Engine/Catalogue/LocalProbeCatalogue.cs`, `ReferenceProfiles.cs`; local IDs, typed inputs, disclosure, dated references and customer targets. |
| Windows providers | `src/LocalDeviceToolkit.Windows/Inspection/Probes/`; small readiness, hardware, network, security/service and event providers. `OperationalProbeRouter.cs` combines these with the existing facts source. |
| Diagnosis | `src/LocalDeviceToolkit.Engine/Diagnostics/`; evidence references, rules, event grouping and ranking. No process launching. |
| Reviewed execution | Existing planning/execution/elevation files plus `ReviewLifetime.cs`, `ReviewedStatePolicy.cs` and `OperationalOutcome.cs`; freshness, verified plan prefix and outcome distinctions. |
| Actions | Focused `Engine/Settings/PowerTimeoutOperation.cs`, `Engine/Diagnostics/DnsCacheOperation.cs`, `DefenderSignatureOperation.cs` with Windows backends. |
| Presentation/reporting | Focused workbench selection/diagnostic models, WPF views, handover/profile services and comparison/export projection. |

Tasks 1–2 establish shared contracts. Tasks 3–7 and 13 implement independent provider families against those contracts. Task 8 interprets their evidence. Task 9 provides reviewed execution for Tasks 10–12. Tasks 14–15 integrate usable screens/reports. Task 16 verifies the complete branch. Do not expose a selectable operation before its backend and review path work. This is one plan because all additions share the capture, evidence and approval contract; deferred repair subsystems remain outside it.

### Verification commands

From `/workspace/Local_Device_Toolkit`, activate tools in every new Linux shell:

```bash
source build/Use-CloudTools.sh
export DOTNET_PROCESSOR_COUNT=2
toolkit_engine_test() {
  dotnet test tests/LocalDeviceToolkit.Tests/LocalDeviceToolkit.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -warnaserror --filter "$1"
}
toolkit_windows_test() {
  dotnet test tests/LocalDeviceToolkit.Windows.Tests/LocalDeviceToolkit.Windows.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -warnaserror --filter "$1"
}
```

Run `dotnet restore LocalDeviceToolkit.sln --locked-mode -m:1` once if assets are missing. Run builds/tests sequentially. Each task below requires an observed failing assertion or specifically missing new API first, then a nonzero passing test count; a zero-test run is not success. Windows-specific live behavior is exercised on Windows, never simulated by claiming Linux ran it. Test doubles must not call real mutation commands.

Each task ends with a scoped commit after its targeted checks pass. Stage only the files named in that task and any explicitly necessary call-site updates; inspect `git diff --cached --check`. Do not commit generated output, local media or credentials. Commit labels are supplied below; commits are local until integration is authorized through the normal task workflow.

## Task 1: Exact capture selection and historical evidence

**Files:** Modify `src/LocalDeviceToolkit.Core/Models.cs`, `src/LocalDeviceToolkit.Engine/Execution/ActionContracts.cs`, `src/LocalDeviceToolkit.Engine/Workbench/WorkbenchSession.cs`, `ReviewRunService.cs` in the same directory, `src/LocalDeviceToolkit.Engine/Elevation/WorkerProtocol.cs`, `WorkerRequestValidator.cs` in the same directory, `src/LocalDeviceToolkit.Engine/Evidence/EvidenceStore.cs`, `src/LocalDeviceToolkit.Windows/Inspection/WindowsDeviceInspector.cs`, `src/LocalDeviceToolkit.Windows/Elevation/UacWorkerBroker.cs`, `WindowsActionDispatcher.cs` in the same directory, `src/LocalDeviceToolkit.Worker/App.xaml.cs`, `src/LocalDeviceToolkit.App/MainWindow.xaml.cs` and their existing test call sites. Create `src/LocalDeviceToolkit.Core/InspectionContracts.cs`, `src/LocalDeviceToolkit.Engine/Evidence/CaptureEvidenceV2.cs`, `tests/LocalDeviceToolkit.Tests/ExactCaptureTests.cs`, `HistoricalCaptureTests.cs` in the same test directory. Extend `tests/LocalDeviceToolkit.Windows.Tests/DeviceInspectorTests.cs`.

**Interfaces:** Define `CheckSelection(string TaskId, JsonElement Parameters)`; replace the transport-only request with `CaptureRequest(int SchemaVersion, string TaskId, IReadOnlyList<string> CheckIds, JsonElement Parameters, WorkflowOrigin Origin)`. Define `CheckCoverage(string CheckId, string TargetDigest, bool Complete, int ReturnedCount, bool HasMore, string? NextCursor, IReadOnlyList<string> Limitations)` and `CapturedChecks(DeviceCapture Capture, CaptureRequest Request, IReadOnlyList<CheckCoverage> Coverage)`. `IDeviceInspector.CaptureAsync(CaptureRequest, CancellationToken)` returns `Task<CapturedChecks>`. `WorkbenchSession.CaptureAtOriginAsync(IReadOnlyList<CheckSelection>, WorkflowOrigin, CancellationToken)` returns `Task<IReadOnlyList<CapturedChecks>>`, issuing one task request at a time. `CaptureEvidenceV2.Create(CapturedChecks)` returns an `EvidenceRecord` of kind `capture.v2`; the envelope stays version 1, its strictly validated payload is the exact-request version 2.

- [x] Write `Only_selected_readiness_check_reaches_source`, `Subset_does_not_complete_composite_control`, `Request_rejects_foreign_check_and_duplicate_ids`, `Target_and_original_user_are_bound`, `Legacy_capture_reads_without_rewrite` and `Mixed_worker_versions_are_refused`. Core assertions: `Assert.Equal(new[]{"secureboot.enabled"}, recordedCheckIds); Assert.Equal(FindingStatus.Unknown, composite.Status); Assert.Equal(originalBytes, reloadedRawBytes);`. Use the existing TestData context and recording facts fake; define that fake inside `ExactCaptureTests.cs`.
- [x] Run `toolkit_engine_test 'FullyQualifiedName~ExactCaptureTests|FullyQualifiedName~HistoricalCaptureTests|FullyQualifiedName~WorkerProtocolTests'` and `toolkit_windows_test 'FullyQualifiedName~DeviceInspectorTests'`; observe the broad-capture regression before changing it.
- [x] Resolve checks from the selected task, validate against its declared checks and origin, reject request tampering and return exact coverage. Use worker request/response version 2 in all modes/call sites; reject version 1 transport while retaining old evidence validators. Do not change historical `DeviceCapture` serialization or infer complete provenance for old broad captures. Store each bounded capture once and adapt current review/report readers to the wrapper.
- [x] Rerun those commands plus existing WorkbenchTests and ReviewRunTests. Include protected single-check capture and failed evidence-save cases.
- [x] Commit: `fix: capture only explicitly selected checks`.

## Task 2: Local probe catalogue, inputs and reference profiles

**Files:** Create `catalogue/local-probes.json`, `catalogue/windows-support.json`, `src/LocalDeviceToolkit.Core/LocalProbeValues.cs`, `src/LocalDeviceToolkit.Engine/Catalogue/LocalProbeCatalogue.cs`, `ReferenceProfiles.cs` in the same directory, `src/LocalDeviceToolkit.Windows/Inspection/OperationalProbeRouter.cs`, `tests/LocalDeviceToolkit.Tests/LocalProbeCatalogueTests.cs`, `ReferenceProfileTests.cs` in the same directory. Modify `catalogue/pins.json`, `src/LocalDeviceToolkit.Engine/Catalogue/CatalogueLoader.cs`, `src/LocalDeviceToolkit.Engine/Workbench/ActionPreview.cs`, `WorkbenchSession.cs` in the same directory, `src/LocalDeviceToolkit.Windows/Inspection/WindowsDeviceInspector.cs`, `src/LocalDeviceToolkit.Engine/Elevation/WorkerRequestValidator.cs`, and the app/worker composition roots.

**Interfaces:** `LocalProbeDefinition(string Id, string Title, OperationScope Scope, PrivilegeRequirement Privilege, bool MayUseNetwork, string Disclosure, string InputSchema)`; `LocalProbeCatalogue.Resolve(string)` returns that definition; `ValidateInput(string, JsonElement)` throws `CatalogueValidationException`. `LocalProbeValues.Accepts(string checkId, JsonElement value)` returns bool using closed per-ID schemas. `ProbeRead(ObservationState State, JsonElement? Value, string Source, DateTimeOffset CapturedAt, CheckCoverage Coverage, string Reason)`; `IProbeProvider.ReadAsync(string, JsonElement, CancellationToken)` returns `Task<ProbeRead>`. `ReferenceProfiles.EvaluateSupport(string edition, string release, DateOnly asOf)` returns `SupportAssessment(SupportState State, DateOnly? EndsOn, DateOnly ReferenceDate, string Reason)`; `SupportState` is `Supported, OutOfSupport, Unknown`. `ClientChecklist(string Id, int Version, string Label, IReadOnlyDictionary<string,JsonElement> Expected, IReadOnlyList<string> RequiredItems, bool AllowReviewedExceptions)` supplies expectations only.

- [ ] Write `Local_checks_do_not_change_30_source_controls`, `Unknown_reference_does_not_invent_expected_value`, `Edition_specific_dates_and_stale_reference`, `Profile_never_selects_or_authorizes`, and strict wrong-type/extra-field cases. Assertions include `Assert.Equal(30, sourced.Controls.Count); Assert.Equal(SupportState.Unknown, unknown.State); Assert.Empty(selectedTasks);`. Reference fixtures use explicit test dates, not the current clock. A support reference is stale after 30 days; display its date and Unknown assessment without changing the approved client target.
- [ ] Run `toolkit_engine_test 'FullyQualifiedName~LocalProbeCatalogueTests|FullyQualifiedName~ReferenceProfileTests|FullyQualifiedName~CatalogueTests'` to establish failures.
- [ ] Register the 24 IDs listed below; route existing source checks unchanged and local checks through `IProbeProvider`. Validate per-check record shapes before persistence. Local task status/inspection uses its local descriptor directly, never an empty lookup in the 30-control M365 catalogue or a fabricated composite compliance result. Protected requests validate registered scope/privilege and reject current-user reads under an alternate SID. Add neutral Observed presentation and the spec's Missing/Unknown/Current/Different vocabulary without changing stored legacy enum meanings. Populate dated support data only from the fetched official source, retaining URL/date/edition identity; no automatic network refresh. Update pins only for reviewed changed catalogue files.
- [ ] Rerun the filter and ActionPreviewTests. Verify unknown enum/string values remain explicit rather than defaulted to success.
- [ ] Commit: `feat: add typed local probe catalogue and reference profiles`.

**Canonical local check IDs (spec row order):** `local.os.support`, `local.identity.context`, `local.restart.reasons`, `local.update.history`, `local.recovery.winre`, `local.bitlocker.state`, `local.storage.capacity`, `local.storage.health`, `local.hardware.identity`, `local.hardware.problems`, `local.power.capabilities`, `local.battery.capacity`, `local.defender.protection`, `local.defender.signatures`, `local.network.adapter`, `local.network.dns`, `local.network.tcp`, `local.proxy.user`, `local.proxy.winhttp`, `local.time.status`, `local.print.spooler`, `local.print.queue`, `local.events.incident`, `local.app.verification`. Each task uses its identical local ID and a separate local provenance reference; none is added to `LocalCatalogue.Controls`.

## Task 3: OS, management, updates and recovery checks

**Files:** Create `src/LocalDeviceToolkit.Windows/Inspection/Probes/ReadinessProbes.cs`, `ReadinessParsers.cs` in that directory, `scripts/diagnostics/Get-ReadinessFacts.ps1`, `tests/LocalDeviceToolkit.Windows.Tests/ReadinessProbeTests.cs`; extend `scripts/registry.json`, OperationalProbeRouter and `tests/LocalDeviceToolkit.Windows.Tests/RegisteredScriptTests.cs`.

**Interfaces:** `ReadinessProbes : IProbeProvider` implements rows 1–6. Inputs: `{}` for rows 1–5 except update history accepts `UpdateHistoryQuery(int Count)` with 1–200; `VolumeQuery(string VolumeId)` for BitLocker. Both query records belong in `Core/InspectionContracts.cs`. `ReadinessParsers.Project(string checkId, JsonElement raw)` returns only the registered `JsonElement` value shape; unapproved fields are omitted before logging. Its raw argument is trusted in-memory provider data, never persisted.

- [ ] Write `Update_history_does_not_start_search`, `Local_join_does_not_call_device_authentication`, `Recovery_unknown_is_not_disabled`, `Partial_restart_providers_remain_incomplete`, `Bitlocker_secrets_never_leave_projection`. Assertions: `Assert.Equal(0, updateSearchCalls); Assert.False(result.Coverage.Complete); Assert.DoesNotContain(recoveryPassword, serializedEvidence);`. Add both culture-specific command-output fixtures and unknown-format refusals.
- [ ] Run `toolkit_windows_test 'FullyQualifiedName~ReadinessProbeTests'`; observe missing projections or wrong current behavior.
- [ ] Use native OS edition/build/architecture and local join APIs rather than unrestricted `dsregcmd /status`; WUA `QueryHistory`, not `Search`; named local restart providers with individual availability; WinRE `/info` with recognized output or Unknown. Query BitLocker through fixed Win32_EncryptableVolume conversion/protection/protector-ID/type methods; never call a method that retrieves a recovery password or external key. Emit only conversion/protection/suspension and nonsecret protector IDs/types, never raw provider objects. Per-provider failure affects coverage instead of erasing evidence from other providers. Parse/pin the changed script using the existing registry verifier.
- [ ] Rerun the filter plus NativeFactsTests and RegisteredScriptTests; require offline tests to record zero outbound requests.
- [ ] Commit: `feat: collect local readiness and recovery evidence`.

## Task 4: Storage, hardware, sleep and battery checks

**Files:** Create `src/LocalDeviceToolkit.Windows/Inspection/Probes/HardwareProbes.cs`, `BatteryReportReader.cs` in that directory, `scripts/diagnostics/Get-HardwareFacts.ps1`, `tests/LocalDeviceToolkit.Windows.Tests/HardwareProbeTests.cs`, `BatteryReportTests.cs` in the same directory. Modify router and script registry.

**Interfaces:** `HardwareProbes : IProbeProvider` implements rows 7–12; storage checks consume `VolumeQuery`, other inputs are `{}`. `BatteryReportReader.Read(Stream xml)` returns `IReadOnlyList<BatteryCapacity>`; `BatteryCapacity(string Reference, ulong? DesignMWh, ulong? FullChargeMWh, uint? CycleCount)` is defined with the reader. `CapacityPercent(BatteryCapacity)` returns `decimal?`, preserving values over 100 rather than clamping or inventing a health score.

- [ ] Write `Unsupported_health_is_unknown`, `Disabled_present_device_is_not_enabled`, `Firmware_date_is_not_update_status`, `Multiple_batteries_remain_separate`, `Missing_or_zero_design_has_no_ratio`, `Xml_entities_and_oversize_reports_refused`. Assertions: `Assert.Null(BatteryReportReader.CapacityPercent(new("battery-1",0,40000,null))); Assert.Equal(2, batteries.Count); Assert.Equal(0, deviceEnableCalls);`.
- [ ] Run `toolkit_windows_test 'FullyQualifiedName~HardwareProbeTests|FullyQualifiedName~BatteryReportTests'` to establish failures.
- [ ] Read selected volume capacity/reliability, exact manufacturer/model/BIOS and present PnP problem codes. Read supported sleep states without a trace. Generate the explicitly selected battery XML report in a private, no-reparse temporary directory, cap raw input at 4 MiB and projected output at 256 KiB; disable DTD/external resolution and delete raw output in finally. Report cleanup failure without exposing raw data. No stress scan, firmware lookup, driver enablement or capability-to-pass heuristic.
- [ ] Rerun the filter and script registry checks. Confirm battery report cancellation/output failures return incomplete coverage and no raw history/serial content persists.
- [ ] Commit: `feat: add bounded hardware and battery checks`.

## Task 5: Targeted networking and separate proxy checks

**Files:** Create `src/LocalDeviceToolkit.Core/NetworkProbeValues.cs`, `src/LocalDeviceToolkit.Windows/Inspection/Probes/NetworkProbes.cs`, `UserProxyReader.cs` in that directory, `scripts/diagnostics/Get-NetworkFacts.ps1`, `tests/LocalDeviceToolkit.Windows.Tests/NetworkProbeTests.cs`, `ProxyProjectionTests.cs` in the same directory. Modify router and registry.

**Interfaces:** Define `AdapterQuery(Guid AdapterId)`, `DnsQuery(string Hostname, string QueryType, Guid AdapterId)`, `TcpQuery(string Host, int Port, Guid AdapterId)` and `NetworkContext(Guid AdapterId, string AddressDigest, string RouteDigest, string ResolverDigest, string VpnDigest)` in `Core/NetworkProbeValues.cs`. `NetworkProbes : IProbeProvider` implements rows 15–19. `UserProxyReader.ReadForOriginalUser(string originalSid)` returns `ProbeRead` and refuses a different current SID. Parameters accept a single hostname/IP, never a command line, URL with credentials, CIDR/range or port list.

- [ ] Write `Changed_target_cannot_reuse_capture`, `Tcp_hostname_discloses_dns`, `No_resolver_override_or_range_scan`, `Proxy_reads_original_user`, `Pac_userinfo_and_query_secrets_are_removed`. Assertions: `Assert.NotEqual(firstTargetDigest, secondTargetDigest); Assert.Equal(0, pacFetchCalls); Assert.DoesNotContain("secret-token", json);`. Test IPv6, internationalized hostnames, whitespace/quote injection and unsupported third-party VPN context.
- [ ] Run `toolkit_windows_test 'FullyQualifiedName~NetworkProbeTests|FullyQualifiedName~ProxyProjectionTests'` and observe failures.
- [ ] Use fixed commands/native APIs and separate argument tokens. Query system DNS normally, not a substituted public resolver. Capture actual route/adapter evidence and disclose inability to bind a query to a chosen adapter; do not falsely claim the selected interface carried it. Time-limit explicit TCP tests; no implicit retries or packet capture. Read connection-specific user proxy with the original-user API and system WinHTTP separately. Preserve sanitized origin/host/path while removing userinfo, query values and fragments from PAC URLs before any error/evidence serialization.
- [ ] Rerun the filter and strict capture-input tests, checking that an unselected network check produces no network call.
- [ ] Commit: `feat: add scoped network and proxy diagnostics`.

## Task 6: Defender, time and printer observations

**Files:** Create `src/LocalDeviceToolkit.Windows/Inspection/Probes/SecurityServiceProbes.cs`, `scripts/diagnostics/Get-SecurityServiceFacts.ps1`, `tests/LocalDeviceToolkit.Windows.Tests/SecurityServiceProbeTests.cs`; modify router and registry.

**Interfaces:** `SecurityServiceProbes : IProbeProvider` implements rows 13–14 and 20–22. `PrinterQuery(string PrinterName, bool AllowRemoteRead)` goes in InspectionContracts; the catalogue validates length, control characters and literal lookup. `DefenderState(string Mode, bool? ProtectionEnabled, string? SignatureVersion, DateTimeOffset? SignatureTime, string? ConfiguredSourceOrder)` is an allowlisted record in `Core/LocalProbeValues.cs`; source-order absence is distinct from unreadability.

- [ ] Write `Passive_Defender_is_not_active`, `Signature_age_does_not_prove_update_available`, `Time_status_does_not_resync`, `Denied_print_queue_is_not_empty`, `Remote_queue_requires_disclosure`, `Print_document_names_never_persist`. Assertions: `Assert.Equal(0, resyncCalls); Assert.False(queue.Coverage.Complete); Assert.DoesNotContain(documentTitle, storedJson);`.
- [ ] Run `toolkit_windows_test 'FullyQualifiedName~SecurityServiceProbeTests'` to establish failures.
- [ ] Project `Get-MpComputerStatus` into separate requested protection/signature values, plus allowed source configuration. Third-party product names give guidance only. Read local `w32tm /query` results, never stripchart/resync. Read spooler and selected printer/jobs without service changes or test prints; disclose shared-queue access and retain provider failures. No job owner/title fields in any persisted output.
- [ ] Rerun filter and registry checks; fake host must record zero mutation invocations across every input case.
- [ ] Commit: `feat: add security time and printer checks`.

## Task 7: Bounded incident event collection

**Files:** Create `src/LocalDeviceToolkit.Core/IncidentValues.cs`, `src/LocalDeviceToolkit.Windows/Inspection/Probes/IncidentEventProbe.cs`, `EventFieldProjection.cs` in the same directory, `tests/LocalDeviceToolkit.Windows.Tests/IncidentEventProbeTests.cs`. Modify router and capture validators.

**Interfaces:** `IncidentQuery(string Symptom, DateTimeOffset FromUtc, DateTimeOffset ToUtc, string? Cursor)`; `EventEvidence(string Channel, string Provider, int EventId, long RecordId, DateTimeOffset TimeUtc, string Resource, IReadOnlyDictionary<string,string> Fields)`; `EventPage(IReadOnlyList<EventEvidence> Events, CheckCoverage Coverage, string ChannelGeneration)` live in IncidentValues. `IncidentEventProbe : IProbeProvider` owns row 23. `EventFieldProjection.Project(string provider, int eventId, IReadOnlyDictionary<string,string>)` returns only registered safe fields; unknown event templates retain metadata, not their free text.

- [ ] Write `Seven_day_window_and_200_record_page_enforced`, `Payload_limit_preserves_omission_flag`, `Channel_denied_or_cleared_marks_gap`, `Cursor_rejects_changed_query_or_generation`, `Sensitive_message_never_serialized`. Assertions: `Assert.True(page.Events.Count <= 200); Assert.True(serializedBytes <= 256*1024); Assert.False(gapped.Coverage.Complete); Assert.DoesNotContain(secret, json);`.
- [ ] Run `toolkit_windows_test 'FullyQualifiedName~IncidentEventProbeTests'` to establish failures.
- [ ] Use local Windows event APIs with server-side provider/time filters and newest-first reads. Bind a validated cursor to request digest, channel generation and last record; never accept an arbitrary event query from UI. Apply the spec's seven-day, 200-record, 256 KiB and 30-second limits. Reserve space for coverage before including events; if a body cannot fit, keep its permitted identity and record omission. A changed/cleared channel invalidates the cursor and preserves the gap. Collection of another page is a separately logged explicit request.
- [ ] Rerun filter with cancellation, disabled channels, missing resources and record-ID reuse fixtures.
- [ ] Commit: `feat: collect bounded incident evidence with coverage`.

## Task 8: Deterministic quiet diagnostic interpretation

**Files:** Create `src/LocalDeviceToolkit.Engine/Diagnostics/DiagnosticModels.cs`, `DiagnosticInterpreter.cs`, `IncidentGrouper.cs`, `DiagnosticRuleCatalogue.cs` in that directory, `tests/LocalDeviceToolkit.Tests/DiagnosticInterpreterTests.cs`, `IncidentGroupingTests.cs` in the same directory.

**Interfaces:** `DiagnosticFinding(string Id, string RuleVersion, string Observed, string Why, string NextStep, string Confidence, bool SecurityRelevant, bool RequiredUnknown, IReadOnlyList<Guid> CaptureIds, string? SuggestedOperationId)`; `DiagnosticSummary(IReadOnlyList<DiagnosticFinding> Expanded, IReadOnlyList<DiagnosticFinding> Remaining, IReadOnlyList<DiagnosticFinding> Signposts)`. `DiagnosticInterpreter.Interpret(IncidentQuery, IReadOnlyList<CapturedChecks>)` returns `DiagnosticSummary`. `IncidentGrouper.Group(IReadOnlyList<EventEvidence>)` returns groups retaining provider/signature/resource/window/channel/record references, counts and first/last times; define `EventGroup` in DiagnosticModels.

- [ ] Write `Expanded_findings_are_limited_but_security_and_unknown_remain_signposted`, `Same_event_id_different_resource_is_not_deduplicated`, `Dcom_requires_exact_documented_identifiers`, `Event_41_does_not_name_root_cause`, `No_evidence_with_gaps_is_not_healthy`. Assertions: `Assert.True(summary.Expanded.Count <= 3); Assert.Contains(summary.Signposts, x=>x.RequiredUnknown); Assert.DoesNotContain("faulty power supply", finding.Why);`.
- [ ] Run `toolkit_engine_test 'FullyQualifiedName~DiagnosticInterpreterTests|FullyQualifiedName~IncidentGroupingTests'` to establish failures.
- [ ] Rank security/blockers, direct symptom/time/target matches and supporting observations deterministically. Keep findings separate from M365 assessment. Version each rule and attach evidence IDs; suggestions contain only registered operation IDs and never queue a change. DCOM benign rules include source reference and exact identifiers from the research. Use the three confidence phrases from the spec; preserve background evidence and all coverage limitations.
- [ ] Rerun filters; assert deterministic ordering for shuffled input and equal timestamps and no false completeness from combining unrelated capture contexts.
- [ ] Commit: `feat: summarize diagnostics without hiding uncertainty`.

## Task 9: Fresh approval, projected state and separate outcomes

**Files:** Create `src/LocalDeviceToolkit.Engine/Workbench/ReviewLifetime.cs`, `src/LocalDeviceToolkit.Engine/Planning/ReviewedStatePolicy.cs`, `src/LocalDeviceToolkit.Core/OperationalOutcome.cs`, `src/LocalDeviceToolkit.Engine/Evidence/ExecutionEvidenceV2.cs`, `tests/LocalDeviceToolkit.Tests/ReviewLifetimeTests.cs`, `ReviewedStatePolicyTests.cs`, `OperationalOutcomeTests.cs` in the same directory. Modify PlanService, RunCoordinator, ProtectedActionService, WorkerProtocol, WindowsActionDispatcher, ReviewRunService, RunTextLog, `src/LocalDeviceToolkit.Engine/Execution/ActionContracts.cs`, `src/LocalDeviceToolkit.Core/Models.cs` and their evidence validators. Core owns the three outcome/run records; Planning owns projections/wrappers/state policy; ActionContracts owns IReviewedOperation, avoiding a Core-to-Engine reference.

**Interfaces:** `ReviewLifetime(TimeProvider)` exposes `void Start(Guid planId)` and `bool IsValid(Guid planId, DateTimeOffset expiresAt)`. `DispatchProjection(string PolicyId, JsonElement ExpectedAtDispatch, IReadOnlyList<Guid> RequiredPriorActions, string Digest)` is distinct from measured `PlannedAction.Before`. `IReviewedStatePolicy.Project(PlannedAction, IReadOnlyList<PlannedAction>)` returns `DispatchProjection`; `Validate(DispatchProjection, IReadOnlyList<ProbeObservation> fresh, IReadOnlyList<ResultRecord> verifiedPrefix)` returns `PlanValidation`. Define `ReviewedExecution(int SchemaVersion, ReviewedPlan Plan, IReadOnlyDictionary<Guid,DispatchProjection> Projections, string Digest)`, `ActionExecution(ActionResult Result, OperationalOutcome Outcome)` and `ExecutedRun(RunRecord Run, IReadOnlyDictionary<Guid,OperationalOutcome> Outcomes, string ReviewDigest)`. `OperationalOutcome(bool? ActionCompleted, bool? ExpectedStateVerified, bool? SymptomResolved, bool SkippedAsUnneeded, string Reason)` is supplementary to the existing result. `IReviewedOperation : ILocalOperation` adds `Task<ActionExecution> ExecuteReviewedAsync(DeviceContext, PlannedAction, DispatchProjection, IReadOnlyList<ResultRecord> verifiedPrefix, CancellationToken)`; new projected operations reject direct calls to their legacy ExecuteAsync entry point.

- [ ] Write `Clock_rollback_cannot_extend_ten_minutes`, `Restarted_session_has_no_valid_review`, `Projected_state_never_replaces_measured_before`, `Missing_or_uncertain_prefix_blocks`, `Policy_id_cannot_be_selected_by_request`, `Result_does_not_equate_action_with_symptom`. Assertions: after advancing elapsed time by 601 seconds and moving UTC backward, `Assert.False(lifetime.IsValid(plan.Id, plan.ExpiresAt)); Assert.Equal(originalBefore, action.Before); Assert.Null(outcome.SymptomResolved);`.
- [ ] Run `toolkit_engine_test 'FullyQualifiedName~ReviewLifetimeTests|FullyQualifiedName~ReviewedStatePolicyTests|FullyQualifiedName~OperationalOutcomeTests|FullyQualifiedName~RunProgressTests'` to establish failures.
- [ ] Bind lifetime to the current app process with UTC and elapsed checks; invalidate on OS resume until native clock behavior is proven. Select state policy only from registered implementation code; existing operations retain strict fingerprint comparison. Store projections and independent outcome fields in new `plan.v2`/`intent.v2`/`result.v2`/`run.v2` payload kinds, preserving frozen legacy readers. Keep ReviewedPlan/PlannedAction DTOs stable inside the version-2 ReviewedExecution wrapper. Recompute its digest with Digest empty, carry the complete wrapper to the worker and require protected authoritative prior results; UI files alone cannot supply a successful prefix. Add `PlanService.CreateExecutionAsync(DeviceContext, LocalCatalogue, IReadOnlyList<ActionRequest>, CancellationToken): Task<ReviewedExecution>` and change the live coordinator/dispatcher path to consume ReviewedExecution, returning ExecutedRun/ActionExecution respectively. The dispatcher invokes IReviewedOperation only after wrapper/prefix validation; an ordinary operation still executes through its exact-state path. No implicit replay of old plans. Append `SkippedAsUnneeded` to ActionOutcome, allow it only in version-2 results with no write and known fresh evidence, and let that explicit no-write case continue the queue; never map it to AppliedVerified.
- [ ] Rerun filters plus PlanningTests, ProtectedActionTests, MalformedPlanTests, ReviewRunTests and RunTextLogTests. Before-write log failure blocks dispatch; after-write failure preserves observed/uncertain outcome and stops remaining actions. Validate projections again after any UAC delay.
- [ ] Commit: `feat: bind reviewed actions to fresh state and distinct outcomes`.

## Task 10: Four independent power timeouts and restoration

**Files:** Create `src/LocalDeviceToolkit.Core/PowerTimeoutValues.cs`, `src/LocalDeviceToolkit.Engine/Settings/PowerTimeoutOperation.cs`, `PowerStatePolicy.cs`, `PowerTimeoutRestoration.cs` in that directory, `src/LocalDeviceToolkit.Windows/Settings/WindowsPowerBackend.cs`, `tests/LocalDeviceToolkit.Tests/PowerTimeoutTests.cs`, `PowerSequenceTests.cs`, `PowerRestorationTests.cs` in the same directory, `tests/LocalDeviceToolkit.Windows.Tests/PowerIdentityTests.cs`. Modify FixedSettingOperation, WindowsSettingsBackend, WindowsOperations, RecordedSettingRestoration and its evidence source, plus catalogue tasks/workflows/pins.

**Interfaces:** `PowerTimeoutSlot { AcSleep, DcSleep, AcDisplay, DcDisplay }`; `PowerTimeoutInput(PowerTimeoutSlot Slot, uint Seconds, bool ConfirmManagedPolicyReview)`; `PowerSnapshot(Guid Scheme, uint AcSleep, uint DcSleep, uint AcDisplay, uint DcDisplay)`. `IPowerBackend.ReadAsync(DeviceContext, CancellationToken)` returns `Task<PowerSnapshot>`; `InspectAsync(PowerTimeoutSlot, DeviceContext, CancellationToken)` returns `Task<ActionReadiness>`; `WriteAsync(PowerTimeoutInput, PowerSnapshot expected, DeviceContext, CancellationToken)` returns `Task`. Four `IReviewedOperation` instances have IDs `settings.power.ac-sleep`, `settings.power.dc-sleep`, `settings.power.ac-display`, `settings.power.dc-display`. `PowerStatePolicy : IReviewedStatePolicy` is the only ordered-setting projection in this release.

- [ ] Write `Each_slot_preserves_three_others`, `Target_accepts_never_or_one_to_1440_minutes`, `Two_selected_slots_use_verified_prefix`, `Legacy_ac_sleep_conflicts_with_new_target`, `Alternate_admin_is_refused_before_write`, `Plan_switch_or_unexpected_index_blocks`, `Restore_preserves_exact_odd_seconds`. Assertions include `Assert.Equal(before.DcSleep, after.DcSleep); Assert.Equal(0, backend.Writes); Assert.Equal(61u, restored.AcSleep);`. Test 0, 60, 86400 accepted; 1 and 86460 rejected as new targets, while raw restoration remains exact.
- [ ] Run engine PowerTimeout/PowerSequence/PowerRestoration filters and Windows PowerIdentityTests; establish failures.
- [ ] Extract only power-specific native code from the existing settings backend. Whitelist the two subgroup/setting GUID pairs from the reviewed documentation, AC/DC identity and original SID; no caller-supplied arbitrary GUID. Read policy sources and preserve active scheme. Compute each expected transition from the reviewed prefix, then validate successful protected results and all four actual indices before the next write. Recheck scheme before activation; report partial write/activation or concurrent drift honestly. Route the legacy no-AC-sleep operation through the same backend/state policy, keeping its fixed Never target and existing historical-restoration reader; share the AC-sleep resource key and original-SID guard. Restoration derives exact values from verified protected records, not user-entered old values.
- [ ] Rerun filters plus FixedSettingTests, RestorationTests, RecordedRestorationReviewEvidenceTests and CrossOperationQueueTests. Real power writes are deferred to Task 16's disposable Windows target.
- [ ] Commit: `feat: add separately reviewed power timeout changes`.

## Task 11: Reviewed DNS cache refresh

**Files:** Create `src/LocalDeviceToolkit.Engine/Diagnostics/DnsCacheOperation.cs`, `DnsStatePolicy.cs` in that directory, `src/LocalDeviceToolkit.Windows/Diagnostics/WindowsDnsCacheBackend.cs`, `scripts/diagnostics/Invoke-DnsCacheRefresh.ps1`, `tests/LocalDeviceToolkit.Tests/DnsCacheOperationTests.cs`, `tests/LocalDeviceToolkit.Windows.Tests/DnsCacheScriptTests.cs`. Modify WindowsOperations, script registry and task/workflow pins.

**Interfaces:** `DnsCacheRefreshInput(DnsQuery Query, TcpQuery? RetestEndpoint, bool ConfirmWholeCacheEffect, bool ConfirmStaleCacheHypothesis)`; `IDnsCacheBackend.ReadContextAsync(CancellationToken)` returns `Task<NetworkContext>`; `ClearAsync(CancellationToken)` returns `Task`; `DnsCacheOperation : IReviewedOperation` uses ID `diagnostics.refresh-dns-cache` and `DnsStatePolicy : IReviewedStatePolicy`. The operation also consumes the registered network provider for explicitly reviewed verification.

- [ ] Write `Unconfirmed_or_round_robin_only_hypothesis_blocks`, `Ttl_drift_is_not_identity_drift`, `Vpn_resolver_change_requires_review`, `Resolved_hypothesis_skips_write`, `Clear_success_does_not_resolve_symptom`, `Unselected_endpoint_is_not_contacted`. Assertions: `Assert.Equal(0, clearCalls); Assert.Null(outcome.SymptomResolved); Assert.Equal(0, endpointRetests);` for the respective scenarios.
- [ ] Run engine DnsCacheOperationTests and Windows DnsCacheScriptTests to establish failures.
- [ ] Bind stable target/network/policy identity separately from timestamps/TTLs; preserve full permitted observations. Execute only fixed `Clear-DnsClientCache` once under reviewed privilege. Disclose whole-device client cache effect and no exact restoration. If fresh evidence no longer supports the hypothesis, emit a no-write result. No DNS-server changes, public DNS query, retry loop or “Fixed” from command completion.
- [ ] Rerun filters, registry parsing and ReviewRunTests; inject log failure before/after execution and verify zero extra dispatches.
- [ ] Commit: `feat: add reviewed local DNS cache refresh`.

## Task 12: Defender signature request with uncertainty guard

**Files:** Create `src/LocalDeviceToolkit.Engine/Diagnostics/DefenderSignatureOperation.cs`, `DefenderStatePolicy.cs`, `DiagnosticActionPending.cs` in that directory, `src/LocalDeviceToolkit.Windows/Diagnostics/WindowsDefenderBackend.cs`, `scripts/diagnostics/Invoke-DefenderSignatureUpdate.ps1`, `tests/LocalDeviceToolkit.Tests/DefenderSignatureOperationTests.cs`, `tests/LocalDeviceToolkit.Windows.Tests/DefenderSignatureScriptTests.cs`. Modify WindowsOperations, protected worker operation registration and manifests.

**Interfaces:** `DefenderSignatureInput(bool ConfirmNetworkSources)`; `IDefenderBackend.ReadAsync(CancellationToken)` returns `Task<DefenderState>`; `RequestUpdateAsync(CancellationToken)` returns `Task<DiagnosticProcessState>`. Define `DiagnosticProcessState(int? Pid, DateTimeOffset? StartedAt, bool Completed, bool OutcomeKnown)` and `DiagnosticActionPending(Guid ActionId, string Target, DeviceContext Context, DateTimeOffset At, DiagnosticProcessState? Process)`. `IDiagnosticActionPendingStore` exposes `Task ClaimAsync(DiagnosticActionPending, CancellationToken)`, `Task<DiagnosticActionPending?> ReadAsync(string target, CancellationToken)`, `Task RecordProcessAsync(string target, Guid actionId, DiagnosticProcessState, CancellationToken)` and `Task ResolveAsync(string target, Guid actionId, CancellationToken)` through protected immutable records; an existing unresolved claim makes ClaimAsync fail. The operation implements IReviewedOperation with ID `diagnostics.update-defender-signatures`; only its trusted reconciliation service can call ResolveAsync after validating completion evidence.

- [ ] Write `Passive_or_unknown_source_blocks`, `Configured_and_default_sources_are_disclosed`, `Command_has_no_source_override`, `Unchanged_version_is_not_fake_upgrade`, `Uncertain_process_blocks_replay_after_restart`, `Cancellation_after_dispatch_is_not_not_started`. Assertions: `Assert.DoesNotContain("-UpdateSource", arguments); Assert.Equal(0, secondRequestCount); Assert.Null(outcome.ExpectedStateVerified);` when update state is uncertain.
- [ ] Run the DefenderSignature engine/Windows filters; observe failing behavior before adding the operation.
- [ ] Launch only bare `Update-MpSignature` in the fixed registered script using the mutation runner's lifetime semantics. Active Defender and readable source policy are required. Preserve Windows source order/default distinction and do not force updates from a new source. Persist a pending claim before dispatch; timeout/UI cancellation does not establish process completion. Reconciliation rechecks observed process identity, Defender update state and fresh signatures; unresolved state prevents another request. Same version can be current; record what was actually observed and leave unsupported “latest” claims Unknown.
- [ ] Rerun filters, MutationProcessTests and protected journal tests. Ensure no killing of a running updater merely because the UI stopped waiting, no silent retry and no advertised rollback.
- [ ] Commit: `feat: request Defender updates with guarded reconciliation`.

## Task 13: Application deployment verification dimensions

**Files:** Create `src/LocalDeviceToolkit.Engine/Deployments/ApplicationVerification.cs`, `src/LocalDeviceToolkit.Engine/Reporting/ManualCheck.cs`, `src/LocalDeviceToolkit.Windows/Inspection/Probes/ApplicationVerificationProbe.cs`, `tests/LocalDeviceToolkit.Tests/ApplicationVerificationTests.cs`. Modify OperationalProbeRouter and existing deployment/report consumers without altering installer execution defaults.

**Interfaces:** Define `ManualCheck(Guid Id, string ItemId, string TargetDigest, DeviceContext Context, ManualReviewOutcome Outcome, string Note, ExecutionActor Actor, DateTimeOffset At)` once in Reporting/ManualCheck.cs. `ApplicationQuery(string ProfileId)` is the row-24 input; it resolves through the existing approved profile store, never accepts an arbitrary install command. `VerificationState { Verified, Failed, Unknown, ManualRequired, NotApplicable }`; `ApplicationVerification(VerificationState PackageChecked, VerificationState Installed, VerificationState Configured, VerificationState SignedIn, VerificationState Licensed, VerificationState Restart, VerificationState Verified, IReadOnlyList<string> Reasons)`. `ApplicationVerificationService.Evaluate(DeploymentProfile, DeploymentInventory, DeploymentReconciliation?, IReadOnlyList<ManualCheck>)` returns that record.

- [ ] Write `Installed_does_not_mean_configured_or_licensed`, `User_scope_mismatch_is_not_verified`, `Pending_process_prevents_ready`, `Offline_bootstrapper_is_not_complete_media`. Assertions: `Assert.Equal(VerificationState.Verified, result.Installed); Assert.Equal(VerificationState.ManualRequired, result.Licensed); Assert.NotEqual(VerificationState.Verified, result.Verified);`.
- [ ] Run `toolkit_engine_test 'FullyQualifiedName~ApplicationVerificationTests|FullyQualifiedName~DeploymentTests'` to establish failures.
- [ ] Implement selected-profile inventory through existing approved package/MSIX/Office logic, including exact identity/version/scope and pending guards. Keep configuration, sign-in, activation and enrollment as separate evidence/manual requirements. No package acquisition, dependency installation or installer replay occurs during verification. Verify manual records against the original device/user and target; an attestation for another account cannot complete this application's readiness.
- [ ] Rerun filters plus DeploymentWorkerRequestTests and Windows deployment inventory fixtures.
- [ ] Commit: `feat: separate application readiness from installation`.

## Task 14: Setup and diagnostic UI integration

**Files:** Create `src/LocalDeviceToolkit.Engine/Workbench/EngineerSelectionModel.cs`, `DiagnosticViewModel.cs` in that directory, `src/LocalDeviceToolkit.App/ProbeInputWindow.cs`, `DiagnosticFindingsView.cs`, `PowerTimeoutInputWindow.cs` in that directory, `tests/LocalDeviceToolkit.Tests/EngineerSelectionTests.cs`. Modify MainWindow.xaml/.cs, TaskSelectionRow.cs, ReviewRunWindow.cs, App.xaml.cs, ReviewRunService, ActionPreview and catalogue tasks/workflows/pins.

**Interfaces:** `EngineerSelectionModel.Suggest(string scenario)` returns ordered task IDs without selecting them; `Select(string taskId, WorkflowOrigin, JsonElement parameters, bool changeRequested)` changes one selection; `Visible(string filter)` returns rows; `HiddenSelectionCount(string filter)` returns int. `DiagnosticViewModel.Show(DiagnosticSummary)` supplies Expanded/Remaining/Signposts and exact evidence references. Extend `ReviewSelection` with explicit check parameters and target digest, keeping action parameters separate. Inputs reference the typed records from Tasks 3–7 and 10–13.

- [ ] Write `Suggest_never_selects`, `Navigation_preserves_explicit_choices`, `Filtered_selections_are_counted`, `Changing_target_invalidates_review`, `Details_do_not_collect_more_events`, `Two_contexts_do_not_share_fresh_status`. Assertions: `Assert.Empty(model.Visible("").Where(r=>r.Selected)); Assert.Equal(2, model.HiddenSelectionCount("battery")); Assert.False(review.CanApply);` for their respective setups.
- [ ] Run `toolkit_engine_test 'FullyQualifiedName~EngineerSelectionTests|FullyQualifiedName~ReviewRunTests|FullyQualifiedName~ActionPreviewTests'` to establish failures.
- [ ] Retain the setup table/list-details design and all existing tasks. Add scenario/symptom suggestions, explicit incident range, typed target inputs and All tasks navigation. Use Check and review, Confirm selected changes and Run and verify wording. Render at most three expanded findings with visible unknown/security counts. Show exact sources and copyable permitted values in details; Next page explicitly creates a linked new collection log. Show current/expected/account, power targets, network/effect disclosures and restoration limits before confirmation. Add separate three-part action outcomes and never promote a check to a change on navigation.
- [ ] Rerun filters and native fixture rendering in Task 16. Verify tab order, accessible checkbox names, minimum column widths and scrolling at the current compact dimensions; no hidden disabled placeholder actions.
- [ ] Commit: `feat: integrate engineer checks and quiet diagnostic review`.

## Task 15: Handover, Settings guidance and run comparison

**Files:** Create `src/LocalDeviceToolkit.Engine/Reporting/HandoverService.cs`, `RunComparisonService.cs`, `ReportDisclosure.cs` in that directory, `src/LocalDeviceToolkit.Windows/Settings/GuidedSettings.cs`, `src/LocalDeviceToolkit.App/HandoverWindow.cs`, `tests/LocalDeviceToolkit.Tests/HandoverTests.cs`, `RunComparisonTests.cs`, `ReportDisclosureTests.cs` in the same directory. Modify ReferenceProfiles, ReportModels, ReportService, ReportExporter, ReportsWindow, existing manual review UI and App composition; consume Task 13's ManualCheck.

**Interfaces:** `HandoverService.Evaluate(ClientChecklist, IReadOnlyList<CapturedChecks>, IReadOnlyList<ManualCheck>, IReadOnlyList<OperationalOutcome>)` returns `HandoverResult(string State, IReadOnlyList<string> Outstanding, IReadOnlyList<string> Exceptions)`. `RunComparisonService.Compare(IReadOnlyList<CapturedChecks> before, IReadOnlyList<CapturedChecks> after)` returns `IReadOnlyList<ComparedValue>`; `ComparedValue(string CheckId, string TargetDigest, string State, string Before, string After)`. `void GuidedSettings.Open(string registeredId)` launches only its fixed URI allowlist. `ReportDisclosure.Project(LocalReport, bool includePermittedIdentifiers)` returns `ReportExportView(ReportPurpose Purpose, DateTimeOffset GeneratedAt, string DeviceLabel, string UserLabel, IReadOnlyList<ReportItemView> Items, IReadOnlyList<string> Limitations)`, where `ReportItemView(string Title, string State, string Summary, IReadOnlyDictionary<string,string> Details)` carries permitted source references/fields. Define both export records in ReportDisclosure.cs; do not redact a DeviceCapture and misrepresent it as original digest-valid evidence.

- [ ] Write `Unknown_required_item_blocks_handover`, `Exceptions_are_not_automated_passes`, `Manual_test_is_specific_to_original_user_and_target`, `Different_schema_or_target_is_not_comparable`, `Open_settings_is_not_verified_change`, `Export_minimizes_identifiers_and_never_includes_secrets`. Assertions: `Assert.NotEqual("Ready for handover", result.State); Assert.Equal("Not comparable", comparison.State); Assert.DoesNotContain(secret, export);`.
- [ ] Run engine Handover/RunComparison/ReportDisclosure filters plus ReportingTests to establish failures.
- [ ] Add ticket/client metadata, strictly validated expectation profiles and eleven independent manual items from the spec. Profiles never preselect tasks or grant execution authority; missing expected values remain unknown. Provide eight independently launched Settings destinations, validating supported fixed URIs and recording launch separately from verification. Build concise handover/ticket summaries and compatible two-run comparisons with dates/limitations. HTML encodes content; CSV keeps formula protection; JSON includes only the selected disclosure projection. Never export raw dumps, recovery passwords, printer titles, credentials or PAC secrets.
- [ ] Rerun filters plus existing manual-review/report-export cases. Test malformed note markup, formula-leading values, denied source records and partial historical capture coverage.
- [ ] Commit: `feat: add clear handover and comparable run reports`.

## Task 16: Whole-branch verification, native UI and release evidence

**Files:** Modify `src/LocalDeviceToolkit.App/MainWindow.xaml.cs` smoke fixtures, `.github/workflows/verify.yml`, `docs/LOCAL-USAGE.md`, `docs/IMPLEMENTATION-STATUS.md`, `docs/WINDOWS-ACCEPTANCE.md`; add `tests/LocalDeviceToolkit.Tests/EngineerWorkflowTests.cs` and `tests/LocalDeviceToolkit.Windows.Tests/EngineerProbeIntegrationTests.cs`. Update README only to describe delivered behavior. Existing `build/Verify-ScriptRegistry.ps1`, `Publish-Portable.ps1` and `Test-Portable.ps1` remain the verification entry points.

**Interfaces:** Consume all prior task contracts. Extend existing `--smoke-output` native mode with synthetic, explicitly labelled fixtures for new task inputs, quiet findings, blocked actions, power review, handover and comparison; smoke rendering must never execute mutations. Preserve the existing real read-only review-log exercise.

- [ ] Write end-to-end fixture tests `Select_check_review_confirm_verify_uses_one_log`, `Unselected_checks_and_actions_never_run`, `Fresh_state_and_logging_failure_stop_queue`, `Protected_sequence_rejects_forged_prefix`, `Old_evidence_is_viewable_but_not_replayable`. Assert timestamps, actor, exact targets, before/after values, unchanged unselected values and separate outcomes; include all 24 registered check IDs and six action IDs in the coverage matrix without superficial count-only acceptance.
- [ ] Run their engine/Windows filters; observe any missing integration behavior and fix it within the owning task contract. Do not relax assertions, silently skip live cases or replace native acceptance with fixture success.
- [ ] Run sequential full verification:

```bash
dotnet restore LocalDeviceToolkit.sln --locked-mode -m:1
dotnet build LocalDeviceToolkit.sln -c Release --no-restore -warnaserror -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test LocalDeviceToolkit.sln -c Release --no-restore -warnaserror -m:1 -p:UseSharedCompilation=false -nodeReuse:false --logger trx --results-directory TestResults/engineer-readiness
pwsh -NoLogo -NoProfile -File build/Verify-ScriptRegistry.ps1
```

Expected: all intended tests execute; no build warnings/errors or unexpected skips/failures. On Windows, run the existing PowerShell 5.1 parsing job, native tests, portable publish/integrity check and extracted-app smoke with SDK paths removed. Inspect actual normal/compact renders; preserve the 21 existing captures and add the new windows. Reject mixed packages or missing scripted providers.
- [ ] Complete the documented disposable Windows 11 matrix: same-user and alternate-user UAC; four power slots and restoration; concurrent plan/policy change; S0/S3/desktop/VM/battery cases; VPN/DNS changes; offline/Defender/third-party AV; local/shared/inaccessible printers; 30-second read bounds; app suspend/resume and review expiry; keyboard/high contrast/Narrator. Run only selected reviewed changes and record exact results. If the current environment cannot perform these tests, list them as pending and do not claim Windows acceptance or production readiness.
- [ ] Obtain the execution method's independent whole-branch review, fix material findings, rerun affected checks, update usage/status with actual counts and limitations, then commit `test: verify engineer readiness workflow and native package`. Prepare a reviewable PR when implementation and available checks are complete; attach it to the task after creation. No automatic merge or environment publication.

## Spec coverage and self-review record

| Approved design section | Owning tasks |
| --- | --- |
| 1–2 Scope, independent selections and simple vocabulary | 1–2, 14 |
| 3 All 24 exact checks and projection limits | 2–7, 13 |
| 4 Six actions, power sequencing/restoration, volatile DNS/Defender state | 9–12 |
| 5 Quiet findings, grouping, confidence and coverage | 7–8, 14 |
| 6 Deployment dimensions, Settings links, manual handover and comparison | 13–15 |
| 7 Exact capture, schema compatibility, provenance and focused providers | 1–2, 9 |
| 8 Logs, original user, privacy, expiry and uncertain outcomes | 1, 3–7, 9–12, 15 |
| 9 Automated/native acceptance | Every task's tests; 16 |
| 10 Deferred scopes | Global constraints; no implementation tasks |

Self-review completed: paths resolve or are explicitly planned new files; shared interfaces have one owner; profiles cannot add hidden checks or execution authority; observed state is distinct from projection; source controls remain unchanged; native acceptance is distinct from Linux verification. Task 13 owns ManualCheck and Task 15 consumes it. Supplementary execution wrappers are versioned separately from unchanged legacy DTOs. The task/step/interface, local-link, source-reference and whitespace checks passed; application tests have not been run for this documentation-only change.

**Execution recommendation:** Native execution in this session, with one fresh independent whole-branch reviewer at the end. The tasks repeatedly touch the same capture/worker/plan interfaces, so keeping their implementation context together reduces integration overhead. Subagent-driven execution remains available for an independent review after every task at greater context cost.

**Execution:** Native selected by the user. Source implementation is delivered; review fixes and available verification are recorded in [the execution record](../2026-10-16-engineer-readiness-execution.md). The original detailed checkboxes are retained as the planned verification matrix; real Windows 11/device acceptance remains explicitly pending. No production-readiness claim is made from Linux fixtures.
