# Implementation status

Updated 15 October 2026. Implemented source and observed automation are separate from real-device acceptance.

## Created

- Native WPF Home, setup task table, diagnostic list/details, Deployments and Reports/history. Shared operations support individual selections, measured current/expected values, explicit review/confirmation and verified results.
- Separate UK preferences, eight application rows and individual readiness checks. New dated text log per review/run includes checks, full sources and changes in the same file; already-correct values are skipped.
- All 30 M365 Windows controls retained with pinned source commit/hash, separate local coverage, manual evidence and honest unknown results.
- Immutable evidence, strict schemas, exact before-state, ten-minute review expiry, durable intent, deduplication across settings/restoration, and stop-on-uncertainty/replay guards.
- Main application runs as the current engineer. A separately reviewed administrator worker handles protected actions while preserving original-user targets and recording the actual administrator.
- Original-user file extensions, UK region/keyboard preservation and exact consumer Copilot removal; long paths, fast startup, AC-only sleep and privilege-aware timezone. Recorded restoration supports the five fixed settings and refuses drift.
- Reviewed local MSI, per-machine OneDrive, original-user MSIX/bundles/dependencies and offline Office Deployment Tool profiles. Complete media hashes, Windows signer verification, protected approvals/staging, durable product guards and explicit read-only reconciliation/resolution.
- Handover, diagnostic and deployment reports; separate manual reviews, raw immutable record viewing, bounded history, and safe non-overwriting HTML/CSV/JSON exports.
- Three pinned scripts with closed parameters. No generic elevated command runner, tenant sign-in, cloud host or automatic policy bypass.
- Self-contained Windows x64 packaging, all-file inventory and an independent ZIP integrity verifier. Normal and compact native UI smoke journeys include the actual dialogs.
- Checksum-verified SDK 10.0.401 and PowerShell 7.6.0 cloud setup, locked restore and reusable activation instructions.

## Checks actually run

The select/review/run candidate passed **410 tests: 295 engine and 115 Windows-adapter tests**, zero failures/skips, on Linux. The full App/Worker solution built with zero warnings/errors. Native Windows verification is tracked separately in the acceptance record; Linux Windows-only branches do not establish native acceptance.

The reviewed cloud candidate passed **296 tests: 185 engine and 111 Windows-adapter tests**, zero failures/skips, and a full App/Worker build with zero warnings/errors. The next native checkpoint at `aa80274` passed **297 tests: 185 engine and 112 adapter tests**, including actual Windows APIs, trust verification and MSI/MSIX metadata fixtures. Twenty package-verifier cases execute PowerShell 7 on Linux, including altered/missing assets and runtime checks. Windows-only test branches do not prove native behavior on Linux.

Earlier native Windows CI passed 77 and then 94 tests, PowerShell 5.1 checks, token/pipe/junction checks and extracted-app WPF renders. The expanded candidate at `6682d2b` passed all 169 engine tests but failed two native adapter fixtures: catalog-signed executable selection and inherited PowerShell module paths. Those fixtures were repaired without changing production trust checks. The MSI COM reader also required suppression of incidental automation method status output; its exact-value native regression then passed. Native packaging exposed legacy .NET Framework ZIP entry separators; archive creation now uses explicit canonical entries while retaining strict validation. Historical native checkpoints and outstanding device gates are recorded in [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md); exact current run results are available in [GitHub Actions](https://github.com/Willzy12h/Local_Device_Toolkit/actions/workflows/verify.yml).

## Still pending outside source implementation

- Interactive UAC cancellation/alternate credentials, real settings/restoration, vendor installs/reboots, execution-policy/MOTW distribution and physical DPI/high-contrast/keyboard/Narrator acceptance on an authorized Windows workstation.
- Client/vendor installers, approved signer pins, licensing rights, tenant sign-in and RMM/EDR enrollment inputs. Missing inputs remain manual and never count as verified deployment.
- User publication of the saved cloud environment configuration and validation in a fresh task.

No customer device was modified by this cloud task. The unavailable earlier WinForms toolkit was not recovered; this is the new WPF implementation selected by the user, adapted from the M365 source with provenance.

## Engineer readiness source expansion — 16 October 2026

The new source adds the 24 separately registered local checks, native typed target inputs, shared setup/diagnostic task navigation, unticked suggestions, bounded incident pages and deterministic event explanations. Source M365 controls remain 30. Check evidence includes target, coverage, local schema/rule versions and exact selection; changes of target invalidate displayed evidence and review. Application inventory separates installed/configured/sign-in/licensing/restart dimensions.

Six separate actions extend the existing review/UAC/journal path: four power timeout slots, whole-device DNS cache refresh with an explicit stale-cache hypothesis, and configured-source Defender signature requests. Original-engineer power guards, protected ordered-state checks, drift refusal and raw recorded restoration are included. Signature process uncertainty keeps a durable guard and requires explicit protected reconciliation. Action completion, expected-state verification and symptom resolution remain distinct; no exit code is presented as a general fix.

Native handover provides eleven independent manual rows, client-checklist loading, target-specific attestations and eight fixed Settings launch buttons. Reports offer minimized export previews and compatible two-capture comparison. Uptime and parent-process identity supplement wall-clock review expiry. Eleven scripts are pinned; package verification checks the complete closed inventory.

Available validation is recorded in the expansion execution report. Native Windows 11/UAC/hardware/network acceptance remains pending as listed in WINDOWS-ACCEPTANCE.md. No customer device was changed. Earlier ZIPs and screenshots belong to their earlier commits; they do not prove this expansion's native behavior.

## Lightweight startup — 18 October 2026

Adds a framework-dependent package and tiny built-in-framework windowed launcher, with WPF startup progress, complete x64 Core/Desktop detection, automatic private runtime installation, fixed Microsoft SHA-512 pins, bounded safe extraction, staging/locking, cancellation, dated logs and automatic main-app launch. Existing compatible runtimes are reused. Runtime installation requests no elevation or reboot; device-action elevation remains separate. Native CI now exercises real private-runtime downloads under a temporary standard Windows account, native startup renders, all 37 application views and the worker’s managed refusal/private-runtime resolution. Record observed CI results separately; source presence is not a passed native check.

Local verification: full Release solution build passed with zero warnings/errors; **481 tests passed (329 engine +152 adapters), zero failures/skips**. Startup fixtures cover compatible/mixed patches, wrong/partial runtimes, safe install/reuse, SHA-512 failure, traversal and elevated-install refusal. Independent review identified four Important issues: restricted-child worker launch, hidden pre-UI errors, mixed-patch detection and inherited roll-forward settings. Source fixes are included; the native outcome is recorded below. Existing 11 registered script hashes remain unchanged and passed verification.

[Windows and Ubuntu run 39159445568](https://github.com/Willzy12h/Local_Device_Toolkit/actions/runs/39159445568) passed at **fde9e16**: both 481-test suites, PowerShell 5.1, both archive verifiers, offline native renders and actual Microsoft runtime download/installation followed by automatic WPF launch under a real temporary standard Windows account. The CI account SID was checked and its token reported **Elevated=false**. Missing-runtime UI, subsequent runtime reuse, inherited roll-forward override handling, blocked-policy native feedback, 37 first-run and 37 subsequent app views, startup preview, and private worker runtime loading/refusal all passed. The test account was removed afterward. The synthetic restricted-token harness could not initialise PowerShell on the CI desktop and was replaced with the real standard account; no such account creation or desktop ACL code ships in the application.

The verified **fde9e16** lightweight ZIP contains **72 files**, is **1,220,229 bytes** compressed and **3,903,932 bytes** extracted (including manifest), before any runtime. SHA-256: `2a57c7fcd9457957b7a90a26d0ab1ce8527bbfd627379fbbba6836a9a58ad675`. Runtime installation required neither app relaunch nor a Windows reboot; the independent worker refusal check used the elevated CI parent and does not establish interactive UAC acceptance. A subsequent small layout adjustment gives the startup Cancel button more room; its exact package/run is tracked in the PR. Physical Windows 11 acceptance remains pending.
