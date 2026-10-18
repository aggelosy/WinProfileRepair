using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using WinProfileRepair;

internal static class TestProgram
{
    private static int assertions;
    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new Exception("FAILED: " + name);
    }
    private static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, name);
    }
    private static RegistrySnapshot Sample()
    {
        RegistrySnapshot snapshot = new RegistrySnapshot
        {
            Hive = RegistryHive.CurrentUser,
            View = RegistryView.Registry64,
            KeyPath = @"Software\Classes\.png\shellex\" + AppInfo.ThumbnailHandler,
            ChildCount = 0
        };
        snapshot.Values.Add(new RegistryValueSnapshot { Name = "", Kind = RegistryValueKind.String, Text = "{8AD5CECD-DF0D-41C3-BA21-1E22114CC73C}" });
        snapshot.Values.Add(new RegistryValueSnapshot { Name = "expanded", Kind = RegistryValueKind.ExpandString, Text = @"%LOCALAPPDATA%\Example" });
        snapshot.Values.Add(new RegistryValueSnapshot { Name = "strings", Kind = RegistryValueKind.MultiString, TextArray = new[] { "", "Unicode: Ελληνικά", "emoji: \uD83D\uDEE0" } });
        snapshot.Values.Add(new RegistryValueSnapshot { Name = "binary", Kind = RegistryValueKind.Binary, BinaryBase64 = Convert.ToBase64String(new byte[] { 0, 255, 1, 128 }) });
        snapshot.Values.Add(new RegistryValueSnapshot { Name = "none", Kind = RegistryValueKind.None, BinaryBase64 = "" });
        snapshot.Values.Add(new RegistryValueSnapshot { Name = "dword", Kind = RegistryValueKind.DWord, Number = -1 });
        snapshot.Values.Add(new RegistryValueSnapshot { Name = "qword", Kind = RegistryValueKind.QWord, Number = Int64.MaxValue });
        return snapshot;
    }
    private static RegistrySnapshot Clone(RegistrySnapshot original)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        return serializer.Deserialize<RegistrySnapshot>(serializer.Serialize(original));
    }
    private static void PolicyTests()
    {
        string thumbnail = @"Software\Classes\.png\shellex\" + AppInfo.ThumbnailHandler;
        string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\old-record";
        Check(RegistryTools.IsPermittedUserKeyPath(thumbnail), "thumbnail leaf accepted");
        Check(RegistryTools.IsPermittedUserKeyPath(thumbnail.ToUpperInvariant()), "case insensitive registry policy");
        Check(RegistryTools.IsPermittedUserKeyPath(uninstall), "uninstall leaf accepted");
        foreach (string invalid in new[] { null, "", @"Software\Classes", thumbnail + @"\child", @"HKCU\" + thumbnail, @"HKLM\" + thumbnail, @"Software\Classes\.png\shellex\IconHandler", @"Software\Classes\.png\shell\open\command", @"Software\Classes\..\shellex\" + AppInfo.ThumbnailHandler, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\", uninstall + @"\child", @"Software\Microsoft\Windows\CurrentVersion\Uninstall\..", uninstall + "/child" })
            Check(!RegistryTools.IsPermittedUserKeyPath(invalid), "reject registry scope: " + invalid);
        RegistrySnapshot sample = Sample();
        Check(RegistryTools.IsAllowedSnapshot(sample, FindingKind.ThumbnailOverride), "eligible current user thumbnail");
        sample.Hive = RegistryHive.LocalMachine;
        Check(!RegistryTools.IsAllowedSnapshot(sample, FindingKind.ThumbnailOverride), "no HKLM writes");
        sample.Hive = RegistryHive.CurrentUser; sample.ChildCount = 1;
        Check(!RegistryTools.IsAllowedSnapshot(sample, FindingKind.ThumbnailOverride), "no recursive registry removal");
        sample.ChildCount = 0; sample.View = RegistryView.Default;
        Check(!RegistryTools.IsAllowedSnapshot(sample, FindingKind.ThumbnailOverride), "explicit registry view required");
        Check(!RegistryTools.IsAllowedSnapshot(Sample(), FindingKind.ReviewOnly), "review-only cannot mutate");
        Check(!RegistryTools.IsAllowedSnapshot(Sample(), FindingKind.StaleUninstallRecord), "kind must match path");
    }
    private static void BackupTests()
    {
        RegistrySnapshot original = Sample();
        RegistryTools.ValidateSnapshot(original, FindingKind.ThumbnailOverride);
        string fingerprint = RegistryTools.Fingerprint(original);
        Check(fingerprint.Length == 64, "SHA256 backup fingerprint");
        Check(RegistryTools.Fingerprint(Clone(original)) == fingerprint, "all registry value types survive JSON round trip");
        RegistrySnapshot reordered = Clone(original); reordered.Values.Reverse();
        Check(RegistryTools.Fingerprint(reordered) == fingerprint, "value enumeration order does not affect fingerprint");
        RegistrySnapshot changed = Clone(original); changed.Values[0].Text = "changed";
        Check(RegistryTools.Fingerprint(changed) != fingerprint, "registry drift detected");
        changed = Clone(original); changed.Hive = RegistryHive.LocalMachine;
        Check(RegistryTools.Fingerprint(changed) != fingerprint, "hive included in fingerprint");
        changed = Clone(original); changed.View = RegistryView.Registry32;
        Check(RegistryTools.Fingerprint(changed) != fingerprint, "view included in fingerprint");
        changed = Clone(original); changed.ChildCount = 1;
        Check(RegistryTools.Fingerprint(changed) != fingerprint, "children included in fingerprint");
        changed = Clone(original); changed.Values[3].BinaryBase64 = Convert.ToBase64String(new byte[] { 1 });
        Check(RegistryTools.Fingerprint(changed) != fingerprint, "binary corruption detected");
        Reject(delegate { RegistrySnapshot item = Sample(); item.Hive = RegistryHive.LocalMachine; RegistryTools.ValidateSnapshot(item, FindingKind.ThumbnailOverride); }, "machine restore refused");
        Reject(delegate { RegistrySnapshot item = Sample(); item.ChildCount = 1; RegistryTools.ValidateSnapshot(item, FindingKind.ThumbnailOverride); }, "recursive restore refused");
        Reject(delegate { RegistrySnapshot item = Sample(); item.Values.Add(new RegistryValueSnapshot { Name = "BINARY", Kind = RegistryValueKind.String, Text = "duplicate" }); RegistryTools.ValidateSnapshot(item, FindingKind.ThumbnailOverride); }, "case-insensitive duplicate values refused");
        Reject(delegate { RegistrySnapshot item = Sample(); item.Values[0].Text = null; RegistryTools.ValidateSnapshot(item, FindingKind.ThumbnailOverride); }, "missing registry strings refused");
        Reject(delegate { RegistrySnapshot item = Sample(); item.Values[2].TextArray = new[] { "ok", null }; RegistryTools.ValidateSnapshot(item, FindingKind.ThumbnailOverride); }, "null multi-string values refused");
        Reject(delegate { RegistrySnapshot item = Sample(); item.Values[5].Number = (long)Int32.MaxValue + 1; RegistryTools.ValidateSnapshot(item, FindingKind.ThumbnailOverride); }, "invalid DWORD range refused");
        Reject(delegate { RegistrySnapshot item = Sample(); item.Values[0].Kind = RegistryValueKind.Unknown; RegistryTools.ValidateSnapshot(item, FindingKind.ThumbnailOverride); }, "unknown registry types refused");
    }
    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control item in root.Controls)
        {
            yield return item;
            foreach (Control child in AllControls(item)) yield return child;
        }
    }
    private static void DetectionTests(string artifacts)
    {
        string fixture = Path.Combine(artifacts, "fixtures");
        Directory.CreateDirectory(fixture);
        string missingRoot = Path.Combine(fixture, "absent-install-" + Guid.NewGuid().ToString("N"));
        string root, executable, icon, reason;
        string uninstaller = Path.Combine(missingRoot, "uninstall.exe");
        string logo = Path.Combine(missingRoot, "app.exe");
        Check(ScanPolicy.GenericUninstallEvidence(missingRoot, uninstaller, logo + ",0", out root, out executable, out icon, out reason), "absent explicit folder and in-folder executables qualify");
        Check(!ScanPolicy.GenericUninstallEvidence(null, uninstaller, logo, out root, out executable, out icon, out reason), "missing uninstaller alone never qualifies");
        Check(!ScanPolicy.GenericUninstallEvidence(missingRoot, Path.Combine(fixture, "cache", "uninstall.exe"), logo, out root, out executable, out icon, out reason), "cached installer outside installation root never qualifies");
        Check(!ScanPolicy.GenericUninstallEvidence(missingRoot, uninstaller, Path.Combine(fixture, "other-app.exe"), out root, out executable, out icon, out reason), "unrelated missing icon never qualifies");
        Check(!ScanPolicy.GenericUninstallEvidence(missingRoot, uninstaller, "", out root, out executable, out icon, out reason), "icon evidence is required");
        string presentRoot = Path.Combine(fixture, "present-install");
        Directory.CreateDirectory(presentRoot);
        Check(!ScanPolicy.GenericUninstallEvidence(presentRoot, Path.Combine(presentRoot, "missing-uninstaller.exe"), Path.Combine(presentRoot, "missing-app.exe"), out root, out executable, out icon, out reason), "existing installation folder requires review despite missing executables");
        Check(ScanPolicy.GetPathPresence(presentRoot) == PathPresence.Exists, "existing directory is detected");
        Check(ScanPolicy.GetPathPresence(missingRoot) == PathPresence.Missing, "missing fixture is detected");
        Check(ScanPolicy.GetPathPresence(null) == PathPresence.Unknown, "invalid path is unknown rather than missing");
        foreach (string invalid in new[] { @"\\server\share\app.exe", @"C:app.exe", @"relative\app.exe", @"C:\example\app.exe:stream", @"%__WINPROFILE_REPAIR_UNDEFINED_TEST_VARIABLE__%\app.exe" })
            Check(!ScanPolicy.TrySafeLocalPath(invalid, out executable, out reason), "unsafe or unresolved local path rejected: " + invalid);
        Check(ScanPolicy.CommandExecutable("\"C:\\Program Files\\Example\\uninstall.exe\" /quiet") == @"C:\Program Files\Example\uninstall.exe", "quoted uninstall executable parsed without execution");
        Check(ScanPolicy.CommandExecutable(@"C:\Program Files\Example\uninstall.exe /quiet") == @"C:\Program Files\Example\uninstall.exe", "legacy unquoted uninstall executable parsed");
        Check(ScanPolicy.CommandExecutable("\"unterminated.exe") == null, "malformed quoted command refused");
        Check(ScanPolicy.CommandExecutable("powershell -command do-something") == null, "shell commands are not treated as executable paths");
        Check(ScanPolicy.IconExecutable("\"C:\\Example\\app.exe\",-1") == @"C:\Example\app.exe", "quoted indexed icon path parsed");
        Check(ScanPolicy.IconExecutable(@"C:\Example\comma,name.exe,3") == @"C:\Example\comma,name.exe", "commas in icon filenames preserved");
        Check(PathSafety.IsPathUnderRoot(@"C:\Menu\Programs\item.lnk", @"C:\Menu"), "strict root child accepted");
        Check(!PathSafety.IsPathUnderRoot(@"C:\MenuOther\item.lnk", @"C:\Menu"), "similarly named root sibling rejected");
        Check(!PathSafety.IsPathUnderRoot(@"C:\Menu\..\Other\item.lnk", @"C:\Menu"), "path traversal outside root rejected");
        Check(!PathSafety.IsPathUnderRoot(@"C:\Menu", @"C:\Menu"), "root itself is not an eligible child");
        byte[] header = new byte[76];
        Buffer.BlockCopy(BitConverter.GetBytes(76), 0, header, 0, 4);
        Buffer.BlockCopy(new Guid("00021401-0000-0000-C000-000000000046").ToByteArray(), 0, header, 4, 16);
        Check(ScanPolicy.IsOrdinaryShortcutHeader(header), "ordinary shell link header recognized");
        Buffer.BlockCopy(BitConverter.GetBytes(0x1000), 0, header, 20, 4);
        Check(!ScanPolicy.IsOrdinaryShortcutHeader(header), "Windows Installer advertised link is excluded");
        Check(!ScanPolicy.IsOrdinaryShortcutHeader(new byte[20]), "truncated link is excluded");
        Check(!ScanPolicy.IsOrdinaryShortcutHeader(new byte[76]), "unrecognized link header is excluded");
        RepairEngine.ValidateBackupIdentity("1", "S-1-test-current", "S-1-test-current");
        Reject(delegate { RepairEngine.ValidateBackupIdentity("1", "S-1-other", "S-1-test-current"); }, "cross-account backup refused");
        Reject(delegate { RepairEngine.ValidateBackupIdentity("future-format", "S-1-test-current", "S-1-test-current"); }, "unknown backup schema refused");
        Reject(delegate { RepairEngine.ValidateBackupIdentity("1", null, "S-1-test-current"); }, "backup identity required");
        RegistrySnapshot snapshot = Sample();
        RepairEngine.ValidateBackupSnapshot(snapshot, FindingKind.ThumbnailOverride, RegistryTools.Fingerprint(snapshot));
        Reject(delegate { RepairEngine.ValidateBackupSnapshot(Sample(), FindingKind.ThumbnailOverride, new string('0', 64)); }, "corrupted registry payload refused");
        Reject(delegate { RegistrySnapshot value = Sample(); value.Values[3].BinaryBase64 = "invalid-base64"; RegistryTools.ValidateSnapshot(value, FindingKind.ThumbnailOverride); }, "malformed backup binary refused");
        Reject(delegate { RegistrySnapshot value = Sample(); value.Values[0].Name = "bad\0name"; RegistryTools.ValidateSnapshot(value, FindingKind.ThumbnailOverride); }, "embedded NUL registry value name refused");
        string packaged = "@{Example.Package_1.0.0.0_x64__abcdefghijklm?ms-resource://Example.Package/Files/icon.png}";
        Check(ImageAssociationScanner.IsPackagedResourceReference(packaged), "safe package icon resource recognized");
        Check(ImageAssociationScanner.ClassifyIconReference(packaged) == "PackageResource", "package resource is not mistaken for a file path");
        Check(ImageAssociationScanner.ClassifyIconReference("%1") == "PerFile", "per-file icon declaration is informational");
        Check(ImageAssociationScanner.ClassifyIconReference(null) == "NotDeclared", "missing static icon can use a dynamic handler");
        foreach (string invalid in new[] { "@{\\\\server\\share\\resources.pri?ms-resource://Example/Files/icon.png}", "@{C:\\resources.pri?ms-resource://Example/Files/icon.png}", "@{https://example.com/resource?ms-resource://Example/Files/icon.png}", packaged.Replace("ms-resource://", "https://"), packaged.Replace("icon.png", "icon.png}extra"), packaged.Replace("Files/icon.png", ""), packaged.Replace("Files/icon.png", "Files/icon.png?extra") })
            Check(!ImageAssociationScanner.IsPackagedResourceReference(invalid), "unsafe indirect resource never reaches native loader: " + invalid);
    }
    private static void UiSmoke(string artifactDirectory)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (MainForm form = new MainForm())
        {
            Check(form.Text.Contains(AppInfo.Version), "preview version visible");
            List<Control> controls = AllControls(form).ToList();
            Button repair = controls.OfType<Button>().Single(b => b.Name == "RepairSelected");
            Button export = controls.OfType<Button>().Single(b => b.Name == "ExportReport");
            Check(controls.OfType<Label>().Any(label => label.Text == "Made by Aggelos Yiannakou \u00b7 2026"), "creator credit visible in application");
            Check(!repair.Enabled, "no repair enabled before scan");
            Check(!export.Enabled, "no report export before scan");
            Check(controls.OfType<DataGridView>().All(g => g.Rows.Count == 0), "startup does not auto-scan or auto-select repairs");
            Check(controls.OfType<Button>().Any(b => b.Name == "RebuildIconCache"), "cache rebuilding is a separate action");
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            ScanReport demo = new ScanReport();
            Finding broken = new Finding { Id = "demo-only", Title = "Example: orphaned .png thumbnail override", CanFix = true, Kind = FindingKind.ThumbnailOverride, Summary = "The selected thumbnail provider is absent; the native fallback is available.", Path = @"HKCU\Software\Classes\.png\shellex\{E357FCCD-A995-4576-B01F-234630154E96}" };
            broken.Evidence.Add("Illustrative test fixture. No live registry entries were changed.");
            broken.Evidence.Add("Actual repairs recheck the finding and save a backup before removal.");
            demo.Findings.Add(broken);
            demo.Findings.Add(new Finding { Id = "review-demo-only", Title = "Example: missing component uninstaller", CanFix = false, Kind = FindingKind.ReviewOnly, Summary = "The installation folder remains present. Repair through the vendor installer.", Path = "Example component" });
            demo.ImageIcons.Add(new ImageIconDiagnostic { Extension = ".png", ProgId = "Example image application", Status = "Resource present", DefaultIcon = "Illustrative resource", ResolvedIcon = "Example image-file icon" });
            form.DisplayReport(demo);
            List<DataGridView> grids = controls.OfType<DataGridView>().ToList();
            DataGridView repairGrid = grids.Single(g => g.Columns.Contains("Apply"));
            DataGridView reviewGrid = grids.Single(g => !g.Columns.Contains("Apply") && g.Columns.Contains("Finding"));
            DataGridView imageGrid = grids.Single(g => g.Columns.Contains("Extension"));
            Check(repairGrid.Rows.Count == 1 && reviewGrid.Rows.Count == 1, "review-only findings kept out of selectable grid");
            Check(!Convert.ToBoolean(repairGrid.Rows[0].Cells["Apply"].Value), "repair checkboxes default off after scan");
            Check(!repair.Enabled, "rendering findings never enables unselected repairs");
            Check(imageGrid.Rows.Count == 1 && imageGrid.ReadOnly, "image association diagnostics stay read-only");
            Check(form.ActiveSection == 0, "findings section initially active");
            Check(export.Enabled, "report export enabled after scan");
            repairGrid.Rows[0].Cells["Apply"].Value = true;
            Check(repair.Enabled, "explicit selection enables repair in repair tab");
            form.SelectSection(1);
            Check(!repair.Enabled, "review-only tab disables repair action");
            Check(Convert.ToBoolean(repairGrid.Rows[0].Cells["Apply"].Value), "section changes preserve explicit repair selection");
            form.SelectSection(2);
            Check(!repair.Enabled, "image diagnostics tab disables repair action");
            form.SelectSection(0);
            Check(repair.Enabled, "returning to findings preserves selected repair eligibility");
            repairGrid.Rows[0].Cells["Apply"].Value = false;
            System.Reflection.MethodInfo setBusy = typeof(MainForm).GetMethod("SetBusy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Check(setBusy != null, "busy-state guard retained");
            setBusy.Invoke(form, new object[] { true, "Test operation in progress" });
            foreach (string name in new[] { "ScanProfile", "RepairSelected", "ExportReport", "RestoreBackup", "RebuildIconCache" })
                Check(!controls.OfType<Button>().Single(b => b.Name == name).Enabled, "busy blocks action: " + name);
            Check(grids.All(g => !g.Enabled), "busy blocks grid edits");
            setBusy.Invoke(form, new object[] { false, "Illustrative preview. No profile changes made." });
            Check(export.Enabled, "report export returns after busy state");
            Application.DoEvents();
            form.PerformLayout();
            using (Bitmap preview = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(preview, new Rectangle(0, 0, preview.Width, preview.Height));
                preview.Save(Path.Combine(artifactDirectory, "ui-preview.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            form.SelectSection(1);
            Application.DoEvents();
            using (Bitmap preview = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(preview, new Rectangle(0, 0, preview.Width, preview.Height));
                preview.Save(Path.Combine(artifactDirectory, "ui-review.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            form.SelectSection(2);
            Application.DoEvents();
            using (Bitmap preview = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(preview, new Rectangle(0, 0, preview.Width, preview.Height));
                preview.Save(Path.Combine(artifactDirectory, "ui-image-icons.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            form.SelectSection(0);
            form.Size = form.MinimumSize;
            Application.DoEvents();
            using (Bitmap preview = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(preview, new Rectangle(0, 0, preview.Width, preview.Height));
                preview.Save(Path.Combine(artifactDirectory, "ui-minimum-size.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Check(form.ClientRectangle.Width > 0 && repairGrid.DisplayedColumnCount(false) >= 2, "minimum-size findings table remains visible");
            System.Reflection.MethodInfo clearScan = typeof(MainForm).GetMethod("ClearScan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Check(clearScan != null, "post-repair scan invalidation retained");
            clearScan.Invoke(form, new object[] { "Scan again after repair." });
            Check(grids.All(g => g.Rows.Count == 0), "clearing scan removes findings and selections");
            Check(!repair.Enabled && !export.Enabled, "cleared scan cannot repair or export stale evidence");
            form.Hide();
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string artifacts = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifacts);
            PolicyTests();
            BackupTests();
            DetectionTests(artifacts);
            assertions += EnvironmentDiagnosticTests.Run(artifacts);
            assertions += ShellDiagnosticTests.Run(artifacts);
            assertions += HistoryTests.Run(artifacts);
            UiSmoke(artifacts);
            assertions += WorkspaceUiTests.Run(artifacts);
            Console.WriteLine("PASS: " + assertions + " assertions. No profile repairs, registry writes, or Explorer restarts were executed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
