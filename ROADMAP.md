# WinProfileRepair roadmap

WinProfileRepair is evolving toward broad Windows profile diagnosis and guided recovery. Each module needs documented evidence, a defined scope, explicit limitations, and tests before repair is offered. These plans are not claims of current capability or release dates.

## Workspace foundation: 0.3 development preview

- Overview, Diagnostics, Repair history, Backups, and Settings.
- Existing guarded icon-registration, stale-record, and shortcut repairs.
- Read-only expanded shell-handler and startup/environment diagnostics.
- Per-module coverage, cancellation, private history, and post-operation diagnostic verification.

## Application and folder diagnosis

- Start, taskbar, and Search symptoms with version-specific evidence.
- One-app MSIX/Store activation, registration, dependency, and policy diagnostics.
- Supported default-app recovery through Windows system UI.
- Known-folder locations, folder collisions, OneDrive/network redirection, and targeted access diagnostics.

## Advanced profile investigation

- Temporary-profile and sign-in event correlation.
- Voluntary comparison of diagnostic exports from working and affected profiles.
- Configuration snapshots and recurring-change investigation.
- Guided recovery for hive loading, roaming profiles, and managed environments.

## Windows-wide workflows

- Separate guided workflows for networking/proxy, printing, audio, Windows Update, and system integrity.
- A narrowly scoped administrative helper if individual tested operations require elevation; the profile workspace remains a normal-user process.

## Release quality

- Broader testing across disposable Windows profiles and VMs, supported builds, display scales, cloud redirection, and company management.
- Code signing, accessibility checks, and reviewed/redacted support exports.
- Repair recipes with stable IDs, prerequisites, exact changes, backup/rollback limits, and verification.

Generic registry deletion, bulk app resets, recursive permission resets, credential removal, and deletion of installer/runtime/driver files are outside automatic cleanup. Some conditions will remain diagnosis or guided recovery rather than an automatic repair.
