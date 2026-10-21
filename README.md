# Local Device Toolkit

A native Windows WPF toolkit for engineers to assess the current device, review local changes and application deployments, and verify their results.

The lightweight edition starts with **Start.exe**: a native progress window checks .NET 10 and silently installs verified private runtime components when missing, then opens the app. Runtime setup needs no administrator rights or reboot. A self-contained offline edition remains available. See [local usage](docs/LOCAL-USAGE.md) for download, servicing and policy requirements.

The toolkit includes setup and diagnostics workspaces, shared checks and queue, current-user settings, selective elevation, recorded restoration, reviewed MSI/OneDrive/MSIX/Office deployments and evidence-based reports. Native device and vendor acceptance remains pending; see the measured status below.

The selected direction is a new WPF application adapted from the relevant parts of [M365 Buildstandards](https://github.com/Willzy12h/M365-Buildstandards). It runs as the current Windows user and requests UAC elevation only for operations that require it.

- [Local engineer usage and building](docs/LOCAL-USAGE.md)
- [Simple text guide and copy-and-paste examples](HOW-TO-USE.txt) (beside Start.exe in both packages)
- [Created work and outstanding implementation](docs/IMPLEMENTATION-STATUS.md)
- [Windows acceptance record](docs/WINDOWS-ACCEPTANCE.md)
- [Engineer readiness expansion and measured results](docs/superpowers/2026-10-16-engineer-readiness-execution.md)
- [Implementation decisions](docs/IMPLEMENTATION-DECISIONS.md)
- [Reviewed design proposal](docs/superpowers/specs/2026-10-14-local-device-toolkit-design.md)
- [Implementation plan](docs/superpowers/plans/2026-10-14-local-device-toolkit.md)
- [30 original M365 Windows control definitions](docs/reference/m365-windows-controls.json)

The engineer readiness expansion adds 24 local checks and six separate reviewed actions, quieter diagnostics, application verification and target-specific handover. Select checks, review current and expected values, then confirm changes; inspection and action results share one dated log per run.

The source control definitions preserve upstream requirements and provenance. They are not executable local scripts or evidence of completed local automation. The earlier PowerShell/WinForms device toolkit has not been imported.

Native Windows, UAC and actual device validation will be reported separately from portable tests and cross-builds. No device changes have been performed.
