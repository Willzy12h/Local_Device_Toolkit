# Native Local Device Toolkit

Date: 14 October 2026

Revised: 15 October 2026 — setup and diagnostics workspaces over shared tasks.

Status: implementation authorised by the user’s pasted request on 15 October 2026. The user selected a new WPF toolkit built from M365 source, with setup and diagnostics sharing the same tasks and execution code.

## 1. Intended outcome

Give an engineer a portable native Windows application for inspecting the current device, comparing its observed configuration with the applicable M365 build standard, selecting local changes or application deployments, and verifying their results.

The primary journeys are New device setup and Diagnostics. The same task may appear in both journeys, but its inputs, probes, scripts, deployment adapter and verification have one canonical implementation.

The application normally runs as the engineer's current Windows user. Only an operation that needs administrator rights requests Windows UAC elevation. User settings continue to target the original user when a different administrator supplies credentials.

Success means a second engineer can launch the extracted package, identify the device and execution identity, assess it, understand unsupported or unknown findings, review a local deployment, execute authorised actions, and export truthful before/after evidence.

The earlier PowerShell/WinForms toolkit is a separate historical product. The user has chosen a new WPF application; this work does not claim to import or preserve unavailable historical code.

## 2. Source and integration decision

Use Willzy12h/M365-Buildstandards at commit 80f01a9482c0ae20e995f9df357c9fca9217f623 on phase/wpf-webview2 as the initial source reference. Its integration predecessor is 4a7425f55385e84462975bcc5bdde5644815ca8f. Main contains only the initial integration documentation.

The source's standard 2026.09.30 contains 30 relevant Windows controls: CMP-WIN-001, CFG-WIN-001 through CFG-WIN-021, and APP-WIN-001 through APP-WIN-008. Their original definitions and provenance are retained in docs/reference/m365-windows-controls.json. They are source definitions, not proof of equivalent local automation.

At inspection, draft PR #128 contains an additional assessment-history diagnostic fix outside the selected merged baseline. Track its disposition; implement and test malformed/unavailable history handling in this toolkit independently of whether that PR merges. Do not import an unreviewed branch wholesale.

Two approaches were considered:

- Preserve the original PowerShell/WinForms toolkit and port M365 improvements. This needs historical source that is not present in the repository.
- Build a new WPF device application from selected M365 components. The user chose this approach. It provides a consistent native interface and permits an explicit local execution boundary.

Keep an import manifest recording the source repository, commit, source paths, original hashes, destination paths, and whether a component was copied or adapted. Later upstream comparisons produce reviewed changes; updating the source pin never silently changes an installed toolkit's actions or catalogue.

| M365 component | Reuse decision |
| --- | --- |
| App.xaml style resources, command-centre layout, observable properties and commands | Adapt into the device UI; retain keyboard/focus and readable status behaviour. |
| Result, assessment-history and current-context presentation patterns | Adapt to device identity, original user SID, catalogue version and capture freshness. |
| Canonical JSON, safe exports and evidence validation | Reuse suitable primitives with local schemas and their relevant tests. |
| Script manifests, typed inputs and owned process handling | Adapt the contracts and process lifecycle; create a dedicated local action catalogue. |
| Windows standards and documented requirements | Retain source IDs, exact expected values and references; author explicit local probes and applicability rules. |
| Portable packaging and extracted-package verification | Adapt for the new application, worker and Windows tests. |

The tenant Graph client, authentication flows, Exchange scripts and Intune policy executor are not local-device implementations. The new product does not require tenant sign-in, a browser, WebView2, Node, Docker, a web server or a background Windows service. Reference detail is presented in native WPF; HTML is an optional exported report format.

## 3. Application structure

Use C# 12 and .NET 10 with SDK 10.0.401 as the initial build baseline, matching the inspected source. Ship self-contained win-x64 packages. Supported deployment targets are Windows 11 x64; other Windows versions may be inventoried but do not receive an unsupported-device readiness claim. ARM64 packaging is outside this initial build.

| Project | Responsibility |
| --- | --- |
| LocalDeviceToolkit.Core | Device/user identities, canonical task definitions, workflow references, catalogue and evidence models, typed action contracts, result states and deterministic validation. |
| LocalDeviceToolkit.Engine | Shared task resolution, assessment, applicability, one device-bound deployment queue, plan creation/invalidation, sequencing, evidence persistence, history and exports. No direct Windows mutations. |
| LocalDeviceToolkit.Windows | Windows probes, approved user-scope actions, process adapters, effective-policy observations and package verification. |
| LocalDeviceToolkit.App | WPF views/view models and workflow orchestration. Normal launch requests asInvoker. |
| LocalDeviceToolkit.Worker | Short-lived elevated Windows worker with a compiled allowlist of administrator operations and its own request validation. |
| Tests and UI review harness | Portable engine tests, Windows adapter/worker tests, WPF interaction/layout checks and extracted-package smoke tests. |

UI code does not construct arbitrary command lines or perform device mutations. Each operation identifies a registered adapter with typed parameters, declared privilege requirements, prerequisites, before-state capture, postcondition checks and cancellation semantics.

## 4. Native engineer workflow

Use the newer M365 teal/neutral visual language with Local Device Toolkit branding and five navigation areas:

1. Home: current device/user/elevation/standard, last evidence and two prominent entry points for New device setup and Diagnostics.
2. New device setup: a staged build workflow that leads from readiness through configuration and applications to verification and handover.
3. Diagnostics: checks grouped around device health, security, applications and user experience, with findings and deliberate repair proposals.
4. Deployments: the shared queue, exact reviewed changes, prerequisites, privilege/scope, execution progress, verification and reconciliation.
5. Reports and history: setup handovers, diagnostic ticket summaries and deployment records over the same underlying evidence.

Search and a task-library command expose all available tasks without creating another execution path. Native build-standard detail is accessible from each relevant task. The common task form, input binding, action review, output panel and export components are reused across the two workspaces.

Selecting a row only opens detail; checking a row adds it to the proposed queue. Navigation never runs a probe or action. No deployment is selected by default. A persistent header identifies the local device and original Windows user; elevated actions also identify the actual administrator account.

Reference details remain visible beside the current task or in a collapsible native drawer at smaller sizes. Errors and outcomes remain reachable when action buttons wrap. Support keyboard operation, labelled controls, visible focus, high-contrast compatibility and scaling checks at 100%, 150% and 200%.

### 4.1 New device setup

Present these stages with actual evidence-based status, requirements, outstanding work and a useful next action:

| Stage | Engineer workflow |
| --- | --- |
| Identify and check readiness | Confirm device, current user, supported OS/build and hardware/security prerequisites. Offer explicit protected checks where required. |
| Assess the build standard | Compare applicable controls with observed local state; show manual checks, policy conflicts and missing evidence. |
| Configure Windows | Review selected current-user and machine settings, including region/keyboard, time zone, extensions, power and supported security/configuration tasks. |
| Install applications | Review installed versions and selected package profiles, vendor inputs, scope, prerequisites and restart behaviour. |
| Verify and hand over | Capture fresh post-change observations, review manual checks and exceptions, and generate a setup/handover report. |

Stage status is Not started, In progress, Needs attention or Complete. Opening a stage or selecting an action does not complete it. Show automated verification, explicitly recorded manual checks, unknowns and optional exclusions separately. A skipped optional task retains its reason; a failed or unknown required check prevents a ready-for-handover claim. A recorded manual check includes the engineer, time, outcome and note and is never relabelled as automated proof.

### 4.2 Diagnostics

The workspace starts with read-only checks. Group tasks into Device health, Security and compliance, Applications and deployments, and Windows and user experience. Allow selection of individual checks or a declared diagnostic group and show what will be read before starting it.

Findings include the observed value, expected standard where applicable, evidence source/time, limitations and a practical next step. A read requiring administrator rights uses the same explicit elevation flow. Missing evidence remains Unknown or Requires elevation.

An available repair is a proposal using the same canonical change action as setup. The engineer chooses Review fix to add it to Deployments; running diagnostics or viewing a finding never performs a repair. Previous deployment failures can be opened for read-only reconciliation from this workspace.

### 4.3 Shared tasks and execution

Store task definitions once, with a stable TaskId, source control/check IDs, input schema, optional change OperationId and evidence/verification requirements. Workflow definitions contain stages and references to TaskId values; they contain no script bodies, command lines or separate copies of business logic. Task definitions resolve the existing shared script/operation registry.

Examples:

| Canonical task | New device setup | Diagnostics | Shared implementation |
| --- | --- | --- | --- |
| windows.file-extensions | Configure Windows: review showing extensions | Windows/user experience: check and review a fix | settings.show-file-extensions and its shared probe |
| windows.power-ac-sleep | Configure Windows: review mains sleep | Windows/user experience: investigate sleep behaviour | settings.no-ac-sleep and its shared probe |
| apps.installation | Install applications: review a chosen product | Applications/deployments: inspect or reconcile a product | packages.install, product inventory and postcondition checks |
| security.device-readiness | Identify/readiness: inspect required security state | Security/compliance: investigate the same state | Shared TPM, Secure Boot, encryption and provider probes |

The two workspaces use one queue bound to the current device and original user. Resolve a change's logical target, such as a setting or product identity, through the registered operation. Identical parameters for an already queued target produce Already queued; conflicting parameters require an explicit keep/replace decision. Different application products can coexist in the queue. An entry point's label or workflow identity cannot change privilege, inputs or authorisation.

Changing the active workspace alone neither starts work nor changes a valid reviewed plan. Changing queued actions, their targets or inputs invalidates review through the existing planner. A mutation and a conflicting diagnostic run must not execute concurrently.

Record workflow/stage/task provenance in versioned captures, plans and results. Viewing the same run from another workspace references its original run ID; it does not copy evidence, count a deployment twice or claim that old verification was newly performed.

### 4.4 Usability and reporting from Build Standard

Reuse grouped task navigation, search/filtering, selection distinct from inspection, pinned standard detail, visible prerequisites, exact reviewed changes, accessible result panels and context-aware next steps. Every disabled Run, Review or Deploy action explains the missing prerequisite. Task cards show current-user/machine scope, actual privilege requirements and whether the capability is automatic, partial or manual.

All report types use the same validated evidence and safe export services:

- Setup/handover: selected standard and device identity, stage status, expected/before/requested/after values, application versions, manual checks, exceptions, restart requirements and outstanding handover work.
- Diagnostic/ticket: selected checks, findings and their limitations, relevant bounded evidence, proposed/applied fixes, verification and follow-up work.
- Deployment: exact reviewed actions and originating workflow, execution/verification outcomes, original engineer and actual elevated actor, and unresolved or restart-required results.

Read-only diagnostics and assessments can produce reports without a mutation run. Report projections must accept their actual capture evidence rather than fabricate a successful deployment record. HTML, CSV, JSON and copyable ticket summaries share the same status interpretation. History filters by workflow, stage, task and run while retaining a single record for each actual run.

## 5. Local assessment and standards mapping

Separate observed state from configured policy and from a conclusion about compliance. Record the control ID, expected value, observed value, probe/version, device and user identity, capture time, evidence limitations and applicability.

Use distinct assessment states: Meets standard, Does not meet standard, Unknown, Requires elevation, Not applicable and Manual verification. A failed probe or missing permission never becomes a passing result or a known empty value.

Map all 30 source controls into the local catalogue. An unsupported probe remains visible with its manual procedure and reason; it is never presented as automated coverage.

| Control group | Initial local behaviour |
| --- | --- |
| CMP-WIN-001 | Read OS version, TPM, Secure Boot, available encryption/security-provider and code-integrity evidence. Evaluate subchecks independently; preserve unknowns and unsupported inspection. |
| CFG-WIN-001 through 006, 008, 009, 013 through 018 | Read supported local policy/effective-state evidence and display the source requirements. Keep enrolment, escrow, remote policy assignment, user provisioning and cloud effectiveness as separately identified evidence or manual checks. |
| CFG-WIN-007 | Read and support a reviewed change to long-path configuration, with supported-OS and application limitations. |
| CFG-WIN-010 | Read and support current-user UK region/keyboard configuration. Welcome-screen and new-user defaults remain separate manual steps. |
| CFG-WIN-011 | Read and support a reviewed change to GMT Standard Time; determine actual privilege requirements on the host. |
| CFG-WIN-012 | Read and support showing file extensions for the original current user. |
| CFG-WIN-019 | Read and support disabling fast startup while preserving other hibernation settings. |
| CFG-WIN-020 | Read and support the active power scheme's AC sleep timeout; preserve battery settings and unrelated power options. |
| CFG-WIN-021 | Identify the specific consumer Copilot package for the current user and offer reviewed removal where supported. Keep Microsoft 365 Copilot and other packages distinct. |
| APP-WIN-001 through 008 | Inventory matching applications and expose explicit deployment profiles for reviewed local installer packages. Missing client/vendor inputs remain visible prerequisites. |

These are implementation requirements, not claims that the probes already exist. Each local transport must be supported on the target Windows edition/build. Do not translate an Intune CSP path into a guessed registry write. Show detected management ownership or conflicts and refuse an automated change that would knowingly fight managed policy.

Required policy-precedence checks have three outcomes: no conflicting local evidence observed, conflict observed, and inspection unavailable. The last two block the affected change with a reason. No observed conflict does not prove the device is unmanaged. Missing OS/edition/identity evidence also blocks actions that depend on it; do not substitute optimistic defaults.

The runtime catalogue must explicitly classify every source control as Automatic, Partial or Manual coverage, list the exact checks/actions available, and name remaining verification. Keep the original reference JSON immutable. The runtime mapping has its own version and digest and must not interpret Graph payloads as executable local instructions.

Local encryption status cannot prove recovery escrow. A configured LAPS policy cannot prove successful password backup. An installed application cannot prove a licence, working sign-in or business function. Reports keep those distinctions.

## 6. Current-user execution and elevation

The main application uses asInvoker and does not collect or store administrator credentials. If it was launched from an already elevated shell, show the actual state and give normal-user relaunch guidance; never label the session unelevated.

User-scope operations execute in the original process identity. Administrator credentials never redirect HKCU, the profile directory or user application installation into another person's account.

Current user means the person running the application, not an inferred customer or another logged-on user. Display the account and SID beside every user-scoped change. A user's decision to launch the toolkit does not authorise changing every local profile.

For an action whose supported adapter requires administrator rights (a machine-wide setting does not automatically imply elevation is necessary):

1. Capture the requested operation, typed values, device identity, original user SID, before-state, source versions, expiry and a single-use run ID in a reviewed plan.
2. Start only the toolkit's worker through the Windows runas/UAC flow. Cancellation of UAC is a cancelled action with no success claim.
3. Establish a local authenticated channel bound to the requesting process and its OS-observed identity, with a bounded lifetime. Request fields alone are not identity proof.
4. The worker validates protocol version, operation allowlist, typed inputs, target device, identity, freshness and prerequisites independently. Re-read relevant machine state before changing it.
5. Present the exact machine actions and original target user in the elevated worker's native review before dispatch. Do not accept free-form PowerShell, shell text or an executable path as an elevation request.
6. Execute only approved operations, write protected per-run evidence, return bounded typed results, and exit when the run ends.

Secure IPC and staged machine-action inputs against substitution, replay and path escape. Derive trusted system executable locations from Windows APIs rather than a mutable PATH. A digest can identify the reviewed bytes but does not authenticate a caller or publisher by itself.

Use a single-run local named pipe with a cryptographically random name, explicit ACLs for the initiating user, administrators and SYSTEM, and rejection of remote pipe clients. Do not use CurrentUserOnly as the sole policy: that would reject the deliberately supported alternate-administrator flow. Both ends verify OS-reported peer process IDs and process creation times. The UI binds the peer to the process handle returned by its own UAC launch; the worker derives the original SID from the pipe server's token and verifies its expected executable identity. A failed or ambiguous launch/peer check refuses execution.

Protocol messages are strict UTF-8 JSON with a 1 MiB maximum frame, a version and a run ID. Set a 30-second connection deadline after a successful UAC launch and recheck the original plan's ten-minute expiry at final dispatch. Reject duplicate/unknown fields, mismatched identities, replay and out-of-sequence messages. Worker action parameters are deserialised only through a closed operation registry. No credential values travel over the pipe.

Create protected worker journals and installer staging beneath ProgramData/LocalDeviceToolkit with inherited permissions disabled and explicit administrator/SYSTEM write access. Check existing locations and refuse unsafe ownership, reparse points or unexpected files. The original user may receive access to the specific run's sanitised results; do not grant general write access to the worker directory. Hold a device-wide single-writer lock across protected before-capture, dispatch and verification.

For installer execution, stage and revalidate the approved payload under administrator-controlled permissions before launching it. Validate file type, cryptographic hash, expected signer where applicable, declared install scope and explicit accepted arguments. An unsigned installer needs an explicit unsigned status and a reviewed hash; never describe its hash as publisher authentication.

Validate all required package files, including transforms, configuration and dependencies, as one reviewed set. Keep staged files stable through execution; validate the staged bytes, not only the user-selected source. Refuse unsupported installer profiles rather than forwarding arbitrary command text. A trusted signer does not override a hash, product, architecture or scope mismatch.

No service, scheduled SYSTEM task or stored credential is introduced to avoid UAC. Windows application-control or execution policy failures are surfaced with useful guidance; the toolkit does not weaken machine policy.

## 7. Local deployment and verification

Plans are selective and expire after ten minutes. Changing the selection, inputs, device/user context, catalogue or relevant before-state invalidates approval. Recheck immediately before each change, including after a long UAC prompt.

Run one mutating action at a time. Capture each action's before-state, requested change, observed execution outcome and independent after-state. Keep all previous rows if a later row fails.

Persist intent durably before dispatch. If that save fails, do not execute the change. If the change may have started and subsequent evidence cannot be saved, retain an uncertain outcome and offer read-only reconciliation rather than replay. Close, cancellation, process loss and out-of-disk-space handling must preserve this distinction.

Deployment profiles define package identity, installer type, original-user or machine scope, required inputs, expected hash/signer, explicit arguments, timeout, success/restart exit-code rules and a meaningful installed-version/postcondition check. A generic successful process exit does not prove application deployment. ESET and remote-management agent packages require client-specific inputs; no tenant-specific package or credentials are invented.

Profiles distinguish MSI, reviewed vendor EXE, current-user MSIX/bundle and Office Deployment Tool packages, including each format's prerequisites. These are supported profile formats, not bundled licensed/vendor installers. A catalogue app without a complete validated profile remains Inventory/Manual deployment. Inventory must avoid Win32_Product because querying it can trigger MSI consistency checks or repair.

Use documented restart-suppression arguments for each installer profile. Installer return values such as 3010 and 1641 remain restart-required/restart-initiated outcomes where documented for that installer. Do not request a restart automatically or promise that an installer cannot initiate one. Do not kill or replay an in-flight installer just because the UI was cancelled. Stop queues at safe boundaries and preserve an uncertain outcome until reconciled.

Separate read-only process supervision from mutation supervision. The source OwnedProcess timeout/tree-kill behaviour may be adapted for bounded diagnostic reads; it must not be reused unchanged for installers. When a mutating process has started, timeout or loss of contact means Outcome unknown unless fresh evidence proves a narrower result. A normally exiting bootstrapper is insufficient if completion was delegated to an unobserved child.

After a change, run the relevant local check again. Distinguish Applied and verified, Applied but verification incomplete, Restart required, Failed, Cancelled before execution and Outcome unknown. Data copied from a previous capture never counts as current verification.

Recovery is operation-specific. Reversible settings may offer a separately reviewed restoration of recorded prior values after checking for subsequent drift. Package removal, app-data restoration, encryption reversal and global rollback are not implied by a saved snapshot.

## 8. Scripts, evidence and local storage

Ship reviewed local PowerShell scripts where a supported Windows command is preferable to a managed API. Use Windows PowerShell 5.1 without profiles for those adapters, with typed parameter binding, bounded output and explicit version/module prerequisites. Preserve execution policy. Do not require a separate PowerShell 7 installation for ordinary use.

Every shipped script has a manifest and pinned content hash. Modified or unregistered scripts are refused by the integrated runner. Script scanning is a useful check, not a security sandbox. Arbitrary script authoring and general elevated command execution are outside this initial product.

Store ordinary settings and evidence under the original user's LocalApplicationData/LocalDeviceToolkit directory. Use atomic creation, versioned strict schemas, bounded loads and run/device/user identity validation. Protected worker records use an administrator-controlled location and record both original and elevated identities.

Use GUID-based record filenames and validate resolved paths stay within the selected evidence root. Reject reparse/path escapes, unknown schema versions, duplicate JSON members, invalid timestamps and inconsistent outcome/evidence combinations. Limit each evidence record to 8 MiB, diagnostic output to 1 MiB per stream, and history to pages of 100 entries. Surface size-limit and directory-access failures; a page boundary is not the end of history.

History loading must report malformed, unreadable, missing and wrong-device records while retaining healthy entries. Never silently rewrite old evidence to fit new outcome terminology.

Exports are deliberate, sanitised and non-overwriting by default. Do not record passwords, recovery keys, tokens or arbitrary full registry/process dumps. Treat device/user identifiers as local client evidence, not data to commit to source control.

## 9. Packaging, development and validation

Prepare a self-contained Windows ZIP containing the WPF app, elevated worker, vetted scripts, catalogue, licence/provenance notices, launch instructions, version inventory and checksum file. Engineers do not need the .NET SDK. Preserve any original source notices in adapted files and record new product versioning separately from M365.

Use the existing isolated cloud checkout; do not create another worktree unless requested. Keep reproducible build/install instructions separate from runtime launch instructions. No background process is required after environment restoration.

Required checks:

- Portable engine tests for applicability, unknowns, strict catalogue/history parsing, plan expiry/invalidation, identity mismatch, per-action results and safe exports.
- Negative worker tests for unregistered actions, command injection, malformed input, replay, stale plans, substituted payloads, unsafe paths and wrong process/device/user context.
- Windows tests for actual probes and user-context handling, UAC cancellation, alternate administrator credentials and per-action privilege dispatch.
- Synthetic installer fixtures covering failure, restart outcomes, cancellation, ambiguous completion and independent installed-state verification. Tests must not install client applications or change a real device without a dedicated authorised test environment.
- WPF interaction and rendering checks for queue selection versus detail, keyboard access, unknown/error outcomes, smaller windows and supported scaling.
- Fresh-package extraction and launch on Windows without the SDK, including paths with spaces, current-user launch, worker discovery, script/catalogue integrity and actual report export.

Linux can run portable engine tests and cross-build Windows targets when configured. It cannot establish WPF, UAC or device behaviour. Record builds, automated tests, native Windows validation and real-device acceptance separately. Sign-off must identify every unrun acceptance check.

## 10. Scope and acceptance

This design covers the new native toolkit, the 30-control Windows reference, supported local probes/actions, local installer deployment, registered scripts, selective elevation, current-user correctness, evidence and portable packaging.

The implementation is complete only when an engineer can follow the assess → review → apply → verify → export workflow in the extracted Windows package, and failures or insufficient evidence are reported truthfully. Every control has an explicit automatic, partial or manual coverage disposition.

Tenant administration, remote fleet execution, wholesale import of the tenant application, automatic encryption/escrow changes, automatic reboots and unsupported CSP-to-registry conversion are outside this initial implementation. These limits do not hide catalogue controls or erase their required manual checks.

The implementation plan is docs/superpowers/plans/2026-10-14-local-device-toolkit.md. Implementation starts after the written plan is reviewed and an execution method is selected, following the requested Superpowers workflow.
