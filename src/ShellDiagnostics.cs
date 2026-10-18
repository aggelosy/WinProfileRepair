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
    internal sealed class ShellHandlerAssessment
    {
        internal string RuleId;
        internal string Classification;
        internal string Summary;
        internal bool RequiresReview;
        internal bool Incomplete;
    }

    // These checks read registrations only. They never activate an extension, load its DLL,
    // change file associations, or infer that a present COM registration is functional.
    public static class ShellDiagnostics
    {
        private const string PreviewHandler = "{8895B1C6-B41F-4C1C-A562-0D564250836F}";
        private const string Category = "Shell & appearance";
        private const int MaximumContextHandlersPerContainer = 128;
        private const int MaximumHandlers = 512;

        private sealed class ScanState
        {
            internal ScanReport Report;
            internal DiagnosticModuleResult Module;
            internal CancellationToken Cancellation;
            internal int Inspected;
            internal int Registered;
            internal int Review;
            internal bool Partial;
            internal bool TotalLimitReported;
            internal Dictionary<string, Finding> Seen = new Dictionary<string, Finding>(StringComparer.OrdinalIgnoreCase);
        }

        public static void Scan(ScanReport report, CancellationToken cancellation)
        {
            if (report == null) throw new ArgumentNullException("report");
            cancellation.ThrowIfCancellationRequested();
            DiagnosticModuleResult module = new DiagnosticModuleResult
            {
                Id = "shell-handlers", Title = "Shell extension registrations", Category = Category, Status = "Completed"
            };
            module.Notes.Add("Read-only inspection of current-user image/PDF preview and icon handlers, plus current-user context-menu handlers for *, AllFilesystemObjects, Directory, and Folder. Thumbnail overrides are checked by the existing thumbnail module.");
            module.Notes.Add("CLSID presence checks include classic COM and PackagedCom ClassIndex registrations in both available registry views. A registration is not a functional test; no extension is instantiated or DLL loaded.");
            module.Notes.Add("Coverage is deliberately bounded: at most " + MaximumContextHandlersPerContainer.ToString(CultureInfo.InvariantCulture) + " handlers per context-menu container and " + MaximumHandlers.ToString(CultureInfo.InvariantCulture) + " distinct registrations overall. ProgID, system-file-association, manifest, overlay, and other extension locations are outside this scan.");
            report.Modules.Add(module);
            ScanState state = new ScanState { Report = report, Module = module, Cancellation = cancellation };
            string[] extensions = { ".png", ".bmp", ".jpg", ".jpeg", ".gif", ".tif", ".tiff", ".pdf" };
            string[] shellObjects = { "*", "AllFilesystemObjects", "Directory", "Folder" };
            foreach (RegistryView view in ScanPolicy.Views())
            {
                foreach (string extension in extensions)
                {
                    InspectHandler(state, RegistryHive.CurrentUser, view, @"Software\Classes\" + extension + @"\shellex\" + PreviewHandler, extension + " preview handler", null);
                    InspectHandler(state, RegistryHive.CurrentUser, view, @"Software\Classes\" + extension + @"\shellex\IconHandler", extension + " icon handler", null);
                }
                foreach (string shellObject in shellObjects)
                {
                    InspectContextContainer(state, RegistryHive.CurrentUser, view, @"Software\Classes\" + shellObject + @"\shellex\ContextMenuHandlers", shellObject);
                }
            }
            module.Status = state.Partial ? "Partial" : "Completed";
            module.Summary = state.Inspected.ToString(CultureInfo.InvariantCulture) + " distinct handler registration(s) inspected; " + state.Registered.ToString(CultureInfo.InvariantCulture) + " COM class(es) registered; " + state.Review.ToString(CultureInfo.InvariantCulture) + " review finding(s).";
            if (state.Partial) module.Notes.Add("Some evidence could not be inspected or an inspection limit was reached. Remaining locations must not be assumed healthy.");
        }

        private static void InspectContextContainer(ScanState state, RegistryHive hive, RegistryView view, string relativePath, string shellObject)
        {
            state.Cancellation.ThrowIfCancellationRequested();
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey container = root.OpenSubKey(relativePath, false))
                {
                    if (container == null) return;
                    string[] children = container.GetSubKeyNames();
                    Array.Sort(children, StringComparer.OrdinalIgnoreCase);
                    int count = Math.Min(children.Length, MaximumContextHandlersPerContainer);
                    if (children.Length > count)
                    {
                        state.Partial = true;
                        state.Module.Notes.Add("Context-menu container limit reached at " + SourcePath(hive, view, relativePath) + "; additional entries were not inspected.");
                    }
                    for (int index = 0; index < count; index++)
                        InspectHandler(state, hive, view, relativePath + "\\" + children[index], shellObject + " context-menu handler " + children[index], children[index]);
                }
            }
            catch (Exception ex)
            {
                if (!ScanPolicy.IsInspectionException(ex)) throw;
                AddInspectionFailure(state, SourcePath(hive, view, relativePath), "Context-menu container could not be inspected: " + ex.GetType().Name + ".");
            }
        }

        private static void InspectHandler(ScanState state, RegistryHive hive, RegistryView view, string relativePath, string title, string contextName)
        {
            state.Cancellation.ThrowIfCancellationRequested();
            if (state.Inspected >= MaximumHandlers)
            {
                state.Partial = true;
                if (!state.TotalLimitReported)
                {
                    state.TotalLimitReported = true;
                    state.Module.Notes.Add("The overall shell-handler inspection limit was reached; remaining entries were not inspected.");
                }
                return;
            }
            string source = SourcePath(hive, view, relativePath);
            try
            {
                string classId;
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = root.OpenSubKey(relativePath, false))
                {
                    if (key == null) return;
                    object raw = key.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    classId = raw as string;
                    Guid contextClass;
                    // Context-menu handlers can use a CLSID as their subkey name with an empty default value.
                    if (String.IsNullOrWhiteSpace(classId) && (raw == null || raw is string) && Guid.TryParse(contextName, out contextClass)) classId = contextName;
                    if (raw != null && classId == null) classId = "[non-string default value]";
                }
                string normalizedPath = hive == RegistryHive.CurrentUser && relativePath.StartsWith(@"Software\Classes\", StringComparison.OrdinalIgnoreCase)
                    ? relativePath.Substring(@"Software\Classes\".Length) : relativePath;
                string identity = view.ToString() + "|" + normalizedPath + "|" + classId;
                Finding previous;
                if (state.Seen.TryGetValue(identity, out previous))
                {
                    if (previous != null && !previous.Evidence.Contains("Also observed at: " + source)) previous.Evidence.Add("Also observed at: " + source);
                    return;
                }
                state.Inspected++;
                Guid clsid;
                bool valid = Guid.TryParse(classId, out clsid);
                PathPresence registration = valid ? ScanPolicy.ComRegistrationState(clsid) : PathPresence.Unknown;
                bool serverInspected = false;
                string serverPath = null;
                string serverNote = null;
                PathPresence serverPresence = PathPresence.Unknown;
                if (valid && registration == PathPresence.Exists)
                {
                    state.Registered++;
                    InspectInprocServer(clsid, view, out serverInspected, out serverPresence, out serverPath, out serverNote);
                }
                ShellHandlerAssessment assessment = Assess(classId, registration, serverInspected, serverPresence);
                Finding finding = null;
                if (assessment.RequiresReview)
                {
                    finding = BuildFinding(title, source, classId, assessment);
                    finding.Evidence.Add("Registration view: " + view + ".");
                    finding.Evidence.Add("COM presence result across classic and packaged registrations: " + registration + ".");
                    if (!String.IsNullOrEmpty(serverPath)) finding.Evidence.Add("Explicit InprocServer32 DLL target: " + serverPath);
                    if (!String.IsNullOrEmpty(serverNote)) finding.Evidence.Add(serverNote);
                    if (serverInspected && serverPresence == PathPresence.Missing && serverPath != null) finding.MissingTargets.Add(serverPath);
                    state.Report.Findings.Add(finding);
                    state.Review++;
                }
                state.Seen.Add(identity, finding);
                if (assessment.Incomplete)
                {
                    state.Partial = true;
                    if (!assessment.RequiresReview) state.Module.Notes.Add(source + ": " + assessment.Summary);
                }
            }
            catch (Exception ex)
            {
                if (!ScanPolicy.IsInspectionException(ex)) throw;
                AddInspectionFailure(state, source, "Handler registration could not be inspected: " + ex.GetType().Name + ".");
            }
        }

        // Pure assessment separates what the observations prove from what remains unknown.
        internal static ShellHandlerAssessment Assess(string classId, PathPresence registration, bool serverInspected, PathPresence serverPresence)
        {
            Guid ignored;
            if (!Guid.TryParse(classId, out ignored))
                return Assessment("shell.handler.invalid-clsid", "Needs review", "This handler entry does not supply a valid COM class identifier. It may be incomplete or intentionally disabled; the scanner cannot establish which.", true, false);
            if (registration == PathPresence.Missing)
                return Assessment("shell.handler.unregistered-clsid", "Confirmed fault", "The referenced COM class has no classic or PackagedCom ClassIndex registration in either available registry view. This does not establish that this entry caused a visible Explorer problem.", true, false);
            if (registration == PathPresence.Unknown)
                return Assessment("shell.handler.registration-unavailable", "Unable to inspect", "The referenced COM registration could not be fully inspected. Its absence has not been established.", true, true);
            if (serverInspected && serverPresence == PathPresence.Missing)
                return Assessment("shell.handler.missing-inproc-target", "Confirmed fault", "The classic COM registration in this view names an explicit local InprocServer32 DLL that is missing. No packaged registration was found. Other activation or fallback behavior has not been tested.", true, false);
            if (serverInspected && serverPresence == PathPresence.Unknown)
                return Assessment("shell.handler.server-unavailable", "Unable to inspect", "The registered DLL target could not be safely inspected. It has not been classified as missing.", true, true);
            return Assessment("shell.handler.registered", "Needs review", "A COM registration exists. DLL loading and handler behavior have not been tested.", false, false);
        }

        private static ShellHandlerAssessment Assessment(string rule, string classification, string summary, bool review, bool incomplete)
        {
            return new ShellHandlerAssessment { RuleId = rule, Classification = classification, Summary = summary, RequiresReview = review, Incomplete = incomplete };
        }

        internal static Finding BuildFinding(string title, string source, string classId, ShellHandlerAssessment assessment)
        {
            if (assessment == null) throw new ArgumentNullException("assessment");
            string fingerprint;
            using (SHA256 hash = SHA256.Create())
                fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes((source ?? String.Empty) + "|" + classId + "|" + assessment.RuleId))).Replace("-", String.Empty);
            Finding finding = new Finding
            {
                Id = "shell-" + fingerprint.Substring(0, 24), EvidenceFingerprint = fingerprint, Title = title, Category = Category, Kind = FindingKind.ReviewOnly, CanFix = false,
                RuleId = assessment.RuleId, Classification = assessment.Classification, Summary = assessment.Summary, Path = source,
                ProposedChange = "Read-only finding. Identify the owning application and compare its supported repair or reinstall procedure before changing any shell registration.",
                BackupPlan = "No changes are proposed by this scan, so no repair backup is created. Export this report before investigating the owning application.",
                VerificationPlan = "After an application-supported repair, rescan this registration and separately check the affected icon, preview pane, or context menu. Registration presence alone does not verify behavior."
            };
            finding.Evidence.Add("Source registration: " + source);
            finding.Evidence.Add("Handler CLSID value: " + (String.IsNullOrEmpty(classId) ? "[empty]" : classId));
            finding.Evidence.Add("No shell extension was instantiated, no DLL was loaded, and no registration was changed.");
            return finding;
        }

        private static void InspectInprocServer(Guid clsid, RegistryView view, out bool inspected, out PathPresence presence, out string serverPath, out string note)
        {
            inspected = false; presence = PathPresence.Unknown; serverPath = null; note = null;
            try
            {
                // A classic InprocServer32 entry need not be the effective server for packaged COM.
                // Do not diagnose its absence or DLL target when any packaged registration is present.
                foreach (RegistryView candidateView in ScanPolicy.Views())
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, candidateView))
                    using (RegistryKey packaged = root.OpenSubKey(@"PackagedCom\ClassIndex\" + clsid.ToString("B"), false))
                        if (packaged != null) { note = "Packaged COM registration is present; classic DLL target inspection was skipped."; return; }
                }
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view))
                using (RegistryKey server = root.OpenSubKey(@"CLSID\" + clsid.ToString("B") + @"\InprocServer32", false))
                {
                    if (server == null) { note = "No classic InprocServer32 entry in this view. Other activation models were not interpreted."; return; }
                    string raw = server.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    string explicitPath;
                    if (!TryExplicitInprocPath(raw, out explicitPath))
                    {
                        inspected = true; note = "The InprocServer32 value is not a single fully qualified local DLL path; search paths, network paths, commands, and ambiguous values are not probed."; return;
                    }
                    string reason;
                    if (!ScanPolicy.TrySafeLocalPath(explicitPath, out serverPath, out reason))
                    {
                        inspected = true; note = "DLL inspection was skipped: " + reason; return;
                    }
                    inspected = true;
                    presence = ScanPolicy.GetPathPresence(serverPath);
                    if (presence == PathPresence.Exists && (File.GetAttributes(serverPath) & FileAttributes.Directory) != 0)
                    {
                        presence = PathPresence.Unknown;
                        note = "The registered DLL target resolves to a directory; no DLL was loaded.";
                    }
                    else note = "Only local file presence was checked. No signature, dependency, export, architecture, or runtime behavior was tested.";
                }
            }
            catch (Exception ex)
            {
                if (!ScanPolicy.IsInspectionException(ex)) throw;
                inspected = true; presence = PathPresence.Unknown; note = "COM server details could not be inspected: " + ex.GetType().Name + ".";
            }
        }

        // Syntax-only helper: never asks the network or filesystem to resolve a registry string.
        internal static bool TryExplicitInprocPath(string raw, out string path)
        {
            path = null;
            if (String.IsNullOrWhiteSpace(raw)) return false;
            // A stored expansion may differ from this process's captured environment.
            // Only literal DLL paths can be used as local-file evidence in this module.
            string value = raw.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') value = value.Substring(1, value.Length - 2);
            if (value.Length < 7 || !Char.IsLetter(value[0]) || value[1] != ':' || value[2] != '\\' ||
                value.IndexOf(':', 2) >= 0 || value.IndexOf('%') >= 0 || value.IndexOf('"') >= 0 || value.IndexOfAny(new char[] { '\0', '\r', '\n', '*', '?', '<', '>', '|' }) >= 0 ||
                !value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return false;
            path = value;
            return true;
        }

        private static string SourcePath(RegistryHive hive, RegistryView view, string path)
        {
            return (hive == RegistryHive.CurrentUser ? "HKCU\\" : "HKCR\\") + path + " [" + view + "]";
        }

        private static void AddInspectionFailure(ScanState state, string source, string message)
        {
            state.Cancellation.ThrowIfCancellationRequested();
            state.Partial = true;
            ShellHandlerAssessment assessment = Assessment("shell.handler.registry-unavailable", "Unable to inspect", message, true, true);
            Finding finding = BuildFinding("Shell handler inspection unavailable", source, null, assessment);
            state.Report.Findings.Add(finding);
            state.Review++;
        }
    }
}
