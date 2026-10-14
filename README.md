# Local Device Toolkit

A planned native Windows WPF toolkit for engineers to assess the current device, review local changes and application deployments, and verify their results.

**Current status: implementation in progress. The shared catalogue/workflow foundation and its portable tests exist. The native application, Windows actions and packaged solution are still being implemented.**

The selected direction is a new WPF application adapted from the relevant parts of [M365 Buildstandards](https://github.com/Willzy12h/M365-Buildstandards). It will run as the current Windows user and request UAC elevation only for operations that require it.

- [Created work and outstanding implementation](docs/IMPLEMENTATION-STATUS.md)
- [Reviewed design proposal](docs/superpowers/specs/2026-10-14-local-device-toolkit-design.md)
- [Implementation plan](docs/superpowers/plans/2026-10-14-local-device-toolkit.md)
- [30 original M365 Windows control definitions](docs/reference/m365-windows-controls.json)

The source control definitions preserve upstream requirements and provenance. They are not executable local scripts or evidence of completed local automation. The earlier PowerShell/WinForms device toolkit has not been imported.

Native Windows, UAC and actual device validation will be reported separately from portable tests and cross-builds. No device changes have been performed.
