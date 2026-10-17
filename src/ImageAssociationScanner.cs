using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinProfileRepair
{
    // These diagnostics describe file-type icons, not previews of the image contents.
    // Reading association metadata or resolving a PRI resource does not change the default app.
    public static class ImageAssociationScanner
    {
        private static readonly string[] Extensions = { ".png", ".bmp", ".jpg", ".jpeg", ".gif", ".tif", ".tiff" };

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int AssocQueryString(uint flags, uint query, string association, string extra,
            StringBuilder output, ref uint length);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string source, StringBuilder output, uint count, IntPtr reserved);

        public static void Scan(ScanReport report)
        {
            if (report == null) throw new ArgumentNullException("report");
            report.Notes.Add("Image file-type icon diagnostics are separate from image-content thumbnails. A small document icon can be the associated application's intended artwork. No default-app association or icon handler is changed or activated by this check.");
            foreach (string extension in Extensions)
            {
                ImageIconDiagnostic diagnostic = new ImageIconDiagnostic { Extension = extension, Status = "NotDeclared" };
                if (diagnostic.Notes == null) diagnostic.Notes = new System.Collections.Generic.List<string>();
                try { ScanExtension(diagnostic); }
                catch (Exception ex)
                {
                    if (!IsReadException(ex)) throw;
                    diagnostic.Status = "Warning";
                    diagnostic.Notes.Add("Association data could not be fully inspected (" + ex.GetType().Name + "). No missing registration or file is inferred from a read error.");
                }
                report.ImageIcons.Add(diagnostic);
            }
        }

        private static void ScanExtension(ImageIconDiagnostic diagnostic)
        {
            RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            string choicePath = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\" + diagnostic.Extension + "\\UserChoice";
            diagnostic.ProgId = ReadValue(RegistryHive.CurrentUser, view, choicePath, "ProgId");
            if (String.IsNullOrWhiteSpace(diagnostic.ProgId))
            {
                diagnostic.ProgId = ReadValue(RegistryHive.ClassesRoot, view, diagnostic.Extension, null);
                diagnostic.Notes.Add("No per-user ProgID was declared; the merged extension registration is used as the registry fallback.");
            }
            string effectiveProgId, queryError;
            if (TryQuery(diagnostic.Extension, 20, out effectiveProgId, out queryError) && !String.IsNullOrWhiteSpace(effectiveProgId))
            {
                if (!String.Equals(diagnostic.ProgId, effectiveProgId, StringComparison.OrdinalIgnoreCase))
                    diagnostic.Notes.Add("Windows reports a different effective ProgID from the raw registry fallback; the effective selection is reported.");
                diagnostic.ProgId = effectiveProgId;
            }
            else diagnostic.Notes.Add("The Windows effective ProgID query was unavailable (" + queryError + "); the registry declaration is reported.");

            string extensionIcon = ReadValue(RegistryHive.ClassesRoot, view, diagnostic.Extension + "\\DefaultIcon", null);
            string progIdIcon = String.IsNullOrWhiteSpace(diagnostic.ProgId) ? null :
                ReadValue(RegistryHive.ClassesRoot, view, diagnostic.ProgId + "\\DefaultIcon", null);
            diagnostic.DefaultIcon = !String.IsNullOrWhiteSpace(extensionIcon) ? extensionIcon : progIdIcon;
            string effectiveIcon;
            if (TryQuery(diagnostic.Extension, 15, out effectiveIcon, out queryError) && !String.IsNullOrWhiteSpace(effectiveIcon))
                diagnostic.DefaultIcon = effectiveIcon;
            else diagnostic.Notes.Add("The Windows effective DefaultIcon query was unavailable (" + queryError + "); the registry declaration is reported.");

            AddIconHandlerNote(diagnostic, view, diagnostic.Extension);
            if (!String.IsNullOrWhiteSpace(diagnostic.ProgId)) AddIconHandlerNote(diagnostic, view, diagnostic.ProgId);
            AddIconHandlerNote(diagnostic, view, "SystemFileAssociations\\image");

            string classification = ClassifyIconReference(diagnostic.DefaultIcon);
            if (classification == "NotDeclared")
            {
                diagnostic.Status = "NotDeclared";
                diagnostic.Notes.Add("No static DefaultIcon is declared. Dynamic icon handling or the shell's fallback can be legitimate; this is informational and never an automatic repair finding.");
                return;
            }
            if (classification == "PerFile")
            {
                diagnostic.Status = "PerFile";
                diagnostic.Notes.Add("The association requests a file-specific icon (%1). A static file path cannot verify this icon; no handler is activated.");
                return;
            }
            if (classification == "PackageResource")
            {
                StringBuilder output = new StringBuilder(32768);
                int hr = SHLoadIndirectString(ScanPolicy.Unquote(diagnostic.DefaultIcon.Trim()), output, (uint)output.Capacity, IntPtr.Zero);
                if (hr != 0 || output.Length == 0)
                {
                    diagnostic.Status = "Warning";
                    diagnostic.Notes.Add("The package/PRI icon reference did not resolve in this user's resource context (" + HResult(hr) + "). This is a review item; it does not authorize changing the default app.");
                    return;
                }
                diagnostic.ResolvedIcon = output.ToString();
                InspectIconPath(diagnostic, diagnostic.ResolvedIcon, true);
                return;
            }
            if (classification == "IndirectResource")
            {
                diagnostic.Status = "Warning";
                diagnostic.Notes.Add("This indirect icon declaration is outside the supported package/PRI syntax and was retained without treating it as an ordinary missing file.");
                return;
            }
            diagnostic.ResolvedIcon = ScanPolicy.IconExecutable(diagnostic.DefaultIcon);
            InspectIconPath(diagnostic, diagnostic.ResolvedIcon, false);
        }

        private static void InspectIconPath(ImageIconDiagnostic diagnostic, string rawPath, bool packageResource)
        {
            string full, reason;
            if (!ScanPolicy.TrySafeLocalPath(rawPath, out full, out reason))
            {
                diagnostic.Status = "Warning";
                diagnostic.Notes.Add("The icon path was not verified: " + reason + " Relative module names or other shell-supported declarations may still be valid.");
                return;
            }
            diagnostic.ResolvedIcon = full;
            PathPresence presence = ScanPolicy.GetPathPresence(full);
            if (presence == PathPresence.Exists && (File.GetAttributes(full) & FileAttributes.Directory) == 0)
            {
                diagnostic.Status = "Present";
                diagnostic.Notes.Add(packageResource
                    ? "The package resource resolves to an existing local file. Artwork, cache state, and rendering were not evaluated."
                    : "The icon file or resource module exists. The resource index and rendered artwork were not validated.");
            }
            else
            {
                diagnostic.Status = "Warning";
                diagnostic.Notes.Add(presence == PathPresence.Missing
                    ? "The declared icon file is confirmed absent. This is diagnostic only; no association change is offered."
                    : "The resolved icon path is inaccessible or is not a regular file. No absence is inferred from an access error.");
            }
        }

        // Kept pure so tests can distinguish PRI/package declarations from ordinary file paths.
        internal static string ClassifyIconReference(string reference)
        {
            if (String.IsNullOrWhiteSpace(reference)) return "NotDeclared";
            string value = ScanPolicy.Unquote(reference.Trim());
            if (value == "%1") return "PerFile";
            if (IsPackagedResourceReference(value)) return "PackageResource";
            if (value.StartsWith("@", StringComparison.Ordinal)) return "IndirectResource";
            return "FileResource";
        }

        internal static bool IsPackagedResourceReference(string reference)
        {
            if (String.IsNullOrWhiteSpace(reference)) return false;
            string value = ScanPolicy.Unquote(reference.Trim());
            if (!value.StartsWith("@{", StringComparison.Ordinal) || !value.EndsWith("}", StringComparison.Ordinal)) return false;
            int separator = value.IndexOf('?', 2);
            if (separator <= 2 || separator >= value.Length - 2 || value.IndexOf('?', separator + 1) >= 0) return false;
            string package = value.Substring(2, separator - 2);
            string resource = value.Substring(separator + 1, value.Length - separator - 2);
            // Only registered package-name lookups are supported. SHLoadIndirectString also
            // accepts arbitrary PRI/module paths; those are intentionally not loaded here.
            if (!Regex.IsMatch(package,
                @"^[A-Za-z0-9][A-Za-z0-9.-]{2,49}_[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}_(x86|x64|arm|arm64|neutral)_[A-Za-z0-9.-]*_[A-Za-z0-9]{13}$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)) return false;
            return Regex.IsMatch(resource, @"^ms-resource://[A-Za-z0-9.-]*/[^?#{}""\x00-\x20\\]+$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        private static void AddIconHandlerNote(ImageIconDiagnostic diagnostic, RegistryView view, string association)
        {
            string handler = ReadValue(RegistryHive.ClassesRoot, view, association + "\\shellex\\IconHandler", null);
            if (!String.IsNullOrWhiteSpace(handler))
                diagnostic.Notes.Add("IconHandler declared at " + association + ": " + handler + ". The handler is not activated or evaluated.");
        }

        private static string ReadValue(RegistryHive hive, RegistryView view, string path, string name)
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey key = root.OpenSubKey(path, false))
            {
                if (key == null) return null;
                object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return value as string;
            }
        }

        private static bool TryQuery(string extension, uint query, out string value, out string error)
        {
            value = null;
            error = null;
            try
            {
                uint length = 32768;
                StringBuilder output = new StringBuilder((int)length);
                int hr = AssocQueryString(0, query, extension, null, output, ref length);
                if (hr == 0) { value = output.ToString(); return true; }
                error = HResult(hr);
            }
            catch (Exception ex)
            {
                if (!IsReadException(ex)) throw;
                error = ex.GetType().Name;
            }
            return false;
        }

        private static bool IsReadException(Exception ex)
        {
            return ScanPolicy.IsInspectionException(ex) || ex is DllNotFoundException || ex is EntryPointNotFoundException;
        }

        private static string HResult(int value)
        {
            return "0x" + unchecked((uint)value).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
