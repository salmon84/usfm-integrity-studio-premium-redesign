using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using UsfmIntegrityStudio.Models;
using UsfmIntegrityStudio.ViewModels;

namespace UsfmIntegrityStudio.Views;

public partial class MainWindow : Window
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private string? _lastLanguageCueConfirmationFingerprint;
    private readonly Dictionary<string, string> _bookIdOverridesByTitle = new(StringComparer.OrdinalIgnoreCase);
    private string? _sessionForcedBookId;
    private const string FolderMergePrefix = "uis-folder-merge-";

    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;
    private const string CompanyWebsiteUrl = "https://digitalglobalvillage.com/";
    private const string RepositoryUrl = "https://github.com/salmon84/usfm-integrity-studio-premium-redesign";
    private const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    private void OpenWebsite_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenExternalUrl(CompanyWebsiteUrl, "website");
    }

    private void OpenExternalUrl(string url, string label)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Vm.Status = $"Unable to open {label} link: {ex.Message}";
            Vm.Issues.Add(new IssueItem("Warning", "EXTERNAL_LINK_OPEN_FAILED", ex.Message, Vm.Issues.Count + 1, "UI"));
        }
    }

    private async void ShowAbout_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var assembly = typeof(MainWindow).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.Ordinal);

        var revision = GetBuildMetadata(metadata, "SourceRevisionId", "unknown");
        var channel = GetBuildMetadata(metadata, "BuildChannel", "development");
        var official = string.Equals(
            GetBuildMetadata(metadata, "OfficialBuild", "false"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var buildStatus = official && string.Equals(channel, "official", StringComparison.OrdinalIgnoreCase)
            ? "Official metadata present. Verify the package signature and published SHA-256 checksum."
            : "Development/community build. It is not an official release package.";

        var dialog = new Window
        {
            Title = "About & Verify",
            Width = 720,
            Height = 650,
            MinWidth = 620,
            MinHeight = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };

        var details = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("145,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 9
        };
        AddConfirmationRow(details, 0, "Product", "USFM Integrity Studio Premium Redesign");
        AddConfirmationRow(details, 1, "Version", informationalVersion);
        AddConfirmationRow(details, 2, "Source revision", revision);
        AddConfirmationRow(details, 3, "Build channel", channel);
        AddConfirmationRow(details, 4, "Official metadata", official ? "Yes" : "No");
        AddConfirmationRow(details, 5, "License", "AGPL-3.0-or-later");
        AddConfirmationRow(details, 6, "Privacy", "Offline processing; no application telemetry");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        var sourceButton = new Button { Content = "Open Source", MinWidth = 110 };
        sourceButton.Click += (_, _) => OpenExternalUrl(RepositoryUrl, "source repository");
        var licenseButton = new Button { Content = "View License", MinWidth = 110 };
        licenseButton.Click += (_, _) => OpenExternalUrl(LicenseUrl, "license");
        var closeButton = new Button { Content = "Close", MinWidth = 100 };
        closeButton.Click += (_, _) => dialog.Close();
        buttons.Children.Add(sourceButton);
        buttons.Children.Add(licenseButton);
        buttons.Children.Add(closeButton);

        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "About & Verify",
                        FontSize = 26,
                        FontWeight = Avalonia.Media.FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = buildStatus,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    new Border
                    {
                        Padding = new Avalonia.Thickness(16),
                        CornerRadius = new Avalonia.CornerRadius(10),
                        BorderThickness = new Avalonia.Thickness(1),
                        BorderBrush = Avalonia.Media.Brushes.SlateGray,
                        Child = details
                    },
                    new TextBlock
                    {
                        Text = RepositoryUrl,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontFamily = Avalonia.Media.FontFamily.Parse("Menlo, monospace")
                    },
                    new TextBlock
                    {
                        Text = "This application does not send scripture content, project paths, machine identifiers, analytics, or crash reports. External links open only after a user clicks them.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Build metadata can be copied by a modified build. Only a valid platform signature and checksum matching the official GitHub release establish package provenance.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontStyle = Avalonia.Media.FontStyle.Italic
                    },
                    buttons
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    private static string GetBuildMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string fallback)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private async void BrowseDocx_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select DOCX File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Word Document") { Patterns = ["*.docx"] },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count > 0)
        {
            ResetBookIndicatorsForNewInput();
            Vm.InputDocxPath = files[0].Path.LocalPath;
            Vm.InputSourceMode = "Single DOCX file";
            EnsureOutputDefaultsForInput(Vm.InputDocxPath);
            Vm.Status = string.IsNullOrWhiteSpace(Vm.OutputFolderPath)
                ? "Input DOCX selected. Select an output root folder to enable conversion."
                : $"Input DOCX selected. Output target ready: {Vm.OutputTargetPreview}";
        }
    }

    private async void BrowseDocxFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder With Chapter DOCX Files",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            ResetBookIndicatorsForNewInput();
            Vm.InputDocxPath = folders[0].Path.LocalPath;
            Vm.InputSourceMode = "DOCX chapter folder";
            EnsureOutputDefaultsForInput(Vm.InputDocxPath);
            Vm.Status = string.IsNullOrWhiteSpace(Vm.OutputFolderPath)
                ? "Input DOCX folder selected. Select an output root folder to enable conversion."
                : $"Input DOCX folder selected. Output target ready: {Vm.OutputTargetPreview}";
        }
    }

    private async void BrowseUsfmProject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select USFM, Text Chunk, or BTTW Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("USFM / BTTW Project") { Patterns = ["*.usfm", "*.txt", "*.tstudio"] },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count > 0)
        {
            ResetBookIndicatorsForNewInput();
            Vm.InputDocxPath = files[0].Path.LocalPath;
            Vm.InputSourceMode = "USFM/project file";
            Vm.Status = "USFM/project input selected.";
        }
    }

    private async void BrowseOutputFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Root Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            Vm.OutputFolderPath = folders[0].Path.LocalPath;
            Vm.Status = "Output folder selected.";
        }
    }

    private void ResetSession_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Vm.IsRunning)
        {
            return;
        }

        Vm.InputDocxPath = string.Empty;
        Vm.InputSourceMode = "Single DOCX file";
        Vm.OutputFolderPath = string.Empty;
        Vm.OutputSetName = "output_ui_run";
        Vm.RunMode = "permissive";
        Vm.SelectedCanon = "Protestant OT";
        Vm.CompatibilityProfile = "BTTW legacy compatibility";
        Vm.LimitChapters = false;
        Vm.MaxChaptersPerBook = 150;
        Vm.InferMissingVerseMarkers = false;
        Vm.PreserveDocxVerseNumbering = true;
        Vm.GenerateBttwProjects = true;
        Vm.ScanSummary = "No scan yet.";
        Vm.DetectedBooksLabel = "Detected books: (not scanned yet)";
        Vm.SelectedBooksLabel = "Selected for conversion: (none yet)";
        Vm.ConsoleLog = string.Empty;
        Vm.Issues.Clear();
        Vm.Status = "Session reset. Ready for a new DOCX or chapter folder.";
        _lastLanguageCueConfirmationFingerprint = null;
        _bookIdOverridesByTitle.Clear();
        _sessionForcedBookId = null;
    }

    private void ResetBookIndicatorsForNewInput()
    {
        Vm.ScanSummary = "No scan yet.";
        Vm.DetectedBooksLabel = "Detected books: (not scanned yet)";
        Vm.SelectedBooksLabel = "Selected for conversion: (none yet)";
    }

    private async void CleanUsfm_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Vm.CanCleanUsfm)
        {
            return;
        }

        await CleanSelectedUsfmOrProjectAsync(allowProjectOutputChoice: false);
    }

    private async void CleanProject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Vm.CanCleanProject)
        {
            return;
        }

        await CleanSelectedUsfmOrProjectAsync(allowProjectOutputChoice: true);
    }

    private async Task CleanSelectedUsfmOrProjectAsync(bool allowProjectOutputChoice)
    {
        var inputPath = Path.GetFullPath(Vm.InputDocxPath);
        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        var outputExtension = extension;
        var exportUsfmFromTstudio = false;
        if (allowProjectOutputChoice)
        {
            var choice = await PromptTstudioCleanOutputAsync();
            if (choice is null)
            {
                Vm.Status = "Cleaning cancelled.";
                return;
            }

            exportUsfmFromTstudio = string.Equals(choice, "usfm", StringComparison.OrdinalIgnoreCase);
            outputExtension = exportUsfmFromTstudio ? ".usfm" : ".tstudio";
        }

        var sourceName = Path.GetFileNameWithoutExtension(inputPath);
        var outputFileType = BuildCleanerOutputFileType(outputExtension, exportUsfmFromTstudio);
        var saveTarget = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = exportUsfmFromTstudio
                ? "Save USFM Export From Cleaned Project"
                : allowProjectOutputChoice
                    ? "Save Cleaned BTTW Project"
                    : "Save Cleaned USFM",
            SuggestedFileName = sourceName + "_cleaned",
            DefaultExtension = outputExtension.TrimStart('.'),
            FileTypeChoices = [outputFileType]
        });

        if (saveTarget is null)
        {
            return;
        }

        var outputPath = NormalizeCleanerOutputExtension(Path.GetFullPath(saveTarget.Path.LocalPath), outputExtension);
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            Vm.Status = "Cleaning blocked: output must be different from input.";
            Vm.Issues.Add(new IssueItem(
                "Error",
                "CLEAN_SAME_PATH_BLOCKED",
                $"Input and output paths are the same: {outputPath}",
                Vm.Issues.Count + 1,
                "USFM Cleaner"));
            return;
        }

        CleanerJobIdentity identity;
        try
        {
            identity = CleanerJobIdentityService.Read(inputPath);
        }
        catch (Exception ex)
        {
            Vm.Status = $"Cleaning blocked: selected file identity could not be verified. {ex.Message}";
            Vm.Issues.Add(new IssueItem(
                "Error",
                "CLEAN_IDENTITY_READ_FAILED",
                ex.Message,
                Vm.Issues.Count + 1,
                "USFM Cleaner"));
            return;
        }

        var confirmed = await ConfirmCleanerJobAsync(
            identity,
            inputPath,
            outputPath,
            exportUsfmFromTstudio);
        if (!confirmed)
        {
            Vm.Status = "Cleaning cancelled at confirmation.";
            return;
        }

        Vm.IsRunning = true;
        Vm.Status = allowProjectOutputChoice
            ? "Cleaning BTTW project artifacts..."
            : "Cleaning USFM artifacts...";
        var canonProfile = Vm.GetCanonProfile();

        try
        {
            var result = await Task.Run(() => exportUsfmFromTstudio
                ? UsfmProjectCleanerService.CleanTstudioToUsfm(inputPath, outputPath, canonProfile)
                : UsfmProjectCleanerService.Clean(inputPath, outputPath, canonProfile));
            Vm.ConsoleLog =
                $"Cleaned output: {result.OutputPath}{Environment.NewLine}" +
                $"Cleaner report: {result.ReportPath}{Environment.NewLine}" +
                $"Files scanned: {result.FilesScanned}{Environment.NewLine}" +
                $"Files changed: {result.FilesChanged}{Environment.NewLine}" +
                $"Duplicate visible verse markers removed: {result.InlineDuplicateMarkersRemoved + result.PendingLineDuplicateMarkersRemoved}{Environment.NewLine}" +
                $"Visible reversed/loose verse markers normalized: {result.VisibleVerseMarkersNormalized}{Environment.NewLine}" +
                $"Stray leading verse markers removed: {result.StrayLeadingVerseMarkersRemoved}{Environment.NewLine}" +
                $"Punctuation/parenthesis spacing fixes: {result.SpacingFixes}{Environment.NewLine}" +
                $"Straight English quotes converted: {result.StraightQuotesConverted}{Environment.NewLine}" +
                $"Straight English single quotes converted: {result.StraightSingleQuotesConverted}{Environment.NewLine}" +
                $"Directional double quotes repaired: {result.DirectionalDoubleQuotesRepaired}{Environment.NewLine}" +
                $"Directional single quotes repaired: {result.DirectionalSingleQuotesRepaired}{Environment.NewLine}" +
                $"Unpaired double quote closers repaired: {result.UnpairedDoubleQuoteClosersRepaired}{Environment.NewLine}" +
                $"Unicode BOM markers removed: {result.ByteOrderMarksRemoved}{Environment.NewLine}" +
                $"Unsafe control characters removed: {result.UnsafeControlCharsRemoved}{Environment.NewLine}" +
                $"Structural chunk files removed: {result.StructuralChunkFilesRemoved}{Environment.NewLine}" +
                $"Manifest finished_chunks removed: {result.ManifestFinishedChunksRemoved}{Environment.NewLine}" +
                $"Post-clean verification issues: {result.VerificationIssueCount}";
            Vm.Status = result.VerificationIssueCount == 0
                ? $"Cleaning completed and verified. Changed {result.FilesChanged} file(s). Output: {result.OutputPath}"
                : $"Cleaning completed with {result.VerificationIssueCount} verification issue(s). Review report: {result.ReportPath}";
            Vm.Issues.Add(new IssueItem(
                "Info",
                "USFM_PROJECT_CLEANED",
                $"Removed {result.InlineDuplicateMarkersRemoved + result.PendingLineDuplicateMarkersRemoved} duplicate visible verse marker artifact(s), normalized {result.VisibleVerseMarkersNormalized} reversed/loose verse marker(s), removed {result.StrayLeadingVerseMarkersRemoved} stray leading verse marker(s), removed {result.ByteOrderMarksRemoved} Unicode BOM marker(s), removed {result.UnsafeControlCharsRemoved} unsafe control character(s), removed {result.StructuralChunkFilesRemoved} impossible chunk file(s), removed {result.ManifestFinishedChunksRemoved} impossible manifest reference(s), converted {result.StraightQuotesConverted} double quote(s), {result.StraightSingleQuotesConverted} single quote(s), repaired {result.DirectionalDoubleQuotesRepaired + result.DirectionalSingleQuotesRepaired + result.UnpairedDoubleQuoteClosersRepaired} directional quote(s), and wrote report: {result.ReportPath}.",
                Vm.Issues.Count + 1,
                "USFM Cleaner"));
            foreach (var warning in result.VerificationIssues.Where(issue =>
                         issue.StartsWith("WARNING NONCANONICAL_", StringComparison.Ordinal)))
            {
                Vm.Issues.Add(new IssueItem(
                    "Warning",
                    "BTTW_CHUNK_LAYOUT_WARNING",
                    warning,
                    Vm.Issues.Count + 1,
                    "USFM Cleaner"));
            }
        }
        catch (Exception ex)
        {
            Vm.Status = $"Cleaning failed: {ex.Message}";
            Vm.Issues.Add(new IssueItem("Error", "USFM_PROJECT_CLEAN_FAILED", ex.Message, Vm.Issues.Count + 1, "USFM Cleaner"));
        }
        finally
        {
            Vm.IsRunning = false;
        }
    }

    private static FilePickerFileType BuildCleanerOutputFileType(string outputExtension, bool exportUsfmFromTstudio)
    {
        return outputExtension switch
        {
            ".tstudio" => new FilePickerFileType("Cleaned BTTW Project (.tstudio)") { Patterns = ["*.tstudio"] },
            ".usfm" => new FilePickerFileType(exportUsfmFromTstudio ? "Cleaned USFM Export (.usfm)" : "Cleaned USFM (.usfm)") { Patterns = ["*.usfm"] },
            ".txt" => new FilePickerFileType("Cleaned Text Chunk (.txt)") { Patterns = ["*.txt"] },
            _ => new FilePickerFileType("Cleaned File") { Patterns = [$"*{outputExtension}"] }
        };
    }

    private static string NormalizeCleanerOutputExtension(string outputPath, string expectedExtension)
    {
        var expected = expectedExtension.StartsWith(".", StringComparison.Ordinal)
            ? expectedExtension
            : "." + expectedExtension;
        var duplicate = expected + expected;
        return outputPath.EndsWith(duplicate, StringComparison.OrdinalIgnoreCase)
            ? outputPath[..^expected.Length]
            : outputPath;
    }

    private async Task<string?> PromptTstudioCleanOutputAsync()
    {
        var dialog = new Window
        {
            Title = "Choose Cleaned Output",
            Width = 460,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var body = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14
        };
        body.Children.Add(new TextBlock
        {
            Text = "This is a BTTW .tstudio project. What output do you want?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        body.Children.Add(new TextBlock
        {
            Text = "Choose cleaned .tstudio to preserve BTTW project metadata, or cleaned .usfm when you only need a USFM export.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var projectButton = new Button { Content = "Cleaned .tstudio", MinWidth = 130 };
        projectButton.Click += (_, _) => dialog.Close("tstudio");
        var usfmButton = new Button { Content = "Cleaned .usfm", MinWidth = 120 };
        usfmButton.Click += (_, _) => dialog.Close("usfm");
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };
        cancelButton.Click += (_, _) => dialog.Close(null);

        buttons.Children.Add(projectButton);
        buttons.Children.Add(usfmButton);
        buttons.Children.Add(cancelButton);
        body.Children.Add(buttons);
        dialog.Content = body;

        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<bool> ConfirmCleanerJobAsync(
        CleanerJobIdentity identity,
        string inputPath,
        string outputPath,
        bool exportUsfmFromTstudio)
    {
        var dialog = new Window
        {
            Title = "Confirm Cleaning Job",
            Width = 720,
            Height = 610,
            MinWidth = 620,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true
        };

        var details = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 14,
            RowSpacing = 9
        };

        var action = exportUsfmFromTstudio
            ? "Clean project and export USFM"
            : identity.InputType.StartsWith("BTTW", StringComparison.Ordinal)
                ? "Clean BTTW Project"
                : "Clean USFM";
        AddConfirmationRow(details, 0, "Action", action);
        AddConfirmationRow(details, 1, "Input type", identity.InputType);
        AddConfirmationRow(details, 2, "Book", identity.BookName);
        AddConfirmationRow(details, 3, "Book ID", identity.BookId);
        AddConfirmationRow(details, 4, "Project title", identity.DisplayTitle);
        AddConfirmationRow(details, 5, "Language", identity.Language);
        AddConfirmationRow(details, 6, "Resource", identity.Resource);
        AddConfirmationRow(details, 7, "Input file", inputPath);

        var outputLabel = new TextBlock
        {
            Text = "Output file",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 12, 0, 2)
        };
        var outputValue = new TextBlock
        {
            Text = outputPath,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = Avalonia.Media.FontFamily.Parse("Menlo, monospace")
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 100 };
        cancelButton.Click += (_, _) => dialog.Close(false);
        var confirmButton = new Button
        {
            Content = "Confirm and Clean",
            MinWidth = 150,
            Classes = { "accent" }
        };
        confirmButton.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);

        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Confirm Selected File",
                        FontSize = 24,
                        FontWeight = Avalonia.Media.FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = "Review this read-only identity before cleaning. The cleaner will process only this selected file.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new Border
                    {
                        Padding = new Avalonia.Thickness(16),
                        CornerRadius = new Avalonia.CornerRadius(10),
                        BorderThickness = new Avalonia.Thickness(1),
                        BorderBrush = Avalonia.Media.Brushes.SlateGray,
                        Child = details
                    },
                    outputLabel,
                    outputValue,
                    new TextBlock
                    {
                        Text = "No previous scan, book selection, or project metadata will be reused or changed by this confirmation.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    buttons
                }
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private static void AddConfirmationRow(Grid grid, int row, string label, string value)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
    }

    private async void ScanDocx_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Vm.CanScan)
        {
            return;
        }

        Vm.IsRunning = true;
        Vm.Status = "Scanning DOCX integrity...";

        try
        {
            using var effectiveInput = PrepareEffectiveInputDocx("scan");
            var scan = DocxScanService.Scan(effectiveInput.DocxPath, Vm.GetCanonProfile());
            Vm.ScanSummary = scan.BuildSummary();
            AddScanIssues(scan.Issues);
            if (await ConfirmLanguageCuesAsync(scan, "Scan"))
            {
                Vm.DetectedBooksLabel = BuildDetectedBooksLabel(scan);
                Vm.Status = "Scan completed.";
            }
        }
        catch (Exception ex)
        {
            Vm.Status = $"Scan failed: {ex.Message}";
            Vm.Issues.Add(new IssueItem("Error", "SCAN_FAILED", ex.Message, Vm.Issues.Count + 1, "Scan"));
        }
        finally
        {
            Vm.IsRunning = false;
        }
    }

    private async void StandardizeDocx_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Vm.CanScan)
        {
            return;
        }

        using var standardizeInput = PrepareEffectiveInputDocx("standardization");
        var scan = DocxScanService.Scan(standardizeInput.DocxPath, Vm.GetCanonProfile());
        Vm.ScanSummary = scan.BuildSummary();
        AddScanIssues(scan.Issues);
        if (!await ConfirmLanguageCuesAsync(scan, "Standardization"))
        {
            return;
        }
        Vm.DetectedBooksLabel = BuildDetectedBooksLabel(scan);

        var selectionOptions = BuildSelectionOptions(scan)
            .Where(o => !string.IsNullOrWhiteSpace(o.BookId))
            .ToList();

        if (selectionOptions.Count == 0)
        {
            selectionOptions = BuildManualBookSelectionOptions(scan);
            Vm.Issues.Add(new IssueItem(
                "Warning",
                "MANUAL_BOOK_SELECTION_REQUIRED",
                "No book heading was detected. Choose the intended book manually; this supports DOCX files that start mid-book with a chapter heading.",
                Vm.Issues.Count + 1,
                "Standardize"));
        }

        var dialog = new BookSelectionWindow(
            selectionOptions,
            "Select Books to Standardize",
            "Select the book(s) to include in standardized DOCX output",
            "Premium: you can standardize one or multiple detected books from this DOCX.",
            "Standardize Selected");
        var selected = await dialog.ShowDialog<List<BookSelectionOption>?>(this);
        if (selected is null || selected.Count == 0)
        {
            Vm.Status = "Standardization cancelled (no books selected).";
            return;
        }

        var selectedIds = selected
            .Where(s => !string.IsNullOrWhiteSpace(s.BookId))
            .Select(s => s.BookId!.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var selectedTitles = selected
            .Select(s => s.Title.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var standardizeSelectedSubset = true;

        var sourceName = Path.GetFileNameWithoutExtension(standardizeInput.DocxPath);
        var saveTarget = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Standardized DOCX Copy",
            SuggestedFileName = sourceName + "_standardized.docx",
            DefaultExtension = "docx",
            FileTypeChoices = [new FilePickerFileType("Word Document") { Patterns = ["*.docx"] }]
        });

        if (saveTarget is null)
        {
            return;
        }

        var inputPath = Path.GetFullPath(standardizeInput.DocxPath);
        var outputPath = Path.GetFullPath(saveTarget.Path.LocalPath);
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            Vm.Status = "Standardization blocked: output DOCX must be different from input DOCX.";
            Vm.Issues.Add(new IssueItem(
                "Error",
                "STANDARDIZE_SAME_PATH_BLOCKED",
                $"Input and output paths are the same: {outputPath}",
                Vm.Issues.Count + 1,
                "Standardize"));
            return;
        }

        Vm.IsRunning = true;
        Vm.Status = "Standardizing DOCX...";
        var workingInputCopy = Path.Combine(
            Path.GetTempPath(),
            $"usfm-standardize-{Guid.NewGuid():N}.docx");
        var outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        var workingOutputDir = Path.Combine(Path.GetTempPath(), $"usfm-standardize-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingOutputDir);
        var tempOutputPath = Path.Combine(
            workingOutputDir,
            $"{Path.GetFileNameWithoutExtension(outputPath)}.work-{Guid.NewGuid():N}.docx");
        var finalCanonReportPath = Path.Combine(
            outputDirectory,
            $"{Path.GetFileNameWithoutExtension(outputPath)}_canon-highlights.txt");

        try
        {
            // Work from an internal temp copy to avoid any source-file lock contention.
            File.Copy(inputPath, workingInputCopy, true);

            var result = DocxScanService.Standardize(
                workingInputCopy,
                tempOutputPath,
                Vm.GetCanonProfile(),
                selectedIds,
                selectedTitles,
                Vm.InferMissingVerseMarkers);

            var savedPath = outputPath;
            var reportPath = finalCanonReportPath;

            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.Copy(tempOutputPath, outputPath, true);
                if (File.Exists(result.CanonHighlightReportPath))
                {
                    File.Copy(result.CanonHighlightReportPath, finalCanonReportPath, true);
                }
            }
            catch (UnauthorizedAccessException)
            {
                var recoveredDir = BuildRecoveryDirectory(inputPath);
                Directory.CreateDirectory(recoveredDir);
                savedPath = Path.Combine(recoveredDir, Path.GetFileName(outputPath));
                reportPath = Path.Combine(
                    recoveredDir,
                    $"{Path.GetFileNameWithoutExtension(savedPath)}_canon-highlights.txt");
                File.Copy(tempOutputPath, savedPath, true);
                if (File.Exists(result.CanonHighlightReportPath))
                {
                    File.Copy(result.CanonHighlightReportPath, reportPath, true);
                }
                Vm.Issues.Add(new IssueItem(
                    "Warning",
                    "STANDARDIZE_FALLBACK_PATH_USED",
                    $"Selected output path was not writable. Saved standardized copy to fallback folder: {savedPath}",
                    Vm.Issues.Count + 1,
                    "Standardize"));
            }
            catch (IOException)
            {
                var recoveredDir = BuildRecoveryDirectory(inputPath);
                Directory.CreateDirectory(recoveredDir);
                savedPath = Path.Combine(recoveredDir, Path.GetFileName(outputPath));
                reportPath = Path.Combine(
                    recoveredDir,
                    $"{Path.GetFileNameWithoutExtension(savedPath)}_canon-highlights.txt");
                File.Copy(tempOutputPath, savedPath, true);
                if (File.Exists(result.CanonHighlightReportPath))
                {
                    File.Copy(result.CanonHighlightReportPath, reportPath, true);
                }
                Vm.Issues.Add(new IssueItem(
                    "Warning",
                    "STANDARDIZE_FALLBACK_PATH_USED",
                    $"Selected output path was not writable/in-use. Saved standardized copy to fallback folder: {savedPath}",
                    Vm.Issues.Count + 1,
                    "Standardize"));
            }

            Vm.Status = standardizeSelectedSubset
                ? $"Standardized selected books ({selected.Count}) copy saved: {savedPath}"
                : $"Standardized full detected set copy saved: {savedPath}";
            Vm.Issues.Add(new IssueItem(
                "Info",
                "DOCX_STANDARDIZED",
                $"Updated {result.ChangedTextNodeCount} text node(s) across {result.ChangedParagraphCount} paragraph(s).",
                Vm.Issues.Count + 1,
                "Standardize"));
            Vm.Issues.Add(new IssueItem(
                "Info",
                "CANON_HIGHLIGHT_REPORT",
                $"Canonical highlight report: {reportPath} ({result.CanonIssueCount} marker issue(s)).",
                Vm.Issues.Count + 1,
                "Standardize"));
            Vm.ConsoleLog =
                $"Standardized DOCX saved: {savedPath}{Environment.NewLine}" +
                $"Canon highlight report: {reportPath}{Environment.NewLine}" +
                $"Updated {result.ChangedTextNodeCount} text node(s) across {result.ChangedParagraphCount} paragraph(s).";

            var summaryDialog = new StandardizeResultWindow(
                result.ChangedParagraphCount,
                result.ChangedTextNodeCount,
                savedPath,
                reportPath);
            var useStandardized = await summaryDialog.ShowDialog<bool?>(this);
            if (useStandardized == true)
            {
                Vm.InputDocxPath = savedPath;
                Vm.InputSourceMode = "Single DOCX file";
                Vm.Status = $"Standardization completed. Using standardized DOCX for next conversion: {savedPath}";
            }
            else
            {
                Vm.Status = $"Standardization completed. Keeping original DOCX for conversion. Standardized copy saved: {savedPath}";
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            var recovered = TryStandardizeToRecoveryPath(
                workingInputCopy,
                inputPath,
                outputPath,
                selectedIds,
                selectedTitles,
                Vm.GetCanonProfile(),
                Vm.InferMissingVerseMarkers,
                out var recoveredDocxPath,
                out var recoveredCanonPath);
            if (recovered)
            {
                Vm.Status = $"Standardization saved to recovery folder: {recoveredDocxPath}";
                Vm.Issues.Add(new IssueItem(
                    "Warning",
                    "STANDARDIZE_RECOVERED_OUTPUT",
                    $"Primary destination was not writable. Recovered standardized DOCX: {recoveredDocxPath}",
                    Vm.Issues.Count + 1,
                    "Standardize"));
                Vm.Issues.Add(new IssueItem(
                    "Info",
                    "CANON_HIGHLIGHT_REPORT",
                    $"Canonical highlight report: {recoveredCanonPath}",
                    Vm.Issues.Count + 1,
                    "Standardize"));
                return;
            }

            Vm.Status = $"Standardization failed: file access denied. Close any open DOCX files and verify write permission. Details: {ex.Message}";
            Vm.Issues.Add(new IssueItem("Error", "STANDARDIZE_ACCESS_DENIED", ex.Message, Vm.Issues.Count + 1, "Standardize"));
        }
        catch (IOException ex)
        {
            var recovered = TryStandardizeToRecoveryPath(
                workingInputCopy,
                inputPath,
                outputPath,
                selectedIds,
                selectedTitles,
                Vm.GetCanonProfile(),
                Vm.InferMissingVerseMarkers,
                out var recoveredDocxPath,
                out var recoveredCanonPath);
            if (recovered)
            {
                Vm.Status = $"Standardization saved to recovery folder: {recoveredDocxPath}";
                Vm.Issues.Add(new IssueItem(
                    "Warning",
                    "STANDARDIZE_RECOVERED_OUTPUT",
                    $"Primary destination was unavailable. Recovered standardized DOCX: {recoveredDocxPath}",
                    Vm.Issues.Count + 1,
                    "Standardize"));
                Vm.Issues.Add(new IssueItem(
                    "Info",
                    "CANON_HIGHLIGHT_REPORT",
                    $"Canonical highlight report: {recoveredCanonPath}",
                    Vm.Issues.Count + 1,
                    "Standardize"));
                return;
            }

            Vm.Status = $"Standardization failed: output file is in use or not writable. Close open files and retry. Details: {ex.Message}";
            Vm.Issues.Add(new IssueItem("Error", "STANDARDIZE_IO_FAILED", ex.Message, Vm.Issues.Count + 1, "Standardize"));
        }
        catch (Exception ex)
        {
            Vm.Status = $"Standardization failed: {ex.Message}";
            Vm.Issues.Add(new IssueItem("Error", "STANDARDIZE_FAILED", ex.Message, Vm.Issues.Count + 1, "Standardize"));
        }
        finally
        {
            try
            {
                if (File.Exists(workingInputCopy))
                {
                    File.Delete(workingInputCopy);
                }

                if (File.Exists(tempOutputPath))
                {
                    File.Delete(tempOutputPath);
                }

                if (Directory.Exists(workingOutputDir))
                {
                    Directory.Delete(workingOutputDir, true);
                }

                var tempCanonReportPath = Path.Combine(
                    workingOutputDir,
                    $"{Path.GetFileNameWithoutExtension(tempOutputPath)}_canon-highlights.txt");
                if (File.Exists(tempCanonReportPath))
                {
                    File.Delete(tempCanonReportPath);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }

            Vm.IsRunning = false;
        }
    }

    private async void RunConversion_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Vm.CanRun)
        {
            return;
        }

        using var conversionInput = PrepareEffectiveInputDocx("conversion");
        var scan = DocxScanService.Scan(conversionInput.DocxPath, Vm.GetCanonProfile());
        Vm.ScanSummary = scan.BuildSummary();
        AddScanIssues(scan.Issues);
        if (!await ConfirmLanguageCuesAsync(scan, "Conversion"))
        {
            return;
        }
        Vm.DetectedBooksLabel = BuildDetectedBooksLabel(scan);

        var selectionOptions = BuildSelectionOptions(scan)
            .Where(o => !string.IsNullOrWhiteSpace(o.BookId))
            .ToList();

        if (selectionOptions.Count == 0)
        {
            selectionOptions = BuildManualBookSelectionOptions(scan);
            Vm.Issues.Add(new IssueItem(
                "Warning",
                "MANUAL_BOOK_SELECTION_REQUIRED",
                "No book heading was detected. Choose the intended book manually; this supports DOCX files that start mid-book with a chapter heading.",
                Vm.Issues.Count + 1,
                "Conversion"));
        }

        // Encourage single-book runs: default to first detected/inferred book.
        selectionOptions[0].IsSelected = true;
        var dialog = new BookSelectionWindow(
            selectionOptions,
            "Select Books for USFM Conversion",
            "Select the book(s) to convert to USFM",
            "You can convert one or multiple detected books.",
            "Convert Selected");
        var selected = await dialog.ShowDialog<List<BookSelectionOption>?>(this);

        if (selected is null || selected.Count == 0)
        {
            Vm.Status = "Conversion cancelled (no books selected).";
            return;
        }

        var selectedIds = selected
            .Where(s => !string.IsNullOrWhiteSpace(s.BookId))
            .Select(s => s.BookId!.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var selectedTitles = selected
            .Select(s => s.Title.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Vm.SelectedBooksLabel = BuildSelectedBooksLabel(selected);

        var converterProjectPath = TryFindConverterProjectPath();
        if (converterProjectPath is null)
        {
            Vm.Status = "Could not locate UsfmContractCli.csproj.";
            return;
        }

        var outputDir = Path.Combine(Vm.OutputFolderPath, Vm.OutputSetName);
        var reportPath = Path.Combine(outputDir, "conversion-report.txt");
        Directory.CreateDirectory(outputDir);

        Vm.IsRunning = true;
        Vm.Status = "Running conversion...";
        Vm.ConsoleLog = string.Empty;

        var singleMappedBook = selectedIds.Count == 1
            ? selectedIds.First()
            : null;
        var multiMappedBooks = selectedIds.Count > 1
            ? string.Join(",", selectedIds.OrderBy(x => x, StringComparer.Ordinal))
            : null;
        var hasMappedSelection = singleMappedBook is not null || multiMappedBooks is not null;
        var preserveNumberingArg = Vm.PreserveDocxVerseNumbering ? " --preserve-verse-markers" : string.Empty;
        const string languageProfileArg = " --profile global-starter";
        var langCodeArg = $" --lang-code {Vm.LanguageCode}";
        const string resourceIdArg = " --resource-id reg";
        const string producerTagArg = " --producer-tag uisprem";

        var args = singleMappedBook is not null
            ? $"run --project \"{converterProjectPath}\" -c Release -- " +
              $"docx-to-usfm \"{conversionInput.DocxPath}\" \"{outputDir}\" {Vm.RunMode} --split-books --book {singleMappedBook} --canon {MapCanonToCliToken(Vm.SelectedCanon)}{languageProfileArg}{langCodeArg}{resourceIdArg}{producerTagArg}{preserveNumberingArg} --report \"{reportPath}\""
            : multiMappedBooks is not null
            ? $"run --project \"{converterProjectPath}\" -c Release -- " +
              $"docx-to-usfm \"{conversionInput.DocxPath}\" \"{outputDir}\" {Vm.RunMode} --split-books --books {multiMappedBooks} --canon {MapCanonToCliToken(Vm.SelectedCanon)}{languageProfileArg}{langCodeArg}{resourceIdArg}{producerTagArg}{preserveNumberingArg} --report \"{reportPath}\""
            : $"run --project \"{converterProjectPath}\" -c Release -- " +
              $"docx-to-usfm \"{conversionInput.DocxPath}\" \"{outputDir}\" {Vm.RunMode} --split-books --canon {MapCanonToCliToken(Vm.SelectedCanon)}{languageProfileArg}{langCodeArg}{resourceIdArg}{producerTagArg}{preserveNumberingArg} --report \"{reportPath}\""
            ;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, evt) =>
            {
                if (evt.Data is null)
                {
                    return;
                }

                stdout.AppendLine(evt.Data);
            };
            process.ErrorDataReceived += (_, evt) =>
            {
                if (evt.Data is null)
                {
                    return;
                }

                stderr.AppendLine(evt.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var combined = stdout.ToString();
            if (stderr.Length > 0)
            {
                combined += Environment.NewLine + "[stderr]" + Environment.NewLine + stderr;
            }

            Vm.ConsoleLog = combined.Trim();
            ParseIssuesFromLog(Vm.ConsoleLog, Vm);

            var compatibilityUpdatedFiles = ApplyCompatibilityProfile(outputDir, Vm.CompatibilityProfile);
            if (compatibilityUpdatedFiles > 0)
            {
                Vm.Issues.Add(new IssueItem(
                    "Info",
                    "COMPATIBILITY_PROFILE_APPLIED",
                    $"{Vm.CompatibilityProfile} formatting applied to {compatibilityUpdatedFiles} file(s).",
                    Vm.Issues.Count + 1,
                    "Conversion"));
            }

            var chapterCapModifiedFiles = 0;
            if (Vm.LimitChapters)
            {
                chapterCapModifiedFiles = ApplyChapterCap(outputDir, Vm.GetEffectiveMaxChaptersPerBook());
                if (chapterCapModifiedFiles > 0)
                {
                    Vm.Issues.Add(new IssueItem(
                        "Info",
                        "CHAPTER_CAP_APPLIED",
                        $"Chapter cap applied (max {Vm.GetEffectiveMaxChaptersPerBook()}) on {chapterCapModifiedFiles} file(s).",
                        Vm.Issues.Count + 1,
                        "Conversion"));
                }
            }

            IReadOnlyList<BttwProjectPackageResult> projectPackages = [];
            if (Vm.GenerateBttwProjects)
            {
                projectPackages = BttwProjectPackageService.PackageDirectory(outputDir, Vm.LanguageCode);
                if (projectPackages.Count > 0)
                {
                    Vm.Issues.Add(new IssueItem(
                        "Info",
                        "BTTW_PROJECT_PACKAGES_CREATED",
                        $"Generated {projectPackages.Count} BTTW .tstudio project package(s).",
                        Vm.Issues.Count + 1,
                        "Conversion"));
                }
            }

            if (!hasMappedSelection)
            {
                var filtered = FilterOutputToSelectedBooks(outputDir, selectedIds, selectedTitles);
                Vm.Status = process.ExitCode == 0
                    ? $"Done. Kept {filtered.kept} selected book file(s), removed {filtered.removed}. Output: {outputDir}"
                    : $"Completed with issues (exit {process.ExitCode}). Kept {filtered.kept}, removed {filtered.removed}. Output: {outputDir}";
            }
            else if (multiMappedBooks is not null)
            {
                var generatedCount = Directory.Exists(outputDir)
                    ? Directory.GetFiles(outputDir, "*.usfm", SearchOption.TopDirectoryOnly).Length
                    : 0;
                Vm.Status = process.ExitCode == 0
                    ? $"Done. Converted selected books ({multiMappedBooks}). Generated {generatedCount} file(s). Output: {outputDir}"
                    : $"Completed with issues (exit {process.ExitCode}) for selected books ({multiMappedBooks}). Generated {generatedCount} file(s). Output: {outputDir}";
            }
            else
            {
                var generatedCount = Directory.Exists(outputDir)
                    ? Directory.GetFiles(outputDir, "*.usfm", SearchOption.TopDirectoryOnly).Length
                    : 0;
                Vm.Status = process.ExitCode == 0
                    ? $"Done. Converted book {singleMappedBook} only. Generated {generatedCount} file(s). Output: {outputDir}"
                    : $"Completed with issues (exit {process.ExitCode}) for book {singleMappedBook}. Generated {generatedCount} file(s). Output: {outputDir}";
            }

            var preflightIssues = UsfmPreflightService.ScanDirectory(outputDir);
            if (projectPackages.Count > 0)
            {
                Vm.ConsoleLog +=
                    $"{Environment.NewLine}Generated BTTW project package(s):{Environment.NewLine}" +
                    string.Join(Environment.NewLine, projectPackages.Select(package =>
                        $"- {package.TstudioPath} ({package.ProjectId}, {package.ChapterCount} chapter(s), {package.ChunkCount} chunk(s))"));
                Vm.Status += $" Generated {projectPackages.Count} .tstudio project package(s).";
            }

            foreach (var issue in preflightIssues)
            {
                Vm.Issues.Add(new IssueItem(
                    issue.Severity,
                    issue.Code,
                    issue.Message,
                    Vm.Issues.Count + 1,
                    "Preflight"));
            }

            if (preflightIssues.Count > 0)
            {
                Vm.Status += $" Preflight flagged {preflightIssues.Count} issue(s).";
            }
            else
            {
                Vm.Status += " Preflight checks passed.";
            }
        }
        catch (Exception ex)
        {
            Vm.Status = $"Run failed: {ex.Message}";
            Vm.ConsoleLog += Environment.NewLine + ex;
        }
        finally
        {
            Vm.IsRunning = false;
        }
    }

    private async void SaveTxtReport_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var saveTarget = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save TXT Report",
            SuggestedFileName = "usfm-integrity-report.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [FilePickerFileTypes.TextPlain]
        });

        if (saveTarget is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(Vm.ScanSummary);
        sb.AppendLine();
        sb.AppendLine(Vm.ConsoleLog ?? string.Empty);
        await File.WriteAllTextAsync(saveTarget.Path.LocalPath, sb.ToString());
        Vm.Status = "TXT report saved.";
    }

    private async void SaveJsonReport_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var saveTarget = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save JSON Report",
            SuggestedFileName = "usfm-integrity-report.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });

        if (saveTarget is null)
        {
            return;
        }

        await File.WriteAllTextAsync(saveTarget.Path.LocalPath, Vm.GetIssuesAsJson());
        Vm.Status = "JSON report saved.";
    }


    private void EnsureOutputDefaultsForInput(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(Vm.OutputFolderPath))
        {
            var baseDirectory = File.Exists(inputPath)
                ? Path.GetDirectoryName(Path.GetFullPath(inputPath))
                : Directory.Exists(inputPath)
                    ? Path.GetFullPath(inputPath)
                    : null;

            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                Vm.OutputFolderPath = baseDirectory;
            }
        }

        if (string.IsNullOrWhiteSpace(Vm.OutputSetName))
        {
            Vm.OutputSetName = "output_ui_run";
        }
    }

    private List<BookSelectionOption> BuildManualBookSelectionOptions(DocxScanResult scan)
    {
        var bookIds = scan.CanonProfileUsed switch
        {
            CanonProfile.ProtestantNt or CanonProfile.CatholicNt or CanonProfile.OrthodoxNt => NewTestamentBookIds,
            _ => OldTestamentBookIds
        };

        var inferred = InferBookIdFromInputPath(Vm.InputDocxPath, scan.CanonProfileUsed);
        return bookIds
            .Select(bookId => new BookSelectionOption
            {
                BookId = bookId,
                Title = GetEnglishBookName(bookId),
                IsSelected = string.Equals(bookId, inferred, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    private static string? InferBookIdFromInputPath(string path, CanonProfile canonProfile)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name) && Directory.Exists(path))
        {
            name = new DirectoryInfo(path).Name;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = Regex.Replace(name.ToUpperInvariant(), @"[^A-Z0-9]+", " ");
        var candidates = canonProfile switch
        {
            CanonProfile.ProtestantNt or CanonProfile.CatholicNt or CanonProfile.OrthodoxNt => NewTestamentBookIds,
            _ => OldTestamentBookIds
        };

        foreach (var bookId in candidates)
        {
            var english = Regex.Replace(GetEnglishBookName(bookId).ToUpperInvariant(), @"[^A-Z0-9]+", " ").Trim();
            if (normalized.Contains($" {bookId} ", StringComparison.Ordinal)
                || normalized.StartsWith($"{bookId} ", StringComparison.Ordinal)
                || normalized.EndsWith($" {bookId}", StringComparison.Ordinal)
                || normalized.Contains(english, StringComparison.Ordinal))
            {
                return bookId;
            }
        }

        return null;
    }

    private void AddScanIssues(IReadOnlyList<ScanIssue> issues)
    {
        foreach (var issue in issues)
        {
            Vm.Issues.Add(new IssueItem(issue.Severity, issue.Code, issue.Message, Vm.Issues.Count + 1, "Scan"));
        }
    }

    private static void ParseIssuesFromLog(string log, MainWindowViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return;
        }

        var regex = new Regex("^\\[(?<sev>[^\\]]+)\\]\\s+(?<code>[^:]+):\\s+(?<msg>.*)$", RegexOptions.Multiline);
        foreach (Match match in regex.Matches(log))
        {
            var severity = match.Groups["sev"].Value.Trim();
            var code = match.Groups["code"].Value.Trim();
            var message = match.Groups["msg"].Value.Trim();
            vm.Issues.Add(new IssueItem(severity, code, message, vm.Issues.Count + 1, "Conversion"));
        }
    }

    private static string? TryFindConverterProjectPath()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(current, "..", "..", "..", "..", "UsfmContractCli", "UsfmContractCli.csproj"));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName ?? string.Empty;
        }

        return null;
    }

    private EffectiveInputDocx PrepareEffectiveInputDocx(string operationName)
    {
        if (string.IsNullOrWhiteSpace(Vm.InputDocxPath))
        {
            throw new InvalidOperationException("No input DOCX or folder selected.");
        }

        if (File.Exists(Vm.InputDocxPath))
        {
            return EffectiveInputDocx.Direct(Vm.InputDocxPath);
        }

        if (!Directory.Exists(Vm.InputDocxPath))
        {
            throw new FileNotFoundException($"Input source not found: {Vm.InputDocxPath}");
        }

        var chapterFiles = Directory
            .GetFiles(Vm.InputDocxPath, "*.docx", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .OrderBy(GetDocxChapterSortKey)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chapterFiles.Count == 0)
        {
            throw new InvalidOperationException("Selected folder does not contain any .docx files.");
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{FolderMergePrefix}{Guid.NewGuid():N}.docx");
        MergeDocxFiles(chapterFiles, tempPath);
        Vm.Issues.Add(new IssueItem(
            "Info",
            "DOCX_FOLDER_MERGED",
            $"Merged {chapterFiles.Count} DOCX file(s) from folder input for {operationName}.",
            Vm.Issues.Count + 1,
            "Input"));
        return EffectiveInputDocx.Temporary(tempPath, chapterFiles.Count);
    }

    private static int GetDocxChapterSortKey(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var match = Regex.Match(name, @"(?<!\p{L})(\d{1,3})(?!\p{L})");
        return match.Success && int.TryParse(match.Groups[1].Value, out var value)
            ? value
            : int.MaxValue;
    }

    private static void MergeDocxFiles(IReadOnlyList<string> chapterFiles, string outputDocxPath)
    {
        if (chapterFiles.Count == 0)
        {
            throw new InvalidOperationException("No chapter DOCX files available to merge.");
        }

        File.Copy(chapterFiles[0], outputDocxPath, true);

        using var outputArchive = ZipFile.Open(outputDocxPath, ZipArchiveMode.Update);
        var outputDocumentEntry = outputArchive.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("word/document.xml is missing in the output DOCX.");

        XDocument outputDocument;
        using (var stream = outputDocumentEntry.Open())
        {
            outputDocument = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        var outputBody = outputDocument.Root?.Element(W + "body")
            ?? throw new InvalidOperationException("w:body is missing in the output DOCX.");

        var sectionProperties = outputBody.Element(W + "sectPr");
        sectionProperties?.Remove();

        foreach (var chapterFile in chapterFiles.Skip(1))
        {
            using var archive = ZipFile.OpenRead(chapterFile);
            var documentEntry = archive.GetEntry("word/document.xml");
            if (documentEntry is null)
            {
                continue;
            }

            using var stream = documentEntry.Open();
            var chapterDocument = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            var chapterBody = chapterDocument.Root?.Element(W + "body");
            if (chapterBody is null)
            {
                continue;
            }

            foreach (var node in chapterBody.Nodes())
            {
                if (node is XElement element && element.Name == W + "sectPr")
                {
                    continue;
                }

                outputBody.Add(CloneNode(node));
            }
        }

        if (sectionProperties is not null)
        {
            outputBody.Add(sectionProperties);
        }

        outputDocumentEntry.Delete();
        var replacementEntry = outputArchive.CreateEntry("word/document.xml", CompressionLevel.Optimal);
        using var replacementStream = replacementEntry.Open();
        outputDocument.Save(replacementStream);
    }

    private static XNode CloneNode(XNode node)
    {
        return node switch
        {
            XElement element => new XElement(element),
            XCData cdata => new XCData(cdata.Value),
            XText text => new XText(text.Value),
            XComment comment => new XComment(comment.Value),
            XProcessingInstruction pi => new XProcessingInstruction(pi.Target, pi.Data),
            XDocumentType dt => new XDocumentType(dt.Name, dt.PublicId, dt.SystemId, dt.InternalSubset),
            _ => throw new NotSupportedException($"Unsupported XML node type for DOCX merge: {node.GetType().Name}")
        };
    }

    private sealed class EffectiveInputDocx : IDisposable
    {
        private EffectiveInputDocx(string docxPath, bool isTemporary, int sourceFileCount)
        {
            DocxPath = docxPath;
            IsTemporary = isTemporary;
            SourceFileCount = sourceFileCount;
        }

        public string DocxPath { get; }
        public bool IsTemporary { get; }
        public int SourceFileCount { get; }

        public static EffectiveInputDocx Direct(string path) => new(path, false, 1);
        public static EffectiveInputDocx Temporary(string path, int sourceFileCount) => new(path, true, sourceFileCount);

        public void Dispose()
        {
            if (!IsTemporary)
            {
                return;
            }

            try
            {
                if (File.Exists(DocxPath))
                {
                    File.Delete(DocxPath);
                }
            }
            catch
            {
                // Best-effort temp cleanup only.
            }
        }
    }

    private static (int kept, int removed) FilterOutputToSelectedBooks(
        string outputDir,
        IReadOnlySet<string> selectedBookIds,
        IReadOnlySet<string> selectedTitles)
    {
        if (!Directory.Exists(outputDir))
        {
            return (0, 0);
        }

        var kept = 0;
        var removed = 0;
        var files = Directory.GetFiles(outputDir, "*.usfm", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            var (bookId, mtTitle) = ReadUsfmBookIdentity(file);

            var keepById = !string.IsNullOrWhiteSpace(bookId) && selectedBookIds.Contains(bookId);
            var keepByTitle = !string.IsNullOrWhiteSpace(mtTitle) && selectedTitles.Contains(mtTitle);
            var keep = keepById || keepByTitle;

            if (keep)
            {
                kept++;
            }
            else
            {
                File.Delete(file);
                removed++;
            }
        }

        return (kept, removed);
    }

    private static (string? id, string? mtTitle) ReadUsfmBookIdentity(string path)
    {
        string? id = null;
        string? mt = null;

        foreach (var line in File.ReadLines(path).Take(24))
        {
            if (id is null && line.StartsWith("\\id ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    id = parts[1].Trim().ToUpperInvariant();
                }
            }

            if (mt is null && line.StartsWith("\\mt ", StringComparison.Ordinal))
            {
                mt = line[4..].Trim();
            }

            if (id is not null && mt is not null)
            {
                break;
            }
        }

        return (id, mt);
    }

    private static string BuildSelectedBooksLabel(IReadOnlyCollection<BookSelectionOption> selected)
    {
        var ids = selected
            .Where(s => !string.IsNullOrWhiteSpace(s.BookId))
            .Select(s => s.BookId!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return $"Selected for conversion: {selected.Count} item(s) (unmapped headings)";
        }

        return $"Selected for conversion: {string.Join(", ", ids)}";
    }

    private string BuildDetectedBooksLabel(DocxScanResult scan)
    {
        var ids = scan.Books
            .Select(book => GetEffectiveBookId(book.Title, book.BookId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count > 0)
        {
            return $"Detected books: {string.Join(", ", ids)}";
        }

        return scan.Books.Count > 0
            ? $"Detected books: {scan.Books.Count} heading(s) found; manual mapping required"
            : "Detected books: (none)";
    }

    private async Task<bool> ConfirmLanguageCuesAsync(DocxScanResult scan, string operationName)
    {
        var cue = BuildLanguageCue(scan);
        if (cue is null)
        {
            return true;
        }

        var fingerprint = $"{Path.GetFullPath(Vm.InputDocxPath)}|{cue.Fingerprint}";
        if (string.Equals(_lastLanguageCueConfirmationFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return true;
        }

        var dialog = new LanguageCueConfirmWindow(cue.Summary, cue.Details, cue.OverrideSeeds);
        var decision = await dialog.ShowDialog<LanguageCueDecision?>(this);
        if (decision?.Confirmed == true)
        {
            if (decision.OverridesByTitle.Count > 0)
            {
                foreach (var entry in decision.OverridesByTitle)
                {
                    _bookIdOverridesByTitle[entry.Key] = entry.Value;
                }

                var distinctOverrideIds = decision.OverridesByTitle.Values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (distinctOverrideIds.Count == 1)
                {
                    _sessionForcedBookId = distinctOverrideIds[0];
                }
            }

            _lastLanguageCueConfirmationFingerprint = fingerprint;
            Vm.Issues.Add(new IssueItem(
                "Info",
                "LANGUAGE_CUE_CONFIRMED",
                decision.OverridesByTitle.Count > 0
                    ? $"User confirmed detected cues with mapping override(s): {cue.Summary}"
                    : $"User confirmed detected cues: {cue.Summary}",
                Vm.Issues.Count + 1,
                operationName));
            return true;
        }

        Vm.Status = $"{operationName} cancelled: please verify language/book cues and canon selection.";
        Vm.Issues.Add(new IssueItem(
            "Warning",
            "LANGUAGE_CUE_REJECTED",
            $"User rejected detected cues: {cue.Summary}",
            Vm.Issues.Count + 1,
            operationName));
        return false;
    }

    private LanguageCueInfo? BuildLanguageCue(DocxScanResult scan)
    {
        var titles = scan.Books
            .Select(b => b.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(12)
            .ToList();
        if (titles.Count == 0)
        {
            return null;
        }

        var sample = string.Join(" ", titles);
        var arabic = CountCharsInRange(sample, '\u0600', '\u06FF');
        var cyrillic = CountCharsInRange(sample, '\u0400', '\u04FF');
        var latin = sample.Count(ch => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'));
        var dominantScript = arabic >= cyrillic && arabic >= latin && arabic > 0
            ? "Arabic-derived script (RTL likely)"
            : cyrillic >= latin && cyrillic > 0
            ? "Cyrillic script"
            : latin > 0
            ? "Latin script"
            : "Mixed/unknown script";

        var detectedBookIds = scan.Books
            .Select(b => GetEffectiveBookId(b.Title, b.BookId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var mapped = detectedBookIds.Count == 0 ? "(none)" : string.Join(",", detectedBookIds);
        var mappingLines = scan.Books
            .Where(b => !string.IsNullOrWhiteSpace(b.Title))
            .Select(b =>
            {
                var title = b.Title.Trim();
                var effectiveBookId = GetEffectiveBookId(title, b.BookId);
                if (string.IsNullOrWhiteSpace(effectiveBookId))
                {
                    return $"- Detected label: {title} -> Unmapped";
                }

                var bookId = effectiveBookId.Trim().ToUpperInvariant();
                return $"- Detected label: {title} -> {GetEnglishBookName(bookId)} ({bookId})";
            })
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToList();
        var overrideSeeds = string.IsNullOrWhiteSpace(_sessionForcedBookId)
            ? scan.Books
            .Where(b => !string.IsNullOrWhiteSpace(b.Title))
            .Select(b =>
            {
                var currentBookId = GetEffectiveBookId(b.Title, b.BookId);
                var currentLabel = string.IsNullOrWhiteSpace(currentBookId)
                    ? "Current mapping: Unmapped"
                    : $"Current mapping: {GetEnglishBookName(currentBookId)} ({currentBookId})";
                return new LanguageCueOverrideSeed(
                    b.Title.Trim(),
                    currentBookId,
                    currentLabel,
                    BuildBookChoices(scan.CanonProfileUsed, currentBookId));
            })
            .DistinctBy(seed => seed.Title, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList()
            : [];

        var forcedBookSuffix = string.IsNullOrWhiteSpace(_sessionForcedBookId)
            ? string.Empty
            : $" Session override active: {GetEnglishBookName(_sessionForcedBookId)} ({_sessionForcedBookId}).";
        var summary = $"Detected cues: {dominantScript}; mapped books: {mapped}.{forcedBookSuffix}";
        var details =
            "Please confirm detected language/script cues before continuing.\n" +
            $"Dominant script guess: {dominantScript}\n" +
            $"Mapped books: {mapped}\n" +
            (!string.IsNullOrWhiteSpace(_sessionForcedBookId)
                ? $"Session override active: {GetEnglishBookName(_sessionForcedBookId)} ({_sessionForcedBookId})\n"
                : string.Empty) +
            (mappingLines.Count > 0
                ? $"Detected label mapping:\n{string.Join("\n", mappingLines)}\n\n"
                : string.Empty) +
            $"Scan summary: {scan.BuildSummary()}\n\n" +
            "If this is not correct, stop and adjust the source DOCX/profile/canon before conversion.";
        var fingerprint = $"{dominantScript}|{mapped}|{titles.Count}|{_sessionForcedBookId ?? string.Empty}";
        return new LanguageCueInfo(summary, details, fingerprint, overrideSeeds);
    }

    private List<BookSelectionOption> BuildSelectionOptions(DocxScanResult scan)
    {
        var options = scan.Books
            .Select(b => new BookSelectionOption
            {
                BookId = GetEffectiveBookId(b.Title, b.BookId),
                Title = b.Title,
                IsSelected = false
            })
            .Where(o => !string.IsNullOrWhiteSpace(o.BookId))
            .ToList();

        if (!string.IsNullOrWhiteSpace(_sessionForcedBookId))
        {
            return
            [
                new BookSelectionOption
                {
                    BookId = _sessionForcedBookId,
                    Title = GetEnglishBookName(_sessionForcedBookId),
                    IsSelected = true
                }
            ];
        }

        return options
            .GroupBy(o => o.BookId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private string? GetEffectiveBookId(string? title, string? detectedBookId)
    {
        if (!string.IsNullOrWhiteSpace(_sessionForcedBookId))
        {
            return _sessionForcedBookId;
        }

        var normalizedTitle = title?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedTitle)
            && _bookIdOverridesByTitle.TryGetValue(normalizedTitle, out var overrideBookId)
            && !string.IsNullOrWhiteSpace(overrideBookId))
        {
            return overrideBookId.Trim().ToUpperInvariant();
        }

        return string.IsNullOrWhiteSpace(detectedBookId)
            ? null
            : detectedBookId.Trim().ToUpperInvariant();
    }

    private static IReadOnlyList<LanguageCueBookChoice> BuildBookChoices(CanonProfile canonProfile, string? currentBookId)
    {
        var bookIds = canonProfile switch
        {
            CanonProfile.ProtestantNt or CanonProfile.CatholicNt or CanonProfile.OrthodoxNt => NewTestamentBookIds,
            _ => OldTestamentBookIds
        };

        var choices = new List<LanguageCueBookChoice>();
        foreach (var bookId in bookIds)
        {
            choices.Add(new LanguageCueBookChoice(bookId, $"{GetEnglishBookName(bookId)} ({bookId})"));
        }

        if (!string.IsNullOrWhiteSpace(currentBookId)
            && choices.All(c => !string.Equals(c.BookId, currentBookId, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Insert(0, new LanguageCueBookChoice(currentBookId, $"{GetEnglishBookName(currentBookId)} ({currentBookId})"));
        }

        return choices;
    }

    private static readonly string[] OldTestamentBookIds =
    [
        "GEN","EXO","LEV","NUM","DEU","JOS","JDG","RUT","1SA","2SA","1KI","2KI","1CH","2CH","EZR","NEH","EST","JOB","PSA","PRO",
        "ECC","SNG","ISA","JER","LAM","EZK","DAN","HOS","JOL","AMO","OBA","JON","MIC","NAM","HAB","ZEP","HAG","ZEC","MAL"
    ];

    private static readonly string[] NewTestamentBookIds =
    [
        "MAT","MRK","LUK","JHN","ACT","ROM","1CO","2CO","GAL","EPH","PHP","COL","1TH","2TH","1TI","2TI","TIT","PHM","HEB","JAS","1PE","2PE","1JN","2JN","3JN","JUD","REV"
    ];

    private static string GetEnglishBookName(string bookId)
    {
        return bookId switch
        {
            "GEN" => "Genesis",
            "EXO" => "Exodus",
            "LEV" => "Leviticus",
            "NUM" => "Numbers",
            "DEU" => "Deuteronomy",
            "JOS" => "Joshua",
            "JDG" => "Judges",
            "RUT" => "Ruth",
            "1SA" => "1 Samuel",
            "2SA" => "2 Samuel",
            "1KI" => "1 Kings",
            "2KI" => "2 Kings",
            "1CH" => "1 Chronicles",
            "2CH" => "2 Chronicles",
            "EZR" => "Ezra",
            "NEH" => "Nehemiah",
            "EST" => "Esther",
            "JOB" => "Job",
            "PSA" => "Psalms",
            "PRO" => "Proverbs",
            "ECC" => "Ecclesiastes",
            "SNG" => "Song of Songs",
            "ISA" => "Isaiah",
            "JER" => "Jeremiah",
            "LAM" => "Lamentations",
            "EZK" => "Ezekiel",
            "DAN" => "Daniel",
            "HOS" => "Hosea",
            "JOL" => "Joel",
            "AMO" => "Amos",
            "OBA" => "Obadiah",
            "JON" => "Jonah",
            "MIC" => "Micah",
            "NAM" => "Nahum",
            "HAB" => "Habakkuk",
            "ZEP" => "Zephaniah",
            "HAG" => "Haggai",
            "ZEC" => "Zechariah",
            "MAL" => "Malachi",
            "MAT" => "Matthew",
            "MRK" => "Mark",
            "LUK" => "Luke",
            "JHN" => "John",
            "ACT" => "Acts",
            "ROM" => "Romans",
            "1CO" => "1 Corinthians",
            "2CO" => "2 Corinthians",
            "GAL" => "Galatians",
            "EPH" => "Ephesians",
            "PHP" => "Philippians",
            "COL" => "Colossians",
            "1TH" => "1 Thessalonians",
            "2TH" => "2 Thessalonians",
            "1TI" => "1 Timothy",
            "2TI" => "2 Timothy",
            "TIT" => "Titus",
            "PHM" => "Philemon",
            "HEB" => "Hebrews",
            "JAS" => "James",
            "1PE" => "1 Peter",
            "2PE" => "2 Peter",
            "1JN" => "1 John",
            "2JN" => "2 John",
            "3JN" => "3 John",
            "JUD" => "Jude",
            "REV" => "Revelation",
            _ => bookId
        };
    }

    private static int CountCharsInRange(string text, char start, char end)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch >= start && ch <= end)
            {
                count++;
            }
        }

        return count;
    }

    private sealed record LanguageCueInfo(
        string Summary,
        string Details,
        string Fingerprint,
        IReadOnlyList<LanguageCueOverrideSeed> OverrideSeeds);

    private static string BuildRecoveryDirectory(string inputPath)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var baseDir = string.IsNullOrWhiteSpace(docs) ? Path.GetTempPath() : docs;
        return Path.Combine(baseDir, "UsfmIntegrityStudio-Recovered");
    }

    private static bool TryStandardizeToRecoveryPath(
        string workingInputCopy,
        string inputPath,
        string outputPath,
        IReadOnlySet<string>? selectedBookIds,
        IReadOnlySet<string>? selectedBookTitles,
        CanonProfile canonProfile,
        bool inferMissingVerseMarkers,
        out string recoveredDocxPath,
        out string recoveredCanonReportPath)
    {
        recoveredDocxPath = string.Empty;
        recoveredCanonReportPath = string.Empty;
        try
        {
            var recoveredDir = BuildRecoveryDirectory(inputPath);
            Directory.CreateDirectory(recoveredDir);
            recoveredDocxPath = Path.Combine(recoveredDir, Path.GetFileName(outputPath));
            var recoveredResult = DocxScanService.Standardize(
                workingInputCopy,
                recoveredDocxPath,
                canonProfile,
                selectedBookIds,
                selectedBookTitles,
                inferMissingVerseMarkers);
            recoveredCanonReportPath = recoveredResult.CanonHighlightReportPath;
            return true;
        }
        catch
        {
            recoveredDocxPath = string.Empty;
            recoveredCanonReportPath = string.Empty;
            return false;
        }
    }

    private static int ApplyCompatibilityProfile(string outputDir, string compatibilityProfile)
    {
        if (!Directory.Exists(outputDir))
        {
            return 0;
        }

        var updated = 0;
        var files = Directory.GetFiles(outputDir, "*.usfm", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            var original = File.ReadAllLines(file).ToList();
            var transformed = compatibilityProfile.Equals("BTTW legacy compatibility", StringComparison.OrdinalIgnoreCase)
                ? ApplyBttwLegacyCompatibility(original)
                : ApplyParatextCompatibility(original);

            if (!original.SequenceEqual(transformed, StringComparer.Ordinal))
            {
                File.WriteAllLines(file, transformed);
                updated++;
            }
        }

        return updated;
    }

    private static int ApplyChapterCap(string outputDir, int maxChapters)
    {
        if (!Directory.Exists(outputDir) || maxChapters < 1)
        {
            return 0;
        }

        var modified = 0;
        var files = Directory.GetFiles(outputDir, "*.usfm", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            var kept = new List<string>(lines.Length);
            var stop = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("\\c ", StringComparison.Ordinal))
                {
                    var token = line[3..].Trim();
                    if (int.TryParse(token, out var chapter) && chapter > maxChapters)
                    {
                        stop = true;
                    }
                }

                if (stop)
                {
                    break;
                }

                kept.Add(line);
            }

            if (kept.Count < lines.Length)
            {
                File.WriteAllLines(file, kept);
                modified++;
            }
        }

        return modified;
    }

    private static List<string> ApplyParatextCompatibility(IReadOnlyList<string> lines)
    {
        var normalized = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (line.StartsWith("\\ide ", StringComparison.Ordinal))
            {
                normalized.Add("\\ide UTF-8");
                continue;
            }

            if (TrySplitCombinedChapterIntro(line, out var chapterLabel, out var intro))
            {
                normalized.Add(chapterLabel);
                normalized.Add($"\\d {intro}");
                continue;
            }

            normalized.Add(line);
        }

        return normalized;
    }

    private static List<string> ApplyBttwLegacyCompatibility(IReadOnlyList<string> lines)
    {
        var firstPass = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (TrySplitCombinedChapterIntro(line, out var chapterLabel, out var intro))
            {
                firstPass.Add(chapterLabel);
                firstPass.Add($"\\d {intro}");
                continue;
            }

            firstPass.Add(line);
        }

        var result = new List<string>(firstPass.Count + 20);
        var inChapter = false;
        var seenVerse = false;
        var hasParagraph = false;

        foreach (var line in firstPass)
        {
            if (line.StartsWith("\\c ", StringComparison.Ordinal))
            {
                inChapter = true;
                seenVerse = false;
                hasParagraph = false;
                result.Add(line);
                continue;
            }

            if (!inChapter)
            {
                result.Add(line);
                continue;
            }

            if (line.StartsWith("\\cl ", StringComparison.Ordinal))
            {
                result.Add(line);
                continue;
            }

            if (line == "\\p")
            {
                hasParagraph = true;
                result.Add(line);
                continue;
            }

            if (line.StartsWith("\\v ", StringComparison.Ordinal))
            {
                if (!hasParagraph)
                {
                    result.Add("\\p");
                    hasParagraph = true;
                }

                seenVerse = true;
                result.Add(line);
                continue;
            }

            if (!seenVerse && !line.StartsWith("\\", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(line))
            {
                if (!hasParagraph)
                {
                    result.Add("\\p");
                    hasParagraph = true;
                }

                result.Add($"\\d {line.Trim()}");
                continue;
            }

            if (!seenVerse && line.StartsWith("\\d ", StringComparison.Ordinal) && !hasParagraph)
            {
                result.Add("\\p");
                hasParagraph = true;
            }

            result.Add(line);
        }

        return result;
    }

    private static bool TrySplitCombinedChapterIntro(string line, out string chapterLabelLine, out string intro)
    {
        chapterLabelLine = string.Empty;
        intro = string.Empty;
        if (!line.StartsWith("\\cl ", StringComparison.Ordinal))
        {
            return false;
        }

        var markerText = line[4..].Trim();
        var match = Regex.Match(markerText, "^(?<chapter>.+?\\b\\d+)\\.\\s+(?<intro>.+)$");
        if (!match.Success)
        {
            return false;
        }

        chapterLabelLine = $"\\cl {match.Groups["chapter"].Value.Trim()}";
        intro = match.Groups["intro"].Value.Trim();
        return !string.IsNullOrWhiteSpace(chapterLabelLine) && !string.IsNullOrWhiteSpace(intro);
    }

    private static string MapCanonToCliToken(string selectedCanon)
    {
        return selectedCanon switch
        {
            "Catholic OT" => "catholic-ot",
            "Orthodox OT" => "orthodox-ot",
            "Protestant NT" => "protestant-nt",
            "Catholic NT" => "catholic-nt",
            "Orthodox NT" => "orthodox-nt",
            _ => "protestant-ot"
        };
    }

}
