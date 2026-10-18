using System;
using System.Collections.Generic;
using System.Threading;

namespace WinProfileRepair
{
    public static class WorkspaceScanner
    {
        public static ScanReport Scan(CancellationToken cancellation, Action<string> progress)
        {
            ScanReport report = ScanEngine.Scan(cancellation, progress);
            foreach (Finding finding in report.Findings) DescribeLegacyFinding(finding);
            cancellation.ThrowIfCancellationRequested();
            if (progress != null) progress("Inspecting per-user preview and context-menu registrations...");
            ShellDiagnostics.Scan(report, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (progress != null) progress("Inspecting startup entries and user environment...");
            EnvironmentDiagnostics.Scan(report, cancellation);
            cancellation.ThrowIfCancellationRequested();
            report.Notes.Add("Workspace diagnostics add read-only shell-handler, startup, and user-environment checks. New diagnostic findings cannot be selected for automatic repair.");
            report.Notes.Add("Cancellation is checked between core modules and during the new modules. Some individual Windows inspection calls must finish before cancellation can take effect.");
            return report;
        }

        internal static void DescribeLegacyFinding(Finding finding)
        {
            if (finding == null) return;
            finding.Classification = finding.CanFix ? "Confirmed fault" : "Needs review";
            finding.BackupPlan = finding.CanFix ? "Preserve the original registry values or shortcut in a current-profile backup before applying the selected repair. Restore refuses conflicting existing data." : "No automatic change is proposed. No backup is created by a read-only scan.";
            finding.VerificationPlan = "Run a new scan to check whether this finding is still detected. A clear scan is limited to these checks; confirm the original symptom in Windows and check again after restarting if needed.";
            switch (finding.Kind)
            {
                case FindingKind.ThumbnailOverride:
                    finding.Category = "Shell & appearance";
                    finding.RuleId = "PROFILE.THUMBNAIL.OVERRIDE";
                    finding.ProposedChange = finding.CanFix ? "Remove only this confirmed orphaned per-user thumbnail override, allowing the existing Windows image handler to be used. No image files or default applications are changed." : "Inspect the provider and available fallback. This override does not meet the automatic repair requirements.";
                    break;
                case FindingKind.StaleUninstallRecord:
                    finding.Category = "Apps & startup";
                    finding.RuleId = "PROFILE.INSTALLATION.RECORD";
                    finding.ProposedChange = finding.CanFix ? "Remove only the selected obsolete current-user uninstall record. Application files and machine installation records are not removed." : "Review the installation evidence or use the vendor's supported repair or uninstaller. A missing uninstaller alone is insufficient for cleanup.";
                    break;
                case FindingKind.BrokenShortcut:
                    finding.Category = "Apps & startup";
                    finding.RuleId = "PROFILE.SHORTCUT.TARGET";
                    finding.ProposedChange = finding.CanFix ? "Back up this broken Start Menu shortcut, then remove the original shortcut. Its target application and personal data are not changed." : "Inspect the shortcut and target before deciding on any manual action.";
                    break;
                default:
                    finding.Category = "Profile review";
                    finding.RuleId = "PROFILE.INSPECTION.REVIEW";
                    finding.ProposedChange = "Review the evidence. This finding cannot be changed automatically.";
                    break;
            }
        }

        internal static bool CanVerifyFindingAbsence(ScanReport report, IEnumerable<string> findingIds, IEnumerable<string> requiredModules)
        {
            if (report == null || report.Modules == null || findingIds == null || requiredModules == null) return false;
            bool hasModule = false;
            foreach (string moduleId in requiredModules)
            {
                hasModule = true;
                bool completed = false;
                foreach (DiagnosticModuleResult module in report.Modules)
                    if (module != null && module.Id == moduleId && module.Status == "Completed") completed = true;
                if (!completed) return false;
            }
            if (!hasModule) return false;
            bool hasFinding = false;
            foreach (string id in findingIds)
            {
                hasFinding = true;
                if (String.IsNullOrEmpty(id)) return false;
                foreach (Finding finding in report.Findings) if (finding.Id == id) return false;
            }
            return hasFinding;
        }
    }
}
