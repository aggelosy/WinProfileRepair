using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WinProfileRepair
{
    public sealed class MainForm : Form
    {
        private readonly ThemeButton scanButton = new ThemeButton();
        private readonly ThemeButton repairButton = new ThemeButton();
        private readonly ThemeButton exportButton = new ThemeButton();
        private readonly NavButton restoreButton = new NavButton();
        private readonly NavButton cacheButton = new NavButton();
        private readonly Label statusLabel = new Label();
        private readonly Label stateLabel = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly ThemeButton cancelScanButton = new ThemeButton();
        private readonly ThemeButton restoreSelectedButton = new ThemeButton();
        private readonly ThemeButton verifyButton = new ThemeButton();
        private readonly NavButton overviewNav = new NavButton();
        private readonly NavButton diagnosticsNav = new NavButton();
        private readonly NavButton historyNav = new NavButton();
        private readonly NavButton backupsNav = new NavButton();
        private readonly NavButton settingsNav = new NavButton();
        private readonly Panel pageHost = new Panel();
        private readonly Label pageTitle = new Label();
        private readonly Label pageDescription = new Label();
        private readonly Label breadcrumb = new Label();
        private readonly Label overviewSummary = new Label();
        private readonly Label historySummary = new Label();
        private readonly Label backupsSummary = new Label();
        private readonly ListView moduleList = new ListView();
        private readonly ListView historyList = new ListView();
        private readonly ListView backupList = new ListView();
        private readonly TextBox historyDetails = new TextBox();
        private readonly TextBox backupDetails = new TextBox();
        private readonly CheckBox technicalDetails = new CheckBox();
        private readonly NumericUpDown detailTextSize = new NumericUpDown();
        private readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        private CancellationTokenSource scanCancellation;
        private string activePage = "Overview";
        private List<RepairHistoryItem> history = new List<RepairHistoryItem>();
        private readonly DataGridView findingsGrid;
        private readonly DataGridView reviewGrid;
        private readonly DataGridView imageGrid;
        private readonly NavButton findingsNav = new NavButton();
        private readonly NavButton reviewNav = new NavButton();
        private readonly NavButton imageNav = new NavButton();
        private readonly StatCard findingsStat = new StatCard();
        private readonly StatCard reviewStat = new StatCard();
        private readonly StatCard imageStat = new StatCard();
        private readonly Label sectionTitle = new Label();
        private readonly Label sectionDescription = new Label();
        private readonly Label inspectorTitle = new Label();
        private readonly Label inspectorType = new Label();
        private readonly Panel gridHost = new Panel();
        private readonly Panel emptyState = new Panel();
        private readonly Label emptyTitle = new Label();
        private readonly Label emptyDescription = new Label();
        private readonly TextBox detailsBox = new TextBox();
        private readonly TextBox logBox = new TextBox();
        private ScanReport report;
        private bool busy;
        private int activeSection;

        internal int ActiveSection { get { return activeSection; } }
        internal string ActivePage { get { return activePage; } }

        public MainForm()
        {
            Text = AppInfo.Name + " " + AppInfo.Version;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 720);
            ClientSize = new Size(1240, 840);
            Font = new Font("Segoe UI", 9F);
            BackColor = UiTheme.Canvas;
            ForeColor = UiTheme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi;

            findingsGrid = CreateGrid(true);
            reviewGrid = CreateGrid(false);
            imageGrid = CreateImageGrid();
            scanButton.Name = "ScanProfile";
            repairButton.Name = "RepairSelected";
            exportButton.Name = "ExportReport";
            restoreButton.Name = "RestoreBackup";
            cacheButton.Name = "RebuildIconCache";
            findingsNav.Name = "FindingsNav";
            reviewNav.Name = "ReviewNav";
            imageNav.Name = "ImageIconsNav";
            cancelScanButton.Name = "CancelScan";
            overviewNav.Name = "OverviewNav";
            diagnosticsNav.Name = "DiagnosticsNav";
            historyNav.Name = "HistoryNav";
            backupsNav.Name = "BackupsNav";
            settingsNav.Name = "SettingsNav";

            var shell = new TableLayoutPanel();
            shell.Dock = DockStyle.Fill;
            shell.Margin = Padding.Empty;
            shell.Padding = Padding.Empty;
            shell.ColumnCount = 2;
            shell.RowCount = 1;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            shell.Controls.Add(BuildSidebar(), 0, 0);
            shell.Controls.Add(BuildWorkspace(), 1, 0);

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy)
                {
                    e.Cancel = true;
                    MessageBox.Show(this, "Please wait for the current operation to finish before closing the application.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            statusLabel.Text = "Run a read-only scan to inspect this Windows profile.";
            SelectSection(0);
            RefreshHistory();
            SelectPage("Overview");
            UpdateButtons();
        }

        private Control BuildSidebar()
        {
            var sidebar = new Panel();
            sidebar.Dock = DockStyle.Fill;
            sidebar.Margin = Padding.Empty;
            sidebar.BackColor = UiTheme.Sidebar;
            sidebar.Padding = new Padding(14, 24, 14, 16);

            var sidebarLayout = new TableLayoutPanel();
            sidebarLayout.Dock = DockStyle.Fill;
            sidebarLayout.Margin = Padding.Empty;
            sidebarLayout.ColumnCount = 1;
            sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sidebarLayout.RowCount = 10;
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            sidebar.Controls.Add(sidebarLayout);

            var brand = new Panel();
            brand.Dock = DockStyle.Fill;
            brand.Margin = new Padding(6, 0, 0, 0);
            var mark = new Label();
            mark.Text = "W";
            mark.Font = new Font("Segoe UI Semibold", 20F);
            mark.ForeColor = Color.White;
            mark.BackColor = Color.FromArgb(57, 61, 68);
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.Size = new Size(42, 42);
            mark.Location = new Point(0, 0);
            brand.Controls.Add(mark);
            var name = new Label();
            name.Text = "WinProfile\r\nRepair";
            name.Font = new Font("Segoe UI Semibold", 10F);
            name.ForeColor = Color.White;
            name.AutoSize = false;
            name.Size = new Size(104, 46);
            name.Location = new Point(52, 0);
            brand.Controls.Add(name);
            sidebarLayout.Controls.Add(brand, 0, 0);

            sidebarLayout.Controls.Add(SidebarCaption("WORKSPACE"), 0, 1);
            ConfigureNav(overviewNav, "Overview", "overview", delegate { SelectPage("Overview"); });
            ConfigureNav(diagnosticsNav, "Diagnostics", "findings", delegate { SelectPage("Diagnostics"); });
            ConfigureNav(historyNav, "Repair history", "history", delegate { SelectPage("Repair history"); });
            ConfigureNav(backupsNav, "Backups", "backups", delegate { SelectPage("Backups"); });
            ConfigureNav(settingsNav, "Settings", "settings", delegate { SelectPage("Settings"); });
            sidebarLayout.Controls.Add(overviewNav, 0, 2);
            sidebarLayout.Controls.Add(diagnosticsNav, 0, 3);
            sidebarLayout.Controls.Add(historyNav, 0, 4);
            sidebarLayout.Controls.Add(backupsNav, 0, 5);
            sidebarLayout.Controls.Add(settingsNav, 0, 6);

            var utilities = new TableLayoutPanel();
            utilities.Dock = DockStyle.Fill;
            utilities.Margin = Padding.Empty;
            utilities.ColumnCount = 1;
            utilities.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            utilities.RowCount = 3;
            utilities.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            utilities.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            utilities.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            utilities.Controls.Add(SidebarCaption("UTILITIES"), 0, 0);
            ConfigureNav(restoreButton, "Restore backup", "restore", RestoreClick);
            ConfigureNav(cacheButton, "Rebuild icon cache", "cache", CacheClick);
            utilities.Controls.Add(restoreButton, 0, 1);
            utilities.Controls.Add(cacheButton, 0, 2);
            sidebarLayout.Controls.Add(utilities, 0, 8);

            var footer = new Label();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(9, 9, 0, 0);
            footer.Text = "\u25cf  Local processing only\r\n     " + AppInfo.Version;
            footer.Font = new Font("Segoe UI", 8F);
            footer.ForeColor = Color.FromArgb(154, 160, 169);
            sidebarLayout.Controls.Add(footer, 0, 9);
            return sidebar;
        }

        private static Label SidebarCaption(string text)
        {
            var label = new Label();
            label.Dock = DockStyle.Fill;
            label.Text = text;
            label.Margin = new Padding(10, 3, 0, 0);
            label.Font = new Font("Segoe UI Semibold", 7.5F);
            label.ForeColor = Color.FromArgb(139, 147, 157);
            return label;
        }

        private static void ConfigureNav(NavButton button, string text, string glyph, EventHandler handler)
        {
            button.Text = text;
            button.Glyph = glyph;
            button.BadgeText = String.Empty;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 1, 0, 3);
            button.Font = new Font("Segoe UI", 8.75F);
            button.AccessibleName = text;
            button.Click += handler;
        }

        private Control BuildWorkspace()
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.Padding = new Padding(26, 20, 26, 10);
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.Controls.Add(BuildHeader(), 0, 0);
            pageHost.Dock = DockStyle.Fill;
            pageHost.Margin = Padding.Empty;
            AddPage("Overview", BuildOverview());
            AddPage("Diagnostics", BuildFindingsCard());
            AddPage("Repair history", BuildHistoryPage(false));
            AddPage("Backups", BuildHistoryPage(true));
            AddPage("Settings", BuildSettings());
            layout.Controls.Add(pageHost, 0, 1);
            layout.Controls.Add(BuildActivity(), 0, 2);
            layout.Controls.Add(BuildStatusBar(), 0, 3);
            layout.SizeChanged += delegate
            {
                bool compact = layout.ClientSize.Height < 740;
                layout.RowStyles[0].Height = compact ? 112 : 120;
                layout.RowStyles[2].Height = compact ? 88 : 110;
            };
            return layout;
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.ColumnCount = 2;
            header.RowCount = 3;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            breadcrumb.Dock = DockStyle.Fill;
            breadcrumb.Text = "Workspace  /  Overview";
            breadcrumb.Font = new Font("Segoe UI", 8.5F);
            breadcrumb.ForeColor = UiTheme.Muted;
            breadcrumb.Margin = Padding.Empty;
            header.Controls.Add(breadcrumb, 0, 0);
            pageTitle.Name = "WorkspacePageTitle";
            pageTitle.Dock = DockStyle.Fill;
            pageTitle.Font = new Font("Segoe UI Semibold", 24F);
            pageTitle.Margin = new Padding(-2, 0, 0, 0);
            header.Controls.Add(pageTitle, 0, 1);
            pageDescription.Dock = DockStyle.Fill;
            pageDescription.Font = new Font("Segoe UI", 8.5F);
            pageDescription.ForeColor = UiTheme.Muted;
            pageDescription.Margin = new Padding(0, 3, 0, 0);
            header.Controls.Add(pageDescription, 0, 2);
            header.SetColumnSpan(pageDescription, 2);
            ConfigureButton(scanButton, "Scan profile", ScanClick);
            scanButton.Primary = true;
            scanButton.Size = new Size(130, 38);
            scanButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            scanButton.Margin = new Padding(0, 5, 0, 0);
            var scanActions = new FlowLayoutPanel();
            scanActions.Dock = DockStyle.Fill;
            scanActions.Margin = Padding.Empty;
            scanActions.FlowDirection = FlowDirection.RightToLeft;
            scanActions.WrapContents = false;
            scanActions.Controls.Add(scanButton);
            ConfigureButton(cancelScanButton, "Cancel scan", delegate
            {
                if (scanCancellation == null) return;
                scanCancellation.Cancel();
                cancelScanButton.Enabled = false;
                statusLabel.Text = "Stopping after the current diagnostic check...";
            });
            cancelScanButton.Size = new Size(110, 38);
            cancelScanButton.Margin = new Padding(0, 5, 7, 0);
            cancelScanButton.Visible = false;
            scanActions.Controls.Add(cancelScanButton);
            header.Controls.Add(scanActions, 1, 1);
            return header;
        }

        private Control BuildStats()
        {
            var stats = new TableLayoutPanel();
            stats.Dock = DockStyle.Fill;
            stats.Margin = Padding.Empty;
            stats.ColumnCount = 3;
            stats.RowCount = 1;
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
            ConfigureStat(findingsStat, "REPAIRABLE", "Selected repairs only");
            ConfigureStat(reviewStat, "READ-ONLY ITEMS", "Information and items to investigate");
            ConfigureStat(imageStat, "IMAGE ICONS", "File association diagnostics");
            findingsStat.Margin = new Padding(0, 0, 10, 0);
            reviewStat.Margin = new Padding(0, 0, 10, 0);
            imageStat.Margin = Padding.Empty;
            stats.Controls.Add(findingsStat, 0, 0);
            stats.Controls.Add(reviewStat, 1, 0);
            stats.Controls.Add(imageStat, 2, 0);
            return stats;
        }

        private void AddPage(string name, Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Margin = Padding.Empty;
            page.Visible = false;
            pages.Add(name, page);
            pageHost.Controls.Add(page);
        }

        internal void SelectPage(string name)
        {
            if (!pages.ContainsKey(name)) throw new ArgumentOutOfRangeException("name");
            activePage = name;
            foreach (KeyValuePair<string, Control> page in pages) page.Value.Visible = page.Key == name;
            pages[name].BringToFront();
            overviewNav.Selected = name == "Overview";
            diagnosticsNav.Selected = name == "Diagnostics";
            historyNav.Selected = name == "Repair history";
            backupsNav.Selected = name == "Backups";
            settingsNav.Selected = name == "Settings";
            breadcrumb.Text = "Workspace  /  " + name;
            pageTitle.Text = name == "Overview" ? "Your Windows profile" : name;
            pageDescription.Text = name == "Overview" ? "Understand the cause, review the evidence, and choose the next step.\r\nRead-only scanning first. Nothing is selected for repair by default."
                : name == "Diagnostics" ? "Inspect each finding with its evidence, proposed change, backup, and verification plan.\r\nConfirmed repair candidates and manual investigation stay separate."
                : name == "Repair history" ? "Follow recorded operations and their later diagnostic checks.\r\nAn applied repair and a verified result are different states."
                : name == "Backups" ? "Review backups connected to recorded repairs and restore through the validated recovery flow.\r\nCache rebuilds have no restore backup."
                : "Adjust how diagnostic evidence is displayed in this session.\r\nReports and backups stay local until you choose to share them.";
            if (name == "Repair history" || name == "Backups") RefreshHistory();
            UpdateButtons();
        }

        private Control BuildOverview()
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(BuildStats(), 0, 0);
            overviewSummary.Name = "OverviewSummary";
            overviewSummary.Dock = DockStyle.Fill;
            overviewSummary.Margin = new Padding(0, 15, 0, 0);
            overviewSummary.Text = "Ready when you are. Run a scan to check the diagnostic areas below.";
            overviewSummary.ForeColor = UiTheme.Muted;
            layout.Controls.Add(overviewSummary, 0, 1);

            var card = new SurfacePanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(18, 14, 18, 12);
            card.Margin = Padding.Empty;
            var content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Margin = Padding.Empty;
            content.ColumnCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowCount = 3;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            content.Controls.Add(BodyLabel("Diagnostic coverage", 12F, true), 0, 0);
            ConfigureList(moduleList, "Diagnostic module coverage");
            moduleList.Name = "ModuleCoverage";
            moduleList.Columns.Add("Area", 190);
            moduleList.Columns.Add("Status", 105);
            moduleList.Columns.Add("Scope / latest result", 440);
            moduleList.FullRowSelect = true;
            moduleList.DoubleClick += delegate { SelectPage("Diagnostics"); };
            moduleList.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { SelectPage("Diagnostics"); e.Handled = true; }
            };
            AddModuleRow("Shell registrations", "Not scanned", "Thumbnail, preview, and context-menu handler registrations.");
            AddModuleRow("Installation records", "Not scanned", "Evidence-based leftovers and broken Start-menu shortcuts.");
            AddModuleRow("Image file icons", "Not scanned", "Read-only file association and icon-resource diagnostics.");
            AddModuleRow("Startup & environment", "Not scanned", "User startup entries, PATH, and temporary-folder configuration.");
            content.Controls.Add(moduleList, 0, 1);
            var note = BodyLabel("This preview checks specific causes. Search, folder recovery, and Windows-wide repair are planned.", 8F, false);
            note.ForeColor = UiTheme.Muted;
            note.Margin = new Padding(0, 10, 0, 0);
            content.Controls.Add(note, 0, 2);
            card.Controls.Add(content);
            layout.Controls.Add(card, 0, 2);
            return layout;
        }

        private void AddModuleRow(string title, string status, string summary)
        {
            var row = new ListViewItem(title);
            row.SubItems.Add(status);
            row.SubItems.Add(summary);
            moduleList.Items.Add(row);
        }

        private static Label BodyLabel(string text, float size, bool strong)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.Margin = Padding.Empty;
            label.Font = new Font(strong ? "Segoe UI Semibold" : "Segoe UI", size);
            return label;
        }

        private static void ConfigureList(ListView list, string accessibleName)
        {
            list.Dock = DockStyle.Fill;
            list.Margin = Padding.Empty;
            list.View = View.Details;
            list.BorderStyle = BorderStyle.None;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.BackColor = UiTheme.SurfaceWhite;
            list.ForeColor = UiTheme.Ink;
            list.Font = new Font("Segoe UI", 9F);
            list.AccessibleName = accessibleName;
        }

        private Control BuildHistoryPage(bool backups)
        {
            var card = new SurfacePanel();
            card.Dock = DockStyle.Fill;
            card.Margin = Padding.Empty;
            card.Padding = new Padding(18, 15, 18, 15);
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            Label summary = backups ? backupsSummary : historySummary;
            summary.Dock = DockStyle.Fill;
            summary.ForeColor = UiTheme.Muted;
            summary.Margin = new Padding(0, 7, 12, 0);
            layout.Controls.Add(summary, 0, 0);
            ThemeButton action = backups ? restoreSelectedButton : verifyButton;
            action.Name = backups ? "RestoreSelectedBackup" : "VerifyRepairs";
            ConfigureButton(action, backups ? "Restore selected backup" : "Scan to verify repairs", backups ? (EventHandler)RestoreSelectedClick : ScanClick);
            action.Dock = DockStyle.Top;
            action.Height = 34;
            action.Margin = Padding.Empty;
            layout.Controls.Add(action, 1, 0);
            ListView list = backups ? backupList : historyList;
            ConfigureList(list, backups ? "Recorded repair backups" : "Recorded repair history");
            list.Name = backups ? "BackupList" : "RepairHistoryList";
            list.Columns.Add("When", 145);
            list.Columns.Add("Operation", 155);
            list.Columns.Add(backups ? "Backup folder" : "Result", backups ? 340 : 145);
            if (!backups) list.Columns.Add("Verification", 220);
            list.SelectedIndexChanged += delegate { ShowHistoryDetails(backups); };
            layout.Controls.Add(list, 0, 1);
            layout.SetColumnSpan(list, 2);
            var caption = BodyLabel(backups ? "BACKUP DETAILS & RESTORE CONDITIONS" : "OPERATION DETAILS & VERIFICATION", 7.5F, true);
            caption.ForeColor = UiTheme.Muted;
            caption.Margin = new Padding(0, 10, 0, 0);
            layout.Controls.Add(caption, 0, 2);
            layout.SetColumnSpan(caption, 2);
            TextBox details = backups ? backupDetails : historyDetails;
            ConfigureTextBox(details);
            details.Font = new Font("Segoe UI", 9F);
            details.AccessibleName = backups ? "Selected backup details" : "Selected operation details";
            layout.Controls.Add(details, 0, 3);
            layout.SetColumnSpan(details, 2);
            card.Controls.Add(layout);
            return card;
        }

        private Control BuildSettings()
        {
            var card = new SurfacePanel();
            card.Dock = DockStyle.Fill;
            card.Margin = Padding.Empty;
            card.Padding = new Padding(24);
            var content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Margin = Padding.Empty;
            content.ColumnCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowCount = 7;
            foreach (int height in new[] { 34, 40, 28, 40, 32, 98 }) content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(BodyLabel("Evidence display", 13F, true), 0, 0);
            technicalDetails.Name = "ShowTechnicalEvidence";
            technicalDetails.Text = "Show rule IDs, registry paths, and raw evidence";
            technicalDetails.Checked = true;
            technicalDetails.Dock = DockStyle.Fill;
            technicalDetails.Margin = Padding.Empty;
            technicalDetails.CheckedChanged += delegate { RefreshSection(); ShowHistoryDetails(false); ShowHistoryDetails(true); };
            content.Controls.Add(technicalDetails, 0, 1);
            content.Controls.Add(BodyLabel("Evidence and activity text size", 9F, false), 0, 2);
            detailTextSize.Name = "EvidenceTextSize";
            detailTextSize.Minimum = 8;
            detailTextSize.Maximum = 14;
            detailTextSize.Value = 9;
            detailTextSize.Width = 90;
            detailTextSize.Margin = new Padding(0, 2, 0, 10);
            detailTextSize.ValueChanged += delegate
            {
                float size = Convert.ToSingle(detailTextSize.Value);
                detailsBox.Font = new Font("Segoe UI", size);
                historyDetails.Font = new Font("Segoe UI", size);
                backupDetails.Font = new Font("Segoe UI", size);
                logBox.Font = new Font("Consolas", size);
            };
            content.Controls.Add(detailTextSize, 0, 3);
            var sessionNote = BodyLabel("These display preferences apply to this session only.", 8.5F, false);
            sessionNote.ForeColor = UiTheme.Muted;
            content.Controls.Add(sessionNote, 0, 4);
            var privacy = BodyLabel("LOCAL BY DEFAULT\r\n\r\nRun as your normal Windows user. Scans are read-only; every repair needs your explicit selection. Exported reports may contain user names, paths, and installed application details. Review a report before sharing it.", 9F, false);
            privacy.ForeColor = UiTheme.Muted;
            content.Controls.Add(privacy, 0, 5);
            card.Controls.Add(content);
            return card;
        }

        private static void ConfigureStat(StatCard card, string caption, string footnote)
        {
            card.Dock = DockStyle.Fill;
            card.Caption = caption;
            card.Value = "\u2014";
            card.Footnote = footnote;
            card.ValueColor = UiTheme.Ink;
            card.AccessibleName = caption;
        }

        private Control BuildFindingsCard()
        {
            var card = new SurfacePanel();
            card.Dock = DockStyle.Fill;
            card.Margin = Padding.Empty;
            card.ShowBorder = true;
            card.Radius = 7;
            card.BackColor = UiTheme.SurfaceWhite;
            card.Padding = new Padding(1);
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(layout);

            var filters = new TableLayoutPanel();
            filters.Dock = DockStyle.Fill;
            filters.Margin = new Padding(12, 5, 12, 0);
            filters.ColumnCount = 3;
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            ConfigureNav(findingsNav, "Findings", "findings", delegate { SelectSection(0); });
            ConfigureNav(reviewNav, "Review and info", "review", delegate { SelectSection(1); });
            ConfigureNav(imageNav, "Image file icons", "image", delegate { SelectSection(2); });
            findingsNav.Light = reviewNav.Light = imageNav.Light = true;
            filters.Controls.Add(findingsNav, 0, 0);
            filters.Controls.Add(reviewNav, 1, 0);
            filters.Controls.Add(imageNav, 2, 0);
            layout.Controls.Add(filters, 0, 0);

            var header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.Padding = new Padding(18, 14, 16, 10);
            header.ColumnCount = 3;
            header.RowCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sectionTitle.Dock = DockStyle.Fill;
            sectionTitle.Font = new Font("Segoe UI Semibold", 12F);
            sectionTitle.Margin = Padding.Empty;
            header.Controls.Add(sectionTitle, 0, 0);
            sectionDescription.Dock = DockStyle.Fill;
            sectionDescription.Font = new Font("Segoe UI", 8F);
            sectionDescription.ForeColor = UiTheme.Muted;
            sectionDescription.Margin = Padding.Empty;
            sectionDescription.AutoEllipsis = true;
            header.Controls.Add(sectionDescription, 0, 1);
            ConfigureButton(exportButton, "Export report", ExportClick);
            exportButton.Size = new Size(121, 34);
            exportButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            exportButton.Margin = new Padding(0, 5, 7, 0);
            header.Controls.Add(exportButton, 1, 0);
            header.SetRowSpan(exportButton, 2);
            ConfigureButton(repairButton, "Repair selected", RepairClick);
            repairButton.Primary = true;
            repairButton.Size = new Size(132, 34);
            repairButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            repairButton.Margin = new Padding(0, 5, 0, 0);
            header.Controls.Add(repairButton, 2, 0);
            header.SetRowSpan(repairButton, 2);
            layout.Controls.Add(header, 0, 1);

            var split = new SplitContainer();
            split.Size = new Size(900, 400);
            split.Dock = DockStyle.Fill;
            split.Margin = Padding.Empty;
            split.BackColor = UiTheme.Border;
            split.FixedPanel = FixedPanel.Panel2;
            split.SplitterWidth = 1;
            split.Panel1MinSize = 240;
            split.Panel2MinSize = 250;
            split.SplitterDistance = 560;
            split.Panel1.BackColor = UiTheme.SurfaceWhite;
            split.Panel2.BackColor = UiTheme.SurfaceWhite;
            split.Resize += delegate
            {
                if (split.Width > split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth)
                    split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(split.Width - split.Panel2MinSize - split.SplitterWidth, (int)(split.Width * 0.62)));
            };
            gridHost.Dock = DockStyle.Fill;
            gridHost.Margin = Padding.Empty;
            gridHost.BackColor = UiTheme.SurfaceWhite;
            gridHost.Controls.Add(findingsGrid);
            gridHost.Controls.Add(reviewGrid);
            gridHost.Controls.Add(imageGrid);
            gridHost.Controls.Add(BuildEmptyState());
            split.Panel1.Controls.Add(gridHost);
            split.Panel2.Controls.Add(BuildInspector());
            layout.Controls.Add(split, 0, 2);
            return card;
        }

        private Control BuildEmptyState()
        {
            emptyState.Dock = DockStyle.Fill;
            emptyState.BackColor = UiTheme.SurfaceWhite;
            emptyState.Padding = new Padding(26);
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 4;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            emptyTitle.Dock = DockStyle.Fill;
            emptyTitle.TextAlign = ContentAlignment.MiddleCenter;
            emptyTitle.Font = new Font("Segoe UI Semibold", 12F);
            emptyTitle.Margin = Padding.Empty;
            emptyDescription.Dock = DockStyle.Fill;
            emptyDescription.TextAlign = ContentAlignment.TopCenter;
            emptyDescription.Font = new Font("Segoe UI", 9F);
            emptyDescription.ForeColor = UiTheme.Muted;
            emptyDescription.Margin = new Padding(0, 5, 0, 0);
            layout.Controls.Add(emptyTitle, 0, 1);
            layout.Controls.Add(emptyDescription, 0, 2);
            emptyState.Controls.Add(layout);
            return emptyState;
        }

        private Control BuildInspector()
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(18, 15, 18, 14);
            layout.Margin = Padding.Empty;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 15));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var eyebrow = new Label();
            eyebrow.Dock = DockStyle.Fill;
            eyebrow.Text = "INSPECTOR";
            eyebrow.Font = new Font("Segoe UI Semibold", 7.5F);
            eyebrow.ForeColor = UiTheme.Muted;
            eyebrow.Margin = Padding.Empty;
            layout.Controls.Add(eyebrow, 0, 0);
            inspectorTitle.Dock = DockStyle.Fill;
            inspectorTitle.Font = new Font("Segoe UI Semibold", 13F);
            inspectorTitle.Margin = Padding.Empty;
            inspectorTitle.AutoEllipsis = true;
            layout.Controls.Add(inspectorTitle, 0, 1);
            inspectorType.Dock = DockStyle.Fill;
            inspectorType.Font = new Font("Segoe UI", 8F);
            inspectorType.ForeColor = UiTheme.Muted;
            inspectorType.Margin = Padding.Empty;
            layout.Controls.Add(inspectorType, 0, 2);
            var divider = new Panel();
            divider.BackColor = UiTheme.Border;
            divider.Dock = DockStyle.Top;
            divider.Height = 1;
            divider.Margin = Padding.Empty;
            layout.Controls.Add(divider, 0, 3);
            ConfigureTextBox(detailsBox);
            detailsBox.Font = new Font("Segoe UI", 9F);
            detailsBox.AccessibleName = "Selected finding evidence and proposed action";
            layout.Controls.Add(detailsBox, 0, 4);
            return layout;
        }

        private Control BuildActivity()
        {
            var panel = new SurfacePanel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = UiTheme.SurfaceWhite;
            panel.Margin = new Padding(0, 14, 0, 0);
            panel.Padding = new Padding(16, 10, 16, 10);
            panel.ShowBorder = true;
            panel.Radius = 7;
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Margin = Padding.Empty;
            layout.ColumnCount = 2;
            layout.RowCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = new Label();
            label.Dock = DockStyle.Fill;
            label.Text = "Activity";
            label.Font = new Font("Segoe UI Semibold", 9F);
            label.Margin = Padding.Empty;
            layout.Controls.Add(label, 0, 0);
            var note = new Label();
            note.Dock = DockStyle.Fill;
            note.Text = "SESSION LOG";
            note.TextAlign = ContentAlignment.TopRight;
            note.Font = new Font("Segoe UI", 7.5F);
            note.ForeColor = UiTheme.Muted;
            note.Margin = Padding.Empty;
            layout.Controls.Add(note, 1, 0);
            ConfigureTextBox(logBox);
            logBox.Font = new Font("Consolas", 8F);
            logBox.ForeColor = UiTheme.Muted;
            logBox.AccessibleName = "Operation activity log";
            layout.Controls.Add(logBox, 0, 1);
            layout.SetColumnSpan(logBox, 2);
            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildStatusBar()
        {
            var footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0, 9, 0, 0);
            footer.ColumnCount = 4;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 87));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 248));
            stateLabel.Dock = DockStyle.Fill;
            stateLabel.Font = new Font("Segoe UI Semibold", 8F);
            stateLabel.ForeColor = UiTheme.Ink;
            stateLabel.Margin = Padding.Empty;
            footer.Controls.Add(stateLabel, 0, 0);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Font = new Font("Segoe UI", 8F);
            statusLabel.ForeColor = UiTheme.Muted;
            statusLabel.AutoEllipsis = true;
            statusLabel.Margin = Padding.Empty;
            footer.Controls.Add(statusLabel, 1, 0);
            progress.Dock = DockStyle.Top;
            progress.Height = 4;
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Margin = new Padding(10, 5, 0, 0);
            progress.Visible = false;
            progress.AccessibleName = "Operation in progress";
            footer.Controls.Add(progress, 2, 0);
            var credit = new Label();
            credit.Name = "CreatorCredit";
            credit.Text = "Made by Aggelos Yiannakou \u00b7 2026";
            credit.Dock = DockStyle.Fill;
            credit.Font = new Font("Segoe UI", 8F);
            credit.ForeColor = UiTheme.Muted;
            credit.TextAlign = ContentAlignment.TopRight;
            credit.Margin = Padding.Empty;
            credit.AccessibleName = credit.Text;
            footer.Controls.Add(credit, 3, 0);
            return footer;
        }

        private static void ConfigureButton(Button button, string text, EventHandler handler)
        {
            button.Text = text;
            button.Font = new Font("Segoe UI Semibold", 8.5F);
            button.AccessibleName = text;
            button.Click += handler;
        }

        private static void ConfigureTextBox(TextBox box)
        {
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.WordWrap = true;
            box.Dock = DockStyle.Fill;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = UiTheme.SurfaceWhite;
            box.ForeColor = UiTheme.Ink;
            box.Margin = Padding.Empty;
        }

        private static DataGridView CreateStyledGrid()
        {
            var grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.RowHeadersVisible = false;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.BackgroundColor = UiTheme.SurfaceWhite;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.GridColor = Color.FromArgb(236, 235, 232);
            grid.EnableHeadersVisualStyles = false;
            grid.AutoGenerateColumns = false;
            grid.ColumnHeadersHeight = 36;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(249, 248, 246);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8F);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 5, 0);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(249, 248, 246);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.Ink;
            grid.DefaultCellStyle.BackColor = UiTheme.SurfaceWhite;
            grid.DefaultCellStyle.ForeColor = UiTheme.Ink;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 8.5F);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 239, 239);
            grid.DefaultCellStyle.SelectionForeColor = UiTheme.Ink;
            grid.DefaultCellStyle.Padding = new Padding(10, 12, 6, 12);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.RowTemplate.MinimumHeight = 58;
            grid.AccessibleName = "Profile scan findings";
            return grid;
        }

        private DataGridView CreateGrid(bool selectable)
        {
            var grid = CreateStyledGrid();
            if (selectable)
            {
                var checkbox = new DataGridViewCheckBoxColumn();
                checkbox.Name = "Apply";
                checkbox.HeaderText = "Repair";
                checkbox.Width = 55;
                checkbox.MinimumWidth = 55;
                checkbox.SortMode = DataGridViewColumnSortMode.NotSortable;
                checkbox.DefaultCellStyle.Padding = Padding.Empty;
                grid.Columns.Add(checkbox);
            }
            var title = new DataGridViewTextBoxColumn();
            title.Name = "Finding";
            title.HeaderText = "Finding";
            title.ReadOnly = true;
            title.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            title.FillWeight = 45;
            title.MinimumWidth = 120;
            grid.Columns.Add(title);
            if (!selectable)
            {
                var classification = new DataGridViewTextBoxColumn();
                classification.Name = "Classification";
                classification.HeaderText = "Classification";
                classification.ReadOnly = true;
                classification.Width = 120;
                classification.MinimumWidth = 100;
                classification.DefaultCellStyle.ForeColor = UiTheme.Muted;
                grid.Columns.Add(classification);
            }
            var summary = new DataGridViewTextBoxColumn();
            summary.Name = "Summary";
            summary.HeaderText = selectable ? "Reason for repair" : "Review notes";
            summary.ReadOnly = true;
            summary.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            summary.FillWeight = 55;
            summary.MinimumWidth = 120;
            summary.DefaultCellStyle.ForeColor = UiTheme.Muted;
            summary.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns.Add(summary);
            grid.SelectionChanged += delegate { ShowDetails(grid); };
            grid.CellValueChanged += delegate { UpdateButtons(); };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            return grid;
        }

        private void ShowDetails(DataGridView grid)
        {
            if (grid.CurrentRow == null) return;
            var finding = grid.CurrentRow.Tag as Finding;
            if (finding == null) return;
            inspectorTitle.Text = finding.Title;
            inspectorType.Text = ClassificationLabel(finding);
            var text = new StringBuilder();
            text.AppendLine("EVIDENCE");
            text.AppendLine(finding.Summary);
            if (technicalDetails.Checked)
            {
                if (!String.IsNullOrEmpty(finding.Category)) text.AppendLine("Area: " + finding.Category);
                if (!String.IsNullOrEmpty(finding.RuleId)) text.AppendLine("Rule: " + finding.RuleId);
                text.AppendLine("Location: " + finding.Path);
                foreach (string evidence in finding.Evidence) text.AppendLine(evidence + Environment.NewLine);
                if (finding.MissingTargets.Count != 0)
                {
                    text.AppendLine("Missing targets:");
                    foreach (string target in finding.MissingTargets) text.AppendLine(target);
                }
            }
            text.AppendLine();
            text.AppendLine("PROPOSED CHANGE");
            text.AppendLine(ValueOr(finding.ProposedChange, finding.CanFix ? "Remove only this confirmed stale item after rechecking its evidence. Nothing is applied until you select and confirm the repair." : "Investigation only. This preview will not change this item."));
            text.AppendLine();
            text.AppendLine("BACKUP / UNDO");
            text.AppendLine(ValueOr(finding.BackupPlan, finding.CanFix ? "Save the original item in a local repair backup before changing it. Restore backup validates profile ownership, paths, and conflicts." : "No change is offered, so no repair backup is created."));
            text.AppendLine();
            text.AppendLine("VERIFICATION");
            text.AppendLine(ValueOr(finding.VerificationPlan, "Scan again after any change. A result no longer detected still needs a visual check of the original problem; restart Windows if needed."));
            detailsBox.Text = text.ToString();
        }

        private static string ValueOr(string value, string fallback) { return String.IsNullOrWhiteSpace(value) ? fallback : value; }

        private static string ClassificationLabel(Finding finding)
        {
            return ValueOr(finding.Classification, finding.CanFix ? "Repair available · not selected by default" : "Needs review");
        }

        private DataGridView CreateImageGrid()
        {
            var grid = CreateStyledGrid();
            grid.ReadOnly = true;
            grid.AccessibleName = "Image file icon diagnostics";
            grid.Columns.Add("Extension", "Type");
            grid.Columns.Add("Status", "Icon status");
            grid.Columns.Add("ProgId", "Application ProgID");
            grid.Columns.Add("Resource", "Icon resource");
            grid.Columns[0].Width = 65;
            grid.Columns[0].MinimumWidth = 55;
            grid.Columns[1].Width = 120;
            grid.Columns[2].Width = 165;
            grid.Columns[3].Width = 260;
            grid.SelectionChanged += delegate { ShowImageDetails(grid); };
            return grid;
        }

        private void ShowImageDetails(DataGridView grid)
        {
            if (grid.CurrentRow == null) return;
            ImageIconDiagnostic icon = grid.CurrentRow.Tag as ImageIconDiagnostic;
            if (icon == null) return;
            inspectorTitle.Text = icon.Extension + " file-type icon";
            inspectorType.Text = "DIAGNOSTIC ONLY";
            StringBuilder text = new StringBuilder();
            text.AppendLine("STATUS");
            text.AppendLine(icon.Status);
            text.AppendLine();
            text.AppendLine("DEFAULT APPLICATION PROGID");
            text.AppendLine(icon.ProgId);
            text.AppendLine();
            text.AppendLine("DEFAULTICON");
            text.AppendLine(icon.DefaultIcon);
            text.AppendLine();
            text.AppendLine("RESOLVED RESOURCE");
            text.AppendLine(icon.ResolvedIcon);
            text.AppendLine();
            foreach (string note in icon.Notes) text.AppendLine(note + Environment.NewLine);
            text.AppendLine("Diagnostic only. Default applications and file associations are not changed.");
            detailsBox.Text = text.ToString();
        }

        private void RefreshHistory()
        {
            try { DisplayHistory(HistoryStore.Read()); }
            catch (Exception ex)
            {
                DisplayHistory(new List<RepairHistoryItem>());
                historySummary.Text = "Repair history could not be read. Existing backups can still be selected manually.";
                backupsSummary.Text = "Recorded backup metadata is unavailable. Use Restore backup to choose a folder.";
                AppendLog("Repair history unavailable: " + ex.Message);
            }
        }

        internal void DisplayHistory(List<RepairHistoryItem> entries)
        {
            history = entries ?? new List<RepairHistoryItem>();
            historyList.BeginUpdate();
            backupList.BeginUpdate();
            historyList.Items.Clear();
            backupList.Items.Clear();
            foreach (RepairHistoryItem entry in history)
            {
                if (entry == null) continue;
                DateTime when;
                string date = DateTime.TryParse(entry.CreatedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out when) ? when.ToLocalTime().ToString("dd MMM yyyy HH:mm") : ValueOr(entry.CreatedUtc, "Unknown time");
                var row = new ListViewItem(date);
                row.SubItems.Add(entry.Operation ?? String.Empty);
                row.SubItems.Add(entry.Status ?? String.Empty);
                row.SubItems.Add(ValueOr(entry.VerificationState, "Not checked"));
                row.Tag = entry;
                historyList.Items.Add(row);
                if (!String.IsNullOrWhiteSpace(entry.BackupDirectory))
                {
                    var backup = new ListViewItem(date);
                    backup.SubItems.Add(entry.Operation ?? String.Empty);
                    backup.SubItems.Add(entry.BackupDirectory);
                    backup.Tag = entry;
                    backupList.Items.Add(backup);
                }
            }
            historyList.EndUpdate();
            backupList.EndUpdate();
            historySummary.Text = historyList.Items.Count == 0 ? "No operations have been recorded by this version yet." : historyList.Items.Count + " recorded operation(s). Select one to review its result.";
            backupsSummary.Text = backupList.Items.Count == 0 ? "No recorded backups. Earlier backup folders can be selected with Restore backup." : backupList.Items.Count + " recorded backup(s). Validation happens before restore.";
            ShowHistoryDetails(false);
            ShowHistoryDetails(true);
            UpdateButtons();
        }

        private void ShowHistoryDetails(bool backups)
        {
            ListView list = backups ? backupList : historyList;
            TextBox box = backups ? backupDetails : historyDetails;
            RepairHistoryItem item = list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as RepairHistoryItem;
            if (item == null)
            {
                box.Text = backups ? "Select a recorded backup to see its operation and recovery conditions.\r\n\r\nRestore checks ownership, manifest contents, paths, and conflicts. Existing or changed items are not overwritten. The list is metadata; a recorded folder may no longer exist."
                    : "Select an operation to inspect its result.\r\n\r\nUse Scan to verify repairs to check whether a recorded finding is still detected. Diagnostic absence does not establish that the original visual symptom is fixed. Check the application or taskbar yourself after restarting when appropriate.";
            }
            else
            {
                var text = new StringBuilder();
                text.AppendLine(item.Operation + " · " + item.Status);
                text.AppendLine(item.Summary);
                text.AppendLine();
                text.AppendLine("VERIFICATION");
                text.AppendLine(ValueOr(item.VerificationState, "Not checked"));
                if (!String.IsNullOrEmpty(item.LastVerifiedUtc)) text.AppendLine("Last diagnostic check (UTC): " + item.LastVerifiedUtc);
                text.AppendLine();
                text.AppendLine("BACKUP / UNDO");
                text.AppendLine(String.IsNullOrWhiteSpace(item.BackupDirectory) ? "No restore backup was recorded for this operation." : item.BackupDirectory);
                if (backups) text.AppendLine("Restore revalidates this backup and refuses conflicts. Review its manifest and trust its origin before restoring.");
                if (technicalDetails.Checked && item.Details != null)
                {
                    text.AppendLine();
                    text.AppendLine("OPERATION DETAILS");
                    foreach (string detail in item.Details) text.AppendLine(detail);
                }
                box.Text = text.ToString();
            }
            UpdateButtons();
        }

        private void RestoreSelectedClick(object sender, EventArgs e)
        {
            if (busy || backupList.SelectedItems.Count != 1) return;
            var item = backupList.SelectedItems[0].Tag as RepairHistoryItem;
            if (item != null && !String.IsNullOrWhiteSpace(item.BackupDirectory)) RestoreFolder(item.BackupDirectory);
        }

        internal void SelectSection(int index)
        {
            if (index < 0 || index > 2) throw new ArgumentOutOfRangeException("index");
            activeSection = index;
            if (pages.ContainsKey("Diagnostics") && activePage != "Diagnostics") SelectPage("Diagnostics");
            findingsNav.Selected = index == 0;
            reviewNav.Selected = index == 1;
            imageNav.Selected = index == 2;
            findingsGrid.Visible = index == 0;
            reviewGrid.Visible = index == 1;
            imageGrid.Visible = index == 2;
            sectionTitle.Text = index == 0 ? "Findings" : index == 1 ? "Review and information" : "Image file icons";
            sectionDescription.Text = index == 0 ? "Review the evidence, then choose individual repairs." : index == 1 ? "Information, observations, and checks that need investigation." : "Inspect application icon resources and file associations.";
            repairButton.Visible = index == 0;
            RefreshSection();
            UpdateButtons();
        }

        private DataGridView ActiveGrid()
        {
            return activeSection == 0 ? findingsGrid : activeSection == 1 ? reviewGrid : imageGrid;
        }

        private void RefreshSection()
        {
            DataGridView active = ActiveGrid();
            bool showEmpty = report == null || active.Rows.Count == 0;
            emptyState.Visible = showEmpty;
            if (showEmpty)
            {
                emptyState.BringToFront();
                emptyTitle.Text = report == null ? "Start with a profile scan" : "No items in this section";
                emptyDescription.Text = report == null ? "A read-only scan checks icon registrations,\r\ninstallation records, and app shortcuts." : "Review the other sections and activity log\r\nfor diagnostics and any skipped checks.";
                inspectorTitle.Text = report == null ? "Evidence, in context" : "No item selected";
                inspectorType.Text = report == null ? "SELECT A FINDING AFTER SCANNING" : "LIMITED PROFILE SCAN";
                detailsBox.Text = report == null ? "Select a row after scanning to inspect its location, supporting evidence, and proposed action.\r\n\r\nNothing is selected for repair by default.\r\n\r\nRun this utility as your normal Windows user." : "No findings in this section. Review the other sections and scan notes for diagnostics and any skipped checks. This is a limited scan, not a complete Windows health assessment.";
            }
            else
            {
                active.BringToFront();
                if (active.CurrentRow == null)
                    active.CurrentCell = active.Rows[0].Cells[activeSection == 0 ? "Finding" : activeSection == 1 ? "Finding" : "Extension"];
                if (activeSection == 2) ShowImageDetails(active);
                else ShowDetails(active);
            }
        }

        private List<Finding> SelectedFindings()
        {
            var selected = new List<Finding>();
            foreach (DataGridViewRow row in findingsGrid.Rows)
            {
                var finding = row.Tag as Finding;
                if (finding != null && finding.CanFix && Convert.ToBoolean(row.Cells["Apply"].Value ?? false)) selected.Add(finding);
            }
            return selected;
        }

        private void UpdateButtons()
        {
            scanButton.Enabled = !busy;
            repairButton.Enabled = !busy && activePage == "Diagnostics" && activeSection == 0 && SelectedFindings().Count != 0;
            exportButton.Enabled = !busy && report != null;
            restoreButton.Enabled = !busy;
            cacheButton.Enabled = !busy;
            findingsGrid.Enabled = !busy;
            reviewGrid.Enabled = !busy;
            imageGrid.Enabled = !busy;
            findingsNav.Enabled = !busy;
            reviewNav.Enabled = !busy;
            imageNav.Enabled = !busy;
            overviewNav.Enabled = diagnosticsNav.Enabled = historyNav.Enabled = backupsNav.Enabled = settingsNav.Enabled = !busy;
            historyList.Enabled = backupList.Enabled = moduleList.Enabled = !busy;
            technicalDetails.Enabled = detailTextSize.Enabled = !busy;
            restoreSelectedButton.Enabled = !busy && backupList.SelectedItems.Count == 1;
            verifyButton.Enabled = !busy && history.Count != 0;
            cancelScanButton.Visible = busy && scanCancellation != null;
            cancelScanButton.Enabled = busy && scanCancellation != null && !scanCancellation.IsCancellationRequested;
            stateLabel.Text = busy ? "\u25cf  Working" : report == null ? "\u25cf  Ready" : "\u25cf  Scanned";
            progress.Visible = busy;
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            statusLabel.Text = status;
            UseWaitCursor = value;
            UpdateButtons();
        }

        private void AppendLog(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }


        private async void ScanClick(object sender, EventArgs e)
        {
            if (busy) return;
            scanCancellation = new CancellationTokenSource();
            SetBusy(true, "Scanning this profile. No changes are being made...");
            try
            {
                AppendLog("Read-only profile scan started.");
                CancellationToken token = scanCancellation.Token;
                var scanned = await Task.Run(() => WorkspaceScanner.Scan(token, AppendLog));
                token.ThrowIfCancellationRequested();
                DisplayReport(scanned);
                foreach (string note in report.Notes) AppendLog(note);
                try { await Task.Run(() => HistoryStore.Verify(scanned, AppendLog)); }
                catch (Exception ex) { AppendLog("Scan completed, but history verification could not be saved: " + ex.Message); }
                RefreshHistory();
                AppendLog("Scan complete. " + findingsGrid.Rows.Count + " eligible finding(s), " + reviewGrid.Rows.Count + " read-only observation(s). Nothing selected for repair.");
                SetBusy(false, "Scan complete. Review the evidence, then select only the repairs you want.");
            }
            catch (OperationCanceledException)
            {
                AppendLog("Scan cancelled. Partial results were discarded; no repairs were made.");
                SetBusy(false, "Scan cancelled. Any displayed results belong to the earlier completed scan.");
            }
            catch (Exception ex) { ShowFailure("Scan failed", ex); }
            finally
            {
                scanCancellation.Dispose();
                scanCancellation = null;
                if (busy) SetBusy(false, "Scan did not complete. See the log.");
                else UpdateButtons();
            }
        }

        internal void DisplayReport(ScanReport scanned)
        {
            report = scanned;
            findingsGrid.Rows.Clear();
            reviewGrid.Rows.Clear();
            imageGrid.Rows.Clear();
            foreach (Finding finding in report.Findings)
            {
                DataGridView grid = finding.CanFix ? findingsGrid : reviewGrid;
                int index = finding.CanFix
                    ? grid.Rows.Add(false, finding.Title, finding.Summary)
                    : grid.Rows.Add(finding.Title, ClassificationLabel(finding), finding.Summary);
                grid.Rows[index].Tag = finding;
            }
            foreach (ImageIconDiagnostic icon in report.ImageIcons)
            {
                int row = imageGrid.Rows.Add(icon.Extension, icon.Status, icon.ProgId, icon.ResolvedIcon);
                imageGrid.Rows[row].Tag = icon;
            }
            findingsNav.BadgeText = findingsStat.Value = findingsGrid.Rows.Count.ToString();
            reviewNav.BadgeText = reviewStat.Value = reviewGrid.Rows.Count.ToString();
            imageNav.BadgeText = imageStat.Value = imageGrid.Rows.Count.ToString();
            moduleList.Items.Clear();
            foreach (DiagnosticModuleResult module in report.Modules)
                AddModuleRow(module.Title, module.Status, module.Summary);
            overviewSummary.Text = "Latest completed scan: " + findingsGrid.Rows.Count + " repair candidate(s), " + reviewGrid.Rows.Count + " read-only observation(s). No repairs selected.";
            SelectSection(0);
            UpdateButtons();
        }

        private async void RepairClick(object sender, EventArgs e)
        {
            findingsGrid.EndEdit();
            List<Finding> selected = SelectedFindings();
            if (selected.Count == 0) return;
            var review = new StringBuilder();
            review.AppendLine("Repair exactly these " + selected.Count + " selected item(s)?");
            review.AppendLine();
            foreach (Finding finding in selected) review.AppendLine("• " + finding.Title + "\r\n  " + finding.Path);
            review.AppendLine();
            review.AppendLine("A backup will be saved under %LOCALAPPDATA%\\WinProfileRepair\\Backups before changes. The repair rechecks each item and refuses changed or unsafe evidence. Review-only findings are excluded.");
            review.AppendLine("The icon cache is rebuilt separately, using its own button.");
            if (MessageBox.Show(this, review.ToString(), "Review selected repairs", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            SetBusy(true, "Backing up and repairing the selected items...");
            try
            {
                OperationResult result = await Task.Run(() => WorkspaceOperations.Apply(selected, AppendLog));
                ShowResult("Selected repairs", result);
                ClearScan("Repair operation finished. Scan again to inspect the current state.");
                RefreshHistory();
            }
            catch (Exception ex) { ShowFailure("Repair failed", ex); ClearScan("Repair failed. Review the log, then scan again."); }
            finally { SetBusy(false, statusLabel.Text); }
        }

        private void ExportClick(object sender, EventArgs e)
        {
            if (report == null) return;
            if (MessageBox.Show(this, "The report may include your Windows user SID, user name, local paths, and installed application details. It is saved only to a file you choose. Review and redact it before sharing.\r\n\r\nSave a local report?", "Report privacy", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Save local scan report";
                dialog.Filter = "JSON report (*.json)|*.json";
                dialog.FileName = "WinProfileRepair-scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = Int32.MaxValue;
                    File.WriteAllText(dialog.FileName, serializer.Serialize(report), new UTF8Encoding(false));
                    AppendLog("Report saved locally: " + dialog.FileName);
                }
                catch (Exception ex) { ShowFailure("Export failed", ex); }
            }
        }

        private void RestoreClick(object sender, EventArgs e)
        {
            string folder;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select one WinProfileRepair backup folder containing its manifest. Restore rechecks ownership, paths, and conflicts.";
                dialog.ShowNewFolderButton = false;
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinProfileRepair", "Backups");
                if (Directory.Exists(root)) dialog.SelectedPath = root;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                folder = dialog.SelectedPath;
            }
            RestoreFolder(folder);
        }

        private async void RestoreFolder(string folder)
        {
            if (busy) return;
            if (MessageBox.Show(this, "Restore backed-up items from this folder?\r\n\r\n" + folder + "\r\n\r\nExisting or changed items will not be overwritten. Restore only backups you trust and created for this Windows profile.", "Review backup restore", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            SetBusy(true, "Validating and restoring the selected backup...");
            try
            {
                OperationResult result = await Task.Run(() => WorkspaceOperations.Restore(folder, AppendLog));
                ShowResult("Backup restore", result);
                ClearScan("Restore operation finished. Scan again to inspect the current state.");
                RefreshHistory();
            }
            catch (Exception ex) { ShowFailure("Restore failed", ex); ClearScan("Restore failed. Review the log, then scan again."); }
            finally { SetBusy(false, statusLabel.Text); }
        }

        private async void CacheClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Rebuild this profile's Windows icon cache?\r\n\r\nExplorer will be stopped temporarily. Your desktop and taskbar will disappear while the cache is rebuilt, and open File Explorer windows may close. Save any work in Explorer first. Explorer will be started again; Windows regenerates the cache as icons are used.\r\n\r\nThis does not repair underlying registration problems. The application will not reboot Windows or reopen your other applications. You can restart Windows afterwards if needed.", "Review icon-cache rebuild", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            SetBusy(true, "Rebuilding this profile's icon cache. Explorer may disappear temporarily...");
            try
            {
                OperationResult result = await Task.Run(() => WorkspaceOperations.RebuildIconCache(AppendLog));
                ShowResult("Icon-cache rebuild", result);
                statusLabel.Text = "Icon-cache operation finished. Check your taskbar and review the log.";
                RefreshHistory();
            }
            catch (Exception ex) { ShowFailure("Icon-cache rebuild failed", ex); statusLabel.Text = "Icon-cache rebuild failed. See the log."; }
            finally { SetBusy(false, statusLabel.Text); }
        }

        private void ClearScan(string status)
        {
            report = null;
            findingsGrid.Rows.Clear();
            reviewGrid.Rows.Clear();
            imageGrid.Rows.Clear();
            findingsNav.BadgeText = reviewNav.BadgeText = imageNav.BadgeText = String.Empty;
            findingsStat.Value = reviewStat.Value = imageStat.Value = "\u2014";
            moduleList.Items.Clear();
            AddModuleRow("Shell registrations", "Not scanned", "Thumbnail, preview, and context-menu handler registrations.");
            AddModuleRow("Installation records", "Not scanned", "Evidence-based leftovers and broken Start-menu shortcuts.");
            AddModuleRow("Image file icons", "Not scanned", "Read-only file association and icon-resource diagnostics.");
            AddModuleRow("Startup & environment", "Not scanned", "User startup entries, PATH, and temporary-folder configuration.");
            overviewSummary.Text = "The profile may have changed. Run a new scan before reviewing repairs.";
            RefreshSection();
            inspectorTitle.Text = "Scan again to continue";
            inspectorType.Text = "PROFILE STATE MAY HAVE CHANGED";
            detailsBox.Text = status;
            statusLabel.Text = status;
            UpdateButtons();
        }

        private void ShowResult(string operation, OperationResult result)
        {
            var text = new StringBuilder();
            text.AppendLine(result.Success ? operation + " completed." : operation + " completed with issues. Review the log.");
            text.AppendLine("Applied: " + result.Applied + "; failed or skipped: " + result.Failed + ".");
            if (!String.IsNullOrEmpty(result.BackupDirectory)) text.AppendLine("Backup: " + result.BackupDirectory);
            MessageBox.Show(this, text.ToString(), AppInfo.Name, MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ShowFailure(string operation, Exception exception)
        {
            AppendLog(operation + ": " + exception.Message);
            MessageBox.Show(this, operation + ".\r\n\r\n" + exception.Message + "\r\n\r\nRun this utility as your normal Windows user. Administrator mode is not supported for profile repairs.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
