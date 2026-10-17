using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace WinProfileRepair
{
    public static class PathSafety
    {
        public static bool IsPathUnderRoot(string path, string root)
        {
            if (String.IsNullOrWhiteSpace(path) || String.IsNullOrWhiteSpace(root)) return false;
            try
            {
                string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalizedPath = Path.GetFullPath(path);
                return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                if (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) return false;
                throw;
            }
        }
    }

    internal sealed class BackupEntry
    {
        public string Id { get; set; }
        public FindingKind Kind { get; set; }
        public string OriginalPath { get; set; }
        public RegistrySnapshot Snapshot { get; set; }
        public string Fingerprint { get; set; }
        public string BackupFile { get; set; }
        public bool Attempted { get; set; }
        public bool Applied { get; set; }
        public bool RestoreAttempted { get; set; }
        public bool Restored { get; set; }
        public string Error { get; set; }
    }

    internal sealed class BackupManifest
    {
        public string SchemaVersion { get; set; }
        public string ApplicationVersion { get; set; }
        public string UserSid { get; set; }
        public string CreatedUtc { get; set; }
        public List<BackupEntry> Entries { get; set; }
        public BackupManifest() { Entries = new List<BackupEntry>(); }
    }

    public static class RepairEngine
    {
        private const string BackupSchema = "1";
        private static readonly object OperationGate = new object();

        public static string BackupRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "Backups"); }
        }

        public static void ValidateBackupIdentity(string schemaVersion, string userSid, string currentUserSid)
        {
            if (!String.Equals(schemaVersion, BackupSchema, StringComparison.Ordinal)) throw new InvalidDataException("Unsupported backup format.");
            if (String.IsNullOrEmpty(userSid) || !String.Equals(userSid, currentUserSid, StringComparison.Ordinal))
                throw new InvalidDataException("The backup belongs to a different Windows account.");
        }

        public static void ValidateBackupSnapshot(RegistrySnapshot snapshot, FindingKind kind, string expectedFingerprint)
        {
            RegistryTools.ValidateSnapshot(snapshot, kind);
            if (!SameHash(RegistryTools.Fingerprint(snapshot), expectedFingerprint)) throw new InvalidDataException("Backup registry hash verification failed.");
        }

        public static OperationResult Apply(IList<Finding> selected, Action<string> log)
        {
            lock (OperationGate)
            {
                OperationResult result = new OperationResult();
                try
                {
                    EnsureNotElevated();
                    if (selected == null || selected.Count == 0) throw new InvalidOperationException("Select at least one repairable finding.");
                    ScanReport rescan = ScanEngine.Scan();
                    Dictionary<string, Finding> live = new Dictionary<string, Finding>(StringComparer.Ordinal);
                    foreach (Finding finding in rescan.Findings) if (!live.ContainsKey(finding.Id)) live.Add(finding.Id, finding);
                    List<Finding> approved = new List<Finding>();
                    HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Finding requested in selected)
                    {
                        Finding current;
                        if (requested == null || String.IsNullOrEmpty(requested.Id) || !unique.Add(requested.Id)) throw new InvalidDataException("The selection contains an invalid or duplicate finding.");
                        if (!live.TryGetValue(requested.Id, out current) || !requested.CanFix || !current.CanFix || requested.Kind != current.Kind || !SameHash(requested.EvidenceFingerprint, current.EvidenceFingerprint) || !String.Equals(requested.Path, current.Path, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("A selected finding changed or is no longer repairable. Run a new scan.");
                        Revalidate(current);
                        approved.Add(current);
                    }

                    string backup = NewBackupDirectory();
                    result.BackupDirectory = backup;
                    BackupManifest manifest = new BackupManifest { SchemaVersion = BackupSchema, ApplicationVersion = AppInfo.Version, UserSid = CurrentSid(), CreatedUtc = DateTime.UtcNow.ToString("o") };
                    for (int i = 0; i < approved.Count; i++)
                    {
                        Finding finding = approved[i];
                        BackupEntry entry = new BackupEntry { Id = finding.Id, Kind = finding.Kind, OriginalPath = finding.Kind == FindingKind.BrokenShortcut ? finding.Path : "HKEY_CURRENT_USER\\" + finding.Snapshot.KeyPath, Fingerprint = finding.EvidenceFingerprint, Snapshot = finding.Snapshot };
                        if (finding.Kind == FindingKind.BrokenShortcut)
                        {
                            entry.BackupFile = Path.Combine("files", i.ToString("D4") + ".lnk");
                            string destination = Path.Combine(backup, entry.BackupFile);
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            CopyDurably(finding.Path, destination);
                            if (!SameHash(HashFile(destination), finding.EvidenceFingerprint)) throw new IOException("Shortcut changed while it was being backed up.");
                        }
                        else
                        {
                            ValidateBackupSnapshot(entry.Snapshot, entry.Kind, entry.Fingerprint);
                        }
                        manifest.Entries.Add(entry);
                    }
                    SaveManifest(backup, manifest); // Every selected item is backed up before the first mutation.
                    Write(result, log, "Backup saved: " + backup);

                    for (int i = 0; i < approved.Count; i++)
                    {
                        Finding finding = approved[i];
                        BackupEntry entry = manifest.Entries[i];
                        try
                        {
                            Revalidate(finding);
                            entry.Attempted = true;
                            SaveManifest(backup, manifest);
                            Revalidate(finding); // Journal writes must not leave a stale target check before deletion.
                            if (finding.Kind == FindingKind.BrokenShortcut)
                            {
                                if (!SameHash(HashFile(finding.Path), finding.EvidenceFingerprint)) throw new IOException("Shortcut changed immediately before removal.");
                                File.Delete(finding.Path);
                                if (ScanPolicy.GetPathPresence(finding.Path) != PathPresence.Missing) throw new IOException("Shortcut removal could not be verified.");
                            }
                            else RegistryTools.DeleteLeaf(finding.Snapshot, finding.Kind);
                            entry.Applied = true;
                            SaveManifest(backup, manifest);
                            result.Applied++;
                            Write(result, log, "Removed: " + finding.Title);
                        }
                        catch (Exception ex)
                        {
                            entry.Error = ex.Message;
                            result.Failed++;
                            SaveManifest(backup, manifest);
                            Write(result, log, "Skipped/failed: " + finding.Title + ": " + ex.Message);
                        }
                    }
                    RefreshAssociations();
                    result.Success = result.Failed == 0;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    Write(result, log, "Repair stopped: " + ex.Message);
                }
                return result;
            }
        }

        private static void Revalidate(Finding finding)
        {
            if (!finding.CanFix) throw new InvalidOperationException("This finding is review-only.");
            foreach (string target in finding.MissingTargets)
            {
                string full, reason;
                if (!ScanPolicy.TrySafeLocalPath(target, out full, out reason) || ScanPolicy.GetPathPresence(full) != PathPresence.Missing)
                    throw new IOException("A missing target is now present or cannot be safely checked: " + target);
            }
            if (finding.Kind == FindingKind.BrokenShortcut)
            {
                ValidateShortcutPath(finding.Path);
                if (finding.MissingTargets.Count == 0 || !SameHash(HashFile(finding.Path), finding.EvidenceFingerprint)) throw new IOException("Shortcut evidence changed.");
            }
            else if (finding.Kind == FindingKind.ThumbnailOverride || finding.Kind == FindingKind.StaleUninstallRecord)
            {
                RegistryTools.ValidateSnapshot(finding.Snapshot, finding.Kind);
                RegistrySnapshot current = RegistryTools.Capture(finding.Snapshot.Hive, finding.Snapshot.View, finding.Snapshot.KeyPath);
                if (current == null || !SameHash(RegistryTools.Fingerprint(current), finding.EvidenceFingerprint)) throw new IOException("Registry entry changed.");
                if (finding.Kind == FindingKind.ThumbnailOverride)
                {
                    string provider = null;
                    foreach (RegistryValueSnapshot value in current.Values) if (value.Name == String.Empty && (value.Kind == RegistryValueKind.String || value.Kind == RegistryValueKind.ExpandString)) provider = value.Text;
                    Guid clsid;
                    string dll, reason;
                    if (!Guid.TryParse(provider, out clsid) || ScanPolicy.ComRegistrationState(clsid) != PathPresence.Missing || !ScanPolicy.NativeFallbackAvailable(out dll, out reason))
                        throw new InvalidOperationException("The thumbnail provider or native fallback changed. Run a new scan.");
                }
                else if (finding.MissingTargets.Count == 0) throw new InvalidOperationException("No missing target supports this uninstall-record repair.");
            }
            else throw new InvalidOperationException("This finding type cannot be repaired automatically.");
        }

        public static OperationResult Restore(string backupDirectory, Action<string> log)
        {
            lock (OperationGate)
            {
                OperationResult result = new OperationResult();
                try
                {
                    EnsureNotElevated();
                    string backup = SafeBackupDirectory(backupDirectory);
                    result.BackupDirectory = backup;
                    string manifestPath = Path.Combine(backup, "manifest.json");
                    RequireSafePath(manifestPath);
                    FileInfo manifestInfo = new FileInfo(manifestPath);
                    if (!manifestInfo.Exists || manifestInfo.Length > 16 * 1024 * 1024) throw new InvalidDataException("Missing or oversized backup manifest.");
                    BackupManifest manifest = Serializer().Deserialize<BackupManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
                    if (manifest == null || manifest.Entries == null || manifest.Entries.Count > 10000) throw new InvalidDataException("Invalid backup manifest.");
                    ValidateBackupIdentity(manifest.SchemaVersion, manifest.UserSid, CurrentSid());
                    HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // Validate all backup payloads before restoring any item.
                    foreach (BackupEntry entry in manifest.Entries)
                    {
                        if (entry == null || String.IsNullOrEmpty(entry.Id) || !unique.Add(entry.Id)) throw new InvalidDataException("Invalid or duplicate backup entry.");
                        if (entry.Kind == FindingKind.BrokenShortcut)
                        {
                            ValidateShortcutPath(entry.OriginalPath);
                            string saved = SafeBackupFile(backup, entry.BackupFile);
                            if (!SameHash(HashFile(saved), entry.Fingerprint)) throw new InvalidDataException("Backup shortcut hash verification failed.");
                        }
                        else
                        {
                            ValidateBackupSnapshot(entry.Snapshot, entry.Kind, entry.Fingerprint);
                            if (!String.Equals(entry.OriginalPath, entry.Snapshot.KeyPath, StringComparison.OrdinalIgnoreCase) && !String.Equals(entry.OriginalPath, "HKCU\\" + entry.Snapshot.KeyPath, StringComparison.OrdinalIgnoreCase) && !String.Equals(entry.OriginalPath, "HKEY_CURRENT_USER\\" + entry.Snapshot.KeyPath, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Backup registry path does not match its snapshot.");
                        }
                    }
                    foreach (BackupEntry entry in manifest.Entries)
                    {
                        if ((!entry.Applied && !entry.Attempted) || entry.Restored) continue;
                        try
                        {
                            entry.RestoreAttempted = true;
                            SaveManifest(backup, manifest);
                            if (entry.Kind == FindingKind.BrokenShortcut)
                            {
                                ValidateShortcutPath(entry.OriginalPath);
                                if (ScanPolicy.GetPathPresence(entry.OriginalPath) != PathPresence.Missing) throw new IOException("Restore refused: the original shortcut exists or cannot be checked.");
                                string saved = SafeBackupFile(backup, entry.BackupFile);
                                if (!SameHash(HashFile(saved), entry.Fingerprint)) throw new InvalidDataException("Backup shortcut changed.");
                                string parent = Path.GetDirectoryName(entry.OriginalPath);
                                RequireSafePath(parent);
                                Directory.CreateDirectory(parent);
                                RequireSafePath(parent);
                                CopyDurably(saved, entry.OriginalPath); // CreateNew refuses any existing destination.
                                if (!SameHash(HashFile(entry.OriginalPath), entry.Fingerprint)) throw new IOException("Restored shortcut verification failed.");
                            }
                            else RegistryTools.RestoreLeaf(entry.Snapshot, entry.Kind);
                            entry.Restored = true;
                            entry.Error = null;
                            SaveManifest(backup, manifest);
                            result.Applied++;
                            Write(result, log, "Restored: " + entry.OriginalPath);
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            entry.Error = ex.Message;
                            SaveManifest(backup, manifest);
                            Write(result, log, "Restore skipped/failed: " + entry.OriginalPath + ": " + ex.Message);
                        }
                    }
                    RefreshAssociations();
                    result.Success = result.Failed == 0;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    Write(result, log, "Restore stopped: " + ex.Message);
                }
                return result;
            }
        }

        private static void ValidateShortcutPath(string path)
        {
            string full = RequireSafePath(path);
            string menu = RequireSafePath(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            if (!String.Equals(path, full, StringComparison.OrdinalIgnoreCase) || !PathSafety.IsPathUnderRoot(full, menu) || !String.Equals(Path.GetExtension(full), ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Shortcut path is outside this account's Start menu.");
        }

        private static string RequireSafePath(string path)
        {
            string full, reason;
            if (!ScanPolicy.TrySafeLocalPath(path, out full, out reason)) throw new IOException("Unsafe or inaccessible path: " + path + ". " + reason);
            return full;
        }

        private static string NewBackupDirectory()
        {
            string root = RequireSafePath(BackupRoot);
            Directory.CreateDirectory(root);
            RequireSafePath(root);
            string directory = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return SafeBackupDirectory(directory);
        }

        private static string SafeBackupDirectory(string directory)
        {
            string full = RequireSafePath(directory);
            string root = RequireSafePath(BackupRoot);
            if (!PathSafety.IsPathUnderRoot(full, root) || !String.Equals(Path.GetDirectoryName(full), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
                throw new InvalidDataException("Choose a backup directory created under " + root);
            return full;
        }

        private static string SafeBackupFile(string backup, string relative)
        {
            if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid backup file path.");
            string full = RequireSafePath(Path.Combine(backup, relative));
            if (!PathSafety.IsPathUnderRoot(full, backup) || !String.Equals(Path.GetDirectoryName(full), Path.Combine(backup, "files"), StringComparison.OrdinalIgnoreCase) || !String.Equals(Path.GetExtension(full), ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup file escapes its storage directory.");
            return full;
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 50 };
        }

        private static void SaveManifest(string directory, BackupManifest manifest)
        {
            string path = Path.Combine(SafeBackupDirectory(directory), "manifest.json");
            RequireSafePath(path);
            string temporary = Path.Combine(directory, "manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
            byte[] bytes = new UTF8Encoding(false).GetBytes(Serializer().Serialize(manifest));
            using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            try
            {
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void CopyDurably(string source, string destination)
        {
            RequireSafePath(source);
            RequireSafePath(destination);
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
        }

        public static string HashFile(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        private static bool SameHash(string left, string right)
        {
            return !String.IsNullOrEmpty(left) && left.Length == 64 && String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string CurrentSid() { using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity.User.Value; }

        private static void EnsureNotElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Run this tool normally, without Run as administrator, so it repairs the intended user profile.");
            }
        }

        private static void Write(OperationResult result, Action<string> log, string message)
        {
            result.Messages.Add(message);
            // A UI that is closing must not interrupt the durable journal or Explorer's finally block.
            if (log != null) { try { log(message); } catch (InvalidOperationException) { } }
        }

        private static void RefreshAssociations() { SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

        public static OperationResult RebuildIconCache(Action<string> log)
        {
            lock (OperationGate)
            {
                OperationResult result = new OperationResult();
                IntPtr explorerHandle = IntPtr.Zero;
                bool stopped = false;
                string explorerPath = null;
                try
                {
                    EnsureNotElevated();
                    string local = RequireSafePath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                    string cacheFolder = RequireSafePath(Path.Combine(local, "Microsoft", "Windows", "Explorer"));
                    List<string> cacheFiles = new List<string>();
                    if (Directory.Exists(cacheFolder))
                        foreach (string file in Directory.GetFiles(cacheFolder, "iconcache*.db", SearchOption.TopDirectoryOnly))
                        {
                            RequireSafePath(file);
                            if (!PathSafety.IsPathUnderRoot(file, cacheFolder)) throw new IOException("Cache file escaped the expected directory.");
                            cacheFiles.Add(file);
                        }
                    string legacy = RequireSafePath(Path.Combine(local, "IconCache.db"));
                    if (ScanPolicy.GetPathPresence(legacy) == PathPresence.Exists) cacheFiles.Add(legacy);
                    if (cacheFiles.Count == 0)
                    {
                        result.Success = true;
                        Write(result, log, "No existing icon-cache databases were found.");
                        return result;
                    }

                    IntPtr shell = GetShellWindow();
                    uint shellPid;
                    if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out shellPid) == 0 || shellPid == 0) throw new IOException("The current desktop's Explorer shell could not be identified safely.");
                    using (Process shellProcess = Process.GetProcessById((int)shellPid))
                    using (Process current = Process.GetCurrentProcess())
                        if (shellProcess.SessionId != current.SessionId) throw new IOException("Explorer belongs to another Windows session.");
                    explorerHandle = OpenProcess(0x00100000 | 0x1000 | 0x0001, false, shellPid);
                    if (explorerHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    StringBuilder image = new StringBuilder(32768);
                    int imageLength = image.Capacity;
                    if (!QueryFullProcessImageName(explorerHandle, 0, image, ref imageLength)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    explorerPath = RequireSafePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
                    if (!String.Equals(image.ToString(), explorerPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("The current shell is not the standard Windows Explorer executable.");
                    IntPtr token;
                    if (!OpenProcessToken(explorerHandle, 0x0008, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    try
                    {
                        using (WindowsIdentity owner = new WindowsIdentity(token))
                            if (!String.Equals(owner.User.Value, CurrentSid(), StringComparison.Ordinal)) throw new IOException("Explorer belongs to another Windows account.");
                    }
                    finally { CloseHandle(token); }

                    Write(result, log, "Restarting this desktop's Explorer shell to rebuild " + cacheFiles.Count + " icon-cache files. Open File Explorer windows may close.");
                    uint currentShellPid;
                    IntPtr currentShell = GetShellWindow();
                    if (currentShell == IntPtr.Zero || GetWindowThreadProcessId(currentShell, out currentShellPid) == 0 || currentShellPid != shellPid)
                        throw new IOException("The desktop shell changed during preflight; try again.");
                    if (!TerminateProcess(explorerHandle, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    stopped = true;
                    if (WaitForSingleObject(explorerHandle, 10000) != 0) throw new IOException("Explorer did not exit within the timeout.");
                    foreach (string file in cacheFiles)
                    {
                        try
                        {
                            RequireSafePath(file);
                            if (ScanPolicy.GetPathPresence(file) == PathPresence.Missing) continue;
                            File.Delete(file);
                            result.Applied++;
                            Write(result, log, "Deleted icon cache: " + Path.GetFileName(file));
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            Write(result, log, "Icon cache remained locked or inaccessible: " + file + ": " + ex.Message);
                        }
                    }
                    result.Success = result.Failed == 0;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    Write(result, log, "Icon-cache rebuild stopped: " + ex.Message);
                }
                finally
                {
                    if (explorerHandle != IntPtr.Zero) CloseHandle(explorerHandle);
                    if (stopped)
                    {
                        try
                        {
                            if (!CurrentShellAvailable())
                            {
                                using (Process restart = Process.Start(new ProcessStartInfo { FileName = explorerPath, UseShellExecute = false, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows) })) { }
                            }
                            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                            while (!CurrentShellAvailable() && DateTime.UtcNow < deadline) Thread.Sleep(200);
                            if (!CurrentShellAvailable()) throw new IOException("Explorer has not returned. Start explorer.exe from Task Manager.");
                            RefreshAssociations();
                            Write(result, log, "Explorer is running again. Windows will regenerate its icon caches; restart Windows if an icon remains stale.");
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.Failed++;
                            Write(result, log, "Explorer restart needs attention: " + ex.Message);
                        }
                    }
                }
                return result;
            }
        }

        private static bool CurrentShellAvailable()
        {
            uint pid;
            IntPtr shell = GetShellWindow();
            if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out pid) == 0 || pid == 0) return false;
            try
            {
                using (Process candidate = Process.GetProcessById((int)pid))
                using (Process current = Process.GetCurrentProcess())
                    if (candidate.SessionId != current.SessionId || candidate.HasExited) return false;
                IntPtr process = OpenProcess(0x1000, false, pid);
                if (process == IntPtr.Zero) return false;
                try
                {
                    StringBuilder path = new StringBuilder(32768);
                    int length = path.Capacity;
                    string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    if (!QueryFullProcessImageName(process, 0, path, ref length) || !String.Equals(path.ToString(), expected, StringComparison.OrdinalIgnoreCase)) return false;
                    IntPtr token;
                    if (!OpenProcessToken(process, 0x0008, out token)) return false;
                    try { using (WindowsIdentity owner = new WindowsIdentity(token)) return String.Equals(owner.User.Value, CurrentSid(), StringComparison.Ordinal); }
                    finally { CloseHandle(token); }
                }
                finally { CloseHandle(process); }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int length);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
