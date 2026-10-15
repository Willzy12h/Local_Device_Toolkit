# Local engineer usage

The target is Windows 11 x64. Open the application as your normal Windows account. Current-user actions configure that engineer's profile, not a future end user's profile. Machine operations request UAC separately and record the actual administrator without replacing the original engineer.

## Start and assess

1. Extract the entire portable ZIP into a local folder, retaining `worker`, `scripts`, `tools`, `catalogue` and documentation. Open `LocalDeviceToolkit.App.exe` normally. The package includes .NET and Windows Desktop runtimes; no SDK, browser, service or tenant login is required.
2. Choose **New device setup** or **Diagnostics**, select a stage/group, then request checks. Navigation and task selection do not run checks or queue changes. Both workspaces reference the same tasks, evidence and operations.
3. Review observed values, capture time and manual limitations. Use **Read with administrator** explicitly for supported protected checks. Routine user checks stay in the original process.
4. For a supported fix, review effective GPO/MDM policy and add it to the shared queue. Review targets, scope, required privilege, parameters and exact before values before applying. Review expires after ten minutes; changed state or policy blocks execution.
5. Inspect each outcome and actual execution account. Recheck affected controls before handover. A restart requirement is reported; the toolkit does not request a device restart. An absent consumer Copilot app is **No change needed**, not a claimed removal.

The Windows fixes cover original-user extensions, UK formats/region/keyboard while preserving existing languages and input methods, exact consumer Copilot removal, long paths, fast startup off, AC-only sleep Never and UK time zone. Time zone preserves the daylight-saving choice and uses the engineer's assigned right when available. Automatic time-zone or observed management conflicts block the local action. System locale, default/welcome-screen profiles and other users are not configured.

## Approved local application deployment

Acquire client-authorized vendor media through the approved process. The toolkit does not download installers, invent licensing or accept command-line overrides.

| Format | Execution | Required inputs |
| --- | --- | --- |
| Machine MSI | Protected administrator worker | Signed x64 MSI, exact ProductCode/version/name/publisher and complete pinned media/cabinets |
| OneDrive machine EXE | Protected administrator worker | Exact signed Microsoft x64 OneDrive setup; reviewed `/allusers /silent` profile |
| MSIX/Appx or bundle | Original engineer | Exact package name/publisher/derived family/version; complete signed x64/neutral dependency set |
| Office ODT | Protected administrator worker | Signed Microsoft ODT, full offline x64 media, exact target Office version, supported product, MonthlyEnterprise/en-gb and explicit licensing/EULA authorization |

MSIX installs only for the current engineer; it does not provision other users. Office does not provide activation, a license key or subscription rights, force apps closed or allow a CDN media fallback. Observed Office management-policy conflicts block local channel changes. Real vendor acceptance remains listed in [Windows acceptance](WINDOWS-ACCEPTANCE.md).

Create a **draft** profile from the complete local media folder using Windows PowerShell 5.1, for example:

```powershell
.\tools\New-DeploymentProfile.ps1 -SourceDirectory 'C:\ApprovedMedia\Chrome' -EntryPoint 'chrome.msi' -ControlId APP-WIN-003 -Format MachineMsi -OutputPath 'C:\Drafts\chrome.json'
```

The helper reads metadata, signatures and hashes; it never launches an installer. The output must be a new file outside the media folder. It supports all four formats. Office additionally needs `-OfficeProductId` (`O365BusinessRetail` or `O365ProPlusRetail`), `-TargetOfficeVersion` matching the four-part offline media version, and `-LicenseAuthorized`. These are engineer/client inputs, not defaults that establish licensing.

In **Setup → Install applications** or **Diagnostics → Applications**, choose the shared application task and **Review fix**. Alternatively, use **Deployments → Choose application deployment**.

1. Choose the local media folder and **Import profile JSON for administrator approval**.
2. Review product/control mapping, scope, version, all file hashes and signer pins. Confirm client authorization, licensing, vendor guidance and effective policy. UAC approval only saves the immutable profile; it installs nothing.
3. Select the approved cached profile/media and add it to the shared queue. Installation remains a separate reviewed plan/action. Machine execution independently loads the protected approved profile; original-user MSIX independently reads its administrator-controlled profile.
4. Review the result, fresh installed identity/version and any restart or uncertainty. Inventory alone does not prove licensing, sign-in, client enrollment or build-standard completion.

| Control | Typical profile or remaining verification |
| --- | --- |
| APP-WIN-001 Microsoft 365 | Office ODT; activation/subscription and tenant configuration remain manual |
| APP-WIN-002 Company Portal | Approved original-user MSIX/bundle; Store acquisition and sign-in as required |
| APP-WIN-003 Chrome | Approved vendor MSI; sign-in/SSO configuration remains separate |
| APP-WIN-004 Reader | Approved vendor MSI; paid-product exceptions require client review |
| APP-WIN-005 OneDrive | Per-machine OneDrive profile; sign-in, Files On-Demand and Known Folder Move remain separate |
| APP-WIN-006 Teams | Approved original-user MSIX/bundle; other-user provisioning/sign-in remain separate |
| APP-WIN-007 RMM | Client/vendor-authorized profile and enrollment inputs; otherwise manual |
| APP-WIN-008 endpoint protection | Client/vendor-authorized profile, licensing and enrollment; otherwise manual |

A profile must genuinely match the selected control. The toolkit verifies the declared package, not the business authorization or every standard requirement. Unknown vendor EXE switches and arbitrary client scripts are not supported profiles.

## Interrupted deployments and restoration

Stopping a queue or losing supervision never kills an installer or retries it automatically. A product guard survives application/worker restarts. Use the profile form's **Read-only reconcile** to inspect fresh installed state and the original process identity without launching anything. Media need not remain present for this read-only path.

**Resolve reviewed completion** is separate and requires explicit vendor-completion/no-pending-children review, fresh exact installed product evidence and a completed original process. Lost process identity can resolve only with positive evidence of a different native boot identifier. A checkbox, absent product or unknown process does not prove safe completion. A separate receipt records the reviewer and evidence; the original uncertain run is preserved. Machine reconciliation uses UAC; user-package reconciliation stays as the original engineer.

Use **Restore a recorded local setting** under Windows configuration to select an original complete verified run/action. It restores only the five supported fixed settings, including recorded registry absence, after a new review and fresh no-drift check. Machine targets are independently derived from protected journals. Changed privilege, policy, power scheme, battery state, catalogue or incomplete evidence blocks restoration. There is no generic undo for application removal, installers, encryption or regional language changes.

## Reports and history

Open **Reports & history → Open saved reports & history**. Select saved records with Ctrl/Shift and create a setup handover, diagnostic ticket or deployment audit. A deployment run loads its actual reviewed plan. Read-only reports need no fabricated deployment run.

History is paged by record ID, not globally by date; use **Next history page** beyond the first 100 files. Corrupt/unavailable records remain visible as notices. **View saved record JSON** reopens a verified original record, including older catalogue versions, for inspection/copying. Projected compliance reports require a matching reference catalogue.

Record explicit manual reviews with source/note and an exclusion or applicability reason where appropriate. They cannot override missing, malformed, denied or failing automatic evidence. Reports retain source IDs, original requester, actual actors, dates, before/after values and limitations. Evidence older than ten minutes is a dated snapshot, not current compliance. Export HTML, CSV or JSON deliberately; existing exports are preserved.

User evidence is under `%LOCALAPPDATA%\LocalDeviceToolkit\evidence`. Protected machine journals/media/runtime are under `%PROGRAMDATA%\LocalDeviceToolkit` with administrator/SYSTEM writes. Approved profiles additionally permit user reads; user caches cannot authorize machine changes. Do not edit journals or delete pending guards to force a retry.

PowerShell execution policy and downloaded-file markings are preserved. If approved scripts cannot run, the result remains unavailable/uncertain. Use the organization's approved signing/distribution process; there is no automatic policy bypass, module installation or file unblocking.

## Build and verify

On Windows, use the SDK pinned by `global.json`, locked restore, warning-as-error build and solution tests. Run `build/Publish-Portable.ps1` from a clean working tree with a new output directory. `-AllowDirty` creates an explicitly labelled local review artifact. No third-party installer media is bundled.

`build/Test-Portable.ps1 -ZipPath <zip> -ResultPath <new-json> -ExpectedCommit <full-sha>` verifies archive/file hashes, required runtime/source assets, raw catalogue/script pins and AMD64 headers without extracting or executing the package. It does not prove native launch, publisher authenticity or device behavior. CI separately extracts and renders the actual application without an SDK on its runtime path. Use the extracted apphost for worker acceptance; `dotnet run` does not establish that boundary.

On the Linux cloud host:

```bash
bash build/Setup-Cloud.sh
source build/Use-CloudTools.sh
dotnet build LocalDeviceToolkit.sln -c Release --no-restore -warnaserror
dotnet test LocalDeviceToolkit.sln -c Release --no-restore -warnaserror
```

Setup pins SDK 10.0.401 and PowerShell 7.6.0, verifies their artifacts and restores locked dependencies. PS7 runs read-only package integrity tests on Linux; it is not Windows PowerShell 5.1 acceptance. No application service needs starting. WPF, UAC and real Windows/vendor operations require an authorized Windows host.
