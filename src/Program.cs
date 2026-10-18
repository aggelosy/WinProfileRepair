using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WinProfileRepair
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (args.Length == 2 && String.Equals(args[0], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string destination = Path.GetFullPath(args[1]);
                        if (File.Exists(destination)) throw new IOException("The report already exists; choose a new filename.");
                        ScanReport report = WorkspaceScanner.Scan(System.Threading.CancellationToken.None, null);
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        serializer.MaxJsonLength = 16 * 1024 * 1024;
                        using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (StreamWriter writer = new StreamWriter(output, new System.Text.UTF8Encoding(false)))
                            writer.Write(serializer.Serialize(report));
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                        return 1;
                    }
                }
                MessageBox.Show("Usage: WinProfileRepair.exe\r\n\r\nRead-only report:\r\nWinProfileRepair.exe --report <new-report.json>\r\n\r\nRepairs are available only through the interactive review screen.", AppInfo.Name);
                return 2;
            }
            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("The operation could not finish.\r\n\r\n" + e.Exception.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            try { Application.Run(new MainForm()); return 0; }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
