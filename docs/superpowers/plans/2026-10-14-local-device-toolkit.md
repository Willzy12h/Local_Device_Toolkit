# Local Device Toolkit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Build a native engineer toolkit that assesses the current Windows device, applies reviewed local changes with appropriate privilege, and records independently verified outcomes.

**Architecture:** Portable Core/Engine projects own contracts, planning and evidence. Windows adapters implement approved operations; a separate short-lived WPF worker performs privileged operations after authenticated IPC and its own review. The native application composes these services and never executes arbitrary commands.

**Tech Stack:** C# 12, .NET 10 SDK 10.0.401, WPF, Windows PowerShell 5.1 where needed, System.Text.Json, xUnit, self-contained win-x64 distribution.

**Spec:** [Native toolkit design](../specs/2026-10-14-local-device-toolkit-design.md).

**Status:** Proposed plan. No task below has been implemented or tested. The user's request authorises reviewing and improving the current work; the written implementation plan and execution method still require review under the requested Superpowers workflow.

## Global Constraints

- Use the existing isolated cloud checkout at /workspace/Local_Device_Toolkit; do not create another worktree unless requested.
- Source baseline: Willzy12h/M365-Buildstandards, commit 80f01a9482c0ae20e995f9df357c9fca9217f623; preserve provenance and source notices for adapted files.
- Use C# 12 and .NET 10 with SDK 10.0.401 as the initial build baseline. Ship self-contained win-x64 packages.
- Supported deployment targets are Windows 11 x64; other Windows versions may be inventoried but do not receive an unsupported-device readiness claim.
- The main application uses asInvoker and does not collect or store administrator credentials.
- Current user means the person running the application. User-scope changes must never be redirected into an alternate administrator's profile.
- No tenant sign-in, browser, WebView2, Node, Docker, web server, background service or scheduled SYSTEM task.
- Preserve all 30 source controls. Classify actual local coverage explicitly; do not execute Graph payloads or infer cloud effectiveness from local state.
- Plans expire after ten minutes. Revalidate context, inputs, policy prerequisites and before-state immediately before each action.
- Limit IPC frames to 1 MiB, evidence records to 8 MiB, diagnostic output to 1 MiB per stream, and history pages to 100 entries.
- Worker IPC has a 30-second connection deadline after successful UAC launch. UAC waiting never refreshes plan expiry.
- Persist intent before mutation. Never automatically replay an ambiguous operation or use diagnostic tree-kill behaviour for an installer.
- No application code, tests, installer payloads or launchers exist yet; commands below describe future checks, not completed validation.

## Review Focus

1. Alternate administrator credentials: HKCU/profile actions still target the original engineer; wrong peer/process/context is refused. T5/T6.
2. Paths with spaces, non-ASCII characters, reparse points or substituted dependencies: valid paths work, unsafe or changed inputs cannot reach elevated dispatch. T2/T5/T8/T12.
3. Installer timeout, detached child, lost UI or restart exit code: retain uncertain/restart outcomes, never kill/replay or equate exit zero with verified installation. T3/T8/T11.
4. Unreadable/duplicate-member history or a full evidence volume: preserve healthy history and refuse writes without durable intent. T2/T3/T11.
5. Missing policy/OS evidence, managed conflicts or unsupported Windows builds: show the limitation and block dependent changes without inventing defaults. T1/T3/T4/T7.

Each task owns the tests named against these conditions. Use synthetic identities and fixtures; real device acceptance is a separately recorded Windows activity.

## Files and shared interfaces

Create LocalDeviceToolkit.sln, global.json, Directory.Build.props, Directory.Packages.props, .gitignore and these projects as their first owning task needs them:

- src/LocalDeviceToolkit.Core: net10.0 models and validation primitives.
- src/LocalDeviceToolkit.Engine: net10.0 assessment, plans, execution coordination, evidence and exports.
- src/LocalDeviceToolkit.Windows: net10.0-windows OS and process adapters.
- src/LocalDeviceToolkit.App: net10.0-windows WPF interface.
- src/LocalDeviceToolkit.Worker: net10.0-windows WPF elevated review/worker.
- tests/LocalDeviceToolkit.Tests: portable engine and contract tests.
- tests/LocalDeviceToolkit.Windows.Tests: Windows adapter, IPC and worker tests.
- tests/LocalDeviceToolkit.App.Tests: view-model and WPF workflow tests.
- tests/LocalDeviceToolkit.UiReview: Windows UI interaction/render harness.

Keep models immutable. T1 defines these shared types; later tasks must use the same names:

- DeviceContext(DeviceId, ComputerName, OriginalUserSid, OriginalUserName, IsElevated, OsVersion, Architecture): string identity/name/architecture fields, bool elevation, Version OS version.
- ExecutionActor(Sid, UserName, IsElevated, ProcessId, ProcessStartedAt): OS-observed string identity fields, bool, int and UTC DateTimeOffset. This records the actual executing account separately from the original user in DeviceContext.
- CatalogueIdentity(Version, SourceCommit, SourceSha256, LocalSha256): string fields, distinct upstream provenance and packaged mapping identity.
- ProbeObservation(ControlId, CheckId, State, Value, Source, CapturedAt, Reason): string IDs/source/reason, ObservationState, nullable JsonElement value, DateTimeOffset capture. Values have per-check schemas; unavailable evidence has no invented value.
- DeviceCapture(Id, Context, Catalogue, CapturedBy, CapturedAt, Observations): Guid, DeviceContext, CatalogueIdentity, ExecutionActor, DateTimeOffset, IReadOnlyList<ProbeObservation>.
- Finding(ControlId, Status, Reason, Observations): FindingStatus is MeetsStandard, DoesNotMeetStandard, Unknown, RequiresElevation, NotApplicable or ManualVerification.
- ActionRequest(OperationId, Parameters): string ID and cloned immutable JsonElement validated by the registered operation schema.
- ActionReadiness(CanExecute, Scope, Privilege, Reason): bool, OperationScope CurrentUser/Machine, PrivilegeRequirement CurrentUser/Administrator, string. Scope and privilege are independent; an adapter determines actual required privilege.
- PlannedAction(Id, Request, Readiness, BeforeDigest): Guid, ActionRequest, ActionReadiness, string.
- ReviewedPlan(Id, Context, Catalogue, CreatedAt, ExpiresAt, Actions, Digest): Guid, the context/catalogue types, UTC DateTimeOffset values, IReadOnlyList<PlannedAction>, string.
- ActionResult(ActionId, Outcome, Actor, Reason, After): Guid, ActionOutcome, nullable ExecutionActor, string, IReadOnlyList<ProbeObservation>. Actor is null only if execution never began. ActionOutcome is AppliedVerified, AppliedUnverified, RestartRequired, Failed, CancelledBeforeExecution or OutcomeUnknown.
- RunRecord(Id, PlanId, Context, StartedAt, FinishedAt, Results): Guid IDs, DeviceContext, UTC DateTimeOffset start/nullable finish, IReadOnlyList<ActionResult>.

ObservationState is Known, AccessDenied, Unavailable, NotSupported or Manual. CoverageMode is Automatic, Partial or Manual. Enums are strict on deserialisation. Record IDs alone never authorise an action. Runtime package/catalogue digests are pins of the actual shipped bytes; the source SHA-256 in the reference describes the original 93-control upstream file and is not the digest of the extracted 30-control reference.

---

### T1: Verified local catalogue and source import foundation

**Files:** Create build/Import-Upstream.ps1, provenance/import-manifest.json, catalogue/local-windows.json, Core/Models/{Context,Catalogue,Observation,Planning,RunRecord}.cs, Core/Json/{StrictJson,CanonicalJson}.cs, Engine/Catalogue/CatalogueLoader.cs and tests/LocalDeviceToolkit.Tests/CatalogueTests.cs. Project-relative paths below are under src unless prefixed tests, build, scripts or catalogue. Modify the solution/build files listed above and preserve docs/reference/m365-windows-controls.json.

**Interfaces:** Produce LocalCatalogue with Identity and Controls; each LocalControl has Id, Name, ExpectedDescription, Coverage, CheckIds, OperationIds and ManualInstructions. CatalogueLoader.Load(string packageRoot, IReadOnlySet<string> knownChecks, IReadOnlySet<string> knownOperations) returns LocalCatalogue or throws CatalogueValidationException. StrictJson.Parse(ReadOnlyMemory<byte> bytes, int maximumBytes) rejects duplicate members and invalid UTF-8. Define TestData reference/context fixtures for later tests.

- [ ] Create the minimal Core/Engine/test project files with central pinned dependencies and write CatalogueTests.All_source_controls_have_truthful_coverage and Changed_reference_or_duplicate_members_are_refused.

Test assertions:

    Assert.Equal(30, catalogue.Controls.Count);
    Assert.Equal(expectedSourceIds, catalogue.Controls.Select(c => c.Id).Order());
    Assert.All(catalogue.Controls.Where(c => c.Coverage == CoverageMode.Manual),
        c => Assert.Empty(c.OperationIds));
    Assert.Throws<CatalogueValidationException>(() => LoadMutatedReference());

- [ ] Run dotnet test tests/LocalDeviceToolkit.Tests -c Release --filter FullyQualifiedName~CatalogueTests and confirm the missing loader/validation causes failure.
- [ ] Implement the contracts and loader. Start unimplemented controls as Manual; move coverage only when adapters/tests exist. Validate source IDs, local mapping pins, unknown/duplicate fields, enums and declared operation/check IDs. Adapt upstream Core/Json/CanonicalJson.cs with its relevant tests and recorded original hashes.
- [ ] Implement the source import command with the fixed commit, HTTPS Git authentication already supplied by the platform, destination containment and an explicit copied/adapted manifest. It must not overwrite existing files or update pins silently. Install SDK 10.0.401 from verified Microsoft release metadata only when execution begins; use /workspace/.tools/dotnet on this cloud host. Generate package lock files and use locked restores thereafter.
- [ ] Rerun CatalogueTests and dotnet build src/LocalDeviceToolkit.Engine -c Release -warnaserror. Require actual nonzero test execution and no warnings.
- [ ] Commit only the foundation, catalogue, provenance and corresponding test/build files.

### T2: Bounded evidence persistence and honest history loading

**Files:** Create Engine/Evidence/{EvidenceRecord,EvidenceSchemaRegistry,EvidenceStore,HistoryPage}.cs and tests/LocalDeviceToolkit.Tests/EvidenceStoreTests.cs.

**Interfaces:** EvidenceRecord(Guid Id, string Kind, int SchemaVersion, DeviceContext Context, DateTimeOffset RecordedAt, JsonElement Data, string Digest). IEvidenceStore.SaveNewAsync(EvidenceRecord record, CancellationToken ct) returns Task; LoadAsync(Guid id, CancellationToken ct) returns Task<EvidenceRecord>; ReadHistoryAsync(string? cursor, CancellationToken ct) returns Task<HistoryPage>. HistoryPage carries entries, per-entry diagnostics and NextCursor. Initially register the capture schema; later tasks add their own typed record schemas.

- [ ] Write EvidenceStoreTests.Damaged_entries_do_not_hide_healthy_history, Duplicate_members_and_escaping_paths_are_refused and Record_limit_is_enforced.

Test assertions:

    Assert.Single(page.Entries);
    Assert.Contains(page.Diagnostics, d => d.Code == "MalformedRecord");
    Assert.Throws<EvidenceValidationException>(() => ParseDuplicateMemberRecord());
    Assert.Throws<EvidenceValidationException>(() => ParseRecordOver(8 * 1024 * 1024));

- [ ] Run dotnet test tests/LocalDeviceToolkit.Tests -c Release --filter FullyQualifiedName~EvidenceStoreTests and observe failures before implementation.
- [ ] Implement atomic non-overwriting writes, canonical digest verification, explicit UTC/schema/context validation and GUID filenames. Validate containment and reparse conditions at open time, not only string normalisation. Preserve healthy rows, distinguish unavailable directory from empty directory, and page 100 entries with a usable continuation.
- [ ] Inject file operations for deterministic access-denied/disk-full/partial-write tests. Verify a failed write does not leave a loadable successful record.
- [ ] Rerun EvidenceStoreTests, including Unicode/spaced roots, wrong-device context and an exactly 100-entry page with more data.
- [ ] Commit evidence storage and its tests.

### T3: Assessment, reviewed plans and durable execution coordination

**Files:** Create Engine/Assessment/AssessmentService.cs, Engine/Planning/{OperationRegistry,PlanService}.cs, Engine/Execution/{RunCoordinator,ActionContracts}.cs and tests/LocalDeviceToolkit.Tests/{AssessmentTests,PlanTests,RunCoordinatorTests}.cs.

**Interfaces:** IDeviceInspector.CaptureAsync(IReadOnlyList<string> controlIds, CancellationToken ct) returns Task<DeviceCapture>. IExecutionContextProvider.GetCurrentAsync(CancellationToken ct) returns Task<DeviceContext>. ILocalOperation exposes Id, InspectAsync(DeviceContext, JsonElement, CancellationToken), CaptureBeforeAsync(DeviceContext, JsonElement, CancellationToken), and ExecuteAsync(DeviceContext, Guid actionId, JsonElement, CancellationToken), returning Task<ActionReadiness>, Task<IReadOnlyList<ProbeObservation>> and Task<ActionResult> respectively. Registry validation creates only known typed ActionRequests.

AssessmentService.Assess(LocalCatalogue, DeviceCapture) returns IReadOnlyList<Finding>. PlanService.CreateAsync(DeviceContext, LocalCatalogue, IReadOnlyList<ActionRequest>, CancellationToken) returns Task<ReviewedPlan>; it uses injected TimeProvider and throws PlanValidationException for a request that cannot form an executable plan. PlanService.Validate(ReviewedPlan, DeviceContext, CatalogueIdentity, IReadOnlyDictionary<Guid,string> currentBeforeDigests, DateTimeOffset now) returns PlanValidation with IsValid and Reasons. IActionDispatcher.DispatchAsync(ReviewedPlan, PlannedAction, CancellationToken) returns Task<ActionResult>. RunCoordinator.ExecuteAsync(ReviewedPlan, CancellationToken stop) returns Task<RunRecord>.

- [ ] Write PlanTests.Expiry_is_exact_and_never_renewed, Policy_inspection_failure_blocks_change and RunCoordinatorTests.Unsaved_intent_never_dispatches.

Test assertions:

    Assert.False(ValidateAt(plan, plan.CreatedAt.AddMinutes(10)).IsValid);
    Assert.False(readinessAfterUnavailablePolicy.CanExecute);
    Assert.Empty(dispatchesAfterIntentSaveFailure);
    Assert.Equal(ActionOutcome.OutcomeUnknown, interruptedStartedAction.Outcome);

- [ ] Run dotnet test tests/LocalDeviceToolkit.Tests -c Release --filter "FullyQualifiedName~AssessmentTests|FullyQualifiedName~PlanTests|FullyQualifiedName~RunCoordinatorTests" and confirm failing cases.
- [ ] Implement per-subcheck assessment and strict aggregation: unknown required evidence cannot produce MeetsStandard. Bind plans to exact device/user, catalogue, cloned parameters and before digests. Re-read context/prerequisites before every action; do not refresh the original ten-minute deadline.
- [ ] Register intent/plan/run evidence schemas. Persist intent before dispatch; serialise mutations; preserve earlier results if later rows fail. A stop before dispatch is CancelledBeforeExecution; a potentially started action without verified completion is OutcomeUnknown.
- [ ] Rerun tests with context/catalogue/input changes, second-action expiry, disk-full before and after dispatch, cancelled queues, and mutated or replayed plans.
- [ ] Commit the assessment/planning/coordination implementation and tests.

### T4: Local Windows inventory and supported probes

**Files:** Create Windows/Inspection/{WindowsContextProvider,WindowsDeviceInspector,RegistryProbe,PolicyProbe,ApplicationInventory,ProtectedProbeRegistry}.cs, Windows/Processes/DiagnosticProcessRunner.cs, scripts/diagnostics/Get-DeviceFacts.ps1, scripts/registry.json and tests/LocalDeviceToolkit.Windows.Tests/{DeviceInspectorTests,PolicyProbeTests,DiagnosticRunnerTests}.cs. Update catalogue/local-windows.json as probes become available.

**Interfaces:** WindowsContextProvider implements IExecutionContextProvider; derive identity from Windows APIs, not submitted strings. WindowsDeviceInspector implements IDeviceInspector. PolicyProbe.InspectAsync(string operationId, CancellationToken ct) returns Task<PolicyInspection> with PolicyInspectionState NoObservedConflict, ConflictObserved or Unavailable plus observed sources/reasons. DiagnosticRequest carries registered ScriptId, validated Parameters and TimeSpan Timeout. DiagnosticProcessRunner.RunAsync(DiagnosticRequest request, CancellationToken ct) returns Task<DiagnosticResult> with separate exit/output/limit/cancellation observations. ProtectedProbeRegistry declares exact read-only check IDs; it does not elevate automatically.

- [ ] Write DeviceInspectorTests.Access_denied_is_not_a_compliant_value, Unsupported_build_remains_visible and DiagnosticRunnerTests.Output_limit_preserves_incomplete_status.

Test assertions:

    Assert.Equal(ObservationState.AccessDenied, protectedObservation.State);
    Assert.Null(protectedObservation.Value);
    Assert.Equal(FindingStatus.Unknown, assessmentWithMissingRequiredOs.Status);
    Assert.Equal(PolicyInspectionState.Unavailable, failedPolicyInspection.State);

- [ ] Run the targeted Windows tests and confirm failures. These tests must actually run on Windows; a Linux cross-build is not a substitute.
- [ ] Implement read-only OS, identity, registry, power and application inventory. Never query Win32_Product. Use exact supported checks for the 30 mapped controls, with Partial/Manual dispositions where local proof cannot establish the requirement.
- [ ] Adapt the upstream owned-process reader only for diagnostics. Use trusted Windows executable paths, PowerShell 5.1 without profiles, pinned scripts, 1 MiB per-stream limits and explicit timeout/exit parsing. Do not read BitLocker recovery secrets or LAPS passwords.
- [ ] Rerun Windows tests using injected APIs and synthetic PowerShell fixtures for singleton/array output, null values, access denied, unsupported cmdlets, conflicting policy and failure to load a dependency. Native read-only probe smoke checks must record actual Windows/build context.
- [ ] Commit probes, declared coverage, scripts and tests.

### T5: Authenticated elevation worker and protected execution

**Files:** Create Core/Protocol/{WorkerMessages,WorkerMessageCodec}.cs, Windows/Elevation/{ElevationBroker,LocalNamedPipe,PeerVerifier,ProtectedRunDirectory,DeviceWriterLock}.cs, Worker/{App.xaml,App.xaml.cs,WorkerDispatcher.cs,WorkerReviewWindow.xaml,app.manifest}, and tests/LocalDeviceToolkit.Windows.Tests/{WorkerProtocolTests,PeerVerifierTests,ProtectedRunDirectoryTests}.cs.

**Interfaces:** WorkerRequest is a strict union of CaptureProtected(runId, checkIds) and ExecuteAction(runId, plan, actionId). WorkerReply is exactly one of Capture, ActionResult or ProtocolError, with the matching run ID. VerifiedPeer records OS-observed PID, creation time and SID. ElevationBroker.CaptureProtectedAsync(IReadOnlyList<string>, CancellationToken) returns Task<DeviceCapture>; DispatchAsync(ReviewedPlan, PlannedAction, CancellationToken) returns Task<ActionResult>. WorkerDispatcher.HandleAsync(WorkerRequest, VerifiedPeer, CancellationToken) returns Task<WorkerReply>.

- [ ] Write WorkerProtocolTests.Unknown_or_oversized_messages_are_refused, PeerVerifierTests.Alternate_admin_is_allowed_only_for_the_launched_peer and ProtectedRunDirectoryTests.User_writable_or_reparse_staging_is_refused.

Test assertions:

    Assert.False(VerifyPeer(samePidDifferentCreationTime).Accepted);
    Assert.False(VerifyPeer(unrelatedAdministratorProcess).Accepted);
    Assert.True(VerifyPeer(expectedLaunchedAlternateAdministrator).Accepted);
    Assert.Throws<ProtocolException>(() => DecodeFrameOver(1024 * 1024));

- [ ] Run the targeted Windows tests and confirm failures.
- [ ] Implement a local named pipe using explicit Windows security descriptors and remote-client refusal. Bind the UI's peer to the returned UAC process handle and both sides to OS-observed PID/creation time. Derive the original SID from the requesting process token; reject uncertain executable/peer identity. Use a 30-second post-launch connection deadline without refreshing plan expiry.
- [ ] Implement independent worker operation/input/context validation, exact-action native review, single-use dispatch and a device-wide writer lock. Keep the OS-verified original user's DeviceContext separate from the actual ExecutionActor: an authorised alternate administrator must be recorded without replacing the target SID. Use administrator/SYSTEM-writable ProgramData staging/journals; refuse unsafe existing ACLs/reparse points. Stage embedded privileged scripts under that protection, preserving execution policy.
- [ ] Rerun tests for duplicate/unknown fields, replay, wrong device/SID, timeout, denied UAC, lost pipe, unavailable privilege, unsafe inherited ACLs and stale before-state. Protected reads use only registered check IDs and return capture evidence, not an Applied action outcome.
- [ ] Commit the protocol/worker implementation. Record interactive UAC and alternate-credential cases as unrun until exercised on an authorised Windows host.

### T6: Current-user settings with identity preservation

**Files:** Create Windows/Actions/User/{ShowFileExtensions,UkRegionKeyboard,ConsumerCopilotRemoval}.cs, scripts/settings/Set-UkUserRegion.ps1 and tests/LocalDeviceToolkit.Windows.Tests/UserActionTests.cs. Update script pins and catalogue coverage.

**Interfaces:** Each class implements ILocalOperation. Register settings.show-file-extensions, settings.uk-region-keyboard and apps.remove-consumer-copilot with strict fixed-purpose parameter schemas. Original-user execution is mandatory; these adapters must refuse an unexpected SID rather than impersonate another profile.

- [ ] Write UserActionTests.Alternate_admin_never_becomes_the_user_target, Uk_keyboard_preserves_unrelated_languages and Copilot_removal_requires_exact_package_identity.

Test assertions:

    Assert.Empty(userRegistryWritesWhenActualSidDiffers);
    Assert.Contains(originalSecondaryLanguage, result.LanguageList);
    Assert.DoesNotContain(microsoft365CopilotPackage, removedPackages);

- [ ] Run dotnet test tests/LocalDeviceToolkit.Windows.Tests -c Release --filter FullyQualifiedName~UserActionTests and confirm failures.
- [ ] Implement show-extensions for original HKCU, UK culture/region/keyboard changes that preserve unrelated language entries, and exact current-user consumer Copilot removal where supported. Validate vendor-documented package identity; a display-name substring never selects a removal target. Capture before state and re-read postconditions.
- [ ] Rerun tests with non-English usernames, missing optional components, absent packages, managed settings, user-switch mismatch and failed post-read. Package absence is No change needed at planning, not a claimed removal.
- [ ] Commit the user actions, script pins, mapping and tests.

### T7: Machine settings and reviewed restoration

**Files:** Create Windows/Actions/Machine/{LongPaths,FastStartup,AcSleep,UkTimeZone}.cs, Engine/Recovery/SettingsRestoration.cs and tests/LocalDeviceToolkit.Windows.Tests/MachineActionTests.cs plus tests/LocalDeviceToolkit.Tests/RestorationTests.cs. Extend worker's closed operation registry.

**Interfaces:** Register settings.long-paths, settings.disable-fast-startup, settings.no-ac-sleep and settings.uk-time-zone as ILocalOperation implementations. Actual privilege is inspected separately from Machine scope. SettingsRestoration.CreateAsync(Guid sourceRunId, Guid actionId, CancellationToken) returns Task<ReviewedPlan> using original validated evidence and fresh state; it never accepts an arbitrary registry path/value.

- [ ] Write MachineActionTests.Unavailable_policy_blocks_mutation, Ac_sleep_preserves_battery_values and RestorationTests.Subsequent_drift_blocks_restore.

Test assertions:

    Assert.Empty(writesAfterPolicyInspectionFailure);
    Assert.Equal(beforeBatteryTimeout, afterBatteryTimeout);
    await Assert.ThrowsAsync<PlanValidationException>(
        () => restoration.CreateAsync(changedRunId, actionId, CancellationToken.None));

- [ ] Run the targeted MachineActionTests and RestorationTests and confirm failures.
- [ ] Implement only documented local operations. Preserve other hibernation options, battery timeouts and unrelated power settings. Recheck the active power scheme and policy before dispatch. Use GMT Standard Time; do not force UAC if the actual account already has the required time-zone right.
- [ ] Implement operation-specific restoration of supported recorded settings, including original absent values. Bind restoration to the same device/setting and refuse drift or invalid evidence. Do not offer general undo for removal, encryption or installers.
- [ ] Rerun the tests, including unsupported builds, registry denial, power-scheme change between review and execution, denied UAC and incomplete post-read. Actual machine mutations require a dedicated authorised acceptance host.
- [ ] Commit machine actions, restoration, catalogue changes and tests.

### T8: Reviewed local package deployment and reconciliation

**Files:** Create Core/Packages/PackageProfile.cs, Windows/Packages/{PackageVerifier,PackageStager,MsiInstaller,VendorExeInstaller,MsixInstaller,OfficeDeploymentInstaller,MutationSupervisor}.cs, Engine/Packages/PackageDeploymentOperation.cs and tests/LocalDeviceToolkit.Windows.Tests/PackageDeploymentTests.cs. Create synthetic installer fixtures under tests/Fixtures/Installers.

**Interfaces:** PackageProfile declares product/control ID, type, architecture, scope, required inputs/files, hashes/signers, permitted argument template, timeout, exit rules and version/postcondition checks. PackageVerifier.VerifyAsync(PackageProfile, IReadOnlyList<string> sourceFiles, CancellationToken) returns Task<PackageVerification>. PackageStager.StageAsync(PackageVerification, VerifiedPeer, CancellationToken) returns Task<StagedPackage>. MutationSupervisor.StartAndObserveAsync(InstallerRequest, CancellationToken stopQueue) returns Task<InstallerObservation>. PackageDeploymentOperation implements ILocalOperation as packages.install.

- [ ] Write PackageDeploymentTests.Staged_dependency_swap_is_refused, Timeout_does_not_kill_or_replay and Exit_zero_without_product_postcondition_is_not_verified.

Test assertions:

    Assert.False(verificationAfterDependencySwap.Accepted);
    Assert.Equal(0, installer.KillCalls);
    Assert.Equal(1, installer.StartCalls);
    Assert.Equal(ActionOutcome.AppliedUnverified, zeroExitWithoutPostcondition.Outcome);
    Assert.Equal(ActionOutcome.RestartRequired, documented3010Result.Outcome);

- [ ] Run dotnet test tests/LocalDeviceToolkit.Windows.Tests -c Release --filter FullyQualifiedName~PackageDeploymentTests and confirm failures.
- [ ] Implement complete-file-set verification, protected staging and stable staged inputs. Use Windows trust APIs for signatures; preserve verification failures. Use argument arrays and supported installer templates, never a shell command or unrestricted elevation request.
- [ ] Implement MSI, reviewed vendor EXE, current-user MSIX/bundle and Office Deployment Tool adapters with explicit prerequisites, dependencies and documented restart suppression. Match APP-WIN-001 through 008 to inventory/deployment profiles; leave items with missing profiles or vendor inputs clearly Manual. Do not bundle third-party/client installers or invent silent arguments.
- [ ] Implement mutation supervision independently of diagnostic tree-kill logic. Test bootstrapper-child handoff, 1641/3010, process loss, stopped queues and failed evidence persistence. Before any retry, provide read-only installed-state reconciliation and require a new reviewed plan.
- [ ] Rerun all package tests with synthetic executables and mocked installer/Windows APIs. Record actual vendor-package acceptance separately.
- [ ] Commit package deployment and its fixtures/tests.

### T9: Native five-area engineer application

**Files:** Create App/{App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs,app.manifest}, App/Infrastructure/{ObservableObject,Commands}.cs, App/ViewModels/{Shell,Overview,Assessment,Deploy,Scripts,History}ViewModel.cs, matching App/Views XAML files, and tests/LocalDeviceToolkit.App.Tests/{NavigationTests,SelectionTests,IdentityPresentationTests,DeploymentJourneyTests}.cs.

**Interfaces:** ShellViewModel owns CurrentPage, Context, Catalogue, BusyState, Error and selection-independent inspection. Page keys are overview, assess, deploy, scripts and history. The composition root injects the T1–T8 services. View-model commands call those interfaces; XAML/code-behind does not invoke Windows operations.

- [ ] Write SelectionTests.Inspecting_a_row_never_queues_it, NavigationTests.Navigation_never_collects_or_mutates and IdentityPresentationTests.Original_and_elevated_accounts_are_distinct.

Test assertions:

    Assert.Empty(viewModel.SelectedActions);
    Assert.Equal(0, inspector.CaptureCallsAfterNavigation);
    Assert.Equal(0, dispatcher.CallsAfterNavigation);
    Assert.Contains(originalSid, userActionReview.TargetIdentity);

- [ ] Run the targeted App tests on Windows and confirm failures.
- [ ] Adapt upstream App.xaml, Infrastructure/ObservableObject.cs and Commands.cs, and the native MainWindow/task/detail layout. Record every import/adaptation in provenance. Retain accessible names, visible focus, readable text statuses, native reference detail and a persistent device/user/elevation header.
- [ ] Wire the full assess → select → review → execute → verify flow. Start with no queued deployment. Disable stale-plan execution and show its reason. Protected probes require an explicit action; routine page navigation does not open UAC. Treat an already elevated parent truthfully.
- [ ] Rerun App tests with synthetic services, partial results, long labels, smaller windows and queued operations. No fixture data appears in ordinary startup; demo/test identities must be explicit.
- [ ] Commit the WPF interface and app tests.

### T10: Registered scripts and engineer tools

**Files:** Create Engine/Scripts/{LocalScriptCatalogue,ScriptInputBinder,RegisteredToolService}.cs, App/Views/ToolInputView.xaml, App/ViewModels/ToolInputViewModel.cs, build/Verify-ScriptRegistry.ps1 and tests/LocalDeviceToolkit.Tests/ScriptCatalogueTests.cs plus tests/LocalDeviceToolkit.App.Tests/ScriptWorkflowTests.cs. Extend scripts/registry.json using the T4/T6 scripts.

**Interfaces:** LocalScriptCatalogue.Load(string root) returns registered manifests only. ScriptInputBinder.Bind(LocalScriptDefinition, IReadOnlyDictionary<string,string?> inputs) returns BoundToolInput with IsValid and Problems. RegisteredToolService.CreateRequest(string scriptId, BoundToolInput) returns a registered ActionRequest or bounded diagnostic request; it never returns arbitrary executable text.

- [ ] Write ScriptCatalogueTests.Changed_body_and_undeclared_parameters_are_refused and ScriptWorkflowTests.Invalid_inputs_cannot_run_or_elevate.

Test assertions:

    Assert.Throws<ScriptValidationException>(() => LoadChangedScript());
    Assert.False(bindingWithUnknownParameter.IsValid);
    Assert.Equal(0, broker.LaunchCallsForInvalidInput);

- [ ] Run the targeted script and App tests and confirm failures.
- [ ] Implement strict manifests, compiled/package-root pins, parameter types, prerequisite/privilege labels and stable operation IDs. Expose only implemented diagnostic/settings tools. Run through their existing adapters and plan rules; never add a generic elevated Script-Runner.
- [ ] Implement PowerShell 5.1 syntax/manifest verification without executing client operations. An execution-policy block remains a visible failure; no policy bypass, automatic module installation or hidden Unblock-File step.
- [ ] Rerun tests for quote/control-character inputs, altered manifests, wrong hashes, missing scripts, unavailable prerequisites and typed arrays/booleans. Verify real PowerShell 5.1 parsing on Windows.
- [ ] Commit script catalogue, UI forms and tests.

### T11: Reopenable history, reports and read-only reconciliation

**Files:** Create Engine/Reports/{ReportProjection,JsonReport,CsvReport,HtmlReport}.cs, Engine/Execution/RunReconciler.cs, App/ViewModels/RunDetailViewModel.cs, App/Views/RunDetailView.xaml and tests/LocalDeviceToolkit.Tests/{ReportTests,ReconciliationTests}.cs plus tests/LocalDeviceToolkit.App.Tests/HistoryWorkflowTests.cs.

**Interfaces:** ReportProjection.From(DeviceCapture?, ReviewedPlan?, RunRecord) returns LocalReport with original device/user, source versions, expected/requested/observed values and explicit limitations. ReportExporter.ExportAsync(LocalReport, ReportFormat, string destination, CancellationToken) returns Task<ExportReceipt> and refuses overwrite by default. RunReconciler.RecheckAsync(Guid runId, CancellationToken) returns Task<ReconciliationRecord>; this record references the immutable original run and cannot dispatch writes.

- [ ] Write ReportTests.Unknown_results_do_not_inflate_success, Csv_formula_and_html_markup_are_escaped and ReconciliationTests.Recheck_never_replays_the_action.

Test assertions:

    Assert.Equal(0, report.VerifiedCountForUnknownOnlyRun);
    Assert.Equal(0, dispatcher.CallsDuringReconciliation);
    Assert.Equal(originalBytes, File.ReadAllBytes(originalRecordPath));

- [ ] Run the targeted report/reconciliation/App tests and confirm failures.
- [ ] Adapt suitable upstream safe export primitives and record their provenance. Implement native read-only history/detail views, healthy entries plus corruption diagnostics, explicit other-device context, paging, copyable summaries and deliberate non-overwriting exports.
- [ ] Implement reconciliation through read-only inspectors and operation-specific postconditions. Preserve original uncertainty/history; record what new evidence proves and what remains unknown. Reconciliation never becomes retrospective approval or automatic retry.
- [ ] Rerun tests for failed writes, failed post-read, mixed outcomes, corrupt history, wrong-device evidence, missing pages, non-ASCII text, spreadsheet formula inputs and HTML injection.
- [ ] Commit reports, history/reconciliation UI and tests.

### T12: Portable Windows package and evidence-based handoff

**Files:** Create build/{Build-Portable,Test-Portable,Test-SourceInventory}.ps1, packaging/Start.cmd, .github/workflows/build.yml, tests/LocalDeviceToolkit.UiReview/{Program.cs,NativeJourneyChecks.cs}, docs/{WINDOWS-ACCEPTANCE,LOCAL-USAGE}.md and environment setup/start instructions. Update README and IMPLEMENTATION-STATUS with measured evidence only.

**Interfaces:** Build-Portable.ps1 produces one ZIP, its SHA-256 and a version/dependency/provenance inventory. Package layout includes the app at the root and a self-contained worker under worker/. Test-Portable.ps1 accepts -ZipPath and -ResultPath and writes a fresh result bound to the package SHA-256. The UI harness receives an output directory and uses only synthetic services.

- [ ] Write package/harness checks that fail when the worker, script pin, catalogue, required runtime dependency or provenance notice is missing, when launch breaks under paths containing spaces/Unicode, or when launcher/script exit status is lost.
- [ ] Run those checks against deliberately incomplete fixture packages and confirm failure.
- [ ] Implement locked restore, warning-as-error builds, portable/Windows/App tests, registry/catalogue verification, self-contained publishing and non-overwriting versioned output. Run the relevant suites before packaging. Do not regenerate integrity pins merely to pass packaging.
- [ ] Configure CI using reviewed pinned actions and minimal read-only permissions. Linux runs portable tests/cross-builds; Windows runs applicable native tests, PowerShell 5.1 parsing, WPF interaction/layout checks and extracted-package launch. Synthetic fixtures must not apply real device settings or install client applications.
- [ ] Run: dotnet build LocalDeviceToolkit.sln -c Release -warnaserror -p:EnableWindowsTargeting=true; dotnet test tests/LocalDeviceToolkit.Tests -c Release. On Windows also run both Windows/App suites, the UI harness and build/Build-Portable.ps1, then Test-Portable.ps1 on the actual ZIP.
- [ ] Check 100%, 150% and 200% rendering/focus, smaller windows, native result visibility and no default queued deployment. Distinguish automated layout simulations from physical DPI/Narrator/keyboard acceptance.
- [ ] Record interactive UAC cancellation, alternate-administrator identity, a clean Windows machine without the SDK, real supported probes/settings and vendor-package acceptance in WINDOWS-ACCEPTANCE. Leave unrun cases explicitly pending; CI does not silently count as those tests.
- [ ] Save tested cloud installation/start instructions when the environment supports the implemented workflow. Runtime startup must not assume any earlier process survived; no worktree creation is required.
- [ ] Commit packaging/documentation and request an independent whole-branch review. Repair supported findings and rerun affected checks. If a PR is created, attach it to the task. Do not claim a merge, release, native readiness or publication that did not occur.

## Coverage and handoff

| Design section | Owning tasks |
| --- | --- |
| 1–2: outcome and source integration | T1, T9, T12 |
| 3: project boundaries | T1–T12 |
| 4: native engineer workflow | T9–T11 |
| 5: all 30 local-control dispositions | T1, T3, T4, T6–T8 |
| 6: current user and elevation | T5–T8 |
| 7: reviewed changes, packages and verification | T2, T3, T6–T8, T11 |
| 8: scripts, evidence and storage | T2, T5, T10, T11 |
| 9–10: packaging, validation and acceptance | T12 |

**Recommended execution:** direct implementation in this session with the executing-plans skill, followed by an independent whole-branch review. These tasks share evolving identity, evidence and execution contracts; implementing them coherently is preferable to concurrent changes to those contracts. Task-by-task subagents remain an available alternative if the user selects them.

Before implementation, review this written plan together with the revised design and confirm the execution method. The plan is not a record of completed software, and no unchecked item may be reported as implemented or verified.
