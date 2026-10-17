using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinProfileRepair
{
    public static class RegistryTools
    {
        private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

        public static bool IsPermittedUserKeyPath(string path)
        {
            RegistrySnapshot snapshot = new RegistrySnapshot { Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64, KeyPath = path, ChildCount = 0 };
            return IsAllowedSnapshot(snapshot, FindingKind.ThumbnailOverride) || IsAllowedSnapshot(snapshot, FindingKind.StaleUninstallRecord);
        }

        public static RegistrySnapshot Capture(RegistryHive hive, RegistryView view, string relativePath)
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey key = root.OpenSubKey(relativePath, false))
            {
                if (key == null) return null;
                RegistrySnapshot snapshot = new RegistrySnapshot { Hive = hive, View = view, KeyPath = relativePath, ChildCount = key.SubKeyCount };
                string[] names = key.GetValueNames();
                Array.Sort(names, StringComparer.Ordinal);
                foreach (string name in names)
                {
                    RegistryValueKind kind = key.GetValueKind(name);
                    object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    RegistryValueSnapshot item = new RegistryValueSnapshot { Name = name, Kind = kind };
                    switch (kind)
                    {
                        case RegistryValueKind.String:
                        case RegistryValueKind.ExpandString: item.Text = (string)value; break;
                        case RegistryValueKind.MultiString: item.TextArray = (string[])value; break;
                        case RegistryValueKind.Binary:
                        case RegistryValueKind.None: item.BinaryBase64 = Convert.ToBase64String((byte[])value); break;
                        case RegistryValueKind.DWord: item.Number = (int)value; break;
                        case RegistryValueKind.QWord: item.Number = (long)value; break;
                        default: throw new NotSupportedException("Unsupported registry value type at " + relativePath + ": " + name);
                    }
                    snapshot.Values.Add(item);
                }
                return snapshot;
            }
        }

        public static string Fingerprint(RegistrySnapshot snapshot)
        {
            if (snapshot == null) return null;
            using (MemoryStream bytes = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(bytes, Encoding.UTF8, true))
                {
                    writer.Write((int)snapshot.Hive);
                    writer.Write((int)snapshot.View);
                    WriteString(writer, snapshot.KeyPath);
                    writer.Write(snapshot.ChildCount);
                    if (snapshot.Values == null) throw new InvalidDataException("Missing registry values.");
                    List<RegistryValueSnapshot> values = new List<RegistryValueSnapshot>(snapshot.Values);
                    values.Sort(delegate(RegistryValueSnapshot a, RegistryValueSnapshot b) { return StringComparer.Ordinal.Compare(a.Name, b.Name); });
                    writer.Write(values.Count);
                    foreach (RegistryValueSnapshot value in values)
                    {
                        WriteString(writer, value.Name);
                        writer.Write((int)value.Kind);
                        switch (value.Kind)
                        {
                            case RegistryValueKind.String:
                            case RegistryValueKind.ExpandString: WriteString(writer, value.Text); break;
                            case RegistryValueKind.MultiString:
                                if (value.TextArray == null) throw new InvalidDataException("Missing multi-string value.");
                                writer.Write(value.TextArray.Length);
                                foreach (string element in value.TextArray) WriteString(writer, element);
                                break;
                            case RegistryValueKind.Binary:
                            case RegistryValueKind.None:
                                byte[] raw = Convert.FromBase64String(value.BinaryBase64);
                                writer.Write(raw.Length); writer.Write(raw); break;
                            case RegistryValueKind.DWord:
                            case RegistryValueKind.QWord: writer.Write(value.Number); break;
                            default: throw new InvalidDataException("Unsupported registry value type.");
                        }
                    }
                    writer.Flush();
                }
                using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "");
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }

        public static bool IsAllowedSnapshot(RegistrySnapshot snapshot, FindingKind kind)
        {
            if (snapshot == null || snapshot.Hive != RegistryHive.CurrentUser || snapshot.ChildCount != 0 || snapshot.Values == null) return false;
            if (snapshot.View != RegistryView.Registry32 && snapshot.View != RegistryView.Registry64) return false;
            string path = snapshot.KeyPath;
            if (String.IsNullOrEmpty(path)) return false;
            if (kind == FindingKind.ThumbnailOverride)
                return Regex.IsMatch(path, @"\ASoftware\\Classes\\\.[A-Za-z0-9]{1,20}\\shellex\\" + Regex.Escape(AppInfo.ThumbnailHandler) + @"\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (kind == FindingKind.StaleUninstallRecord && path.StartsWith(UninstallRoot, StringComparison.OrdinalIgnoreCase))
            {
                string leaf = path.Substring(UninstallRoot.Length);
                return leaf.Length > 0 && leaf.Length <= 255 && leaf.IndexOf('\\') < 0 && leaf.IndexOf('/') < 0 && leaf.IndexOf('\0') < 0 && leaf != "." && leaf != "..";
            }
            return false;
        }

        internal static void DeleteLeaf(RegistrySnapshot expected, FindingKind kind)
        {
            if (!IsAllowedSnapshot(expected, kind)) throw new InvalidOperationException("Registry path is outside the repair policy.");
            RegistrySnapshot current = Capture(expected.Hive, expected.View, expected.KeyPath);
            if (current == null || !String.Equals(Fingerprint(current), Fingerprint(expected), StringComparison.Ordinal))
                throw new InvalidOperationException("Registry entry changed after the scan; rescan before repairing.");
            using (RegistryKey root = RegistryKey.OpenBaseKey(expected.Hive, expected.View)) root.DeleteSubKey(expected.KeyPath, true);
            if (Capture(expected.Hive, expected.View, expected.KeyPath) != null) throw new IOException("The registry entry could not be removed.");
        }

        internal static void RestoreLeaf(RegistrySnapshot snapshot, FindingKind kind)
        {
            ValidateSnapshot(snapshot, kind);
            using (RegistryKey root = RegistryKey.OpenBaseKey(snapshot.Hive, snapshot.View))
            {
                int separator = snapshot.KeyPath.LastIndexOf('\\');
                using (RegistryKey parent = root.OpenSubKey(snapshot.KeyPath.Substring(0, separator), true))
                {
                    if (parent == null) throw new IOException("Restore refused: the original registry parent no longer exists.");
                    SafeRegistryHandle handle;
                    int disposition;
                    int error = RegCreateKeyEx(parent.Handle, snapshot.KeyPath.Substring(separator + 1), 0, null, 0, 0x2001F, IntPtr.Zero, out handle, out disposition);
                    if (error != 0)
                    {
                        if (handle != null) handle.Dispose();
                        throw new Win32Exception(error);
                    }
                    using (handle)
                    {
                        if (disposition != 1) throw new IOException("Restore refused: the registry entry already exists.");
                        try
                        {
                            using (RegistryKey key = RegistryKey.FromHandle(handle, snapshot.View))
                            {
                                foreach (RegistryValueSnapshot item in snapshot.Values)
                                {
                                    if (item.Kind == RegistryValueKind.None)
                                    {
                                        byte[] raw = (byte[])ToRegistryValue(item);
                                        int setError = RegSetValueEx(key.Handle, item.Name, 0, 0, raw, raw.Length);
                                        if (setError != 0) throw new Win32Exception(setError);
                                    }
                                    else key.SetValue(item.Name, ToRegistryValue(item), item.Kind);
                                }
                                key.Flush();
                            }
                            RegistrySnapshot restored = Capture(snapshot.Hive, snapshot.View, snapshot.KeyPath);
                            if (restored == null || !String.Equals(Fingerprint(restored), Fingerprint(snapshot), StringComparison.Ordinal))
                                throw new IOException("The recreated registry entry did not match its backup.");
                        }
                        catch (Exception ex)
                        {
                            throw new IOException("Restore created the registry entry but did not finish or could not verify every value. It may be partially restored; the backup is intact. Review this entry before retrying. The tool will not overwrite or automatically delete it.", ex);
                        }
                    }
                }
            }
        }

        public static void ValidateSnapshot(RegistrySnapshot snapshot, FindingKind kind)
        {
            if (!IsAllowedSnapshot(snapshot, kind)) throw new InvalidDataException("Backup registry path is outside the restore policy.");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryValueSnapshot item in snapshot.Values)
            {
                if (item == null || item.Name == null || item.Name.IndexOf('\0') >= 0 || !names.Add(item.Name)) throw new InvalidDataException("Invalid or duplicate registry value name.");
                ToRegistryValue(item);
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegCreateKeyEx(SafeRegistryHandle key, string subKey, int reserved, string keyClass, int options, int desiredAccess, IntPtr securityAttributes, out SafeRegistryHandle result, out int disposition);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegSetValueEx(SafeRegistryHandle key, string valueName, int reserved, int type, byte[] data, int size);

        private static object ToRegistryValue(RegistryValueSnapshot item)
        {
            switch (item.Kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    if (item.Text == null) throw new InvalidDataException("Missing string value.");
                    return item.Text;
                case RegistryValueKind.MultiString:
                    if (item.TextArray == null || Array.IndexOf(item.TextArray, null) >= 0) throw new InvalidDataException("Invalid multi-string value.");
                    return item.TextArray;
                case RegistryValueKind.Binary:
                case RegistryValueKind.None:
                    if (item.BinaryBase64 == null) throw new InvalidDataException("Missing binary registry value.");
                    try { return Convert.FromBase64String(item.BinaryBase64); }
                    catch (FormatException ex) { throw new InvalidDataException("Invalid binary registry value.", ex); }
                case RegistryValueKind.DWord:
                    if (item.Number < Int32.MinValue || item.Number > Int32.MaxValue) throw new InvalidDataException("Invalid DWORD value.");
                    return (int)item.Number;
                case RegistryValueKind.QWord: return item.Number;
                default: throw new InvalidDataException("Unsupported registry value type.");
            }
        }
    }
}
