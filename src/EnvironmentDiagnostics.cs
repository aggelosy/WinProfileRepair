using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace WinProfileRepair
{
    internal sealed class EnvironmentPathEvidence
    {
        internal PathPresence Presence;
        internal bool SafeLocal;
        internal bool IsDirectory;
        internal string Reason;
    }

    internal sealed class EnvironmentCheckResult
    {
        internal string Classification;
        internal string Summary;
        internal string ResolvedPath;
        internal bool MissingConfirmed;
        internal bool InspectionLimited;
    }

    // Deliberately does not execute commands, expand values against the scanner's potentially
    // stale process environment, activate shortcut COM, or change user/machine configuration.
    public static class EnvironmentDiagnostics
    {
        private const int MaxEntries = 256;
        private const int MaxValueLength = 32768;
        private const int MaxCommandLength = 8192;
        private const string Category = "Apps & startup";
        private const string RunRoot = "Software\\Microsoft\\Windows\\CurrentVersion\\";
        private static readonly HashSet<string> CommandHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
            "rundll32.exe", "regsvr32.exe", "explorer.exe", "conhost.exe", "wt.exe"
        };

        public static void Scan(ScanReport report, CancellationToken cancellation)
        {
            if (report == null) throw new ArgumentNullException("report");
            cancellation.ThrowIfCancellationRequested();
            DiagnosticModuleResult module = new DiagnosticModuleResult
            {
                Id = "startup-environment", Title = "Startup & environment", Category = Category,
                Status = "Completed"
            };
            int initial = report.Findings.Count;
            int startupCount = ScanStartupRegistry(report, module, cancellation);
            int environmentCount = ScanUserEnvironment(report, module, cancellation);
            InventoryStartupFolder(report, module, cancellation);
            module.Summary = startupCount.ToString(CultureInfo.InvariantCulture) + " startup registration(s), " +
                environmentCount.ToString(CultureInfo.InvariantCulture) + " user environment value(s); " +
                (report.Findings.Count - initial).ToString(CultureInfo.InvariantCulture) + " review or information item(s).";
            module.Notes.Add("Read-only: Run/RunOnce and Environment under HKCU, plus a bounded current-user Startup folder inventory. Identical values shared by registry views are counted once.");
            module.Notes.Add("No startup enablement, execution, writability, machine PATH, scheduled task, service, policy, or full shortcut-target assessment is performed. A missing entry is not evidence that an application is unused.");
            module.Notes.Add("Environment references are retained literally; the scanner's process environment can differ from registry values after changes. No commands or startup shortcuts are launched.");
            report.Modules.Add(module);
        }

        private static int ScanStartupRegistry(ScanReport report, DiagnosticModuleResult module, CancellationToken cancellation)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (RegistryView view in ScanPolicy.Views())
            foreach (string leaf in new[] { "Run", "RunOnce" })
            {
                cancellation.ThrowIfCancellationRequested();
                string path = RunRoot + leaf;
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    using (RegistryKey key = root.OpenSubKey(path, false))
                    {
                        if (key == null) continue;
                        string[] names = key.GetValueNames();
                        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                        if (names.Length > MaxEntries) Partial(module, "HKCU\\" + path + ": inventory capped at " + MaxEntries + " values per registry view.");
                        for (int i = 0; i < Math.Min(names.Length, MaxEntries); i++)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            string name = names[i];
                            string location = "HKCU\\" + path + " [" + ValueName(name) + "]";
                            try
                            {
                                RegistryValueKind kind = key.GetValueKind(name);
                                if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString)
                                {
                                    if (!seen.Add(path.ToUpperInvariant() + "|" + name.ToUpperInvariant() + "|" + kind)) continue;
                                    count++;
                                    AddFinding(report, "startup.value-type", "Startup value needs review", location,
                                        Result("Needs review", "The startup value is not a string command. No executable or absence is inferred."), view);
                                    continue;
                                }
                                string raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                                if (!seen.Add(path.ToUpperInvariant() + "|" + name.ToUpperInvariant() + "|" + kind + "|" + Digest(raw ?? String.Empty))) continue;
                                count++;
                                EnvironmentCheckResult result = ClassifyStartupCommand(raw, InspectLocalPath);
                                if (result.InspectionLimited || result.Classification == "Unable to inspect") Partial(module, "At least one startup command was outside the supported inspection scope.");
                                if (result.Classification != null)
                                    AddFinding(report, result.MissingConfirmed ? "startup.missing-executable" : "startup.command-review",
                                        result.MissingConfirmed ? "Startup executable is absent" : "Startup command needs review", location, result, view);
                            }
                            catch (Exception ex)
                            {
                                if (!ScanPolicy.IsInspectionException(ex)) throw;
                                Partial(module, "A startup registry value could not be read (" + ex.GetType().Name + ").");
                                AddFinding(report, "startup.unreadable", "Startup value could not be inspected", location,
                                    Result("Unable to inspect", "The registry value could not be read. No missing executable is inferred."), view);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!ScanPolicy.IsInspectionException(ex)) throw;
                    Partial(module, "HKCU\\" + path + " could not be inspected in " + view + " (" + ex.GetType().Name + ").");
                }
            }
            return count;
        }

        private static int ScanUserEnvironment(ScanReport report, DiagnosticModuleResult module, CancellationToken cancellation)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (RegistryView view in ScanPolicy.Views())
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    using (RegistryKey key = root.OpenSubKey("Environment", false))
                    {
                        if (key == null) continue;
                        string[] names = key.GetValueNames();
                        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                        if (names.Length > MaxEntries) Partial(module, "User environment inventory capped at " + MaxEntries + " values per registry view.");
                        for (int i = 0; i < Math.Min(names.Length, MaxEntries); i++)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            string name = names[i];
                            string location = "HKCU\\Environment [" + ValueName(name) + "]";
                            try
                            {
                                RegistryValueKind kind = key.GetValueKind(name);
                                string raw = (kind == RegistryValueKind.String || kind == RegistryValueKind.ExpandString)
                                    ? key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string : null;
                                if (!seen.Add(name.ToUpperInvariant() + "|" + kind + "|" + Digest(raw ?? String.Empty))) continue;
                                count++;
                                if (name.Length == 0 || name.IndexOf('=') >= 0 || name.IndexOf('\0') >= 0)
                                    AddFinding(report, "environment.variable-name", "User environment variable name needs review", location,
                                        Result("Needs review", "The stored variable name is empty or contains a character that cannot form a normal name=value environment entry. The value was not changed."), view);
                                if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString)
                                {
                                    AddFinding(report, "environment.value-type", "User environment value needs review", location,
                                        Result("Needs review", "The user environment value has a non-string registry type; its contents were not interpreted."), view);
                                    continue;
                                }
                                if (raw == null || raw.Length > MaxValueLength)
                                {
                                    Partial(module, "A user environment value exceeded the bounded inspection scope or changed during inspection.");
                                    AddFinding(report, "environment.value-limit", "User environment value could not be inspected", location,
                                        Result("Unable to inspect", "The value is unavailable or exceeds the inspection limit. No invalid environment is inferred."), view);
                                    continue;
                                }
                                if (String.Equals(name, "Path", StringComparison.OrdinalIgnoreCase))
                                    ScanPathValue(report, module, raw, location, view, cancellation);
                                else if (String.Equals(name, "TEMP", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "TMP", StringComparison.OrdinalIgnoreCase))
                                {
                                    EnvironmentCheckResult result = ClassifyDirectoryValue(raw, InspectLocalPath);
                                    if (result.InspectionLimited || result.Classification == "Unable to inspect") Partial(module, "A user temporary-directory value could not be fully inspected.");
                                    if (result.Classification != null) AddFinding(report, "environment.temporary-directory", "User " + name.ToUpperInvariant() + " directory", location, result, view);
                                }
                                else if (HasUnpairedPercent(raw))
                                    AddFinding(report, "environment.reference-syntax", "User variable contains an unpaired percent sign", location,
                                        Result("Information", "The value contains an unpaired percent sign. It may be intentional literal text; no undefined variable or corruption is inferred."), view);
                                // General user-variable values are intentionally not copied to evidence: they can contain credentials.
                            }
                            catch (Exception ex)
                            {
                                if (!ScanPolicy.IsInspectionException(ex)) throw;
                                Partial(module, "A user environment value could not be read (" + ex.GetType().Name + ").");
                                AddFinding(report, "environment.unreadable", "User environment value could not be inspected", location,
                                    Result("Unable to inspect", "The registry value could not be read. No invalid value is inferred."), view);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!ScanPolicy.IsInspectionException(ex)) throw;
                    Partial(module, "HKCU\\Environment could not be inspected in " + view + " (" + ex.GetType().Name + ").");
                }
            }
            return count;
        }

        private static void ScanPathValue(ScanReport report, DiagnosticModuleResult module, string raw, string location,
            RegistryView view, CancellationToken cancellation)
        {
            bool balanced;
            List<string> entries = SplitPathEntries(raw, out balanced);
            if (!balanced)
                AddFinding(report, "environment.path-quotes", "User PATH quoting needs review", location,
                    Result("Needs review", "The stored PATH has unmatched quotes. Entries were not inspected to avoid assuming where a directory ends."), view);
            if (!balanced) return;
            if (entries.Count > MaxEntries) Partial(module, "User PATH inspection capped at " + MaxEntries + " entries.");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Min(entries.Count, MaxEntries); i++)
            {
                cancellation.ThrowIfCancellationRequested();
                string entry = entries[i].Trim();
                string itemLocation = location + " entry " + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (!seen.Add(entry))
                {
                    AddFinding(report, "environment.path-duplicate", "Repeated literal user PATH entry", itemLocation,
                        Result("Information", "This entry repeats another literal user PATH entry. This is not proof of a failure and is not automatically removed."), view);
                    continue;
                }
                EnvironmentCheckResult result = ClassifyDirectoryValue(entry, InspectLocalPath);
                if (result.InspectionLimited || result.Classification == "Unable to inspect") Partial(module, "At least one user PATH directory could not be fully inspected.");
                if (result.Classification != null) AddFinding(report, "environment.path-directory", "User PATH directory", itemLocation, result, view);
            }
        }

        private static void InventoryStartupFolder(ScanReport report, DiagnosticModuleResult module, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup, Environment.SpecialFolderOption.DoNotVerify);
                if (String.IsNullOrWhiteSpace(startup)) { Partial(module, "Current-user Startup folder location was not available."); return; }
                EnvironmentPathEvidence state = InspectLocalPath(startup);
                if (!state.SafeLocal || state.Presence == PathPresence.Unknown)
                { Partial(module, "The Startup folder was not inventoried: " + state.Reason); return; }
                if (state.Presence == PathPresence.Missing) { module.Notes.Add("The current-user Startup folder is absent; this can be normal."); return; }
                if (!state.IsDirectory) { Partial(module, "The Startup folder location is not a directory."); return; }
                int count = 0, shortcuts = 0;
                foreach (string file in Directory.EnumerateFiles(startup, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (count >= MaxEntries) { Partial(module, "Startup folder inventory capped at " + MaxEntries + " files."); break; }
                    count++;
                    if (String.Equals(System.IO.Path.GetExtension(file), ".lnk", StringComparison.OrdinalIgnoreCase)) shortcuts++;
                }
                module.Notes.Add("Current-user Startup folder: " + count.ToString(CultureInfo.InvariantCulture) + " file(s), including " +
                    shortcuts.ToString(CultureInfo.InvariantCulture) + " .lnk file(s). Shortcut targets were not resolved or executed.");
            }
            catch (Exception ex)
            {
                if (!ScanPolicy.IsInspectionException(ex)) throw;
                Partial(module, "Startup folder inventory could not complete (" + ex.GetType().Name + "). No missing shortcut target is inferred.");
            }
        }

        internal static EnvironmentCheckResult ClassifyStartupCommand(string command, Func<string, EnvironmentPathEvidence> inspect)
        {
            string executable, reason;
            if (!TryStartupExecutable(command, out executable, out reason))
                return new EnvironmentCheckResult { Classification = "Needs review", Summary = reason, InspectionLimited = true };
            EnvironmentPathEvidence state = inspect(executable);
            if (!state.SafeLocal || state.Presence == PathPresence.Unknown)
                return Result("Unable to inspect", state.Reason ?? "The executable cannot be safely inspected; no absence is inferred.");
            if (state.Presence == PathPresence.Missing)
                return new EnvironmentCheckResult { Classification = "Needs review", ResolvedPath = executable, MissingConfirmed = true,
                    Summary = "The explicitly named local executable is absent at scan time. The registration may be obsolete or awaiting installation; review its owner before changing it." };
            if (state.IsDirectory) return Result("Needs review", "The executable path identifies a directory. No startup command was run to test it.");
            return new EnvironmentCheckResult { ResolvedPath = executable };
        }

        internal static bool TryStartupExecutable(string command, out string executable, out string reason)
        {
            executable = null;
            reason = null;
            if (String.IsNullOrWhiteSpace(command)) { reason = "The command is empty; no missing executable is inferred."; return false; }
            if (command.Length > MaxCommandLength) { reason = "The command exceeds the bounded parser limit; its target was not inferred."; return false; }
            string value = command.Trim();
            foreach (char character in value)
                if (Char.IsControl(character)) { reason = "The command contains control characters and was not interpreted."; return false; }
            if (value[0] == '"')
            {
                int close = value.IndexOf('"', 1);
                if (close <= 1 || (close + 1 < value.Length && !Char.IsWhiteSpace(value[close + 1])))
                { reason = "The executable quoting is incomplete or ambiguous; no target absence is inferred."; return false; }
                executable = value.Substring(1, close - 1);
            }
            else
            {
                int end = 0;
                while (end < value.Length && !Char.IsWhiteSpace(value[end])) end++;
                executable = value.Substring(0, end);
            }
            // CreateProcess can interpret unquoted space-containing paths as several different
            // executables. Do not use the permissive uninstall-command parser for this module.
            // https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw
            if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || executable.IndexOf('"') >= 0)
            { executable = null; reason = "The command does not identify an unambiguous explicit .exe path. Shell commands, scripts and unquoted paths with spaces require manual review."; return false; }
            if (executable.IndexOf('%') >= 0)
            { executable = null; reason = "The executable uses environment references. They were not expanded against the scanner's potentially stale process environment."; return false; }
            if (!IsAbsoluteDrivePath(executable))
            { executable = null; reason = "The executable is relative, remote, or not a fully resolved local drive path. No PATH search or network access was attempted."; return false; }
            string fileName = executable.Substring(Math.Max(executable.LastIndexOf('\\'), executable.LastIndexOf('/')) + 1);
            if (CommandHosts.Contains(fileName))
            { executable = null; reason = "This command uses a shell or command host. Its arguments and target require manual review and were not executed."; return false; }
            return true;
        }

        internal static EnvironmentCheckResult ClassifyDirectoryValue(string raw, Func<string, EnvironmentPathEvidence> inspect)
        {
            if (raw == null || raw.Length > MaxValueLength) return Result("Unable to inspect", "The directory value is unavailable or exceeds the inspection limit.");
            string value = raw.Trim();
            if (value.Length == 0) return Result("Information", "The stored directory entry is empty. Its effective behavior was not inferred or changed.");
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') value = value.Substring(1, value.Length - 2);
            if (value.IndexOf('%') >= 0)
                return new EnvironmentCheckResult { Classification = "Information", InspectionLimited = true,
                    Summary = HasUnpairedPercent(value)
                        ? "The directory contains an unpaired percent sign. It may be literal text; no path was expanded or inspected."
                        : "The directory contains environment references. They were left unresolved because current process values do not prove the registry value's effective expansion." };
            if (value.IndexOf('"') >= 0 || !IsAbsoluteDrivePath(value))
                return Result("Unable to inspect", "The entry is not an unambiguous absolute local drive path; relative, remote and quoted syntax was not interpreted.");
            EnvironmentPathEvidence state = inspect(value);
            if (!state.SafeLocal || state.Presence == PathPresence.Unknown)
                return Result("Unable to inspect", state.Reason ?? "The directory cannot be safely inspected; no absence is inferred.");
            if (state.Presence == PathPresence.Missing)
                return new EnvironmentCheckResult { Classification = "Needs review", ResolvedPath = value, MissingConfirmed = true,
                    Summary = "The literal local directory is absent at scan time. It may be created on demand or belong to an optional application; it is not automatically removed or recreated." };
            if (!state.IsDirectory) return Result("Needs review", "The directory entry identifies a file. No change or write test was performed.");
            return new EnvironmentCheckResult { ResolvedPath = value };
        }

        internal static List<string> SplitPathEntries(string value, out bool balanced)
        {
            List<string> entries = new List<string>();
            bool quoted = false;
            int start = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '"') quoted = !quoted;
                if (value[i] == ';' && !quoted) { entries.Add(value.Substring(start, i - start)); start = i + 1; }
                if (entries.Count > MaxEntries) break;
            }
            entries.Add(value.Substring(start));
            balanced = !quoted;
            return entries;
        }

        internal static bool HasUnpairedPercent(string value)
        {
            bool paired = true;
            foreach (char character in value) if (character == '%') paired = !paired;
            return !paired;
        }

        private static bool IsAbsoluteDrivePath(string value)
        {
            if (value.Length < 3 || !Char.IsLetter(value[0]) || value[1] != ':' || value[2] != '\\' || value.IndexOf(':', 2) >= 0) return false;
            foreach (char character in value)
                if (Char.IsControl(character) || character == '*' || character == '?' || character == '<' || character == '>' || character == '|') return false;
            return true;
        }

        private static EnvironmentPathEvidence InspectLocalPath(string raw)
        {
            string full = null, reason = null;
            // TrySafeLocalPath expands variables; reject them here first to retain registry semantics.
            if (raw.IndexOf('%') >= 0 || !ScanPolicy.TrySafeLocalPath(raw, out full, out reason))
                return new EnvironmentPathEvidence { Presence = PathPresence.Unknown, SafeLocal = false,
                    Reason = raw.IndexOf('%') >= 0 ? "Environment references were left unresolved." : reason };
            PathPresence presence = ScanPolicy.GetPathPresence(full);
            if (presence != PathPresence.Exists) return new EnvironmentPathEvidence { Presence = presence, SafeLocal = true,
                Reason = presence == PathPresence.Unknown ? "The local path could not be inspected; no absence is inferred." : null };
            try
            {
                FileAttributes attributes = File.GetAttributes(full);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return new EnvironmentPathEvidence { Presence = PathPresence.Unknown, SafeLocal = false, Reason = "Reparse-point paths are not used as absence evidence." };
                return new EnvironmentPathEvidence { Presence = PathPresence.Exists, SafeLocal = true, IsDirectory = (attributes & FileAttributes.Directory) != 0 };
            }
            catch (Exception ex)
            {
                if (!ScanPolicy.IsInspectionException(ex)) throw;
                return new EnvironmentPathEvidence { Presence = PathPresence.Unknown, SafeLocal = false, Reason = "The path changed or could not be inspected (" + ex.GetType().Name + ")." };
            }
        }

        private static EnvironmentCheckResult Result(string classification, string summary)
        { return new EnvironmentCheckResult { Classification = classification, Summary = summary }; }

        private static void AddFinding(ScanReport report, string rule, string title, string path, EnvironmentCheckResult result, RegistryView view)
        {
            string id = Digest(rule + "|" + path.ToUpperInvariant() + "|" + view + "|" + result.Classification + "|" + result.ResolvedPath).Substring(0, 24);
            foreach (Finding existing in report.Findings) if (existing.Id == id) return;
            Finding finding = new Finding
            {
                Id = id, Kind = FindingKind.ReviewOnly, CanFix = false, RuleId = rule, Category = Category,
                Title = title, Path = path, Summary = result.Summary, Classification = result.Classification,
                ProposedChange = "Review only. No startup registration, environment variable, directory or file is changed.",
                BackupPlan = "No backup is needed for this read-only diagnostic. Export and review the owning setting before any manual edit.",
                VerificationPlan = "After an intentional change, start a new sign-in session and rescan; test the affected application manually. A scan alone does not prove startup or command execution works."
            };
            finding.Evidence.Add("Registry view: " + view + ". Identical shared-view observations are deduplicated.");
            if (!String.IsNullOrEmpty(result.ResolvedPath)) finding.Evidence.Add("Literal target: " + Display(result.ResolvedPath, 1024));
            if (result.MissingConfirmed && !String.IsNullOrEmpty(result.ResolvedPath)) finding.MissingTargets.Add(result.ResolvedPath);
            report.Findings.Add(finding);
        }

        private static void Partial(DiagnosticModuleResult module, string note)
        {
            module.Status = "Partial";
            if (!module.Notes.Contains(note)) module.Notes.Add(note);
        }

        private static string Display(string text, int maximum)
        {
            if (text == null) return String.Empty;
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
            return text.Length <= maximum ? text : text.Substring(0, maximum) + "...";
        }

        private static string ValueName(string name)
        {
            return name.Length <= 180 ? Display(name, 180) : Display(name, 150) + " #" + Digest(name).Substring(0, 12);
        }

        private static string Digest(string text)
        {
            using (SHA256 hash = SHA256.Create()) return ScanPolicy.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
    }
}
