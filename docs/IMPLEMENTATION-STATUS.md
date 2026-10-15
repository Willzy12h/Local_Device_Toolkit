# Implementation status

Updated 15 October 2026. This records implemented source and observed checks, not promised native results.

## Created

| Capability | Current evidence |
| --- | --- |
| Shared M365 reference | All 30 original Windows controls retained with pinned source commit/hash and separate, truthful local coverage. |
| Native WPF application | Home, New device setup, Diagnostics, Deployments, Reports/history; shared task detail, original reference viewer, explicit checks, evidence time and manual limitations. |
| Shared execution model | One canonical task/operation catalogue and queue across both workflows; identical requests deduplicate; before-state, policy checks, ten-minute review expiry and stop-on-uncertainty guards. |
| Local checks | Native OS/user identity plus a pinned, bounded read-only PowerShell probe for protection, configuration and inventory. Unknown, denied and manual evidence remain distinct. |
| Evidence | Immutable records, digest/schema verification, atomic writes, bounded paging and visible corruption/access diagnostics. |
| Selective elevation | Separate administrator review, OS-observed process/token/pipe peers, remote-client refusal, protected staging/journals and replay refusal. Original engineer and actual administrator remain separate. |
| Local fixes | Show file extensions, UK region/keyboard preserving languages, exact consumer Copilot removal for the original engineer; machine long paths, fast startup off and AC sleep Never. Fixed typed operations; independent readback. |
| Build/package | Self-contained Windows x64 App and Worker publish; reviewed script checks, ZIP, SHA-256 and full package file inventory. Working-tree previews are labelled. |
| Cloud setup | Checksum-pinned SDK 10.0.401, tested locked restore and saved environment draft. Activation/publication and fresh-task restoration are separate. |

## Checks actually run

- Portable solution: **94 tests passed, zero failed/skipped** (74 engine/session/contract tests and 20 Windows-adapter tests) in the current Linux build.
- WPF App and Worker cross-build: zero warnings/errors with warnings treated as errors.
- Self-contained Windows app and worker published and packaged on Linux. PE x64 apphost and included .NET/Windows Desktop runtime confirmed.
- Script hash and syntax checked with PowerShell 7.6.0 on Linux; **this is not Windows PowerShell 5.1 validation**.
- Official SDK archive SHA-512 verified; cloud setup's existing-install/locked-restore path passed.

Windows CI at commit `049b2fd` passed 77 tests, PowerShell 5.1 parsing, native token/pipe/junction checks, packaging and actual renders of all five WPF pages ([run](https://github.com/Willzy12h/Local_Device_Toolkit/actions/runs/38660754520)). The newer current-user actions and UI refinements require another native run.

Windows-specific tests take explicit unsupported-host branches on Linux. These counts do not prove Windows API, pipe ACL, UAC, GUI rendering or real-device mutation behaviour. CI is configured to check native process/pipe APIs, PowerShell 5.1 syntax and actual extracted-app WPF renders; results must be recorded after execution.

## Remaining work

| Area | Still required |
| --- | --- |
| Current-user actions | Source and dedicated tests implemented; native read-only script tests and interactive mutation acceptance pending. |
| Machine settings | UK time zone with privilege detection; operation-specific restoration and further native verification. |
| Deployments | Approved MSI/vendor EXE/current-user MSIX/Office profiles, full-file staging/signers, installation reconciliation and actual package inputs. Inventory alone is not deployment. |
| Reports | Evidence-based setup handover, diagnostic ticket and deployment projections; HTML/CSV; complete history paging and uncertain-run reconciliation. JSON capture export already exists. |
| UI journeys | Package input/review/result forms and final usability/accessibility/native journey checks. Workflow/task/check/queue foundations already exist. |
| Script integration | Extend the reviewed registry for remaining fixed operations; never add arbitrary elevated script/command execution. |
| Native acceptance | Windows CI results; interactive alternate-admin/cancelled UAC, restricted execution-policy, DPI/high-contrast/keyboard/Narrator and representative device checks. |
| Final review | Fresh whole-branch review, remaining fixes, final package and final environment-draft commit update. |

No real device settings have been changed from this Linux workspace. The earlier PowerShell/WinForms toolkit was unavailable; this is the new WPF implementation selected by the user. Relevant M365 code was adapted with provenance; the cloud tenant tool was not copied wholesale.
