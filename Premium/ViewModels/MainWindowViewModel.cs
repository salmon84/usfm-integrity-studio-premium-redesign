using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using UsfmIntegrityStudio.Models;

namespace UsfmIntegrityStudio.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string inputDocxPath = string.Empty;

    [ObservableProperty]
    private string inputSourceMode = "Single DOCX file";

    [ObservableProperty]
    private string outputFolderPath = string.Empty;

    [ObservableProperty]
    private string outputSetName = "output_ui_run";

    [ObservableProperty]
    private string languageCode = "und";

    [ObservableProperty]
    private string runMode = "permissive";

    [ObservableProperty]
    private string selectedCanon = "Protestant OT";

    [ObservableProperty]
    private string compatibilityProfile = "BTTW legacy compatibility";

    [ObservableProperty]
    private bool limitChapters = false;

    [ObservableProperty]
    private int maxChaptersPerBook = 150;

    [ObservableProperty]
    private bool inferMissingVerseMarkers = false;

    [ObservableProperty]
    private bool preserveDocxVerseNumbering = true;

    [ObservableProperty]
    private bool generateBttwProjects = true;

    [ObservableProperty]
    private string status = "Ready.";

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string consoleLog = string.Empty;

    [ObservableProperty]
    private string scanSummary = "No scan yet.";

    [ObservableProperty]
    private string detectedBooksLabel = "Detected books: (not scanned yet)";

    [ObservableProperty]
    private string selectedBooksLabel = "Selected for conversion: (none yet)";

    public ObservableCollection<IssueItem> Issues { get; } = [];

    public bool CanRun => !IsRunning
        && !string.IsNullOrWhiteSpace(InputDocxPath)
        && IsDocxInputPath(InputDocxPath)
        && !string.IsNullOrWhiteSpace(OutputFolderPath)
        && !string.IsNullOrWhiteSpace(OutputSetName);

    public bool CanScan => !IsRunning
        && !string.IsNullOrWhiteSpace(InputDocxPath)
        && IsDocxInputPath(InputDocxPath);

    public bool CanCleanUsfmProject => !IsRunning
        && !string.IsNullOrWhiteSpace(InputDocxPath)
        && IsCleanableUsfmProjectPath(InputDocxPath);

    public bool CanCleanUsfm => !IsRunning
        && !string.IsNullOrWhiteSpace(InputDocxPath)
        && IsUsfmPath(InputDocxPath);

    public bool CanCleanProject => !IsRunning
        && !string.IsNullOrWhiteSpace(InputDocxPath)
        && IsProjectPath(InputDocxPath);

    public string OutputFormatHint => GenerateBttwProjects
        ? "Output format: split .USFM files + BTTW .tstudio project packages + reports."
        : "Output format: split .USFM files (.usfm) + report files (.txt/.json).";
    public string LanguageCodeHint => "Three-letter language code used in output filenames, for example skr, rus, urd.";
    public string InputSourceHint => InputSourceMode.Equals("DOCX chapter folder", StringComparison.OrdinalIgnoreCase)
        ? "Folder mode: the app will merge all DOCX files in the selected folder into one temporary book-level DOCX before scan, standardization, and conversion."
        : "Single-file mode: use one DOCX that contains the book content you want to process.";
    public string ModeHint => RunMode.Equals("strict", StringComparison.OrdinalIgnoreCase)
        ? "Strict mode: fails faster and reports chapter/verse irregularities more aggressively."
        : "Permissive mode: continues conversion with warnings when irregular formatting is detected.";
    public string CompatibilityHint => CompatibilityProfile.Equals("BTTW legacy compatibility", StringComparison.OrdinalIgnoreCase)
        ? "BTTW legacy: applies conservative marker normalization for wider compatibility with older editor workflows."
        : "Paratext-compatible: normalizes output toward standard USFM 3 structure and validation expectations.";
    public string ChapterCapHint =>
        $"Optional chapter cap: keep up to {GetEffectiveMaxChaptersPerBook()} chapter(s) per selected book during conversion.";
    public string StandardizationHint =>
        "Standardization creates a new DOCX copy, normalizes whitespace and punctuation spacing, and generates a canon-highlight report.";
    public string VerseInferenceHint => InferMissingVerseMarkers
        ? "Enabled: attempts to infer missing verse markers from paragraph flow within each chapter."
        : "Disabled: keeps original verse markers only (no inference).";
    public string PreserveNumberingHint => PreserveDocxVerseNumbering
        ? "Enabled: keeps DOCX chapter/verse numbering as-is in conversion."
        : "Disabled: allows parser reconstruction when numbering appears malformed.";
    public string BttwProjectHint => GenerateBttwProjects
        ? "Enabled: each generated USFM book also gets a BTTW .tstudio project package stamped ts-desktop 1073x."
        : "Disabled: conversion outputs USFM only; use Clean USFM/Project later for existing project files.";
    public string CanonHint => "Canonical checks compare missing/extra chapter-verse markers against the selected OT or NT canon profile.";

    public string OutputTargetPreview =>
        string.IsNullOrWhiteSpace(OutputFolderPath) || string.IsNullOrWhiteSpace(OutputSetName)
            ? "Output target: (not set)"
            : $"Output target: {Path.Combine(OutputFolderPath, OutputSetName)}";

    public string GetIssuesAsJson()
    {
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            inputDocx = InputDocxPath,
            outputFolder = OutputFolderPath,
            outputSetName = OutputSetName,
            mode = RunMode,
            canonProfile = SelectedCanon,
            limitChapters = LimitChapters,
            maxChaptersPerBook = GetEffectiveMaxChaptersPerBook(),
            scanSummary = ScanSummary,
            issueCount = Issues.Count,
            issues = Issues
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    partial void OnInputDocxPathChanged(string value)
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanCleanUsfmProject));
        OnPropertyChanged(nameof(CanCleanUsfm));
        OnPropertyChanged(nameof(CanCleanProject));
    }

    partial void OnInputSourceModeChanged(string value)
    {
        OnPropertyChanged(nameof(InputSourceHint));
    }

    partial void OnOutputFolderPathChanged(string value)
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(OutputTargetPreview));
    }

    partial void OnOutputSetNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(OutputTargetPreview));
    }

    partial void OnLanguageCodeChanged(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "und"
            : value.Trim().ToLowerInvariant();

        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            LanguageCode = normalized;
        }
    }

    partial void OnRunModeChanged(string value)
    {
        OnPropertyChanged(nameof(ModeHint));
    }

    partial void OnCompatibilityProfileChanged(string value)
    {
        OnPropertyChanged(nameof(CompatibilityHint));
    }

    partial void OnLimitChaptersChanged(bool value)
    {
        OnPropertyChanged(nameof(ChapterCapHint));
    }

    partial void OnMaxChaptersPerBookChanged(int value)
    {
        var normalized = value switch
        {
            < 1 => 1,
            > 300 => 300,
            _ => value
        };

        if (normalized != value)
        {
            MaxChaptersPerBook = normalized;
            return;
        }

        OnPropertyChanged(nameof(ChapterCapHint));
    }

    partial void OnInferMissingVerseMarkersChanged(bool value)
    {
        OnPropertyChanged(nameof(VerseInferenceHint));
    }

    partial void OnPreserveDocxVerseNumberingChanged(bool value)
    {
        OnPropertyChanged(nameof(PreserveNumberingHint));
    }

    partial void OnGenerateBttwProjectsChanged(bool value)
    {
        OnPropertyChanged(nameof(BttwProjectHint));
        OnPropertyChanged(nameof(OutputFormatHint));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanCleanUsfmProject));
        OnPropertyChanged(nameof(CanCleanUsfm));
        OnPropertyChanged(nameof(CanCleanProject));
    }

    private static bool IsCleanableUsfmProjectPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".usfm" or ".txt" or ".tstudio";
    }

    private static bool IsUsfmPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".usfm" or ".txt";
    }

    private static bool IsProjectPath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".tstudio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDocxInputPath(string path)
    {
        return Directory.Exists(path) || string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase);
    }

    public CanonProfile GetCanonProfile()
    {
        return SelectedCanon switch
        {
            "Catholic OT" => Models.CanonProfile.CatholicOt,
            "Orthodox OT" => Models.CanonProfile.OrthodoxOt,
            "Protestant NT" => Models.CanonProfile.ProtestantNt,
            "Catholic NT" => Models.CanonProfile.CatholicNt,
            "Orthodox NT" => Models.CanonProfile.OrthodoxNt,
            _ => Models.CanonProfile.ProtestantOt
        };
    }

    public int GetEffectiveMaxChaptersPerBook()
    {
        return MaxChaptersPerBook switch
        {
            < 1 => 1,
            > 300 => 300,
            _ => MaxChaptersPerBook
        };
    }
}

public record IssueItem(
    string Severity,
    string Code,
    string Message,
    int Index,
    string Source);
