# Implementation status

Updated 15 October 2026. Implemented source and observed automation are separate from real-device acceptance.

## Created

- Native WPF Home, New device setup, Diagnostics, Deployments and Reports/history. Setup and diagnostics reference the same canonical tasks, operations and queue.
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

The reviewed cloud candidate passed **296 tests: 185 engine and 111 Windows-adapter tests**, zero failures/skips, and a full App/Worker build with zero warnings/errors. The next native checkpoint at `aa80274` passed **297 tests: 185 engine and 112 adapter tests**, including actual Windows APIs, trust verification and MSI/MSIX metadata fixtures. Twenty package-verifier cases execute PowerShell 7 on Linux, including altered/missing assets and runtime checks. Windows-only test branches do not prove native behavior on Linux.

Earlier native Windows CI passed 77 and then 94 tests, PowerShell 5.1 checks, token/pipe/junction checks and extracted-app WPF renders. The expanded candidate at `6682d2b` passed all 169 engine tests but failed two native adapter fixtures: catalog-signed executable selection and inherited PowerShell module paths. Those fixtures were repaired without changing production trust checks. The MSI COM reader also required suppression of incidental automation method status output; its exact-value native regression then passed. Native packaging exposed legacy .NET Framework ZIP entry separators; archive creation now uses explicit canonical entries while retaining strict validation. Historical native checkpoints and outstanding device gates are recorded in [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md); exact current run results are available in [GitHub Actions](https://github.com/Willzy12h/Local_Device_Toolkit/actions/workflows/verify.yml).

## Still pending outside source implementation

- Interactive UAC cancellation/alternate credentials, real settings/restoration, vendor installs/reboots, execution-policy/MOTW distribution and physical DPI/high-contrast/keyboard/Narrator acceptance on an authorized Windows workstation.
- Client/vendor installers, approved signer pins, licensing rights, tenant sign-in and RMM/EDR enrollment inputs. Missing inputs remain manual and never count as verified deployment.
- User publication of the saved cloud environment configuration and validation in a fresh task.

No customer device was modified by this cloud task. The unavailable earlier WinForms toolkit was not recovered; this is the new WPF implementation selected by the user, adapted from the M365 source with provenance.
