# Security

## Reporting a vulnerability

If the public GitHub repository has private vulnerability reporting enabled, use **Security → Report a vulnerability**. If it is unavailable, open an issue asking the maintainers for a private reporting channel without publishing exploit details, personal logs, or backup contents. No separate security contact has been established for this preview.

Include the affected version, the Windows version, a description of the trust boundary involved, and reproduction steps using synthetic data. Do not include user SIDs, personal paths, account tokens, credentials, or unredacted registry exports.

## Trust and data handling

This utility reads local configuration and can change selected records and shortcuts in the current profile. Treat its executable and backup manifests as trusted code/data. Download releases from [the project's release page](https://github.com/aggelosy/WinProfileRepair/releases), compare published checksums, or build the reviewed source yourself. Do not run a modified executable or restore a backup from an unknown source.

Repairs require an unelevated process, explicit selection, evidence revalidation, and a backup. The intended repair scope excludes machine-wide installation removal, drivers, services, and application data. These constraints reduce risk; they do not replace independent testing and code review.

Exported reports and backups can contain private Windows account and application information. Keep them local unless you have reviewed and redacted them. The application does not automatically upload them or collect telemetry.

The Workspace also stores private operation metadata under `%LOCALAPPDATA%\WinProfileRepair\History`. A completed interactive scan can update this history, but it does not change Windows configuration. History and backup summaries are descriptive metadata: restore uses the original validated backup, and repair uses fresh live evidence. Corrupted or foreign-account history is not a source of authorization. A clear diagnostic check does not prove that every symptom has been fixed.

The initial preview has been evaluated on one machine. No supported security-update schedule or long-term support commitment has been established.
