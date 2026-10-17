using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace WinProfileRepair
{
    public enum FindingKind { ThumbnailOverride, StaleUninstallRecord, BrokenShortcut, ReviewOnly }

    public sealed class RegistryValueSnapshot
    {
        public string Name { get; set; }
        public RegistryValueKind Kind { get; set; }
        public string Text { get; set; }
        public string[] TextArray { get; set; }
        public string BinaryBase64 { get; set; }
        public long Number { get; set; }
    }

    public sealed class RegistrySnapshot
    {
        public RegistryHive Hive { get; set; }
        public RegistryView View { get; set; }
        public string KeyPath { get; set; }
        public int ChildCount { get; set; }
        public List<RegistryValueSnapshot> Values { get; set; }
        public RegistrySnapshot() { Values = new List<RegistryValueSnapshot>(); }
    }

    public sealed class Finding
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public FindingKind Kind { get; set; }
        public bool CanFix { get; set; }
        public string Summary { get; set; }
        public string Path { get; set; }
        public string EvidenceFingerprint { get; set; }
        public List<string> Evidence { get; set; }
        public List<string> MissingTargets { get; set; }
        public RegistrySnapshot Snapshot { get; set; }
        public Finding() { Evidence = new List<string>(); MissingTargets = new List<string>(); }
    }

    public sealed class ScanReport
    {
        public string ApplicationVersion { get; set; }
        public string ScannedAtUtc { get; set; }
        public string UserSid { get; set; }
        public List<Finding> Findings { get; set; }
        public List<string> Notes { get; set; }
        public List<ImageIconDiagnostic> ImageIcons { get; set; }
        public ScanReport() { Findings = new List<Finding>(); Notes = new List<string>(); ImageIcons = new List<ImageIconDiagnostic>(); }
    }

    public sealed class ImageIconDiagnostic
    {
        public string Extension { get; set; }
        public string ProgId { get; set; }
        public string DefaultIcon { get; set; }
        public string ResolvedIcon { get; set; }
        public string Status { get; set; }
        public List<string> Notes { get; set; }
        public ImageIconDiagnostic() { Notes = new List<string>(); }
    }

    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string BackupDirectory { get; set; }
        public int Applied { get; set; }
        public int Failed { get; set; }
        public List<string> Messages { get; set; }
        public OperationResult() { Messages = new List<string>(); }
    }

    public static class AppInfo
    {
        public const string Name = "WinProfileRepair";
        public const string Version = "0.2.0-preview";
        public const string ThumbnailHandler = "{E357FCCD-A995-4576-B01F-234630154E96}";
    }
}
