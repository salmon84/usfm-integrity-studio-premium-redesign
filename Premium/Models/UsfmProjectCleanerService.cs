using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UsfmTools.Text;

namespace UsfmIntegrityStudio.Models;

public sealed record UsfmCleanResult(
    string InputPath,
    string OutputPath,
    int FilesScanned,
    int FilesChanged,
    int InlineDuplicateMarkersRemoved,
    int PendingLineDuplicateMarkersRemoved,
    int StrayLeadingVerseMarkersRemoved,
    int VisibleVerseMarkersNormalized,
    int SpacingFixes,
    int StraightQuotesConverted,
    int StraightSingleQuotesConverted,
    int DirectionalDoubleQuotesRepaired,
    int DirectionalSingleQuotesRepaired,
    int UnpairedDoubleQuoteClosersRepaired,
    int DirectSpeechFixes,
    int ByteOrderMarksRemoved,
    int UnsafeControlCharsRemoved,
    int StructuralChunkFilesRemoved,
    int ManifestFinishedChunksRemoved,
    int VerificationIssueCount,
    string ReportPath,
    IReadOnlyList<string> StructuralRepairs,
    IReadOnlyList<string> VerificationIssues);

internal sealed record TstudioManifest(string ProjectId, string ProjectName, string ResourceName, string Format);

internal readonly record struct VerseSegment(int Number, string Text);

public static class UsfmProjectCleanerService
{
    private const string BttwGeneratorName = "ts-desktop";
    private const string BttwModifiedBuild = "1073x";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly Regex InlineDuplicateRegex = new(
        @"(\\v\s*)(\d+)(\s+)([0-9۰-۹٠-٩ا]+)\s*([.)۔:]+(?:\s*[.)۔:]+)*)\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LeadingNumberRegex = new(
        @"^\s*([0-9۰-۹٠-٩ا]+)\s*([.)۔:]+(?:\s*[.)۔:]+)*)?\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VisibleVerseMarkerTokenRegex = new(
        @"(?<![\p{L}\p{M}\p{N}])([0-9۰-۹٠-٩]{1,3})\s*([.)۔:]+(?:\s*[.)۔:]+)*)\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PendingVerseRegex = new(
        @"(\\v\s*)(\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LeadingVerseMarkerRegex = new(
        @"^\s*\\v\s*(\d+)\s+(?=\\v\s*\d+\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ReversedVisibleVerseMarkerRegex = new(
        @"(?<![\\A-Za-z])/?([0-9۰-۹٠-٩]+)\s+v\s*[.)۔:]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LooseVisibleVerseMarkerRegex = new(
        @"(?<![\\A-Za-z])\bv\s+([0-9۰-۹٠-٩]+)\s*[.)۔:]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VerseMarkerRegex = new(
        @"\\v\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex UsfmVerseLineRegex = new(
        @"^(\s*\\v\s*)(\d+)(\s+)(.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex UsfmChapterMarkerRegex = new(
        @"(?<!\S)\\c\s+\d+\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UsfmVerseMarkerRegex = new(
        @"(?<!\S)\\v\s*\d+\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BidiControlRegex = new(
        @"[\u200e\u200f\u202a-\u202e\u2066-\u2069]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmptyVerseBodyJunkRegex = new(
        @"[\s\u200e\u200f\u202a-\u202e\ufeff۔.،,؛:!؟?()\[\]{}‘’""'\-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChapterTitleSpacingRegex = new(
        @"^\s*(باب)\s*([0-9۰-۹٠-٩]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OpeningDoubleQuoteAfterSentenceRegex = new(
        @"([.!?؟۔])(\s+)”(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OpeningSingleQuoteAfterSentenceRegex = new(
        @"([.!?؟۔])(\s+)‘(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnsafeControlCharRegex = new(
        @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static UsfmCleanResult Clean(string inputPath, string outputPath, CanonProfile canonProfile = CanonProfile.ProtestantOt)
    {
        var input = Path.GetFullPath(inputPath);
        var output = Path.GetFullPath(outputPath);
        var extension = Path.GetExtension(input).ToLowerInvariant();

        return extension switch
        {
            ".tstudio" => CleanTstudio(input, output, canonProfile),
            ".usfm" or ".txt" => CleanTextFile(input, output),
            _ => throw new InvalidOperationException("Unsupported cleaner input. Select a .usfm, .txt, or .tstudio file.")
        };
    }

    public static UsfmCleanResult CleanTstudioToUsfm(string inputPath, string outputPath, CanonProfile canonProfile = CanonProfile.ProtestantOt)
    {
        var input = Path.GetFullPath(inputPath);
        var output = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(input), ".tstudio", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("USFM export requires a .tstudio input project.");
        }

        return CleanTstudio(input, output, canonProfile, exportUsfm: true);
    }

    private static UsfmCleanResult CleanTextFile(string inputPath, string outputPath)
    {
        var stats = new CleanStats();
        var (text, hadBom) = ReadCleanableText(inputPath, stats);
        var cleaned = CleanText(text, stats);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(outputPath, cleaned, Utf8NoBom);

        var verificationIssues = VerifyCleanText(cleaned, outputPath);
        var result = stats.ToResult(
            inputPath,
            outputPath,
            filesScanned: 1,
            filesChanged: hadBom || cleaned != text ? 1 : 0,
            structuralRepairs: [],
            verificationIssues);
        WriteReport(result);
        return result;
    }

    private static UsfmCleanResult CleanTstudio(string inputPath, string outputPath, CanonProfile canonProfile, bool exportUsfm = false)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"uis-tstudio-clean-{Guid.NewGuid():N}");
        var stats = new CleanStats();
        var filesScanned = 0;
        var filesChanged = 0;

        try
        {
            ZipFile.ExtractToDirectory(inputPath, tempRoot);
            var chunkLayoutWarnings = FindTstudioChunkLayoutWarnings(tempRoot, canonProfile);
            ThrowIfTstudioIsContaminated(tempRoot, canonProfile);
            var sourceContextsByProjectRoot = LoadSourceContexts(tempRoot);

            foreach (var filePath in Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories)
                         .Where(IsCleanableTextFile))
            {
                filesScanned++;
                var (original, hadBom) = ReadCleanableText(filePath, stats);
                var cleaned = CleanText(original, stats);
                cleaned = RemoveLeadingOutOfChunkVerseMarker(filePath, cleaned, stats);
                cleaned = CleanDirectSpeechAgainstSource(filePath, cleaned, sourceContextsByProjectRoot, stats);
                cleaned = CleanTstudioChunkText(filePath, cleaned, stats);
                cleaned = CleanTstudioTitleText(filePath, cleaned, stats);
                if (hadBom || !string.Equals(original, cleaned, StringComparison.Ordinal))
                {
                    filesChanged++;
                    File.WriteAllText(filePath, cleaned, Utf8NoBom);
                }
            }

            var structuralRepairs = RepairTstudioStructure(tempRoot, canonProfile, stats)
                .Concat(StampTstudioGeneratorBuild(tempRoot))
                .ToList();
            var verificationIssues = chunkLayoutWarnings
                .Concat(VerifyTstudioStructure(tempRoot, canonProfile))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(issue => issue, StringComparer.Ordinal)
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            if (exportUsfm)
            {
                ExportTstudioAsUsfm(tempRoot, outputPath);
            }
            else
            {
                ZipFile.CreateFromDirectory(tempRoot, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }

            var result = stats.ToResult(inputPath, outputPath, filesScanned, filesChanged, structuralRepairs, verificationIssues);
            WriteReport(result);
            return result;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup failure should not invalidate a successful cleaned output.
            }
        }
    }

    private static IReadOnlyList<string> RepairTstudioStructure(string tempRoot, CanonProfile canonProfile, CleanStats stats)
    {
        var repairs = new List<string>();
        foreach (var projectRoot in EnumerateProjectRoots(tempRoot))
        {
            var manifestPath = Path.Combine(projectRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = ReadManifest(manifestPath);
            var expectedVerses = TryGetVerseCounts(manifest.ProjectId, canonProfile);
            if (expectedVerses is null)
            {
                repairs.Add($"Structural check skipped for {manifest.ProjectId}: no verse-count table for selected canon.");
                continue;
            }

            repairs.Add($"Validated project structure without deleting scripture chunks: {manifest.ProjectId}.");
        }

        return repairs;
    }

    private static void ThrowIfTstudioIsContaminated(string tempRoot, CanonProfile canonProfile)
    {
        var issues = FindBlockingTstudioContamination(tempRoot, canonProfile);
        if (issues.Count == 0)
        {
            return;
        }

        throw new InvalidDataException(
            "Project integrity check blocked cleaning because conflicting or unsafe project data was found. "
            + "No cleaned output was created and no scripture files were deleted."
            + Environment.NewLine
            + string.Join(Environment.NewLine, issues.Select(issue => "- " + issue)));
    }

    private static IReadOnlyList<string> FindBlockingTstudioContamination(string tempRoot, CanonProfile canonProfile)
    {
        var issues = new List<string>();
        foreach (var projectRoot in EnumerateProjectRoots(tempRoot))
        {
            var manifestPath = Path.Combine(projectRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = ReadManifest(manifestPath);
            var bookId = manifest.ProjectId.ToUpperInvariant();
            var expectedVerses = TryGetVerseCounts(bookId, canonProfile);
            var verseOwners = new Dictionary<(int Chapter, int Verse), string>();
            var chunkTexts = new List<(string RelativePath, string Text)>();

            foreach (var filePath in Directory.EnumerateFiles(projectRoot, "*.txt", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(projectRoot, filePath);
                var text = File.ReadAllText(filePath, Utf8NoBom);

                if (IsTitlePath(relativePath))
                {
                    if (UsfmChapterMarkerRegex.IsMatch(text) || UsfmVerseMarkerRegex.IsMatch(text))
                    {
                        issues.Add($"TITLE_CONTAINS_USFM_MARKER: {bookId} {relativePath}");
                    }

                    continue;
                }

                if (!TryParseChunkPath(relativePath, out var chapter, out var chunkStart))
                {
                    continue;
                }

                if (expectedVerses is not null && IsImpossibleChunk(chapter, chunkStart, expectedVerses))
                {
                    issues.Add($"IMPOSSIBLE_CHUNK_PATH: {bookId} {relativePath}");
                    continue;
                }

                var hasCanonicalChunkStart = true;
                if (BttwCanonicalChunkMap.ContainsBook(bookId))
                {
                    hasCanonicalChunkStart = BttwCanonicalChunkMap.IsCanonicalStart(bookId, chapter, chunkStart);
                }

                chunkTexts.Add((relativePath, text));
                foreach (Match marker in VerseMarkerRegex.Matches(text))
                {
                    if (!int.TryParse(marker.Groups[1].Value, out var verse))
                    {
                        continue;
                    }

                    if (hasCanonicalChunkStart
                        && BttwCanonicalChunkMap.TryFindChunkStart(bookId, chapter, verse, out var expectedStart)
                        && expectedStart != chunkStart)
                    {
                        issues.Add(
                            $"VERSE_OUTSIDE_CANONICAL_CHUNK: {bookId} {chapter}:{verse} is in {relativePath}; expected {FormatChunkPath(chapter, expectedStart)}");
                    }

                    var key = (chapter, verse);
                    if (verseOwners.TryGetValue(key, out var owner) && !string.Equals(owner, relativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add($"DUPLICATE_VERSE_COVERAGE: {bookId} {chapter}:{verse} appears in {owner} and {relativePath}");
                    }
                    else
                    {
                        verseOwners[key] = relativePath;
                    }
                }
            }

            issues.AddRange(FindTitleChunkDuplication(projectRoot, bookId, chunkTexts));
            if (expectedVerses is not null)
            {
                issues.AddRange(ReadImpossibleFinishedChunks(manifestPath, expectedVerses)
                    .Select(chunk => $"IMPOSSIBLE_FINISHED_CHUNK: {bookId} {chunk}"));
            }
        }

        return issues.Distinct(StringComparer.Ordinal).OrderBy(issue => issue, StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<string> FindTstudioChunkLayoutWarnings(string tempRoot, CanonProfile canonProfile)
    {
        var warnings = new List<string>();
        foreach (var projectRoot in EnumerateProjectRoots(tempRoot))
        {
            var manifestPath = Path.Combine(projectRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = ReadManifest(manifestPath);
            var bookId = manifest.ProjectId.ToUpperInvariant();
            if (!BttwCanonicalChunkMap.ContainsBook(bookId))
            {
                continue;
            }

            var expectedVerses = TryGetVerseCounts(bookId, canonProfile);
            foreach (var filePath in Directory.EnumerateFiles(projectRoot, "*.txt", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(projectRoot, filePath);
                if (!TryParseChunkPath(relativePath, out var chapter, out var chunkStart)
                    || BttwCanonicalChunkMap.IsCanonicalStart(bookId, chapter, chunkStart)
                    || (expectedVerses is not null && IsImpossibleChunk(chapter, chunkStart, expectedVerses)))
                {
                    continue;
                }

                warnings.Add($"WARNING NONCANONICAL_CHUNK_PATH: {bookId} {relativePath}");
            }

            warnings.AddRange(FindNoncanonicalFinishedChunks(manifestPath, bookId)
                .Select(warning => "WARNING " + warning));
        }

        return warnings.Distinct(StringComparer.Ordinal).OrderBy(warning => warning, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> FindTitleChunkDuplication(
        string projectRoot,
        string bookId,
        IReadOnlyList<(string RelativePath, string Text)> chunkTexts)
    {
        foreach (var titlePath in Directory.EnumerateFiles(projectRoot, "title.txt", SearchOption.AllDirectories))
        {
            var title = NormalizeForContaminationComparison(File.ReadAllText(titlePath, Utf8NoBom));
            if (title.Length < 40)
            {
                continue;
            }

            foreach (var chunk in chunkTexts)
            {
                var chunkText = NormalizeForContaminationComparison(chunk.Text);
                if (chunkText.Length < 40)
                {
                    continue;
                }

                var shorterLength = Math.Min(title.Length, chunkText.Length);
                if (string.Equals(title, chunkText, StringComparison.Ordinal)
                    || (shorterLength >= 80
                        && (title.Contains(chunkText, StringComparison.Ordinal)
                            || chunkText.Contains(title, StringComparison.Ordinal))))
                {
                    var titleRelative = Path.GetRelativePath(projectRoot, titlePath);
                    yield return $"TITLE_DUPLICATES_CHUNK_TEXT: {bookId} {titleRelative} duplicates {chunk.RelativePath}";
                    break;
                }
            }
        }
    }

    private static IEnumerable<string> FindNoncanonicalFinishedChunks(string manifestPath, string bookId)
    {
        if (!BttwCanonicalChunkMap.ContainsBook(bookId))
        {
            yield break;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Utf8NoBom));
        if (!doc.RootElement.TryGetProperty("finished_chunks", out var chunks) || chunks.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var chunk in chunks.EnumerateArray())
        {
            var value = chunk.ValueKind == JsonValueKind.String ? chunk.GetString() : null;
            if (value is null
                || value.EndsWith("-title", StringComparison.OrdinalIgnoreCase)
                || !TryParseFinishedChunk(value, out var chapter, out var verse))
            {
                continue;
            }

            if (!BttwCanonicalChunkMap.IsCanonicalStart(bookId, chapter, verse))
            {
                yield return $"NONCANONICAL_FINISHED_CHUNK: {bookId} {value}";
            }
        }
    }

    private static bool IsTitlePath(string relativePath)
    {
        return string.Equals(Path.GetFileName(relativePath), "title.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForContaminationComparison(string text)
    {
        return Regex.Replace(text, @"[\s\p{P}\p{S}\\]+", string.Empty, RegexOptions.CultureInvariant);
    }

    private static string FormatChunkPath(int chapter, int verse)
    {
        return $"{chapter:00}/{verse:00}.txt";
    }

    private static IReadOnlyList<string> VerifyTstudioStructure(string tempRoot, CanonProfile canonProfile)
    {
        var issues = new List<string>();
        foreach (var projectRoot in EnumerateProjectRoots(tempRoot))
        {
            var manifestPath = Path.Combine(projectRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = ReadManifest(manifestPath);
            var expectedVerses = TryGetVerseCounts(manifest.ProjectId, canonProfile);

            foreach (var filePath in Directory.EnumerateFiles(projectRoot, "*.txt", SearchOption.AllDirectories))
            {
                var data = File.ReadAllBytes(filePath);
                var relativePath = Path.GetRelativePath(projectRoot, filePath);
                if (data.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
                {
                    issues.Add($"BOM remains: {relativePath}");
                }

                var text = Utf8NoBom.GetString(data);
                if (UnsafeControlCharRegex.IsMatch(text))
                {
                    issues.Add($"Unsafe control character remains: {relativePath}");
                }

                if (BidiControlRegex.IsMatch(text))
                {
                    issues.Add($"Bidi control character remains: {relativePath}");
                }

                if (expectedVerses is not null && TryParseChunkPath(relativePath, out var chapter, out var verse) && IsImpossibleChunk(chapter, verse, expectedVerses))
                {
                    issues.Add($"Impossible chunk remains: {relativePath}");
                }

                if (TryParseChunkPath(relativePath, out _, out _))
                {
                    issues.AddRange(FindTstudioChunkTextIssues(relativePath, text));
                }
            }

            if (expectedVerses is not null)
            {
                foreach (var chunk in ReadImpossibleFinishedChunks(manifestPath, expectedVerses))
                {
                    issues.Add($"Impossible manifest finished_chunk remains: {chunk}");
                }
            }
        }

        return issues;
    }

    private static IEnumerable<string> FindTstudioChunkTextIssues(string relativePath, string text)
    {
        if (UsfmChapterMarkerRegex.IsMatch(text))
        {
            yield return $"Literal USFM chapter marker remains in chunk: {relativePath}";
        }

        var bodyWithoutMarkers = UsfmVerseMarkerRegex.Replace(text, string.Empty);
        var bodyWithoutJunk = EmptyVerseBodyJunkRegex.Replace(bodyWithoutMarkers, string.Empty);
        if (bodyWithoutJunk.Length == 0)
        {
            yield return $"Empty chunk text remains: {relativePath}";
        }
    }

    private static IReadOnlyList<string> VerifyCleanText(string text, string outputPath)
    {
        var issues = new List<string>();
        if (text.StartsWith('\ufeff'))
        {
            issues.Add($"BOM remains: {Path.GetFileName(outputPath)}");
        }

        if (UnsafeControlCharRegex.IsMatch(text))
        {
            issues.Add($"Unsafe control character remains: {Path.GetFileName(outputPath)}");
        }

        if (BidiControlRegex.IsMatch(text))
        {
            issues.Add($"Bidi control character remains: {Path.GetFileName(outputPath)}");
        }

        return issues;
    }

    private static IEnumerable<string> EnumerateProjectRoots(string tempRoot)
    {
        var projectDirs = Directory.EnumerateDirectories(tempRoot)
            .Where(dir => File.Exists(Path.Combine(dir, "manifest.json")))
            .ToList();
        if (projectDirs.Count > 0)
        {
            foreach (var dir in projectDirs)
            {
                yield return dir;
            }

            yield break;
        }

        if (File.Exists(Path.Combine(tempRoot, "manifest.json")))
        {
            yield return tempRoot;
        }
    }

    private static IReadOnlyList<string> StampTstudioGeneratorBuild(string tempRoot)
    {
        var repairs = new List<string>();
        var manifestPaths = new List<string>();
        var rootManifestPath = Path.Combine(tempRoot, "manifest.json");
        if (File.Exists(rootManifestPath))
        {
            manifestPaths.Add(rootManifestPath);
        }

        manifestPaths.AddRange(EnumerateProjectRoots(tempRoot)
            .Select(projectRoot => Path.Combine(projectRoot, "manifest.json"))
            .Where(File.Exists));

        foreach (var manifestPath in manifestPaths.Distinct(StringComparer.Ordinal))
        {
            var json = JsonNode.Parse(File.ReadAllText(manifestPath, Utf8NoBom)) as JsonObject;
            if (json is null)
            {
                continue;
            }

            var generator = json["generator"] as JsonObject;
            if (generator is null)
            {
                generator = new JsonObject();
                json["generator"] = generator;
            }

            var originalName = generator["name"]?.GetValue<string>();
            var originalBuild = generator["build"]?.GetValue<string>();
            if (originalName == BttwGeneratorName && originalBuild == BttwModifiedBuild)
            {
                continue;
            }

            generator["name"] = BttwGeneratorName;
            generator["build"] = BttwModifiedBuild;
            File.WriteAllText(manifestPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);

            var relativePath = Path.GetRelativePath(tempRoot, manifestPath);
            repairs.Add($"Stamped BTTW generator metadata in {relativePath}: {BttwGeneratorName} {BttwModifiedBuild}");
        }

        return repairs;
    }

    private static int[]? TryGetVerseCounts(string projectId, CanonProfile canonProfile)
    {
        var bookId = projectId.ToUpperInvariant();
        var versification = DocxScanService.GetVersificationProfile(canonProfile);
        return versification.VerseCountsByBook.TryGetValue(bookId, out var counts) ? counts.ToArray() : null;
    }

    private static bool IsImpossibleChunk(int chapter, int verse, IReadOnlyList<int> expectedVerses)
    {
        return chapter < 1 || chapter > expectedVerses.Count || verse < 1 || verse > expectedVerses[chapter - 1];
    }

    private static bool TryParseChunkPath(string relativePath, out int chapter, out int verse)
    {
        chapter = 0;
        verse = 0;
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length == 2
            && int.TryParse(parts[0], out chapter)
            && parts[1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parts[1], "title.txt", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(Path.GetFileNameWithoutExtension(parts[1]), out verse);
    }

    private static IReadOnlyList<string> ReadImpossibleFinishedChunks(string manifestPath, IReadOnlyList<int> expectedVerses)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Utf8NoBom));
        if (!doc.RootElement.TryGetProperty("finished_chunks", out var chunks) || chunks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var impossible = new List<string>();
        foreach (var chunk in chunks.EnumerateArray())
        {
            if (chunk.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = chunk.GetString();
            if (value is not null
                && TryParseFinishedChunk(value, out var chapter, out var verse)
                && IsImpossibleChunk(chapter, verse, expectedVerses))
            {
                impossible.Add(value);
            }
        }

        return impossible;
    }

    private static bool TryParseFinishedChunk(string value, out int chapter, out int verse)
    {
        chapter = 0;
        verse = 0;
        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out chapter) && int.TryParse(parts[1], out verse);
    }

    private static void WriteReport(UsfmCleanResult result)
    {
        var lines = new List<string>
        {
            "UIS Premium USFM/Project Cleaner Report",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Input: {result.InputPath}",
            $"Output: {result.OutputPath}",
            $"Files scanned: {result.FilesScanned}",
            $"Files changed: {result.FilesChanged}",
            $"Duplicate visible verse markers removed: {result.InlineDuplicateMarkersRemoved + result.PendingLineDuplicateMarkersRemoved}",
            $"Stray leading verse markers removed: {result.StrayLeadingVerseMarkersRemoved}",
            $"Visible reversed/loose verse markers normalized: {result.VisibleVerseMarkersNormalized}",
            $"Punctuation/parenthesis spacing fixes: {result.SpacingFixes}",
            $"Straight English quotes converted: {result.StraightQuotesConverted}",
            $"Straight English single quotes converted: {result.StraightSingleQuotesConverted}",
            $"Directional double quotes repaired: {result.DirectionalDoubleQuotesRepaired}",
            $"Directional single quotes repaired: {result.DirectionalSingleQuotesRepaired}",
            $"Unpaired double quote closers repaired: {result.UnpairedDoubleQuoteClosersRepaired}",
            $"Source-checked direct speech fixes: {result.DirectSpeechFixes}",
            $"Unicode BOM markers removed: {result.ByteOrderMarksRemoved}",
            $"Unsafe control characters removed: {result.UnsafeControlCharsRemoved}",
            $"Structural chunk files removed: {result.StructuralChunkFilesRemoved}",
            $"Manifest finished_chunks removed: {result.ManifestFinishedChunksRemoved}",
            $"Verification issues after cleaning: {result.VerificationIssueCount}",
            string.Empty,
            "Structural repairs:"
        };

        lines.AddRange(result.StructuralRepairs.Count > 0 ? result.StructuralRepairs.Select(item => "- " + item) : ["- none"]);
        lines.Add(string.Empty);
        lines.Add("Post-clean verification:");
        lines.AddRange(result.VerificationIssues.Count > 0 ? result.VerificationIssues.Select(item => "- " + item) : ["- passed"]);

        Directory.CreateDirectory(Path.GetDirectoryName(result.ReportPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllLines(result.ReportPath, lines, Utf8NoBom);
    }

    private static bool IsCleanableTextFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".txt" or ".usfm";
    }

    private static string CleanDirectSpeechAgainstSource(
        string filePath,
        string text,
        IReadOnlyDictionary<string, SourceContext> sourceContextsByProjectRoot,
        CleanStats stats)
    {
        if (!text.Contains("کہ", StringComparison.Ordinal) || (!text.Contains('’') && !text.Contains('‘')))
        {
            return text;
        }

        if (!TryGetChunkReference(filePath, sourceContextsByProjectRoot, out var context, out var chapter, out var startVerse))
        {
            return text;
        }

        var verseNumbers = ExtractVerseNumbers(text);
        if (verseNumbers.Count == 0)
        {
            verseNumbers.Add(startVerse);
        }

        var sourceText = context.GetSourceText(chapter, verseNumbers.Min(), verseNumbers.Max());
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return text;
        }

        var sourceHasCompleteQuote = HasCompleteSourceQuote(sourceText);
        var targetHasClosingQuote = HasTargetClosingQuoteAfterKehOpening(text);
        var cleaned = sourceHasCompleteQuote && targetHasClosingQuote
            ? RemoveRedundantKehBeforeOpeningQuote(text)
            : RemoveOpeningQuoteAfterKeh(text);

        if (!string.Equals(cleaned, text, StringComparison.Ordinal))
        {
            stats.DirectSpeechFixes++;
        }

        return cleaned;
    }

    private static string RemoveLeadingOutOfChunkVerseMarker(string filePath, string text, CleanStats stats)
    {
        if (!TryParseChunkFileReference(filePath, out _, out var startVerse))
        {
            return text;
        }

        var match = LeadingVerseMarkerRegex.Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out var leadingVerse)
            || leadingVerse == startVerse)
        {
            return text;
        }

        stats.StrayLeadingVerseMarkersRemoved++;
        return text.Remove(match.Index, match.Length).TrimStart();
    }

    private static string CleanTstudioChunkText(string filePath, string text, CleanStats stats)
    {
        if (!TryParseChunkFileReference(filePath, out _, out var startVerse))
        {
            return text;
        }

        var cleaned = UsfmChapterMarkerRegex.Replace(text, string.Empty);
        cleaned = StripBidiControlCharacters(cleaned, stats);
        cleaned = RemoveVisibleVerseMarkersFromChunk(cleaned, startVerse, stats);

        return (ContainsArabicScript(cleaned)
            ? ScripturePunctuationNormalizer.NormalizeArabicDerivedSpacing(cleaned)
            : ScripturePunctuationNormalizer.NormalizeCommonSpacing(cleaned)).Trim();
    }

    private static string CleanTstudioTitleText(string filePath, string text, CleanStats stats)
    {
        if (!Path.GetFileName(filePath).Equals("title.txt", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (IsFrontTitleFile(filePath))
        {
            return text;
        }

        var match = LeadingNumberRegex.Match(text);
        if (!match.Success || match.Groups[2].Value.Length == 0)
        {
            return text;
        }

        stats.VisibleVerseMarkersNormalized++;
        return match.Groups[3].Value.TrimStart();
    }

    private static bool IsFrontTitleFile(string filePath)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
        return string.Equals(parent, "front", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveVisibleVerseMarkersFromChunk(string text, int startVerse, CleanStats stats)
    {
        var cleaned = CleanVerseBody(startVerse, text, out var leadingChanged);
        if (leadingChanged)
        {
            stats.VisibleVerseMarkersNormalized++;
        }

        return VisibleVerseMarkerTokenRegex.Replace(cleaned, match =>
        {
            var markerIndex = match.Index;
            var previous = markerIndex - 1;
            while (previous >= 0 && char.IsWhiteSpace(cleaned[previous]))
            {
                previous--;
            }

            if (previous >= 0 && !IsVerseMarkerBoundaryBefore(cleaned[previous]))
            {
                return match.Value;
            }

            stats.VisibleVerseMarkersNormalized++;
            return string.Empty;
        });
    }

    private static bool IsVerseMarkerBoundaryBefore(char value)
    {
        return value is '.' or '۔' or '!' or '?' or '؟' or ':' or ';' or '؛' or ')' or ']' or '}' or '‘' or '’' or '"' or '\'';
    }

    private static string StripBidiControlCharacters(string text, CleanStats stats)
    {
        return BidiControlRegex.Replace(text, _ =>
        {
            stats.UnsafeControlCharsRemoved++;
            return string.Empty;
        });
    }

    private static string RemoveRedundantKehBeforeOpeningQuote(string text)
    {
        return Regex.Replace(
            text,
            @"(?<!\S)کہ\s+(?=[’])",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static string RemoveOpeningQuoteAfterKeh(string text)
    {
        return Regex.Replace(
            text,
            @"(?<!\S)(کہ)\s+[’]+",
            "$1 ",
            RegexOptions.CultureInvariant);
    }

    private static bool HasTargetClosingQuoteAfterKehOpening(string text)
    {
        var match = Regex.Match(text, @"(?<!\S)کہ\s+[’]+", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        return text.IndexOf('‘', match.Index + match.Length) >= 0;
    }

    private static bool HasCompleteSourceQuote(string sourceText)
    {
        var doubleQuoteCount = sourceText.Count(ch => ch == '"');
        if (doubleQuoteCount >= 2 && doubleQuoteCount % 2 == 0)
        {
            return true;
        }

        var singleQuoteCount = sourceText.Count(ch => ch == '\'');
        return singleQuoteCount >= 2 && singleQuoteCount % 2 == 0;
    }

    private static List<int> ExtractVerseNumbers(string text)
    {
        var verses = new List<int>();
        foreach (Match match in Regex.Matches(text, @"\\v\s*([0-9۰-۹٠-٩]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            if (TryParseScriptNumber(match.Groups[1].Value, out var verse))
            {
                verses.Add(verse);
            }
        }

        return verses;
    }

    private static bool TryParseScriptNumber(string raw, out int number)
    {
        number = 0;
        var found = false;
        foreach (var ch in raw)
        {
            int digit;
            if (ch is >= '0' and <= '9')
            {
                digit = ch - '0';
            }
            else if (ch is >= '۰' and <= '۹')
            {
                digit = ch - '۰';
            }
            else if (ch is >= '٠' and <= '٩')
            {
                digit = ch - '٠';
            }
            else
            {
                return false;
            }

            number = (number * 10) + digit;
            found = true;
        }

        return found;
    }

    private static bool TryGetChunkReference(
        string filePath,
        IReadOnlyDictionary<string, SourceContext> sourceContextsByProjectRoot,
        out SourceContext context,
        out int chapter,
        out int startVerse)
    {
        context = default!;
        chapter = 0;
        startVerse = 0;

        var directory = Path.GetDirectoryName(filePath);
        var chapterName = directory is null ? null : Path.GetFileName(directory);
        var verseName = Path.GetFileNameWithoutExtension(filePath);
        if (!int.TryParse(chapterName, out chapter) || !int.TryParse(verseName, out startVerse))
        {
            return false;
        }

        var projectRoot = directory is null ? null : Directory.GetParent(directory)?.FullName;
        if (projectRoot is null || !sourceContextsByProjectRoot.TryGetValue(projectRoot, out var foundContext))
        {
            return false;
        }

        context = foundContext;
        return true;
    }

    private static bool TryParseChunkFileReference(string filePath, out int chapter, out int startVerse)
    {
        chapter = 0;
        startVerse = 0;

        var directory = Path.GetDirectoryName(filePath);
        var chapterName = directory is null ? null : Path.GetFileName(directory);
        var verseName = Path.GetFileNameWithoutExtension(filePath);
        return int.TryParse(chapterName, out chapter) && int.TryParse(verseName, out startVerse);
    }

    private static IReadOnlyDictionary<string, SourceContext> LoadSourceContexts(string tempRoot)
    {
        var contexts = new Dictionary<string, SourceContext>(StringComparer.Ordinal);
        foreach (var projectRoot in EnumerateProjectRoots(tempRoot))
        {
            var manifestPath = Path.Combine(projectRoot, "manifest.json");
            var manifest = ReadManifest(manifestPath);
            var sourcePath = FindLocalSourceUsfm(manifest.ProjectId);
            if (sourcePath is null)
            {
                continue;
            }

            contexts[projectRoot] = SourceContext.Load(sourcePath);
        }

        return contexts;
    }

    private static string? FindLocalSourceUsfm(string projectId)
    {
        var bookId = projectId.ToUpperInvariant();
        var candidateRoots = new[]
        {
            Environment.GetEnvironmentVariable("UIS_SOURCE_TEXT_ROOT"),
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        }
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Select(root => Path.GetFullPath(root!))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

        foreach (var root in candidateRoots)
        {
            var current = root;
            for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
            {
                var sourceDir = Path.Combine(current, "Source Text (eng) USFM files", "en_ulb");
                if (Directory.Exists(sourceDir))
                {
                    var match = Directory.EnumerateFiles(sourceDir, $"*-{bookId}.usfm", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();
                    if (match is not null)
                    {
                        return match;
                    }
                }

                current = Directory.GetParent(current)?.FullName ?? string.Empty;
            }
        }

        return null;
    }

    private static (string Text, bool HadBom) ReadCleanableText(string filePath, CleanStats stats)
    {
        var data = File.ReadAllBytes(filePath);
        var hadBom = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF;
        if (hadBom)
        {
            stats.ByteOrderMarksRemoved++;
            data = data[3..];
        }

        return (Utf8NoBom.GetString(data), hadBom);
    }

    private static void ExportTstudioAsUsfm(string tempRoot, string outputPath)
    {
        var projectRoot = EnumerateProjectRoots(tempRoot).SingleOrDefault() ?? tempRoot;
        var manifest = ReadManifest(Path.Combine(projectRoot, "manifest.json"));
        var bookTitle = ReadTranslatedBookTitle(projectRoot) ?? manifest.ProjectName;
        var idLine = string.IsNullOrWhiteSpace(manifest.ResourceName)
            ? manifest.ProjectId
            : $"{manifest.ProjectId} {manifest.ResourceName}";
        var lines = new List<string>
        {
            $"\\id {idLine}",
            $"\\ide {manifest.Format}",
            $"\\h {bookTitle}",
            $"\\toc1 {bookTitle}",
            $"\\toc2 {bookTitle}",
            $"\\toc3 {manifest.ProjectId}",
            $"\\mt {bookTitle}"
        };

        foreach (var chapterDir in Directory.EnumerateDirectories(projectRoot)
                     .OrderBy(path => int.TryParse(Path.GetFileName(path), out var chapter) ? chapter : int.MaxValue))
        {
            if (!int.TryParse(Path.GetFileName(chapterDir), out var chapterNumber))
            {
                continue;
            }

            var chunkFiles = Directory.EnumerateFiles(chapterDir, "*.txt")
                .Where(path => !string.Equals(Path.GetFileName(path), "title.txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => int.TryParse(Path.GetFileNameWithoutExtension(path), out var verse) ? verse : int.MaxValue)
                .ToList();

            if (chunkFiles.Count == 0)
            {
                continue;
            }

            lines.Add($"\\c {chapterNumber}");

            foreach (var chunkFile in chunkFiles)
            {
                var content = File.ReadAllText(chunkFile, Utf8NoBom).Trim();
                if (content.Length > 0)
                {
                    var verseNumber = int.TryParse(Path.GetFileNameWithoutExtension(chunkFile), out var parsedVerse)
                        ? parsedVerse
                        : 0;
                    lines.Add(verseNumber > 0 && !VerseMarkerRegex.IsMatch(content)
                        ? $"\\v {verseNumber} {content}"
                        : content);
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, Utf8NoBom);
    }

    private static string? ReadTranslatedBookTitle(string projectRoot)
    {
        var frontTitlePath = Path.Combine(projectRoot, "front", "title.txt");
        if (!File.Exists(frontTitlePath))
        {
            return null;
        }

        var title = File.ReadAllText(frontTitlePath, Utf8NoBom).Trim();
        return title.Length > 0 ? title : null;
    }

    private static TstudioManifest ReadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return new TstudioManifest("unknown", "Unknown", "Unknown", "usfm");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Utf8NoBom));
        var root = doc.RootElement;
        var project = root.TryGetProperty("project", out var projectElement) ? projectElement : default;
        var resource = root.TryGetProperty("resource", out var resourceElement) ? resourceElement : default;

        var projectId = TryGetString(project, "id")?.ToUpperInvariant() ?? "UNK";
        var projectName = TryGetString(project, "name") ?? projectId;
        var resourceName = TryGetString(resource, "name") ?? TryGetString(resource, "id") ?? string.Empty;
        var format = TryGetString(root, "format") ?? "usfm";
        return new TstudioManifest(projectId, projectName, resourceName, format);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string CleanText(string text, CleanStats stats)
    {
        if (text.StartsWith('\ufeff'))
        {
            text = text.TrimStart('\ufeff');
            stats.ByteOrderMarksRemoved++;
        }

        text = UnsafeControlCharRegex.Replace(text, match =>
        {
            stats.UnsafeControlCharsRemoved++;
            return string.Empty;
        });

        text = StripBidiControlCharacters(text, stats);

        text = NormalizeVisibleVerseMarkerArtifacts(text, stats);

        var cleaned = InlineDuplicateRegex.Replace(text, match =>
        {
            var verseNumber = int.Parse(match.Groups[2].Value);
            var candidates = PossibleMarkerNumbers(match.Groups[4].Value);
            if (!candidates.Contains(verseNumber))
            {
                return match.Value;
            }

            stats.InlineDuplicateMarkersRemoved++;
            return $"{match.Groups[1].Value}{match.Groups[2].Value} ";
        });

        var lineBreak = cleaned.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = cleaned.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int? pendingVerseNumber = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var chapterTitle = ChapterTitleSpacingRegex.Replace(line, "$1 $2");
            if (!string.Equals(chapterTitle, line, StringComparison.Ordinal))
            {
                stats.SpacingFixes++;
                line = chapterTitle;
            }

            if (pendingVerseNumber is not null)
            {
                var updated = CleanVerseBody(pendingVerseNumber.Value, line, out var changed);
                if (changed)
                {
                    stats.PendingLineDuplicateMarkersRemoved++;
                    line = updated;
                }
                pendingVerseNumber = null;
            }

            var pending = PendingVerseRegex.Match(line);
            if (pending.Success && int.TryParse(pending.Groups[2].Value, out var verse))
            {
                pendingVerseNumber = verse;
            }

            var spaced = ContainsArabicScript(line)
                ? ScripturePunctuationNormalizer.NormalizeArabicDerivedSpacing(line)
                : ScripturePunctuationNormalizer.NormalizeCommonSpacing(line);
            if (!string.Equals(spaced, line, StringComparison.Ordinal))
            {
                stats.SpacingFixes++;
                line = spaced;
            }

            lines[i] = line;
        }

        cleaned = SplitAnchoredUsfmVerseLines(lines, lineBreak, stats);
        cleaned = ConvertStraightQuotes(cleaned, stats);
        cleaned = RepairDirectionalQuotes(cleaned, stats);
        cleaned = RepairUnpairedDoubleQuoteClosers(cleaned, stats);
        var quoteSpaced = ContainsArabicScript(cleaned)
            ? ScripturePunctuationNormalizer.NormalizeArabicDerivedQuoteSpacing(cleaned)
            : ScripturePunctuationNormalizer.NormalizeDirectionalQuoteSpacing(cleaned);
        if (!string.Equals(quoteSpaced, cleaned, StringComparison.Ordinal))
        {
            stats.SpacingFixes++;
        }

        return quoteSpaced;
    }

    private static string SplitAnchoredUsfmVerseLines(string[] lines, string lineBreak, CleanStats stats)
    {
        var output = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var match = UsfmVerseLineRegex.Match(line);
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out var verseNumber))
            {
                output.Add(line);
                continue;
            }

            var segments = SplitMergedUsfmVerseText(verseNumber, match.Groups[4].Value);
            if (segments.Count <= 1)
            {
                output.Add(line);
                continue;
            }

            output.Add(string.Join(
                " ",
                segments.Select(segment => $"{match.Groups[1].Value}{segment.Number}{match.Groups[3].Value}{segment.Text}")));

            stats.VisibleVerseMarkersNormalized += segments.Count - 1;
        }

        return string.Join(lineBreak, output);
    }

    private static List<VerseSegment> SplitMergedUsfmVerseText(int firstVerseNumber, string verseText)
    {
        var segments = new List<VerseSegment>();
        var currentVerse = firstVerseNumber;
        var currentStart = SkipDuplicatedVisibleVerseMarker(verseText, 0, firstVerseNumber);
        var index = currentStart;

        while (index < verseText.Length)
        {
            var markerBoundaryStart = index;
            if (TrySkipUsfmVerseMarkerPrefix(verseText, index, out var markerNumberStart)
                && markerNumberStart < verseText.Length
                && IsVerseNumberDigit(verseText[markerNumberStart]))
            {
                index = markerNumberStart;
            }
            else if (!IsVerseNumberDigit(verseText[index]))
            {
                index++;
                continue;
            }

            if (!TryReadVerseNumberAt(verseText, index, out var candidateVerse, out var end)
                || candidateVerse != currentVerse + 1)
            {
                index++;
                continue;
            }

            var markerEnd = SkipDuplicatedVisibleVerseMarker(verseText, end, candidateVerse);
            var hasDuplicatedMarkerBoundary = markerEnd > end;
            var hasUsfmMarkerBoundary = markerBoundaryStart < index;
            var hasInlineBoundary = hasDuplicatedMarkerBoundary || hasUsfmMarkerBoundary || IsInlineVerseBoundary(verseText, index);
            var hasDotBoundary = IsDotDelimitedBoundary(verseText, index, end);
            if (!hasInlineBoundary && !hasDotBoundary)
            {
                index++;
                continue;
            }

            var boundaryEnd = hasUsfmMarkerBoundary ? markerBoundaryStart : index;
            var piece = verseText[currentStart..boundaryEnd].Trim();
            if (!string.IsNullOrWhiteSpace(piece))
            {
                segments.Add(new VerseSegment(currentVerse, piece));
            }

            currentVerse = candidateVerse;
            currentStart = hasDuplicatedMarkerBoundary ? markerEnd : hasDotBoundary ? end + 1 : end;
            if (!hasDuplicatedMarkerBoundary)
            {
                currentStart = SkipMarkerArtifacts(verseText, currentStart);
            }

            index = currentStart;
        }

        var tail = verseText[currentStart..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            segments.Add(new VerseSegment(currentVerse, tail));
        }

        return segments.Count == 0 ? [new VerseSegment(firstVerseNumber, verseText.Trim())] : segments;
    }

    private static bool TryReadVerseNumberAt(string text, int start, out int verseNumber, out int end)
    {
        verseNumber = 0;
        end = start;
        var digitScript = GetVerseDigitScript(start < text.Length ? text[start] : '\0');
        if (digitScript == 0)
        {
            return false;
        }

        while (end < text.Length
               && IsVerseNumberDigit(text[end])
               && GetVerseDigitScript(text[end]) == digitScript
               && end - start < 3)
        {
            end++;
        }

        return end > start && TryNormalizeDigits(text[start..end], out verseNumber);
    }

    private static int GetVerseDigitScript(char value)
    {
        if (value is >= '0' and <= '9')
        {
            return 1;
        }

        if (value is >= '\u0660' and <= '\u0669')
        {
            return 2;
        }

        if (value is >= '\u06F0' and <= '\u06F9')
        {
            return 3;
        }

        return 0;
    }

    private static bool IsVerseNumberDigit(char value)
    {
        return value is >= '0' and <= '9'
            || value is >= '\u0660' and <= '\u0669'
            || value is >= '\u06F0' and <= '\u06F9';
    }

    private static bool TrySkipUsfmVerseMarkerPrefix(string text, int start, out int afterPrefix)
    {
        afterPrefix = start;
        var current = start;
        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        if (current + 1 >= text.Length
            || text[current] != '\\'
            || (text[current + 1] != 'v' && text[current + 1] != 'V'))
        {
            return false;
        }

        current += 2;
        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        afterPrefix = current;
        return true;
    }

    private static int SkipDuplicatedVisibleVerseMarker(string text, int start, int verseNumber)
    {
        var current = start;
        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        if (TrySkipUsfmVerseMarkerPrefix(text, current, out var afterUsfmPrefix))
        {
            current = afterUsfmPrefix;
        }

        if (!TryReadVerseNumberAt(text, current, out var duplicateVerse, out var afterDuplicate)
            || duplicateVerse != verseNumber)
        {
            return start;
        }

        var afterMarker = afterDuplicate;
        while (afterMarker < text.Length && char.IsWhiteSpace(text[afterMarker]))
        {
            afterMarker++;
        }

        if (afterMarker < text.Length && text[afterMarker] is '.' or ')' or '۔' or ':')
        {
            afterMarker++;
        }

        while (afterMarker < text.Length && char.IsWhiteSpace(text[afterMarker]))
        {
            afterMarker++;
        }

        return afterMarker;
    }

    private static bool IsInlineVerseBoundary(string text, int index)
    {
        if (index <= 0 || index >= text.Length || char.IsDigit(text[index - 1]))
        {
            return false;
        }

        if (IsBoundaryPunctuation(text[index - 1]))
        {
            return true;
        }

        if (!char.IsWhiteSpace(text[index - 1]))
        {
            return false;
        }

        var previous = index - 1;
        while (previous >= 0 && char.IsWhiteSpace(text[previous]))
        {
            previous--;
        }

        return previous < 0 || IsBoundaryPunctuation(text[previous]);
    }

    private static bool IsDotDelimitedBoundary(string text, int index, int end)
    {
        if (end >= text.Length || text[end] != '.')
        {
            return false;
        }

        if (index > 0 && char.IsDigit(text[index - 1]))
        {
            return false;
        }

        if (end + 1 >= text.Length)
        {
            return true;
        }

        var next = text[end + 1];
        return char.IsWhiteSpace(next) || next is '"' or '\'' or '«' or '“' || char.IsLetter(next);
    }

    private static int SkipMarkerArtifacts(string text, int start)
    {
        var current = start;
        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        if (current < text.Length && text[current] is '.' or ')' or '۔' or ':')
        {
            current++;
        }

        while (current < text.Length && char.IsWhiteSpace(text[current]))
        {
            current++;
        }

        return current;
    }

    private static bool IsBoundaryPunctuation(char value)
    {
        return value is '.' or '۔' or '!' or '?' or '؟' or ':' or ';' or ')' or ']' or '"' or '\'' or '»' or '”';
    }

    private static string NormalizeVisibleVerseMarkerArtifacts(string text, CleanStats stats)
    {
        text = ReversedVisibleVerseMarkerRegex.Replace(text, match =>
        {
            if (!TryParseScriptNumber(match.Groups[1].Value, out var verseNumber))
            {
                return match.Value;
            }

            stats.VisibleVerseMarkersNormalized++;
            return FormatCanonicalVerseMarker(text, match.Index, verseNumber);
        });

        return LooseVisibleVerseMarkerRegex.Replace(text, match =>
        {
            if (!TryParseScriptNumber(match.Groups[1].Value, out var verseNumber))
            {
                return match.Value;
            }

            stats.VisibleVerseMarkersNormalized++;
            return FormatCanonicalVerseMarker(text, match.Index, verseNumber);
        });
    }

    private static string FormatCanonicalVerseMarker(string fullText, int markerIndex, int verseNumber)
    {
        var needsLeadingSpace = markerIndex > 0 && !char.IsWhiteSpace(fullText[markerIndex - 1]);
        return $"{(needsLeadingSpace ? " " : string.Empty)}\\v {verseNumber} ";
    }

    private static string RepairDirectionalQuotes(string text, CleanStats stats)
    {
        if (ContainsArabicScript(text))
        {
            return RepairArabicDirectionalQuotes(text, stats);
        }

        text = NormalizeLegacyDoubleQuotePairs(text, stats);

        var cleaned = OpeningDoubleQuoteAfterSentenceRegex.Replace(text, match =>
        {
            stats.DirectionalDoubleQuotesRepaired++;
            return $"{match.Groups[1].Value}{match.Groups[2].Value}“";
        });

        return OpeningSingleQuoteAfterSentenceRegex.Replace(cleaned, match =>
        {
            stats.DirectionalSingleQuotesRepaired++;
            return $"{match.Groups[1].Value}{match.Groups[2].Value}’";
        });
    }

    private static string RepairArabicDirectionalQuotes(string text, CleanStats stats)
    {
        text = NormalizeArabicLegacyDoubleQuotePairs(text, stats);

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch is '“' or '”')
            {
                var replacement = IsOpeningQuoteContext(text, index) ? '”' : '“';
                if (replacement != ch)
                {
                    stats.DirectionalDoubleQuotesRepaired++;
                }
                builder.Append(replacement);
                continue;
            }

            if (ch is '‘' or '’')
            {
                var replacement = IsOpeningQuoteContext(text, index) ? '’' : '‘';
                if (replacement != ch)
                {
                    stats.DirectionalSingleQuotesRepaired++;
                }
                builder.Append(replacement);
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string RepairUnpairedDoubleQuoteClosers(string text, CleanStats stats)
    {
        var builder = new StringBuilder(text.Length);
        var doubleBalance = 0;
        var singleBalance = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '“')
            {
                doubleBalance++;
                builder.Append('“');
                continue;
            }

            if (text[index] == '”')
            {
                if (doubleBalance > 0)
                {
                    doubleBalance--;
                }
                builder.Append('”');
                continue;
            }

            var ch = text[index];
            if (ch == '’')
            {
                singleBalance++;
                builder.Append(ch);
                continue;
            }

            if (ch == '‘')
            {
                if (singleBalance > 0)
                {
                    singleBalance--;
                    builder.Append(ch);
                }
                else if (doubleBalance > 0 && IsLikelyQuoteCloseContext(text, index))
                {
                    doubleBalance--;
                    builder.Append('”');
                    stats.UnpairedDoubleQuoteClosersRepaired++;
                }
                else
                {
                    builder.Append(ch);
                }
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string NormalizeLegacyDoubleQuotePairs(string text, CleanStats stats)
    {
        if (!text.Contains("’’", StringComparison.Ordinal) && !text.Contains("‘‘", StringComparison.Ordinal))
        {
            return text;
        }

        var converted = text.Replace("’’", "“", StringComparison.Ordinal)
            .Replace("‘‘", "”", StringComparison.Ordinal);
        if (!string.Equals(converted, text, StringComparison.Ordinal))
        {
            stats.DirectionalDoubleQuotesRepaired++;
        }

        return converted;
    }

    private static string NormalizeArabicLegacyDoubleQuotePairs(string text, CleanStats stats)
    {
        if (!text.Contains("’’", StringComparison.Ordinal) && !text.Contains("‘‘", StringComparison.Ordinal))
        {
            return text;
        }

        var converted = text.Replace("’’", "”", StringComparison.Ordinal)
            .Replace("‘‘", "“", StringComparison.Ordinal);
        if (!string.Equals(converted, text, StringComparison.Ordinal))
        {
            stats.DirectionalDoubleQuotesRepaired++;
        }

        return converted;
    }

    private static bool IsLikelyQuoteCloseContext(string text, int quoteIndex)
    {
        var previous = quoteIndex - 1;
        while (previous >= 0 && text[previous] is ' ' or '\t')
        {
            previous--;
        }

        var next = quoteIndex + 1;
        while (next < text.Length && text[next] is ' ' or '\t')
        {
            next++;
        }

        var nextIsBoundary = next >= text.Length
            || text[next] is '\r' or '\n'
            || (next + 1 < text.Length && text[next] == '\\' && text[next + 1] == 'v');

        return previous >= 0
            && text[previous] is '.' or '!' or '?' or '؟' or '۔'
            && nextIsBoundary;
    }

    private static string ConvertStraightQuotes(string text, CleanStats stats)
    {
        if (!text.Contains('"', StringComparison.Ordinal) && !text.Contains('\'', StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var useArabicQuoteDirection = ContainsArabicScript(text);
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '"')
            {
                var isOpening = IsOpeningQuoteContext(text, index);
                builder.Append(useArabicQuoteDirection
                    ? isOpening ? '”' : '“'
                    : isOpening ? '“' : '”');
                stats.StraightQuotesConverted++;
                continue;
            }

            if (ch == '\'' && ShouldConvertStraightSingleQuote(text, index))
            {
                builder.Append(IsOpeningQuoteContext(text, index) ? "’" : "‘");
                stats.StraightSingleQuotesConverted++;
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool ShouldConvertStraightSingleQuote(string text, int quoteIndex)
    {
        var previous = quoteIndex > 0 ? text[quoteIndex - 1] : '\0';
        var next = quoteIndex + 1 < text.Length ? text[quoteIndex + 1] : '\0';
        return !(IsAsciiWordChar(previous) && IsAsciiWordChar(next));
    }

    private static bool IsAsciiWordChar(char value)
    {
        return value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_';
    }

    private static bool IsOpeningQuoteContext(string text, int quoteIndex)
    {
        var previous = quoteIndex - 1;
        while (previous >= 0 && text[previous] is ' ' or '\t')
        {
            previous--;
        }

        if (previous >= 0
            && text[previous] is '.' or '!' or '?' or '؟' or '۔'
            && text[quoteIndex - 1] is not (' ' or '\t'))
        {
            return false;
        }

        var next = quoteIndex + 1;
        while (next < text.Length && text[next] is ' ' or '\t')
        {
            next++;
        }

        if (next >= text.Length || text[next] is '\r' or '\n' or ')' or ']' or '}' or '،' or ',' or '.' or ':' or ';' or '؛' or '!' or '?' or '؟' or '۔' or '—' or '–' or '-')
        {
            return false;
        }

        if (previous < 0)
        {
            return true;
        }

        return text[quoteIndex - 1] is ' ' or '\t'
            || text[previous] is '\r' or '\n' or '(' or '[' or '{' or '،' or ',' or ':' or ';' or '؛' or '—' or '–' or '-';
    }

    private static bool ContainsArabicScript(string value)
    {
        foreach (var ch in value)
        {
            if (ch >= '\u0600' && ch <= '\u06FF')
            {
                return true;
            }
        }

        return false;
    }

    private static string CleanVerseBody(int verseNumber, string body, out bool changed)
    {
        changed = false;
        var match = LeadingNumberRegex.Match(body);
        if (!match.Success)
        {
            return body;
        }

        var candidates = PossibleMarkerNumbers(match.Groups[1].Value);
        var punctuation = match.Groups[2].Value;
        if (punctuation.Length == 0 || !candidates.Contains(verseNumber))
        {
            return body;
        }

        changed = true;
        return match.Groups[3].Value.TrimStart();
    }

    private static HashSet<int> PossibleMarkerNumbers(string value)
    {
        var candidates = new HashSet<int>();
        if (TryNormalizeDigits(value, out var direct))
        {
            candidates.Add(direct);
        }

        if (value.Contains('ا', StringComparison.Ordinal))
        {
            var withAlefAsOne = value.Replace('ا', '۱');
            if (TryNormalizeDigits(withAlefAsOne, out var directAlef))
            {
                candidates.Add(directAlef);
            }

            var reversed = new string(withAlefAsOne.Reverse().ToArray());
            if (TryNormalizeDigits(reversed, out var reverseAlef))
            {
                candidates.Add(reverseAlef);
            }
        }

        return candidates;
    }

    private static bool TryNormalizeDigits(string value, out int number)
    {
        number = 0;
        var found = false;

        foreach (var ch in value)
        {
            int digit;
            if (ch is >= '0' and <= '9')
            {
                digit = ch - '0';
            }
            else if (ch is >= '۰' and <= '۹')
            {
                digit = ch - '۰';
            }
            else if (ch is >= '٠' and <= '٩')
            {
                digit = ch - '٠';
            }
            else
            {
                return false;
            }

            number = (number * 10) + digit;
            found = true;
        }

        return found;
    }

    private sealed class SourceContext
    {
        private readonly Dictionary<(int Chapter, int Verse), string> _verses;

        private SourceContext(Dictionary<(int Chapter, int Verse), string> verses)
        {
            _verses = verses;
        }

        public static SourceContext Load(string usfmPath)
        {
            var verses = new Dictionary<(int Chapter, int Verse), string>();
            int? chapter = null;
            int? verse = null;

            foreach (var rawLine in File.ReadLines(usfmPath, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                var chapterMatch = Regex.Match(line, @"^\\c\s+(\d+)", RegexOptions.CultureInvariant);
                if (chapterMatch.Success)
                {
                    chapter = int.Parse(chapterMatch.Groups[1].Value);
                    verse = null;
                    continue;
                }

                var verseMatch = Regex.Match(line, @"^\\v\s*(\d+)\s*(.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (chapter is not null && verseMatch.Success)
                {
                    verse = int.Parse(verseMatch.Groups[1].Value);
                    verses[(chapter.Value, verse.Value)] = verseMatch.Groups[2].Value.Trim();
                    continue;
                }

                if (chapter is not null && verse is not null && line.Length > 0)
                {
                    var text = Regex.Replace(line, @"^\\[a-z0-9]+\*?\s*", string.Empty, RegexOptions.CultureInvariant).Trim();
                    if (text.Length > 0)
                    {
                        verses[(chapter.Value, verse.Value)] += " " + text;
                    }
                }
            }

            return new SourceContext(verses);
        }

        public string GetSourceText(int chapter, int startVerse, int endVerse)
        {
            var parts = new List<string>();
            for (var verse = startVerse; verse <= endVerse; verse++)
            {
                if (_verses.TryGetValue((chapter, verse), out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }

            return string.Join(" ", parts);
        }
    }

    private sealed class CleanStats
    {
        public int InlineDuplicateMarkersRemoved { get; set; }
        public int PendingLineDuplicateMarkersRemoved { get; set; }
        public int StrayLeadingVerseMarkersRemoved { get; set; }
        public int VisibleVerseMarkersNormalized { get; set; }
        public int SpacingFixes { get; set; }
        public int StraightQuotesConverted { get; set; }
        public int StraightSingleQuotesConverted { get; set; }
        public int DirectionalDoubleQuotesRepaired { get; set; }
        public int DirectionalSingleQuotesRepaired { get; set; }
        public int UnpairedDoubleQuoteClosersRepaired { get; set; }
        public int DirectSpeechFixes { get; set; }
        public int ByteOrderMarksRemoved { get; set; }
        public int UnsafeControlCharsRemoved { get; set; }
        public int StructuralChunkFilesRemoved { get; set; }
        public int ManifestFinishedChunksRemoved { get; set; }

        public UsfmCleanResult ToResult(
            string inputPath,
            string outputPath,
            int filesScanned,
            int filesChanged,
            IReadOnlyList<string> structuralRepairs,
            IReadOnlyList<string> verificationIssues)
        {
            return new UsfmCleanResult(
                inputPath,
                outputPath,
                filesScanned,
                filesChanged,
                InlineDuplicateMarkersRemoved,
                PendingLineDuplicateMarkersRemoved,
                StrayLeadingVerseMarkersRemoved,
                VisibleVerseMarkersNormalized,
                SpacingFixes,
                StraightQuotesConverted,
                StraightSingleQuotesConverted,
                DirectionalDoubleQuotesRepaired,
                DirectionalSingleQuotesRepaired,
                UnpairedDoubleQuoteClosersRepaired,
                DirectSpeechFixes,
                ByteOrderMarksRemoved,
                UnsafeControlCharsRemoved,
                StructuralChunkFilesRemoved,
                ManifestFinishedChunksRemoved,
                verificationIssues.Count,
                outputPath + ".report.txt",
                structuralRepairs,
                verificationIssues);
        }
    }
}
