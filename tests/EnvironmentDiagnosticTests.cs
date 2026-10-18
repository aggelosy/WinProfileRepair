using System;
using System.Collections.Generic;
using System.Threading;
using WinProfileRepair;

internal static class EnvironmentDiagnosticTests
{
    private static int assertions;

    private static void Check(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new Exception("FAILED: environment diagnostics: " + name);
    }

    internal static int Run(string artifacts)
    {
        assertions = 0;
        int probes = 0;
        Func<string, EnvironmentPathEvidence> absent = delegate(string path)
        {
            probes++;
            return new EnvironmentPathEvidence { SafeLocal = true, Presence = PathPresence.Missing };
        };
        EnvironmentCheckResult missing = EnvironmentDiagnostics.ClassifyStartupCommand("\"C:\\Fixture App\\app.exe\" --silent", absent);
        Check(missing.MissingConfirmed && missing.Classification == "Needs review", "explicit quoted missing .exe is review only");
        Check(missing.ResolvedPath == @"C:\Fixture App\app.exe", "only executable is passed to inspection");
        Check(probes == 1, "one explicit executable probe");
        EnvironmentCheckResult withArgument = EnvironmentDiagnostics.ClassifyStartupCommand(@"C:\Fixture\app.exe --data %OTHER_ENV%", absent);
        Check(withArgument.MissingConfirmed, "uninterpreted arguments do not expand into executable path");
        string[] ambiguous =
        {
            null, "", " ", @"C:\Program Files\Example\app.exe /quiet", "\"C:\\App\\app.exe", "\"C:\\App\\app.exe\"/quiet",
            @"app.exe /quiet", @".\app.exe", @"C:app.exe", @"\\server\share\app.exe", @"\\?\C:\App\app.exe",
            @"%LOCALAPPDATA%\App\app.exe", @"C:\App\%NAME%.exe", @"C:\App\script.cmd", "https://example.invalid/app.exe",
            @"C:\App\app.exe:stream.exe", @"C:\App\*.exe", "C:\\App\\app.exe\n --quiet", @"C:\Windows\System32\cmd.exe /c C:\gone.exe",
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -File C:\gone.ps1", @"C:\Tools\pwsh.exe -c Remove-Item x",
            @"C:\Windows\System32\rundll32.exe C:\gone.dll,Entry", @"C:\Windows/System32/cmd.exe /c x",
            new String('x', 8193)
        };
        foreach (string command in ambiguous)
        {
            int before = probes;
            EnvironmentCheckResult result = EnvironmentDiagnostics.ClassifyStartupCommand(command, absent);
            Check(!result.MissingConfirmed && result.Classification == "Needs review", "ambiguous or indirect command is not confirmed missing: " + command);
            Check(probes == before, "ambiguous or indirect command never probes a guessed path");
        }
        EnvironmentCheckResult unknown = EnvironmentDiagnostics.ClassifyStartupCommand(@"C:\Fixture\app.exe", delegate(string path)
        { return new EnvironmentPathEvidence { SafeLocal = true, Presence = PathPresence.Unknown, Reason = "Access denied fixture" }; });
        Check(!unknown.MissingConfirmed && unknown.Classification == "Unable to inspect", "access denial is not absence");
        EnvironmentCheckResult reparse = EnvironmentDiagnostics.ClassifyStartupCommand(@"C:\Cloud\app.exe", delegate(string path)
        { return new EnvironmentPathEvidence { SafeLocal = false, Presence = PathPresence.Missing, Reason = "Reparse fixture" }; });
        Check(!reparse.MissingConfirmed && reparse.Classification == "Unable to inspect", "unsafe cloud or reparse path is not absence even if inspector reports missing");
        EnvironmentCheckResult present = EnvironmentDiagnostics.ClassifyStartupCommand(@"C:\Fixture\app.exe", delegate(string path)
        { return new EnvironmentPathEvidence { SafeLocal = true, Presence = PathPresence.Exists }; });
        Check(present.Classification == null && !present.MissingConfirmed, "existing executable path is not a finding or proof of startup execution");
        EnvironmentCheckResult directory = EnvironmentDiagnostics.ClassifyStartupCommand(@"C:\Fixture\app.exe", delegate(string path)
        { return new EnvironmentPathEvidence { SafeLocal = true, Presence = PathPresence.Exists, IsDirectory = true }; });
        Check(directory.Classification == "Needs review" && !directory.MissingConfirmed, "directory named .exe is review and not absence");

        string candidate, reason;
        Check(EnvironmentDiagnostics.TryStartupExecutable("\"C:\\Fixture App\\app.EXE\" --silent", out candidate, out reason) &&
            candidate == @"C:\Fixture App\app.EXE", "quoted case-insensitive .exe parser");
        Check(EnvironmentDiagnostics.TryStartupExecutable(@"C:\Fixture\app.exe", out candidate, out reason), "unquoted no-space absolute executable");

        int directoryProbes = 0;
        Func<string, EnvironmentPathEvidence> absentDirectory = delegate(string path)
        {
            directoryProbes++;
            return new EnvironmentPathEvidence { SafeLocal = true, Presence = PathPresence.Missing };
        };
        EnvironmentCheckResult missingDirectory = EnvironmentDiagnostics.ClassifyDirectoryValue(@"C:\Optional\bin", absentDirectory);
        Check(missingDirectory.MissingConfirmed && missingDirectory.Classification == "Needs review", "missing literal directory requires review without cleanup");
        Check(directoryProbes == 1, "literal directory inspected once");
        foreach (string value in new[] { "", " ", @"%LOCALAPPDATA%\Temp", @"%PATH%", @"C:\%MISSING_VARIABLE%\bin", @"C:\literal%name" })
        {
            int before = directoryProbes;
            EnvironmentCheckResult result = EnvironmentDiagnostics.ClassifyDirectoryValue(value, absentDirectory);
            Check(result.Classification == "Information" && !result.MissingConfirmed, "empty or environment reference is information");
            Check(directoryProbes == before, "references are never expanded against process environment or inspected");
        }
        foreach (string value in new[] { @"\\server\share\bin", @"relative\bin", @"C:bin", "\"C:\\mismatch", @"C:\bad*\bin", "C:\\bad\nbin" })
        {
            int before = directoryProbes;
            EnvironmentCheckResult result = EnvironmentDiagnostics.ClassifyDirectoryValue(value, absentDirectory);
            Check(result.Classification == "Unable to inspect" && !result.MissingConfirmed, "remote, ambiguous, malformed or relative directory is not absence");
            Check(directoryProbes == before, "remote or malformed directories are not probed");
        }
        EnvironmentCheckResult quotedDirectory = EnvironmentDiagnostics.ClassifyDirectoryValue("\"C:\\Fixture App\\bin\"", absentDirectory);
        Check(quotedDirectory.MissingConfirmed && quotedDirectory.ResolvedPath == @"C:\Fixture App\bin", "complete directory quotes are handled without command parsing");
        EnvironmentCheckResult existingDirectory = EnvironmentDiagnostics.ClassifyDirectoryValue(@"C:\Fixture\bin", delegate(string path)
        { return new EnvironmentPathEvidence { SafeLocal = true, Presence = PathPresence.Exists, IsDirectory = true }; });
        Check(existingDirectory.Classification == null && !existingDirectory.MissingConfirmed, "existing directory does not trigger repair");
        EnvironmentCheckResult fileAsDirectory = EnvironmentDiagnostics.ClassifyDirectoryValue(@"C:\Fixture\bin", delegate(string path)
        { return new EnvironmentPathEvidence { SafeLocal = true, Presence = PathPresence.Exists }; });
        Check(fileAsDirectory.Classification == "Needs review" && !fileAsDirectory.MissingConfirmed, "file used as directory is not missing directory evidence");
        Check(EnvironmentDiagnostics.ClassifyDirectoryValue(null, absentDirectory).Classification == "Unable to inspect", "unavailable directory value is unknown");
        Check(EnvironmentDiagnostics.ClassifyDirectoryValue(new String('x', 32769), absentDirectory).Classification == "Unable to inspect", "directory inspection length bounded");

        bool balanced;
        List<string> split = EnvironmentDiagnostics.SplitPathEntries("C:\\One;\"C:\\Two;literal\\bin\";C:\\Three;", out balanced);
        Check(balanced && split.Count == 4 && split[1] == "\"C:\\Two;literal\\bin\"" && split[3] == "", "PATH parser preserves quoted semicolons and trailing empty entry");
        EnvironmentDiagnostics.SplitPathEntries("C:\\One;\"C:\\Two", out balanced);
        Check(!balanced, "unmatched PATH quote detected before directory inspection");
        List<string> capped = EnvironmentDiagnostics.SplitPathEntries(new String(';', 10000), out balanced);
        Check(capped.Count <= 258, "PATH parser entry count is bounded");
        Check(EnvironmentDiagnostics.HasUnpairedPercent("literal%") && !EnvironmentDiagnostics.HasUnpairedPercent("%PATH%"), "reference syntax is distinguished without assuming variable values");
        Check(!EnvironmentDiagnostics.HasUnpairedPercent("100%%"), "paired literal percent signs do not imply undefined variable");

        ScanReport canceledReport = new ScanReport();
        bool canceled = false;
        try { EnvironmentDiagnostics.Scan(canceledReport, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled && canceledReport.Findings.Count == 0 && canceledReport.Modules.Count == 0, "pre-cancellation prevents all host inspection and output");
        return assertions;
    }
}
