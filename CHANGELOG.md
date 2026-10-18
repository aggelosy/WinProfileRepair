# Changelog

## 0.3.0-preview (development)

- Implemented the selected Workspace layout with Overview, Diagnostics, Repair history, Backups, and session Settings.
- Added structured evidence, proposed changes, backup/undo conditions, and verification guidance in the inspector.
- Added bounded read-only inspection of per-user preview/icon/context-menu handlers, startup registrations, Startup-folder inventory, and user PATH/TEMP/TMP values.
- Added per-module coverage and partial-inspection status, scan progress, and cancellation between core modules and within the expanded modules.
- Added private operation history and compatibility summaries for older backup journals. History metadata never authorizes a repair or restore.
- Added diagnostic verification that checks profile identity, scan chronology, relevant module coverage, and recurring findings at the same target. A finding no longer detected still requires checking the original symptom.
- Preserved the original repair allowlist, revalidation, backup schema, restore validation, and separate icon-cache operation. New diagnostic findings are never selected for automatic repair.
- Expanded synthetic tests for parser ambiguity, inaccessible/remote/reparse-point targets, history corruption and persistence, verification, legacy backup summaries, and Workspace navigation/rendering.

## 0.2.0-preview

- Redesigned the desktop interface with a neutral workspace, dark navigation rail, compact summary cards, and a dedicated evidence inspector.
- Kept findings, manual review, and image-file diagnostics in separate sections; utility actions remain separate from selected repairs.
- Preserved all scan, repair, restore, export, and icon-cache functions and confirmation steps. The scanning and repair engine source files are unchanged from 0.1.0-preview.
- Retained the existing backup format, including compatibility with backups created by 0.1.0-preview.
- Added UI checks for navigation, explicit selection, busy states, stale-evidence clearing, and minimum-size rendering.

## 0.1.0-preview

- Read-only profile scan for orphaned thumbnail overrides, stale installation metadata, and broken Start Menu shortcuts.
- Read-only image-file association and icon-resource diagnostics.
- Explicitly selected, revalidated current-user cleanup with durable backups and guarded restore.
- Separate current-session Explorer/icon-cache rebuild.
- Local JSON report export and read-only command-line report mode.
- Source build, policy/backup/false-positive/UI checks, release packaging, and Windows GitHub Actions workflow.

This is an unsigned preview evaluated on one Windows machine. Live repair/restore and Explorer-restart operations in the new executable have not been exercised on multiple Windows installations; the underlying repair was verified manually during the original investigation. Default applications, machine installations, drivers, services, installed runtimes, and personal application data are outside automatic cleanup.
