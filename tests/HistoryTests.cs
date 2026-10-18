using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using WinProfileRepair;

internal static class HistoryTests
{
    private static int assertions;
    private const string Sid = "S-1-fixture-only";
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("FAILED: history: " + message);
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, message);
    }
    private static Finding Finding()
    {
        return new Finding { Id = "original-finding", EvidenceFingerprint = new string('A', 64), Kind = FindingKind.ThumbnailOverride,
            Path = @"HKCU\Software\Classes\.png\shellex\fixture", CanFix = true, Title = "Synthetic thumbnail override" };
    }
    private static RepairHistoryItem Item()
    {
        return HistoryStore.CreateItem("Repair", new OperationResult { Success = true, Applied = 1 }, new List<Finding> { Finding() }, Sid);
    }
    private static ScanReport LaterReport(RepairHistoryItem item)
    {
        ScanReport report = new ScanReport { UserSid = Sid, ScannedAtUtc = DateTime.Parse(item.CreatedUtc).ToUniversalTime().AddMinutes(1).ToString("o") };
        report.Modules.Add(new DiagnosticModuleResult { Id = "thumbnail-overrides", Status = "Completed" });
        return report;
    }
    internal static int Run(string artifacts)
    {
        assertions = 0;
        RepairHistoryItem item = Item();
        Check(item.Status == "Applied" && item.VerificationState == "Needs a new scan", "applied does not mean verified fixed");
        Check(item.FindingPaths.Single() == Finding().Path && item.RequiredModules.Single() == "thumbnail-overrides", "history retains target and relevant module identity");
        ScanReport report = LaterReport(item);
        Check(HistoryStore.ClassifyVerification(item, report) == "No longer detected; visual check needed", "absent finding still requires symptom confirmation");
        report.Modules.Add(new DiagnosticModuleResult { Id = "start-menu-shortcuts", Status = "Partial" });
        Check(HistoryStore.ClassifyVerification(item, report) == "No longer detected; visual check needed", "unrelated incomplete module does not prevent a relevant check");
        report.Findings.Add(new Finding { Id = "changed-content-new-identity", Path = Finding().Path.ToLowerInvariant() });
        Check(HistoryStore.ClassifyVerification(item, report) == "Finding still detected", "changed fingerprint at the same target is not mistaken for a repair");
        report.Findings.Clear();
        report.Findings.Add(new Finding { Id = Finding().Id, Path = "different path" });
        Check(HistoryStore.ClassifyVerification(item, report) == "Finding still detected", "matching finding identity remains detected");
        report.Findings.Clear(); report.Modules[0].Status = "Partial";
        Check(HistoryStore.ClassifyVerification(item, report) == "Check incomplete", "skipped or failed relevant checks cannot clear a finding");
        report.Modules.Clear();
        Check(HistoryStore.ClassifyVerification(item, report) == "Check incomplete", "old report without coverage cannot verify absence");
        report = LaterReport(item); report.UserSid = "S-1-other";
        Check(HistoryStore.ClassifyVerification(item, report) == "Check incomplete", "another profile cannot verify this account's repair");
        report = LaterReport(item); report.ScannedAtUtc = DateTime.Parse(item.CreatedUtc).AddMinutes(-1).ToString("o");
        Check(HistoryStore.ClassifyVerification(item, report) == "Check incomplete", "scan predating operation cannot verify it");
        report.ScannedAtUtc = "invalid";
        Check(HistoryStore.ClassifyVerification(item, report) == "Check incomplete", "malformed scan time cannot verify it");
        report = LaterReport(item); item.FindingPaths[0] = null;
        Check(HistoryStore.ClassifyVerification(item, report) == "Check incomplete", "missing target cannot be called clear");
        item = Item(); item.RequiredModules.Clear();
        Check(HistoryStore.ClassifyVerification(item, LaterReport(item)) == "Check incomplete", "no module evidence cannot establish absence");
        item = Item();
        RepairHistoryItem stopped = HistoryStore.CreateItem("Repair", new OperationResult { Failed = 1 }, new List<Finding> { Finding() }, Sid);
        Check(stopped.Status == "Stopped" && stopped.VerificationState == "Not applicable", "failed operation does not claim a completed repair");
        RepairHistoryItem cache = HistoryStore.CreateItem("Icon cache", new OperationResult { Success = true, Applied = 3 }, null, Sid);
        Check(cache.VerificationState == "Visual check needed" && cache.FindingIds.Count == 0, "cache rebuild is not backed-up registry repair");
        HistoryStore.ValidateItem(item, Sid);
        Reject(delegate { HistoryStore.ValidateItem(item, "S-1-other"); }, "foreign history rejected");
        string originalId = item.Id; item.Id = @"..\escape";
        Reject(delegate { HistoryStore.ValidateItem(item, Sid); }, "history filename traversal rejected");
        item.Id = originalId; item.SchemaVersion = "future";
        Reject(delegate { HistoryStore.ValidateItem(item, Sid); }, "unknown schema rejected");
        item = Item(); item.FindingPaths.Clear();
        Reject(delegate { HistoryStore.ValidateItem(item, Sid); }, "mismatched evidence arrays rejected");
        item = Item(); item.RequiredModules.Add("unsupported-module");
        Reject(delegate { HistoryStore.ValidateItem(item, Sid); }, "unsupported verification module metadata rejected");
        item = Item();
        string fixture = Path.Combine(artifacts, "history-fixture-" + Guid.NewGuid().ToString("N"));
        HistoryStore.SaveTo(fixture, item, Sid);
        List<RepairHistoryItem> items = HistoryStore.ReadFrom(fixture, Sid);
        Check(items.Count == 1 && items[0].FindingPaths.Single() == Finding().Path, "private fixture history round trip preserves target");
        item.VerificationState = "No longer detected; visual check needed";
        HistoryStore.SaveTo(fixture, item, Sid);
        Check(HistoryStore.ReadFrom(fixture, Sid).Single().VerificationState == item.VerificationState, "atomic update preserves the latest verification");
        Check(!Directory.GetFiles(fixture, "*.tmp").Any(), "successful write leaves no temporary journal");
        File.WriteAllText(Path.Combine(fixture, Guid.NewGuid().ToString("N") + ".json"), "{corrupted history");
        File.Copy(Path.Combine(fixture, item.Id + ".json"), Path.Combine(fixture, Guid.NewGuid().ToString("N") + ".json"));
        File.WriteAllText(Path.Combine(fixture, Guid.NewGuid().ToString("N") + ".json"), new string('x', 1024 * 1024 + 1));
        Check(HistoryStore.ReadFrom(fixture, Sid).Count == 1, "corrupt, oversized, and filename-mismatched items do not crash history loading");
        Check(HistoryStore.ReadFrom(fixture, "S-1-other").Count == 0, "foreign account metadata is not surfaced");
        Reject(delegate { HistoryStore.SaveTo(@"\\server\share", item, Sid); }, "remote history store rejected before writing");
        Reject(delegate { HistoryStore.SaveTo("relative-store", item, Sid); }, "relative store rejected");
        string backupRoot = Path.Combine(artifacts, "backup-summary-fixture-" + Guid.NewGuid().ToString("N"));
        string backupDirectory = Path.Combine(backupRoot, "old-backup"); Directory.CreateDirectory(backupDirectory);
        BackupManifest old = new BackupManifest { SchemaVersion = "1", UserSid = Sid, ApplicationVersion = "0.2.0-preview", CreatedUtc = DateTime.UtcNow.ToString("o") };
        old.Entries.Add(new BackupEntry { Id = "fixture", Applied = true, OriginalPath = Finding().Path });
        string manifest = Path.Combine(backupDirectory, "manifest.json");
        string oldJson = new JavaScriptSerializer().Serialize(old); File.WriteAllText(manifest, oldJson);
        List<RepairHistoryItem> summaries = new List<RepairHistoryItem>();
        HistoryStore.ReadBackupSummariesFrom(summaries, Sid, backupRoot);
        Check(summaries.Count == 1 && summaries[0].Status == "Existing backup", "previous preview backup remains visible");
        Check(summaries[0].VerificationState == "Review original journal", "legacy metadata is not presented as verified");
        Check(File.ReadAllText(manifest) == oldJson, "history loading never rewrites the original backup journal");
        HistoryStore.ReadBackupSummariesFrom(summaries, Sid, backupRoot);
        Check(summaries.Count == 1, "earlier backup summaries are deduplicated");
        summaries.Clear(); HistoryStore.ReadBackupSummariesFrom(summaries, "S-1-other", backupRoot);
        Check(summaries.Count == 0, "foreign backup summary excluded");
        bool cancelled = false;
        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { WorkspaceScanner.Scan(cancellation.Token, null); } catch (OperationCanceledException) { cancelled = true; }
        }
        Check(cancelled, "pre-cancelled workspace scan stops before any inspection");
        Finding legacy = Finding(); WorkspaceScanner.DescribeLegacyFinding(legacy);
        Check(legacy.CanFix && legacy.Kind == FindingKind.ThumbnailOverride && !String.IsNullOrEmpty(legacy.BackupPlan), "legacy metadata enrichment preserves repair eligibility and explains backup");
        ScanReport coverage = new ScanReport();
        coverage.Findings.Add(new Finding { Summary = "The provider registration could not be fully inspected." });
        Check(ScanEngine.StageHasInspectionGap(coverage, 0, 0, 0), "unknown finding evidence marks a module partial without requiring a general note");
        coverage.Findings.Clear(); coverage.ImageIcons.Add(new ImageIconDiagnostic { Status = "Warning" });
        Check(ScanEngine.StageHasInspectionGap(coverage, 0, 0, 0), "image resource warning prevents a completed-inspection claim");
        coverage.ImageIcons[0].Status = "NotDeclared"; coverage.ImageIcons[0].Notes.Add("No static icon is declared; a dynamic handler can be legitimate.");
        Check(!ScanEngine.StageHasInspectionGap(coverage, 0, 0, 0), "legitimate dynamic icon information is not treated as an inspection error");
        coverage.ImageIcons[0].Notes.Add("Effective query unavailable; registry fallback reported.");
        Check(ScanEngine.StageHasInspectionGap(coverage, 0, 0, 0), "unavailable effective association query remains explicit");
        return assertions;
    }
}
