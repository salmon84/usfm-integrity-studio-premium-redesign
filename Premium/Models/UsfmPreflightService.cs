using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace UsfmIntegrityStudio.Models;

internal sealed record UsfmPreflightIssue(string Severity, string Code, string Message, string FilePath);

internal static class UsfmPreflightService
{
    private static readonly Regex StrandedBackslashRegex = new(
        @"\\\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex JammedVerseNumberRegex = new(
        @"\\v\s+[-0-9]+[^-\s0-9]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex ForeignUsfmCodeRegex = new(
        @"\\[^a-z\+\s]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static IReadOnlyList<UsfmPreflightIssue> ScanDirectory(string outputDir)
    {
        var issues = new List<UsfmPreflightIssue>();
        if (!Directory.Exists(outputDir))
        {
            return issues;
        }

        foreach (var path in Directory.GetFiles(outputDir, "*.usfm", SearchOption.TopDirectoryOnly))
        {
            ScanFile(path, issues);
        }

        return issues;
    }

    private static void ScanFile(string path, ICollection<UsfmPreflightIssue> issues)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length < 200)
            {
                issues.Add(new UsfmPreflightIssue(
                    "Warning",
                    "USFM_SMALL_FILE",
                    $"{Path.GetFileName(path)} is very small ({info.Length} bytes). Check for incomplete output.",
                    path));
            }

            var text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false));

            if (StrandedBackslashRegex.IsMatch(text))
            {
                issues.Add(new UsfmPreflightIssue(
                    "Warning",
                    "USFM_STRANDED_BACKSLASH",
                    $"{Path.GetFileName(path)} contains a backslash followed by whitespace.",
                    path));
            }

            if (JammedVerseNumberRegex.Match(text) is { Success: true } jammed)
            {
                issues.Add(new UsfmPreflightIssue(
                    "Warning",
                    "USFM_JAMMED_VERSE_NUMBER",
                    $"{Path.GetFileName(path)} contains a verse marker without required spacing near '{jammed.Value.Trim()}'.",
                    path));
            }

            foreach (Match match in ForeignUsfmCodeRegex.Matches(text))
            {
                issues.Add(new UsfmPreflightIssue(
                    "Warning",
                    "USFM_FOREIGN_MARKER_CHAR",
                    $"{Path.GetFileName(path)} contains an unexpected marker sequence '{match.Value}'.",
                    path));
            }
        }
        catch (Exception ex)
        {
            issues.Add(new UsfmPreflightIssue(
                "Error",
                "USFM_PREFLIGHT_READ_FAILED",
                $"Failed to scan {Path.GetFileName(path)}: {ex.Message}",
                path));
        }
    }
}
