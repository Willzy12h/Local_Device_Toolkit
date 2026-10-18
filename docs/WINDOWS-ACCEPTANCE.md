# Windows acceptance record

No real device settings or client applications have been changed by this cloud task. Do not infer vendor installation or interactive UAC acceptance from build/test/render results.

## Observed automation

- `049b2fd`: [Windows/Linux CI](https://github.com/Willzy12h/Local_Device_Toolkit/actions/runs/38660754520) passed 77 tests, native token/pipe/junction checks, PowerShell 5.1 parsing, self-contained packaging and five actual extracted-application WPF renders.
- `b3802a8`: [Windows/Linux CI](https://github.com/Willzy12h/Local_Device_Toolkit/actions/runs/38665135124) passed 94 tests, including actual Windows PowerShell read-only original-user checks and UI refinements.
- `50ab862`: native Windows-adapter tests passed; deployment engine fixture tests failed because a Linux-only absolute fixture path was not fully qualified on Windows. The fixture now uses the host's absolute path. The production path validation was preserved.
- `6682d2b`: 169 engine tests passed on Windows; two adapter fixtures failed (catalog-signed executable selection and inherited PowerShell module paths). Fixtures were corrected while preserving production trust checks.
- Final cloud candidate: 185 engine and 111 adapter tests passed on Linux, zero failures/skips. Windows-specific branches are not exercised by that count. Twenty ZIP-verifier cases execute PowerShell 7 on Linux. [Native rerun at `aa80274`](https://github.com/Willzy12h/Local_Device_Toolkit/actions/runs/38670204538) passed 185 engine +112 adapter tests, Windows PowerShell 5.1 parsing and publishing. Its actual ZIP integrity check correctly refused backslash entry names emitted by legacy .NET Framework ZIP creation; archive creation now explicitly uses canonical forward-slash entries. The final workflow verifies that archive and the actual extracted application. A parallel PR run exposed a 15-second fixture cleanup deadline; the observation assertion remains unchanged and fixture allowance is extended.

- Select/review/run checkpoint `32c1cb7`: [native Windows and Linux CI](https://github.com/Willzy12h/Local_Device_Toolkit/actions/runs/38707148089) passed 295 engine +115 adapter tests, PowerShell 5.1 checks, complete ZIP verification and 21 extracted-app views. Its dated read-only log correctly reported unsupported Windows Server rather than authorizing changes. Visual review identified squeezed setup columns; the subsequent layout fix retains minimum widths, single-line labels, bounded rows and horizontal scrolling. Current workflow results establish the final layout checkpoint.

## Required device acceptance

Use a dedicated authorized Windows 11 x64 workstation and record the exact portable ZIP SHA-256, commit, Windows build, accounts, before/after evidence IDs and outcome for each case.

| Case | Status |
| --- | --- |
| Extracted package without SDK, spaces/Unicode path | Earlier CI passed; final package/render outcome is recorded in the workflow run |
| Actual UAC cancellation; no protected operation starts | Pending interactive Windows host |
| Alternate administrator credentials preserve original user target and record actual administrator | Pending interactive Windows host |
| Main app/worker build mismatch, stale review, changed state and replay refusal | Automated contract coverage; native interaction pending |
| Original-user extensions, UK language preservation and exact consumer Copilot removal | Read paths/contract coverage; real mutations pending |
| Long paths, fast startup, AC-only sleep, timezone privilege and exact restoration/absence | Contract coverage/native read paths; real mutations pending |
| Restricted execution policy/MOTW and approved signed distribution | Pending authorized distribution/Windows host |
| Approved MSI, OneDrive, original-user MSIX/bundles and Office offline media | Synthetic metadata/guard coverage; actual vendor media/install acceptance pending |
| 3010/1641, detached bootstrapper, interrupted process, durable guard and reviewed resolution | Contract/process tests; representative vendor/reboot interaction pending |
| 100%, 150%, 200% actual display DPI, high contrast, keyboard focus and Narrator | Pending physical accessibility acceptance; compact renders are not a substitute |

Client RMM/EDR media, tenant sign-in, licensing rights and client-specific authorization must be supplied through the approved engineer/vendor process. The toolkit does not invent these or collect administrator passwords.

Exact current commit/run outcomes, including the final archive verification and 21 normal/compact WPF views, are available in [Verify native toolkit](https://github.com/Willzy12h/Local_Device_Toolkit/actions/workflows/verify.yml). A failed or unrun step is not native acceptance.

## Engineer readiness expansion — 16 October 2026

The expansion adds 24 local checks, four separate power timeout actions, DNS cache refresh and a guarded Defender signature request. Linux fixtures and cross-compilation do **not** establish Windows 11 acceptance. Until a recorded disposable-device run is attached, the following remain pending:

- Same-engineer UAC and alternate-credential refusal for all new and legacy power writes/restoration; protected prefix validation across multiple selected power actions; all four indices and scheme preserved; exact raw-seconds restoration and concurrent scheme/policy changes.
- S0/S3 laptops, battery/no-battery desktops, VM/unsupported media-health providers, multi-battery systems, localized WinRE/time output, denied provider/channel access, encrypted/suspended volumes and recovery references without key retrieval.
- VPN/split-DNS/NRPT changes, positive/negative cache hypotheses, no cache restoration claim, only selected-host retest, offline/third-party AV/passive Defender, configured source order, update process timeout/cancellation/restart and explicit protected reconciliation.
- Local/shared/unavailable printer inventory without document/owner data, literal-target identity, approved-profile inventory and unresolved installer states. No actual installer media or customer sign-in is supplied by this repository.
- App sleep/resume, wall-clock rollback and waiting at UAC beyond the review deadline; original app restart must require a new review.
- Native normal/compact layouts for local task lists, incident/network inputs, each repair review, power timeouts and handover; keyboard/tab order, high contrast, Narrator and physical DPI.

CI renders native WPF without executing mutations and parses scripts under Windows PowerShell 5.1. These checks complement this matrix; they do not replace it. A manual-only checklist is scoped to its selected requirements and original engineer, not a certification of the eventual end user's device or tenant state.

Native CI evidence for `e472383`: [run 38890927681](https://github.com/Willzy12h/Local_Device_Toolkit/actions/runs/38890927681) passed both jobs, 469 tests, PowerShell 5.1 checks, complete 858-file portable package verification, extracted self-contained app launch and 37 normal/compact renders. The actual dated read-only review log is included in the artifact. The Windows Server runner does not satisfy the physical Windows 11, interactive UAC or accessibility matrix above. Exact archive provenance and SHA-256 are in [the execution record](superpowers/2026-10-16-engineer-readiness-execution.md).

## Silent private runtime startup — 18 October 2026

The lightweight edition adds Start.exe using the built-in framework to show WPF startup progress before .NET 10 is available. Missing runtime installation uses pinned Microsoft Core/Desktop ZIPs, runs without elevation, and automatically opens the main app. No runtime-install reboot or app relaunch is required. The offline self-contained edition remains available. Native CI exercises an actual missing-runtime first run with a controlled global-discovery fixture and restricted token, incompatible inherited runtime flags, subsequent reuse, policy-blocked native feedback and private worker runtime loading/refusal in the elevated parent. Interactive UAC approval is not automated by that refusal check. Observed CI outcome will be recorded after completion. Physical Windows 11 acceptance above remains pending.
