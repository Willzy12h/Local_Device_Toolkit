# Local engineer usage

The intended deployment target is Windows 11 x64. The main application runs as the Windows user who opens it. Current-user actions affect that engineer's profile; they do not configure another user's profile under alternate administrator credentials.

1. Extract the complete portable ZIP into a local folder; retain the `worker` and `scripts` subfolders.
2. Open `LocalDeviceToolkit.App.exe` normally. The portable package includes .NET and Windows Desktop runtimes; the engineer does not need an SDK.
3. Choose **New device setup** or **Diagnostics**. Select a stage/group and request checks. Opening pages performs no configuration checks or changes.
4. Review observed values, capture time and manual limitations. Use **Read with administrator** explicitly for supported protected checks. UAC cancellation starts no protected action.
5. For a supported fix, confirm the managed-policy review and add it to the shared queue. Review the exact targets, scope, privilege, inputs and before-state before applying. Machine changes receive a separate administrator review.
6. Inspect each result and actual execution account. Recheck affected controls before handover. Resolve unknown outcomes before making another attempt; never assume an installer or settings action completed from its exit code alone.
7. Export recorded evidence under **Reports & history**. Local evidence lives in `%LOCALAPPDATA%\LocalDeviceToolkit\evidence`; protected journals live under `%PROGRAMDATA%\LocalDeviceToolkit\journals` with administrator/SYSTEM access.

The current implementation and remaining features are listed in [Implementation status](IMPLEMENTATION-STATUS.md). Application deployment forms and the final report projections are still being implemented. Do not treat manual controls, application inventory or cloud-policy references as completed local automation.

The toolkit preserves normal PowerShell execution policy and downloaded-file markings. If an approved script cannot run, the check remains unavailable. Use your organisation's approved signing/distribution process; the toolkit does not bypass that policy.

## Build from source

Use the SDK pinned by `global.json`. On Windows, run `dotnet restore LocalDeviceToolkit.sln --locked-mode`, build/test the solution, then run `build/Publish-Portable.ps1` in PowerShell. Choose a new output directory; existing packages are not overwritten. A clean working tree is required unless `-AllowDirty` explicitly marks a local review package.

Use the extracted portable executable for native worker acceptance. Starting the app through `dotnet run` does not establish the packaged apphost/worker identity boundary.

On the Linux cloud host, run `bash build/Setup-Cloud.sh`. Build/test with `/workspace/.tools/dotnet/dotnet` and the cache variables documented in that script. Linux can cross-build/package the application; Windows is required to run WPF, Windows PowerShell 5.1 and UAC acceptance.
