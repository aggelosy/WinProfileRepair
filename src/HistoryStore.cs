using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace WinProfileRepair
{
    // History is private, descriptive metadata. It never authorizes a repair or a restore.
    public static class HistoryStore
    {
        private static readonly object Gate = new object();
        private const int MaximumFiles = 500;
        private const int MaximumBytes = 1024 * 1024;
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "History"); } }

        public static List<RepairHistoryItem> Read()
        {
            lock (Gate)
            {
                List<RepairHistoryItem> items = new List<RepairHistoryItem>();
                try
                {
                    string sid = CurrentSid();
                    items = ReadFrom(Root, sid);
                    ReadBackupSummaries(items, sid);
                    items.Sort(delegate(RepairHistoryItem left, RepairHistoryItem right) { return String.CompareOrdinal(right.CreatedUtc, left.CreatedUtc); });
                }
                catch (Exception ex)
                {
                    if (!IsStorageException(ex)) throw;
                    items.Add(new RepairHistoryItem { Id = "history-unavailable", Operation = "History", Status = "Unable to inspect", Summary = "Some local history or backup metadata could not be inspected (" + ex.GetType().Name + "). Use Restore backup to select an existing operation folder manually.", VerificationState = "Not applicable" });
                }
                return items;
            }
        }

        internal static List<RepairHistoryItem> ReadFrom(string root, string sid)
        {
            List<RepairHistoryItem> result = new List<RepairHistoryItem>();
            string safeRoot = SafePath(root);
            if (!Directory.Exists(safeRoot)) return result;
            int seen = 0;
            foreach (string file in Directory.EnumerateFiles(safeRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (++seen > MaximumFiles) break;
                try
                {
                    string safeFile = SafePath(file);
                    if (!PathSafety.IsPathUnderRoot(safeFile, safeRoot) || new FileInfo(safeFile).Length > MaximumBytes) continue;
                    RepairHistoryItem item = Serializer().Deserialize<RepairHistoryItem>(File.ReadAllText(safeFile, Encoding.UTF8));
                    ValidateItem(item, sid);
                    if (!String.Equals(Path.GetFileNameWithoutExtension(safeFile), item.Id, StringComparison.Ordinal)) continue;
                    result.Add(item);
                }
                catch (Exception ex) { if (!IsStorageException(ex)) throw; }
            }
            return result;
        }

        internal static void ValidateItem(RepairHistoryItem item, string sid)
        {
            Guid id;
            DateTime timestamp;
            if (item == null || item.SchemaVersion != "1" || String.IsNullOrEmpty(sid) || item.UserSid != sid ||
                !Guid.TryParseExact(item.Id, "N", out id) ||
                !DateTime.TryParse(item.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp) ||
                item.Details == null || item.Details.Count > 1024 || item.FindingIds == null || item.FindingIds.Count > 1024 ||
                item.FindingPaths == null || item.FindingPaths.Count != item.FindingIds.Count ||
                item.FindingFingerprints == null || item.FindingFingerprints.Count != item.FindingIds.Count || item.RequiredModules == null || item.RequiredModules.Count > 3)
                throw new InvalidDataException("Invalid or foreign repair history metadata.");
            if (item.Operation != "Repair" && item.Operation != "Restore" && item.Operation != "Icon cache")
                throw new InvalidDataException("Unknown history operation.");
            foreach (string module in item.RequiredModules)
                if (module != "thumbnail-overrides" && module != "installation-records" && module != "start-menu-shortcuts")
                    throw new InvalidDataException("Unknown verification module.");
        }

        public static void Record(string operation, OperationResult result, IList<Finding> selected)
        {
            if (result == null) throw new ArgumentNullException("result");
            lock (Gate)
            {
                RepairHistoryItem item = CreateItem(operation, result, selected, CurrentSid());
                SaveTo(Root, item, item.UserSid);
                if (operation == "Restore" && !String.IsNullOrEmpty(result.BackupDirectory) && (result.Success || result.Applied > 0))
                    foreach (RepairHistoryItem prior in ReadFrom(Root, item.UserSid))
                        if (prior.Operation == "Repair" && String.Equals(prior.BackupDirectory, result.BackupDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            prior.Status = result.Success ? "Restored" : "Restore partially applied";
                            prior.VerificationState = "Original repair was undone; review restore result";
                            SaveTo(Root, prior, item.UserSid);
                        }
            }
        }

        internal static RepairHistoryItem CreateItem(string operation, OperationResult result, IList<Finding> selected, string sid)
        {
            RepairHistoryItem item = new RepairHistoryItem
            {
                Id = Guid.NewGuid().ToString("N"), SchemaVersion = "1", UserSid = sid,
                CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Operation = operation,
                Status = result.Success ? (result.Applied > 0 ? "Applied" : "No changes needed") : result.Applied > 0 ? "Partially applied" : "Stopped",
                Summary = result.Applied.ToString(CultureInfo.InvariantCulture) + " applied; " + result.Failed.ToString(CultureInfo.InvariantCulture) + " failed or skipped.",
                BackupDirectory = result.BackupDirectory,
                VerificationState = operation == "Repair" && result.Applied > 0 ? "Needs a new scan" : operation == "Restore" ? "Restored state needs a new scan" : operation == "Icon cache" ? "Visual check needed" : "Not applicable"
            };
            if (result.Messages != null)
                foreach (string message in result.Messages) if (item.Details.Count < 1024) item.Details.Add(message);
            if (selected != null)
                foreach (Finding finding in selected)
                {
                    if (finding == null || item.FindingIds.Count >= 1024) continue;
                    item.FindingIds.Add(finding.Id);
                    item.FindingPaths.Add(finding.Path);
                    item.FindingFingerprints.Add(finding.EvidenceFingerprint);
                    string module = finding.Kind == FindingKind.ThumbnailOverride ? "thumbnail-overrides" :
                        finding.Kind == FindingKind.StaleUninstallRecord ? "installation-records" :
                        finding.Kind == FindingKind.BrokenShortcut ? "start-menu-shortcuts" : null;
                    if (module != null && !item.RequiredModules.Contains(module)) item.RequiredModules.Add(module);
                }
            return item;
        }

        public static void Verify(ScanReport report, Action<string> log)
        {
            if (report == null) return;
            lock (Gate)
            {
                try
                {
                    string sid = CurrentSid();
                    if (report.UserSid != sid) throw new InvalidDataException("Scan belongs to another profile.");
                    foreach (RepairHistoryItem item in ReadFrom(Root, sid))
                    {
                        if (item.Operation != "Repair" || item.FindingIds.Count == 0 || (item.Status != "Applied" && item.Status != "Partially applied")) continue;
                        string verification = ClassifyVerification(item, report);
                        if (verification == item.VerificationState && item.LastVerifiedUtc == report.ScannedAtUtc) continue;
                        item.VerificationState = verification;
                        item.LastVerifiedUtc = report.ScannedAtUtc;
                        SaveTo(Root, item, sid);
                    }
                }
                catch (Exception ex)
                {
                    if (!IsStorageException(ex)) throw;
                    if (log != null) log("The scan completed, but repair-history verification could not be saved (" + ex.GetType().Name + ").");
                }
            }
        }

        internal static string ClassifyVerification(RepairHistoryItem item, ScanReport report)
        {
            if (item == null || report == null || report.Findings == null || item.UserSid != report.UserSid)
                return "Check incomplete";
            DateTime scanned, created;
            if (!DateTime.TryParse(report.ScannedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out scanned) ||
                !DateTime.TryParse(item.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created) || scanned.ToUniversalTime() <= created.ToUniversalTime())
                return "Check incomplete";
            if (item.FindingIds == null || item.FindingPaths == null || item.FindingIds.Count == 0 || item.FindingPaths.Count != item.FindingIds.Count)
                return "Check incomplete";
            foreach (Finding finding in report.Findings)
                if (item.FindingIds.Contains(finding.Id) || ContainsPath(item.FindingPaths, finding.Path)) return "Finding still detected";
            foreach (string path in item.FindingPaths) if (String.IsNullOrWhiteSpace(path)) return "Check incomplete";
            return WorkspaceScanner.CanVerifyFindingAbsence(report, item.FindingIds, item.RequiredModules)
                ? "No longer detected; visual check needed" : "Check incomplete";
        }

        private static bool ContainsPath(List<string> paths, string candidate)
        {
            if (String.IsNullOrWhiteSpace(candidate)) return false;
            foreach (string path in paths) if (String.Equals(path, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static void SaveTo(string root, RepairHistoryItem item, string sid)
        {
            ValidateItem(item, sid);
            string directory = SafePath(root);
            Directory.CreateDirectory(directory);
            string target = SafePath(Path.Combine(directory, item.Id + ".json"));
            if (!PathSafety.IsPathUnderRoot(target, directory)) throw new InvalidDataException("History path escaped its store.");
            byte[] bytes = new UTF8Encoding(false).GetBytes(Serializer().Serialize(item));
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("History item is too large.");
            string temporary = SafePath(Path.Combine(directory, item.Id + "-" + Guid.NewGuid().ToString("N") + ".tmp"));
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                SafePath(target);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void ReadBackupSummaries(List<RepairHistoryItem> items, string sid)
        {
            ReadBackupSummariesFrom(items, sid, RepairEngine.BackupRoot);
        }

        internal static void ReadBackupSummariesFrom(List<RepairHistoryItem> items, string sid, string backupRoot)
        {
            string root = SafePath(backupRoot);
            if (!Directory.Exists(root)) return;
            int count = 0;
            foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                if (++count > MaximumFiles) break;
                try
                {
                    string safeDirectory = SafePath(directory);
                    if (!PathSafety.IsPathUnderRoot(safeDirectory, root)) continue;
                    bool seen = false;
                    foreach (RepairHistoryItem present in items)
                        if (String.Equals(present.BackupDirectory, safeDirectory, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }
                    if (seen) continue;
                    string manifestPath = SafePath(Path.Combine(safeDirectory, "manifest.json"));
                    if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > MaximumBytes) continue;
                    BackupManifest manifest = Serializer().Deserialize<BackupManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
                    if (manifest == null || manifest.Entries == null || manifest.Entries.Count > 1024) continue;
                    RepairEngine.ValidateBackupIdentity(manifest.SchemaVersion, manifest.UserSid, sid);
                    RepairHistoryItem item = new RepairHistoryItem { Id = BackupId(safeDirectory), SchemaVersion = "backup-1", UserSid = sid,
                        CreatedUtc = manifest.CreatedUtc, Operation = "Repair", Status = "Existing backup", BackupDirectory = safeDirectory,
                        Summary = manifest.Entries.Count.ToString(CultureInfo.InvariantCulture) + " journaled item(s) from version " + manifest.ApplicationVersion + ".", VerificationState = "Review original journal" };
                    foreach (BackupEntry entry in manifest.Entries)
                    {
                        if (entry == null) continue;
                        item.Details.Add((entry.Restored ? "Restored" : entry.Applied ? "Applied" : entry.Attempted ? "Attempted" : "Backed up only") + ": " + entry.OriginalPath);
                    }
                    item.Details.Add("This is a read-only summary of an earlier backup. Restore revalidates the full backup and never trusts this summary as authorization.");
                    items.Add(item);
                }
                catch (Exception ex) { if (!IsStorageException(ex)) throw; }
            }
        }

        private static string BackupId(string directory)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory))).Replace("-", "").Substring(0, 32).ToLowerInvariant();
        }
        private static string SafePath(string path)
        {
            string safe, reason;
            if (!ScanPolicy.TrySafeLocalPath(path, out safe, out reason)) throw new InvalidDataException("Unsafe or unavailable history path: " + reason);
            return safe;
        }
        private static string CurrentSid() { using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity.User.Value; }
        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 50 }; }
        private static bool IsStorageException(Exception ex)
        {
            return ScanPolicy.IsInspectionException(ex) || ex is InvalidDataException || ex is System.Security.SecurityException;
        }
    }

    public static class WorkspaceOperations
    {
        public static OperationResult Apply(IList<Finding> selected, Action<string> log)
        {
            OperationResult result = RepairEngine.Apply(selected, log);
            Record("Repair", result, selected, log);
            return result;
        }
        public static OperationResult Restore(string backupDirectory, Action<string> log)
        {
            OperationResult result = RepairEngine.Restore(backupDirectory, log);
            Record("Restore", result, null, log);
            return result;
        }
        public static OperationResult RebuildIconCache(Action<string> log)
        {
            OperationResult result = RepairEngine.RebuildIconCache(log);
            Record("Icon cache", result, null, log);
            return result;
        }
        private static void Record(string operation, OperationResult result, IList<Finding> selected, Action<string> log)
        {
            try { HistoryStore.Record(operation, result, selected); }
            catch (Exception ex)
            {
                string message = "Operation finished, but local history could not be saved (" + ex.GetType().Name + "). The operation's original backup/journal remains the source of repair and restore evidence.";
                result.Messages.Add(message);
                if (log != null) { try { log(message); } catch (InvalidOperationException) { } }
            }
        }
    }
}
