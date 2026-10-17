using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace WinProfileRepair
{
    internal enum PathPresence { Missing, Exists, Unknown }

    // File.Exists intentionally is not used as proof that an installation is absent.
    internal static class ScanPolicy
    {
        internal static PathPresence GetPathPresence(string path)
        {
            try { File.GetAttributes(path); return PathPresence.Exists; }
            catch (FileNotFoundException) { return PathPresence.Missing; }
            catch (DirectoryNotFoundException) { return PathPresence.Missing; }
            catch (IOException) { return PathPresence.Unknown; }
            catch (UnauthorizedAccessException) { return PathPresence.Unknown; }
            catch (SecurityException) { return PathPresence.Unknown; }
            catch (ArgumentException) { return PathPresence.Unknown; }
            catch (NotSupportedException) { return PathPresence.Unknown; }
        }

        internal static bool IsChildOf(string path, string directory)
        {
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(directory)) return false;
            return path.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TrySafeLocalPath(string raw, out string full, out string reason)
        {
            full = null;
            reason = null;
            if (String.IsNullOrWhiteSpace(raw)) { reason = "No path was supplied."; return false; }
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
                if (expanded.IndexOf('%') >= 0 || expanded.Length < 3 ||
                    !Char.IsLetter(expanded[0]) || expanded[1] != ':' || expanded[2] != '\\' ||
                    expanded.IndexOf(':', 2) >= 0)
                { reason = "Path is not a fully resolved local drive path."; return false; }
                full = System.IO.Path.GetFullPath(expanded);
                if (new DriveInfo(System.IO.Path.GetPathRoot(full)).DriveType != DriveType.Fixed)
                { reason = "Path is not on a fixed local drive."; full = null; return false; }
                string current = full.Length == 3 ? full : full.TrimEnd('\\');
                while (!String.IsNullOrEmpty(current))
                {
                    PathPresence state = GetPathPresence(current);
                    if (state == PathPresence.Unknown)
                    { reason = "Path or an ancestor could not be inspected."; full = null; return false; }
                    if (state == PathPresence.Exists && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    { reason = "Path passes through a reparse point."; full = null; return false; }
                    string parent = System.IO.Path.GetDirectoryName(current);
                    if (String.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                    current = parent;
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!IsInspectionException(ex)) throw;
                reason = "Path could not be inspected: " + ex.GetType().Name + ".";
                full = null;
                return false;
            }
        }

        internal static bool IsInspectionException(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException ||
                ex is ArgumentException || ex is NotSupportedException || ex is COMException ||
                ex is InvalidOperationException || ex is Win32Exception || ex is FormatException || ex is InvalidCastException;
        }

        internal static RegistryView[] Views()
        {
            return Environment.Is64BitOperatingSystem
                ? new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new RegistryView[] { RegistryView.Registry32 };
        }

        internal static PathPresence ComRegistrationState(Guid clsid)
        {
            bool unknown = false;
            foreach (RegistryView view in Views())
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view))
                    {
                        using (RegistryKey key = root.OpenSubKey("CLSID\\" + clsid.ToString("B")))
                            if (key != null) return PathPresence.Exists;
                        using (RegistryKey key = root.OpenSubKey("PackagedCom\\ClassIndex\\" + clsid.ToString("B")))
                            if (key != null) return PathPresence.Exists;
                    }
                }
                catch (Exception ex) { if (!IsInspectionException(ex)) throw; unknown = true; }
            }
            return unknown ? PathPresence.Unknown : PathPresence.Missing;
        }

        internal static bool NativeFallbackAvailable(out string dll, out string reason)
        {
            dll = null;
            reason = null;
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view))
                using (RegistryKey fallback = root.OpenSubKey("SystemFileAssociations\\image\\shellex\\" + AppInfo.ThumbnailHandler))
                {
                    Guid clsid;
                    if (fallback == null || !Guid.TryParse(Convert.ToString(fallback.GetValue(null)), out clsid))
                    { reason = "The standard image thumbnail fallback is not registered."; return false; }
                    if (clsid != new Guid("C7657C4A-9F68-40FA-A4DF-96BC08EB3551"))
                    { reason = "The image fallback is not the recognized Windows Photo Thumbnail Provider; review only."; return false; }
                    using (RegistryKey server = root.OpenSubKey("CLSID\\" + clsid.ToString("B") + "\\InprocServer32"))
                    {
                        string serverPath = server == null ? null : Convert.ToString(server.GetValue(null));
                        if (!TrySafeLocalPath(Unquote(serverPath), out dll, out reason)) return false;
                        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        if (!String.Equals(dll, System.IO.Path.Combine(windows, "System32\\PhotoMetadataHandler.dll"), StringComparison.OrdinalIgnoreCase) ||
                            GetPathPresence(dll) != PathPresence.Exists || (File.GetAttributes(dll) & FileAttributes.Directory) != 0)
                        { reason = "The standard fallback does not resolve to an existing Windows DLL."; dll = null; return false; }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsInspectionException(ex)) throw;
                reason = "The standard image fallback could not be inspected: " + ex.GetType().Name + ".";
                return false;
            }
        }

        internal static string Unquote(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return text;
            text = text.Trim();
            return text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"' ? text.Substring(1, text.Length - 2) : text;
        }

        internal static string CommandExecutable(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return null;
            string value = Environment.ExpandEnvironmentVariables(command.Trim());
            if (value[0] == '"')
            {
                int close = value.IndexOf('"', 1);
                return close > 1 ? value.Substring(1, close - 1) : null;
            }
            // Unquoted executable paths occur in real ARP records; stop at the executable suffix.
            int suffix = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (suffix >= 0 && (suffix + 4 == value.Length || Char.IsWhiteSpace(value[suffix + 4]))) return value.Substring(0, suffix + 4);
            return null;
        }

        internal static string IconExecutable(string icon)
        {
            if (String.IsNullOrWhiteSpace(icon)) return null;
            string value = icon.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = value.IndexOf('"', 1);
                return close > 1 ? value.Substring(1, close - 1) : null;
            }
            int comma = value.LastIndexOf(',');
            int index;
            if (comma >= 0 && Int32.TryParse(value.Substring(comma + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                value = value.Substring(0, comma);
            return value;
        }

        internal static bool GenericUninstallEvidence(string installLocation, string missingUninstaller, string icon,
            out string root, out string executable, out string iconFile, out string reason)
        {
            root = null; executable = null; iconFile = null; reason = null;
            if (!TrySafeLocalPath(Unquote(installLocation), out root, out reason) ||
                !TrySafeLocalPath(missingUninstaller, out executable, out reason) ||
                !TrySafeLocalPath(IconExecutable(icon), out iconFile, out reason)) return false;
            if (!IsChildOf(executable, root) || !IsChildOf(iconFile, root))
            { reason = "Uninstaller and application icon must both be inside the explicit installation folder."; return false; }
            if (GetPathPresence(root) != PathPresence.Missing || GetPathPresence(executable) != PathPresence.Missing ||
                GetPathPresence(iconFile) != PathPresence.Missing)
            { reason = "The installation folder and both application paths are not all confirmed absent."; return false; }
            return true;
        }

        internal static string FileFingerprint(string file)
        {
            using (FileStream input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(input));
        }

        internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", String.Empty); }

        internal static bool IsOrdinaryShortcutHeader(byte[] header)
        {
            if (header == null || header.Length < 76 || BitConverter.ToUInt32(header, 0) != 76) return false;
            byte[] clsid = new byte[16];
            Buffer.BlockCopy(header, 4, clsid, 0, 16);
            if (new Guid(clsid) != new Guid("00021401-0000-0000-C000-000000000046")) return false;
            // MS-SHLLINK LinkFlags: SLDF_HAS_DARWINID identifies Windows Installer links.
            // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/ae350202-3ba9-4790-9e9e-98935f4ee5af
            return (BitConverter.ToUInt32(header, 20) & 0x00001000U) == 0;
        }
    }

    public static class ScanEngine
    {
        private const string UninstallRoot = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
        private const string OldCursorKey = "62625861-8486-5be9-9e46-1da50df5f8ff";
        private static readonly HashSet<string> RasterExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".bmp", ".jpg", ".jpeg", ".gif", ".tif", ".tiff" };

        private sealed class UninstallEntry
        {
            internal RegistryHive Hive;
            internal RegistryView View;
            internal string KeyName;
            internal RegistrySnapshot Snapshot;
        }

        public static ScanReport Scan()
        {
            ScanReport report = new ScanReport();
            report.ApplicationVersion = AppInfo.Version;
            report.ScannedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            try { using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) report.UserSid = identity.User == null ? null : identity.User.Value; }
            catch (SecurityException) { report.Notes.Add("The current user SID could not be read."); }
            report.Notes.Add("Read-only scan: direct per-user extension thumbnail overrides, installed-program records, and current-user Start Menu .lnk files. No unknown COM providers are activated.");
            report.Notes.Add("Machine records, MSI packages, drivers, services, application folders, advertised shortcuts, and network paths are never automatically cleaned. Installation records do not prove that an application is unused.");
            ScanThumbnailOverrides(report);
            ScanUninstallRecords(report);
            ScanShortcuts(report);
            ImageAssociationScanner.Scan(report);
            report.Notes.Add("This scan does not inventory every installation mechanism or every shell registration. No data is transmitted; exported reports can contain local file paths and should be reviewed before sharing.");
            return report;
        }

        private static void ScanThumbnailOverrides(ScanReport report)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryView view in ScanPolicy.Views())
            {
                try
                {
                    using (RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    using (RegistryKey classes = hive.OpenSubKey("Software\\Classes"))
                    {
                        if (classes == null) continue;
                        foreach (string extension in classes.GetSubKeyNames())
                        {
                            if (!extension.StartsWith(".", StringComparison.Ordinal)) continue;
                            string path = "Software\\Classes\\" + extension + "\\shellex\\" + AppInfo.ThumbnailHandler;
                            try
                            {
                                RegistrySnapshot snapshot = RegistryTools.Capture(RegistryHive.CurrentUser, view, path);
                                if (snapshot == null) continue;
                                string provider = Value(snapshot, String.Empty);
                                Guid clsid;
                                if (!Guid.TryParse(provider, out clsid))
                                {
                                    AddReviewFinding(report, "Unrecognized thumbnail override", "HKCU\\" + path,
                                        "The override default value is not a valid CLSID; review only.");
                                    continue;
                                }
                                PathPresence state = ScanPolicy.ComRegistrationState(clsid);
                                if (state == PathPresence.Exists) continue;
                                string signature = path + "|" + ContentFingerprint(snapshot);
                                if (!seen.Add(signature)) continue;
                                Finding finding = RegistryFinding(FindingKind.ThumbnailOverride, "Orphaned " + extension + " thumbnail override", snapshot);
                                finding.Summary = state == PathPresence.Missing
                                    ? "This profile selects a thumbnail provider whose COM class is not registered."
                                    : "The thumbnail provider registration could not be fully inspected.";
                                finding.Evidence.Add("Provider CLSID: " + clsid.ToString("B"));
                                finding.Evidence.Add("Checked merged classic COM and PackagedCom ClassIndex registrations in each available registry view.");
                                string dll, reason;
                                bool fallback = ScanPolicy.NativeFallbackAvailable(out dll, out reason);
                                finding.CanFix = state == PathPresence.Missing && RasterExtensions.Contains(extension) &&
                                    snapshot.ChildCount == 0 && fallback;
                                if (fallback) finding.Evidence.Add("Standard image fallback DLL: " + dll);
                                else finding.Evidence.Add(reason);
                                if (!RasterExtensions.Contains(extension)) finding.Evidence.Add("This extension is outside the supported raster repair set; review only.");
                                if (snapshot.ChildCount != 0) finding.Evidence.Add("The override has child keys; review only.");
                                report.Findings.Add(finding);
                            }
                            catch (Exception ex)
                            {
                                RecordError(report, "Thumbnail override " + path, ex);
                                AddReviewFinding(report, "Thumbnail override could not be inspected", "HKCU\\" + path,
                                    "A registry read failed; no repair is permitted and no absence is inferred.");
                            }
                        }
                    }
                }
                catch (Exception ex) { RecordError(report, "Per-user class inventory (" + view + ")", ex); }
            }
        }

        private static void ScanUninstallRecords(ScanReport report)
        {
            List<UninstallEntry> entries = new List<UninstallEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in ScanPolicy.Views())
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstall = root.OpenSubKey(UninstallRoot))
                    {
                        if (uninstall == null) continue;
                        foreach (string keyName in uninstall.GetSubKeyNames())
                        {
                            try
                            {
                                RegistrySnapshot snapshot = RegistryTools.Capture(hive, view, UninstallRoot + "\\" + keyName);
                                if (snapshot == null) continue;
                                string signature = hive + "|" + snapshot.KeyPath + "|" + ContentFingerprint(snapshot);
                                if (seen.Add(signature)) entries.Add(new UninstallEntry { Hive = hive, View = view, KeyName = keyName, Snapshot = snapshot });
                            }
                            catch (Exception ex)
                            {
                                RecordError(report, "Uninstall record " + hive + "\\" + keyName, ex);
                                AddReviewFinding(report, "Installation record could not be inspected", HiveLabel(hive) + "\\" + UninstallRoot + "\\" + keyName,
                                    "A registry read failed; no repair is permitted and no absence is inferred.");
                            }
                        }
                    }
                }
                catch (Exception ex) { RecordError(report, "Uninstall inventory " + hive + " (" + view + ")", ex); }
            }
            foreach (UninstallEntry entry in entries)
            {
                try
                {
                    string displayName = Value(entry.Snapshot, "DisplayName");
                    if (String.IsNullOrWhiteSpace(displayName)) continue;
                    string command = Value(entry.Snapshot, "UninstallString");
                    if (String.IsNullOrWhiteSpace(command)) command = Value(entry.Snapshot, "QuietUninstallString");
                    string executable, reason;
                    string rawExecutable = ScanPolicy.CommandExecutable(command);
                    if (String.IsNullOrEmpty(rawExecutable)) continue;
                    bool safe = ScanPolicy.TrySafeLocalPath(rawExecutable, out executable, out reason);
                    // A rejected path is not evidence of an incomplete installation.
                    if (!safe)
                    {
                        if (IsReadFailureReason(reason))
                        {
                            report.Notes.Add("Installation uninstaller path was not fully inspected for " + displayName + ": " + reason);
                            AddReviewFinding(report, "Installation path could not be inspected", HiveLabel(entry.Hive) + "\\" + entry.Snapshot.KeyPath,
                                "The uninstaller path or an ancestor is inaccessible; no absence is inferred.");
                        }
                        continue;
                    }
                    PathPresence state = ScanPolicy.GetPathPresence(executable);
                    if (state == PathPresence.Exists) continue;
                    Finding finding = RegistryFinding(FindingKind.StaleUninstallRecord, "Installation record: " + displayName, entry.Snapshot);
                    finding.Summary = state == PathPresence.Missing
                        ? "The registered uninstaller is missing; additional evidence is required before cleanup."
                        : "The registered uninstaller could not be inspected; review only.";
                    finding.Evidence.Add("Registered uninstaller: " + executable);
                    if (state == PathPresence.Missing) finding.MissingTargets.Add(executable);
                    bool protectedRecord = IsOne(entry.Snapshot, "WindowsInstaller") || IsOne(entry.Snapshot, "SystemComponent") ||
                        Value(entry.Snapshot, "UninstallString").IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (protectedRecord) finding.Evidence.Add("MSI or system-component record: automatic removal is disabled.");
                    if (entry.Hive != RegistryHive.CurrentUser) finding.Evidence.Add("Machine-wide record: review only.");
                    if (entry.Snapshot.ChildCount != 0) finding.Evidence.Add("The record has child keys: review only.");
                    if (state == PathPresence.Missing && entry.Hive == RegistryHive.CurrentUser && !protectedRecord && entry.Snapshot.ChildCount == 0)
                    {
                        string folder, uninstaller, icon;
                        if (ScanPolicy.GenericUninstallEvidence(Value(entry.Snapshot, "InstallLocation"), executable,
                            Value(entry.Snapshot, "DisplayIcon"), out folder, out uninstaller, out icon, out reason))
                        {
                            finding.CanFix = true;
                            finding.Summary = "The explicit installation folder, registered uninstaller, and application icon are all confirmed absent.";
                            AddUnique(finding.MissingTargets, folder);
                            AddUnique(finding.MissingTargets, icon);
                            finding.Evidence.Add("Explicit installation folder is absent: " + folder);
                            finding.Evidence.Add("Application icon inside that folder is absent: " + icon);
                        }
                        else if (IsOldCursorRecord(entry) && NewCursorIntact(entries, report, out folder))
                        {
                            finding.CanFix = true;
                            finding.Summary = "Obsolete Cursor 0.45.14 uninstaller metadata remains alongside a newer, intact current-user Cursor installation.";
                            finding.Evidence.Add("Newer Cursor application and unins000.exe both exist in: " + folder);
                            finding.Evidence.Add("Only this obsolete installation record is eligible; current application files are preserved.");
                        }
                        else finding.Evidence.Add(reason ?? "Automatic cleanup prerequisites were not satisfied.");
                    }
                    report.Findings.Add(finding);
                }
                catch (Exception ex) { RecordError(report, "Uninstall evaluation " + entry.KeyName, ex); }
            }
        }

        private static bool IsOldCursorRecord(UninstallEntry entry)
        {
            return String.Equals(entry.KeyName, OldCursorKey, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Value(entry.Snapshot, "DisplayVersion"), "0.45.14", StringComparison.OrdinalIgnoreCase) &&
                Value(entry.Snapshot, "DisplayName").IndexOf("Cursor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool NewCursorIntact(List<UninstallEntry> entries, ScanReport report, out string folder)
        {
            folder = null;
            foreach (UninstallEntry current in entries)
            {
                if (current.Hive != RegistryHive.CurrentUser || !String.Equals(Value(current.Snapshot, "DisplayName"), "Cursor (User)", StringComparison.OrdinalIgnoreCase)) continue;
                Version version;
                if (!Version.TryParse(Value(current.Snapshot, "DisplayVersion"), out version) || version <= new Version(0, 45, 14)) continue;
                string uninstaller, reason;
                if (!ScanPolicy.TrySafeLocalPath(ScanPolicy.CommandExecutable(Value(current.Snapshot, "UninstallString")), out uninstaller, out reason)) continue;
                if (!String.Equals(System.IO.Path.GetFileName(uninstaller), "unins000.exe", StringComparison.OrdinalIgnoreCase) ||
                    ScanPolicy.GetPathPresence(uninstaller) != PathPresence.Exists) continue;
                string candidate = System.IO.Path.GetDirectoryName(uninstaller);
                string executable, ignored;
                if (!ScanPolicy.TrySafeLocalPath(System.IO.Path.Combine(candidate, "Cursor.exe"), out executable, out ignored) ||
                    ScanPolicy.GetPathPresence(executable) != PathPresence.Exists) continue;
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
                Version fileVersion = new Version(Math.Max(0, info.FileMajorPart), Math.Max(0, info.FileMinorPart), Math.Max(0, info.FileBuildPart));
                if (fileVersion <= new Version(0, 45, 14)) continue;
                folder = candidate;
                return true;
            }
            return false;
        }

        private static void ScanShortcuts(ScanReport report)
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            string safeRoot, reason;
            if (!ScanPolicy.TrySafeLocalPath(startMenu, out safeRoot, out reason))
            { report.Notes.Add("Current-user Start Menu was not scanned: " + reason); return; }
            if (ScanPolicy.GetPathPresence(safeRoot) != PathPresence.Exists) return;
            object shell = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell", false);
                if (type == null) { report.Notes.Add("WScript.Shell is unavailable; .lnk inspection was skipped."); return; }
                shell = Activator.CreateInstance(type);
                foreach (string file in ShortcutFiles(safeRoot, report))
                {
                    object shortcut = null;
                    try
                    {
                        string source, rejected;
                        if (!ScanPolicy.TrySafeLocalPath(file, out source, out rejected) || !ScanPolicy.IsChildOf(source, safeRoot))
                        {
                            if (IsReadFailureReason(rejected)) report.Notes.Add("Shortcut source could not be inspected: " + file + ". " + rejected);
                            continue;
                        }
                        byte[] header = new byte[76];
                        using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            int read = 0, count;
                            while (read < header.Length && (count = input.Read(header, read, header.Length - read)) != 0) read += count;
                            if (read != header.Length || !ScanPolicy.IsOrdinaryShortcutHeader(header))
                            { report.Notes.Add("Advertised or unrecognized shortcut skipped: " + source); continue; }
                        }
                        shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { source });
                        string target = Convert.ToString(shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
                        string arguments = Convert.ToString(shortcut.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
                        // Reading CreateShortcut properties does not resolve, execute, or save the link.
                        string expandedTarget;
                        if (!ScanPolicy.TrySafeLocalPath(target, out expandedTarget, out rejected))
                        {
                            if (IsReadFailureReason(rejected)) report.Notes.Add("Shortcut target could not be inspected: " + source + ". " + rejected);
                            continue;
                        }
                        PathPresence state = ScanPolicy.GetPathPresence(expandedTarget);
                        List<string> missing = new List<string>();
                        string description = null;
                        if (state == PathPresence.Missing && String.Equals(System.IO.Path.GetExtension(expandedTarget), ".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            missing.Add(expandedTarget);
                            description = "The shortcut points directly to an executable that is confirmed absent.";
                        }
                        else if (state == PathPresence.Exists && AnacondaScriptMissing(source, expandedTarget, arguments, out missing))
                            description = "This Anaconda shortcut invokes a missing script, and its entire installation folder is confirmed absent.";
                        else if (state == PathPresence.Unknown)
                            report.Notes.Add("Shortcut target could not be inspected: " + source);
                        if (description == null) continue;
                        Finding finding = new Finding();
                        finding.Kind = FindingKind.BrokenShortcut;
                        finding.Title = "Broken Start Menu shortcut: " + System.IO.Path.GetFileNameWithoutExtension(source);
                        finding.Path = source;
                        finding.Summary = description;
                        finding.CanFix = true;
                        finding.EvidenceFingerprint = ScanPolicy.FileFingerprint(source);
                        finding.Id = Id("shortcut|" + source + "|" + finding.EvidenceFingerprint);
                        finding.Evidence.Add("Target: " + expandedTarget);
                        if (!String.IsNullOrWhiteSpace(arguments)) finding.Evidence.Add("Arguments: " + arguments);
                        finding.Evidence.Add("Only this .lnk file is eligible; application data is preserved.");
                        finding.MissingTargets.AddRange(missing);
                        report.Findings.Add(finding);
                    }
                    catch (Exception ex) { RecordError(report, "Shortcut " + file, ex); }
                    finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
                }
            }
            catch (Exception ex) { RecordError(report, "Shortcut inventory", ex); }
            finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        }

        private static IEnumerable<string> ShortcutFiles(string root, ScanReport report)
        {
            Stack<string> directories = new Stack<string>();
            directories.Push(root);
            List<string> result = new List<string>();
            while (directories.Count != 0)
            {
                string directory = directories.Pop();
                try
                {
                    result.AddRange(Directory.GetFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly));
                    foreach (string child in Directory.GetDirectories(directory))
                    {
                        string safe, reason;
                        if (ScanPolicy.TrySafeLocalPath(child, out safe, out reason)) directories.Push(safe);
                        else report.Notes.Add("Start Menu subfolder skipped: " + child + ". " + reason);
                    }
                }
                catch (Exception ex) { RecordError(report, "Start Menu folder " + directory, ex); }
            }
            return result;
        }

        private static bool AnacondaScriptMissing(string shortcut, string host, string arguments, out List<string> missing)
        {
            missing = new List<string>();
            string group = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(shortcut));
            if (!String.Equals(group, "Anaconda3 (64-bit)", StringComparison.OrdinalIgnoreCase)) return false;
            string rawRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "anaconda3");
            string root, reason;
            if (!ScanPolicy.TrySafeLocalPath(rawRoot, out root, out reason) || ScanPolicy.GetPathPresence(root) != PathPresence.Missing) return false;
            string name = System.IO.Path.GetFileNameWithoutExtension(shortcut);
            string relative;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (String.Equals(name, "Anaconda Prompt", StringComparison.OrdinalIgnoreCase) &&
                (String.Equals(host, System.IO.Path.Combine(windows, "System32\\cmd.exe"), StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(host, System.IO.Path.Combine(windows, "SysWOW64\\cmd.exe"), StringComparison.OrdinalIgnoreCase)))
                relative = "Scripts\\activate.bat";
            else if (String.Equals(name, "Anaconda Powershell Prompt", StringComparison.OrdinalIgnoreCase) &&
                (String.Equals(host, System.IO.Path.Combine(windows, "System32\\WindowsPowerShell\\v1.0\\powershell.exe"), StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(host, System.IO.Path.Combine(windows, "SysWOW64\\WindowsPowerShell\\v1.0\\powershell.exe"), StringComparison.OrdinalIgnoreCase)))
                relative = "shell\\condabin\\conda-hook.ps1";
            else return false;
            string script;
            if (!ScanPolicy.TrySafeLocalPath(System.IO.Path.Combine(root, relative), out script, out reason) ||
                ScanPolicy.GetPathPresence(script) != PathPresence.Missing || String.IsNullOrEmpty(arguments) ||
                arguments.IndexOf(script, StringComparison.OrdinalIgnoreCase) < 0) return false;
            missing.Add(root);
            missing.Add(script);
            return true;
        }

        private static Finding RegistryFinding(FindingKind kind, string title, RegistrySnapshot snapshot)
        {
            Finding finding = new Finding();
            finding.Kind = kind;
            finding.Title = title;
            finding.Snapshot = snapshot;
            finding.Path = HiveLabel(snapshot.Hive) + "\\" + snapshot.KeyPath;
            finding.EvidenceFingerprint = RegistryTools.Fingerprint(snapshot);
            finding.Id = Id(kind + "|" + finding.Path + "|" + snapshot.View + "|" + finding.EvidenceFingerprint);
            return finding;
        }

        private static string ContentFingerprint(RegistrySnapshot snapshot)
        {
            // The Windows views can expose the same shared per-user key. Deduplicate identical
            // contents while retaining the real view in the chosen finding and repair snapshot.
            RegistrySnapshot normalized = new RegistrySnapshot { Hive = snapshot.Hive, View = RegistryView.Default,
                KeyPath = snapshot.KeyPath, ChildCount = snapshot.ChildCount, Values = snapshot.Values };
            return RegistryTools.Fingerprint(normalized);
        }

        private static string HiveLabel(RegistryHive hive)
        {
            return hive == RegistryHive.CurrentUser ? "HKCU" : hive == RegistryHive.LocalMachine ? "HKLM" : hive.ToString();
        }

        private static bool IsReadFailureReason(string reason)
        {
            return !String.IsNullOrEmpty(reason) && reason.IndexOf("inspect", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddReviewFinding(ScanReport report, string title, string path, string summary)
        {
            foreach (Finding present in report.Findings)
                if (!present.CanFix && String.Equals(present.Path, path, StringComparison.OrdinalIgnoreCase) && present.Title == title) return;
            Finding finding = new Finding { Kind = FindingKind.ReviewOnly, Title = title, Path = path,
                Summary = summary, CanFix = false, Id = Id("review|" + path + "|" + title) };
            report.Findings.Add(finding);
        }

        private static string Value(RegistrySnapshot snapshot, string name)
        {
            foreach (RegistryValueSnapshot value in snapshot.Values)
                if (String.Equals(value.Name ?? String.Empty, name, StringComparison.OrdinalIgnoreCase))
                    return value.Kind == RegistryValueKind.String || value.Kind == RegistryValueKind.ExpandString ? value.Text ?? String.Empty : String.Empty;
            return String.Empty;
        }

        private static bool IsOne(RegistrySnapshot snapshot, string name)
        {
            foreach (RegistryValueSnapshot value in snapshot.Values)
                if (String.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase))
                    return (value.Kind == RegistryValueKind.DWord || value.Kind == RegistryValueKind.QWord) ? value.Number == 1 : value.Text == "1";
            return false;
        }

        private static void AddUnique(List<string> targets, string value)
        {
            foreach (string target in targets) if (String.Equals(target, value, StringComparison.OrdinalIgnoreCase)) return;
            targets.Add(value);
        }

        private static string Id(string text)
        {
            using (SHA256 hash = SHA256.Create()) return ScanPolicy.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Substring(0, 24);
        }

        private static void RecordError(ScanReport report, string scope, Exception ex)
        {
            if (ex is System.Reflection.TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            if (!ScanPolicy.IsInspectionException(ex) && !(ex is MissingMethodException) && !(ex is TypeLoadException)) throw ex;
            report.Notes.Add(scope + " could not be fully inspected (" + ex.GetType().Name + "). No absence is inferred from this error.");
        }
    }
}
