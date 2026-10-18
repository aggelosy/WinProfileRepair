using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace WinProfileRepair
{
    internal static class WorkspaceUiTests
    {
        internal static int Run(string artifacts)
        {
            int assertions = 0;
            Action<bool, string> check = delegate(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException("Workspace UI: " + description);
                assertions++;
            };
            using (var form = new MainForm())
            {
                form.DisplayHistory(new List<RepairHistoryItem>());
                List<Control> controls = AllControls(form).ToList();
                check(form.ActivePage == "Overview", "Overview is the initial page");
                check(controls.OfType<DataGridView>().All(grid => grid.Rows.Count == 0), "opening the workspace does not scan");
                foreach (string name in new[] { "OverviewNav", "DiagnosticsNav", "HistoryNav", "BackupsNav", "SettingsNav" })
                    check(controls.OfType<Button>().Any(button => button.Name == name), "navigation exists: " + name);
                Button repair = controls.OfType<Button>().Single(button => button.Name == "RepairSelected");
                Button cancel = controls.OfType<Button>().Single(button => button.Name == "CancelScan");
                check(!repair.Enabled, "no repair is enabled on startup");
                check(!cancel.Visible, "cancellation is not exposed when no scan is running");
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Show();
                Capture(form, artifacts, "workspace-overview.png");

                var report = new ScanReport();
                report.Modules.Add(new DiagnosticModuleResult { Id = "shell", Title = "Shell registrations", Category = "Shell", Status = "Complete", Summary = "One illustrative repair candidate; no live changes made." });
                report.Modules.Add(new DiagnosticModuleResult { Id = "startup", Title = "Startup & environment", Category = "Startup", Status = "Complete", Summary = "One illustrative item for manual investigation." });
                var candidate = new Finding
                {
                    Id = "workspace-test-only", Title = "Example: orphaned PNG thumbnail override", CanFix = true,
                    Kind = FindingKind.ThumbnailOverride, Summary = "The selected provider is absent and a native fallback is available.",
                    Path = "Illustrative registry path", Category = "Shell", RuleId = "SHELL.TEST", Classification = "Confirmed repair candidate",
                    ProposedChange = "Remove this illustrative stale override only after review.",
                    BackupPlan = "Back up the original registry value before changing it.",
                    VerificationPlan = "Scan again and visually check the affected icon."
                };
                candidate.Evidence.Add("Synthetic fixture. No registry or application data was changed.");
                report.Findings.Add(candidate);
                report.Findings.Add(new Finding { Id = "workspace-review", Title = "Example: missing startup target", CanFix = false, Kind = FindingKind.ReviewOnly, Summary = "Inspect the owning application before changing this entry.", Classification = "Investigation required", Category = "Startup", RuleId = "STARTUP.TEST", Path = "Illustrative startup entry" });
                report.Findings.Add(new Finding { Id = "workspace-information", Title = "Example: dynamic shell provider", CanFix = false, Kind = FindingKind.ReviewOnly, Summary = "The provider uses a valid dynamic registration. This is information, not an established fault.", Classification = "Information", Category = "Shell", RuleId = "SHELL.INFO.TEST", Path = "Illustrative provider" });
                report.Findings.Add(new Finding { Id = "workspace-incomplete", Title = "Example: target could not be inspected", CanFix = false, Kind = FindingKind.ReviewOnly, Summary = "Access was unavailable; no conclusion about health can be made.", Classification = "Unable to inspect", Category = "Startup", RuleId = "STARTUP.ACCESS.TEST", Path = "Illustrative protected location" });
                report.Findings.Add(new Finding { Id = "workspace-legacy", Title = "Example: earlier review item", CanFix = false, Kind = FindingKind.ReviewOnly, Summary = "Earlier reports may omit classification.", Path = "Illustrative earlier entry" });
                report.ImageIcons.Add(new ImageIconDiagnostic { Extension = ".png", Status = "Resource present", ProgId = "Example image viewer", DefaultIcon = "Illustrative resource", ResolvedIcon = "Example image icon" });
                form.DisplayReport(report);
                check(form.ActivePage == "Diagnostics" && form.ActiveSection == 0, "completed reports open Findings in Diagnostics");
                check(!repair.Enabled, "rendering a new report never selects repairs");
                DataGridView observations = controls.OfType<DataGridView>().Single(grid => !grid.Columns.Contains("Apply") && grid.Columns.Contains("Finding"));
                check(observations.Columns.Contains("Classification") && observations.Columns["Classification"].ReadOnly, "read-only observations show classification directly in the list");
                check(observations.Rows.Count == 4, "classification rendering preserves all read-only observations");
                DataGridViewRow information = observations.Rows.Cast<DataGridViewRow>().Single(row => ((Finding)row.Tag).Id == "workspace-information");
                check(Convert.ToString(information.Cells["Classification"].Value) == "Information" && !observations.Columns.Contains("Apply"), "an informational observation is visibly classified and cannot be selected for repair");
                DataGridViewRow incomplete = observations.Rows.Cast<DataGridViewRow>().Single(row => ((Finding)row.Tag).Id == "workspace-incomplete");
                check(Convert.ToString(incomplete.Cells["Classification"].Value) == "Unable to inspect", "incomplete inspection is distinguished from a detected fault");
                DataGridViewRow legacy = observations.Rows.Cast<DataGridViewRow>().Single(row => ((Finding)row.Tag).Id == "workspace-legacy");
                check(Convert.ToString(legacy.Cells["Classification"].Value) == "Needs review", "older observations receive a neutral classification fallback");
                ListView modules = controls.OfType<ListView>().Single(list => list.Name == "ModuleCoverage");
                check(modules.Items.Count == 2, "overview coverage uses actual report modules");
                TextBox evidence = controls.OfType<TextBox>().Single(box => box.AccessibleName == "Selected finding evidence and proposed action");
                foreach (string heading in new[] { "EVIDENCE", "PROPOSED CHANGE", "BACKUP / UNDO", "VERIFICATION" })
                    check(evidence.Text.Contains(heading), "inspector presents " + heading);
                Capture(form, artifacts, "workspace-diagnostics.png");
                form.SelectSection(1);
                check(!repair.Enabled, "read-only observations never expose an enabled repair action");
                Capture(form, artifacts, "workspace-review-information.png");
                form.SelectSection(0);
                var repairGrid = controls.OfType<DataGridView>().Single(grid => grid.Columns.Contains("Apply"));
                repairGrid.Rows[0].Cells["Apply"].Value = true;
                check(repair.Enabled, "explicit selection enables a diagnostic repair");
                form.SelectPage("Overview");
                check(!repair.Enabled, "a hidden diagnostic selection cannot enable a repair on another page");
                form.SelectSection(0);
                check(repair.Enabled, "returning to the finding preserves explicit selection");
                repairGrid.Rows[0].Cells["Apply"].Value = false;

                var entries = new List<RepairHistoryItem>();
                entries.Add(new RepairHistoryItem
                {
                    Id = "history-test-only", CreatedUtc = "2026-10-18T09:00:00Z", Operation = "Selected repairs", Status = "Applied",
                    Summary = "Illustrative repair operation. No profile changes were made.",
                    BackupDirectory = @"C:\Example\WinProfileRepair\Backups\example-backup",
                    VerificationState = "No longer detected; visual check needed", Details = new List<string> { "Synthetic history entry for UI verification." }
                });
                form.SelectPage("Repair history");
                form.DisplayHistory(entries);
                ListView history = controls.OfType<ListView>().Single(list => list.Name == "RepairHistoryList");
                check(history.Items.Count == 1, "repair history renders supplied metadata");
                history.Items[0].Selected = true;
                Capture(form, artifacts, "workspace-history.png");
                form.SelectPage("Backups");
                form.DisplayHistory(entries);
                ListView backups = controls.OfType<ListView>().Single(list => list.Name == "BackupList");
                check(backups.Items.Count == 1, "backup rows connect to recorded operations");
                Button restore = controls.OfType<Button>().Single(button => button.Name == "RestoreSelectedBackup");
                check(!restore.Enabled, "backup restore requires explicit row selection");
                backups.Items[0].Selected = true;
                Application.DoEvents();
                check(restore.Enabled, "selecting a backup enables the validated restore action");
                Capture(form, artifacts, "workspace-backups.png");

                form.SelectPage("Settings");
                CheckBox technical = controls.OfType<CheckBox>().Single(box => box.Name == "ShowTechnicalEvidence");
                NumericUpDown textSize = controls.OfType<NumericUpDown>().Single(input => input.Name == "EvidenceTextSize");
                technical.Checked = false;
                form.SelectSection(0);
                check(!evidence.Text.Contains("SHELL.TEST") && evidence.Text.Contains("PROPOSED CHANGE"), "concise evidence keeps the repair plan while hiding technical identifiers");
                form.SelectPage("Settings");
                textSize.Value = 11;
                check(Math.Abs(evidence.Font.Size - 11F) < 0.1F, "text-size preference changes evidence font");
                Capture(form, artifacts, "workspace-settings.png");
                MethodInfo busy = typeof(MainForm).GetMethod("SetBusy", BindingFlags.NonPublic | BindingFlags.Instance);
                busy.Invoke(form, new object[] { true, "Synthetic busy state" });
                check(!restore.Enabled && !textSize.Enabled && !technical.Enabled, "busy state also guards new workspace controls");
                busy.Invoke(form, new object[] { false, "Synthetic UI test complete" });
                form.Hide();
            }
            return assertions;
        }

        private static IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in AllControls(child)) yield return descendant;
            }
        }

        private static void Capture(MainForm form, string artifacts, string name)
        {
            Application.DoEvents();
            form.PerformLayout();
            using (var image = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                image.Save(Path.Combine(artifacts, name), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
