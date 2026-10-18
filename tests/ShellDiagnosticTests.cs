using System;
using System.IO;
using System.Threading;
using WinProfileRepair;

internal static class ShellDiagnosticTests
{
    private static int assertions;

    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new Exception("FAILED: shell diagnostics: " + name);
    }

    internal static int Run(string artifacts)
    {
        assertions = 0;
        const string clsid = "{12345678-1234-4234-8234-123456789ABC}";
        ShellHandlerAssessment missing = ShellDiagnostics.Assess(clsid, PathPresence.Missing, false, PathPresence.Unknown);
        Check(missing.RuleId == "shell.handler.unregistered-clsid" && missing.Classification == "Confirmed fault" && missing.RequiresReview, "a missing classic and packaged registration is confirmed evidence");
        Check(!missing.Incomplete, "proven missing registration is a completed check");
        ShellHandlerAssessment inaccessible = ShellDiagnostics.Assess(clsid, PathPresence.Unknown, false, PathPresence.Unknown);
        Check(inaccessible.Classification == "Unable to inspect" && inaccessible.Incomplete, "inaccessible COM registration is not absence");
        ShellHandlerAssessment present = ShellDiagnostics.Assess(clsid, PathPresence.Exists, false, PathPresence.Unknown);
        Check(!present.RequiresReview && !present.Incomplete, "packaged or other registered activation model is not declared broken without a DLL");
        Check(present.Summary.IndexOf("not been tested", StringComparison.Ordinal) >= 0, "registration presence does not claim runtime health");
        ShellHandlerAssessment dllMissing = ShellDiagnostics.Assess(clsid, PathPresence.Exists, true, PathPresence.Missing);
        Check(dllMissing.RuleId == "shell.handler.missing-inproc-target" && dllMissing.RequiresReview, "explicit missing local inproc DLL gets a review finding");
        ShellHandlerAssessment dllUnknown = ShellDiagnostics.Assess(clsid, PathPresence.Exists, true, PathPresence.Unknown);
        Check(dllUnknown.Classification == "Unable to inspect" && dllUnknown.Incomplete, "unsafe or inaccessible DLL target is unknown");
        Check(!ShellDiagnostics.Assess(clsid, PathPresence.Exists, true, PathPresence.Exists).RequiresReview, "present DLL does not generate a speculative failure");
        foreach (string invalid in new string[] { null, "", " ", "-", "not a CLSID", "{1234}", clsid + " extra" })
        {
            ShellHandlerAssessment result = ShellDiagnostics.Assess(invalid, PathPresence.Missing, true, PathPresence.Missing);
            Check(result.RuleId == "shell.handler.invalid-clsid" && result.Classification == "Needs review", "invalid CLSID never becomes confirmed absent COM: " + invalid);
        }
        Finding finding = ShellDiagnostics.BuildFinding("Example preview handler", @"HKCU\Software\Classes\.pdf\shellex\Example [Registry64]", clsid, missing);
        Check(finding.Kind == FindingKind.ReviewOnly && !finding.CanFix, "even a proven missing class is never automatically repaired");
        Check(finding.Snapshot == null, "no mutable registry snapshot is attached");
        Check(finding.Category == "Shell & appearance" && finding.RuleId == missing.RuleId && finding.Classification == missing.Classification, "evidence fields survive finding construction");
        Check(finding.Path.StartsWith("HKCU\\", StringComparison.Ordinal) && finding.Evidence[0].Contains(finding.Path), "finding retains the source registration");
        Check(!String.IsNullOrWhiteSpace(finding.ProposedChange) && !String.IsNullOrWhiteSpace(finding.BackupPlan) && !String.IsNullOrWhiteSpace(finding.VerificationPlan), "read-only finding explains the plan and verification limits");
        Check(finding.VerificationPlan.IndexOf("separately check", StringComparison.Ordinal) >= 0, "functional verification remains separate from registration checks");
        Finding repeated = ShellDiagnostics.BuildFinding("Example preview handler", finding.Path, clsid, missing);
        Check(repeated.Id == finding.Id && repeated.EvidenceFingerprint == finding.EvidenceFingerprint, "unchanged evidence has a stable finding identity across scans");
        string path;
        Check(ShellDiagnostics.TryExplicitInprocPath(@"C:\Example\handler.dll", out path) && path == @"C:\Example\handler.dll", "explicit local DLL path accepted without filesystem I/O");
        Check(ShellDiagnostics.TryExplicitInprocPath("\"C:\\Program Files\\Example\\handler.DLL\"", out path) && path == @"C:\Program Files\Example\handler.DLL", "fully quoted DLL paths are recognized");
        Check(!ShellDiagnostics.TryExplicitInprocPath(@"%SystemRoot%\System32\shell32.dll", out path) && path == null, "even known environment variables are not expanded as absence evidence");
        foreach (string invalid in new string[] { null, "", @"handler.dll", @"C:handler.dll", @"\handler.dll", @"\\server\share\handler.dll", @"\\?\C:\Example\handler.dll", @"C:\Example\handler.dll:stream", @"C:\Example\handler.dll,0", @"C:\Example\handler.exe", "\"C:\\Example\\handler.dll\" /arg", "\"C:\\Example\\handler.dll", @"%__WPR_SHELL_UNRESOLVED_TEST__%\handler.dll", @"C:\Example\*.dll", "C:\\Example\\handler\n.dll" })
            Check(!ShellDiagnostics.TryExplicitInprocPath(invalid, out path) && path == null, "ambiguous, network, or non-DLL targets rejected: " + invalid);
        // No real registry writes, third-party COM activation, DLL loading, or file probes are used above.
        bool cancelled = false;
        ScanReport report = new ScanReport();
        try { ShellDiagnostics.Scan(report, new CancellationToken(true)); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && report.Modules.Count == 0 && report.Findings.Count == 0, "cancelled scan returns before inspecting registry or adding results");
        return assertions;
    }
}
