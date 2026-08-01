using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UsfmTools.Text;

namespace UsfmIntegrityStudio.Models;

public enum CanonProfile
{
    ProtestantOt,
    CatholicOt,
    OrthodoxOt,
    ProtestantNt,
    CatholicNt,
    OrthodoxNt
}

internal static class DocxScanService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly Regex ChapterRegex = new(
        "^(?:Глава|Chapter|باب)\\s*([0-9\\u0660-\\u0669\\u06F0-\\u06F9]+)\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BookTitleRegex = new(
        "^(?:(?:\\d+|ПЕРВАЯ|ВТОРАЯ|ТРЕТЬЯ|ЧЕТВЕРТАЯ|I|II|III|IV)\\s+)?(?:Книга|КНИГА|Book)\\s+.+(?:\\((?:\\d+\\s*)?[A-Za-z][^)]+\\))?\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex StandaloneBookTitleRegex = new(
        "^(?:\\d+\\s+[^()]{1,120}|ПСАЛТЫРЬ|Псалтирь)\\s*\\((?:\\d+\\s*)?[A-Za-z][^)]+\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EquivalentBookTitleRegex = new(
        "^(?:\\d+\\s+)?(?:Книга\\s+)?[\\p{L}\\p{M}\\s\\-]{2,80}\\s*=\\s*(?:\\d+\\s*)?[A-Za-z][A-Za-z\\s\\-]{1,80}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex VerseLeadRegex = new(
        @"^(?:\\v\s*)?([0-9\u0660-\u0669\u06F0-\u06F9]{1,3})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GenericTranslationLabelRegex = new(
        "^(?:سرائیکی\\s*ترجمہ|اُردو\\s*ترجمہ|اردو\\s*ترجمہ|translation)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex LooseLeadingVerseRegex = new(
        @"^\s*(?<n>\d{1,3})(?<tail>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmbeddedVerseMarkerRegex = new(
        @"(?:(?<=^)|(?<=[\s\.\!\?\;\:\)\]""'»”]))(?<n>\d{1,3})(?:[.)])?\s+(?=[\p{L}\p{M}""'«“\[])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, int> ProtestantOtChapterCounts = new(StringComparer.Ordinal)
    {
        ["GEN"] = 50, ["EXO"] = 40, ["LEV"] = 27, ["NUM"] = 36, ["DEU"] = 34,
        ["JOS"] = 24, ["JDG"] = 21, ["RUT"] = 4, ["1SA"] = 31, ["2SA"] = 24,
        ["1KI"] = 22, ["2KI"] = 25, ["1CH"] = 29, ["2CH"] = 36, ["EZR"] = 10,
        ["NEH"] = 13, ["EST"] = 10, ["JOB"] = 42, ["PSA"] = 150, ["PRO"] = 31,
        ["ECC"] = 12, ["SNG"] = 8, ["ISA"] = 66, ["JER"] = 52, ["LAM"] = 5,
        ["EZK"] = 48, ["DAN"] = 12, ["HOS"] = 14, ["JOL"] = 3, ["AMO"] = 9,
        ["OBA"] = 1, ["JON"] = 4, ["MIC"] = 7, ["NAM"] = 3, ["HAB"] = 3,
        ["ZEP"] = 3, ["HAG"] = 2, ["ZEC"] = 14, ["MAL"] = 4
    };

    private static readonly Dictionary<string, int> CommonNtChapterCounts = new(StringComparer.Ordinal)
    {
        ["MAT"] = 28, ["MRK"] = 16, ["LUK"] = 24, ["JHN"] = 21, ["ACT"] = 28,
        ["ROM"] = 16, ["1CO"] = 16, ["2CO"] = 13, ["GAL"] = 6, ["EPH"] = 6,
        ["PHP"] = 4, ["COL"] = 4, ["1TH"] = 5, ["2TH"] = 3, ["1TI"] = 6,
        ["2TI"] = 4, ["TIT"] = 3, ["PHM"] = 1, ["HEB"] = 13, ["JAS"] = 5,
        ["1PE"] = 5, ["2PE"] = 3, ["1JN"] = 5, ["2JN"] = 1, ["3JN"] = 1,
        ["JUD"] = 1, ["REV"] = 22
    };

    internal static readonly HashSet<string> OtBookIds = new(StringComparer.Ordinal)
    {
        "GEN","EXO","LEV","NUM","DEU","JOS","JDG","RUT","1SA","2SA","1KI","2KI","1CH","2CH","EZR","NEH","EST","JOB","PSA","PRO",
        "ECC","SNG","ISA","JER","LAM","EZK","DAN","HOS","JOL","AMO","OBA","JON","MIC","NAM","HAB","ZEP","HAG","ZEC","MAL"
    };

    internal static readonly HashSet<string> NtBookIds = new(StringComparer.Ordinal)
    {
        "MAT","MRK","LUK","JHN","ACT","ROM","1CO","2CO","GAL","EPH","PHP","COL","1TH","2TH","1TI","2TI","TIT","PHM","HEB","JAS","1PE","2PE","1JN","2JN","3JN","JUD","REV"
    };

    // Phase 1.1 canonical chapter verse counts (Protestant OT) for books currently in active workflow.
    private static readonly Dictionary<string, int[]> ProtestantOtVerseCounts = new(StringComparer.Ordinal)
    {
        ["GEN"] = [31,25,24,26,32,22,24,22,29,32,32,20,18,24,21,16,27,33,38,18,34,24,20,67,34,35,46,22,35,43,55,32,20,31,29,43,36,30,23,23,57,38,34,34,28,34,31,22,33,26],
        ["EXO"] = [22,25,22,31,23,30,25,32,35,29,10,51,22,31,27,36,16,27,25,26,36,31,33,18,40,37,21,43,46,38,18,35,23,35,35,38,29,31,43,38],
        ["LEV"] = [17,16,17,35,19,30,38,36,24,20,47,8,59,57,33,34,16,30,37,27,24,33,44,23,55,46,34],
        ["NUM"] = [54,34,51,49,31,27,89,26,23,36,35,16,33,45,41,50,13,32,22,29,35,41,30,25,18,65,23,31,40,16,54,42,56,29,34,13],
        ["DEU"] = [46,37,29,49,33,25,26,20,29,22,32,32,18,29,23,22,20,22,21,20,23,30,25,22,19,19,26,68,29,20,30,52,29,12],
        ["JOS"] = [18,24,17,24,15,27,26,35,27,43,23,24,33,15,63,10,18,28,51,9,45,34,16,33],
        ["JDG"] = [36,23,31,24,31,40,25,35,57,18,40,15,25,20,20,31,13,31,30,48,25],
        ["RUT"] = [22,23,18,22],
        ["1SA"] = [28,36,21,22,12,21,17,22,27,27,15,25,23,52,35,23,58,30,24,42,15,23,29,22,44,25,12,25,11,31,13],
        ["2SA"] = [27,32,39,12,25,23,29,18,13,19,27,31,39,33,37,23,29,33,43,26,22,51,39,25],
        ["1KI"] = [53,46,28,34,18,38,51,66,28,29,43,33,34,31,34,34,24,46,21,43,29,53],
        ["2KI"] = [18,25,27,44,27,33,20,29,37,36,21,21,25,29,38,20,41,37,37,21,26,20,37,20,30],
        ["1CH"] = [54,55,24,43,26,81,40,40,44,14,47,41,14,17,29,43,27,17,19,8,30,19,32,31,31,32,34,21,30],
        ["2CH"] = [17,18,17,22,14,42,22,18,31,19,23,16,22,15,19,14,19,34,11,37,20,12,21,27,28,23,9,27,36,27,21,33,25,33,27,23],
        ["EZR"] = [11,70,13,24,17,22,28,36,15,44],
        ["NEH"] = [11,20,32,23,19,19,73,18,38,39,36,47,31],
        ["EST"] = [22,23,15,17,14,14,10,17,32,3],
        ["JOB"] = [22,13,26,21,27,30,21,22,35,22,20,25,28,22,35,22,16,21,29,29,34,30,17,25,6,14,23,28,25,31,40,22,33,37,16,33,24,41,30,24,34,17],
        ["PSA"] = [6,12,8,8,12,10,17,9,20,18,7,8,6,7,5,11,15,50,14,9,13,31,6,10,22,12,14,9,11,12,24,11,22,22,28,12,40,22,13,17,13,11,5,26,17,11,9,14,20,23,19,9,6,7,23,13,11,11,17,12,8,12,11,10,13,20,7,35,36,5,24,20,28,23,10,13,20,72,13,19,16,8,18,12,13,17,7,18,52,17,16,15,5,23,11,13,12,9,9,5,8,28,22,35,45,48,43,13,31,7,10,10,9,8,18,19,2,29,176,7,8,9,4,8,5,6,5,6,8,8,3,18,3,3,21,26,9,8,24,13,10,7,12,15,21,10,20,14,9,6],
        ["PRO"] = [33,22,35,27,23,35,27,36,18,32,31,28,25,35,33,33,28,24,29,30,31,29,35,34,28,28,27,28,27,33,31]
    };

    private static readonly Dictionary<string, int[]> CommonNtVerseCounts = new(StringComparer.Ordinal)
    {
        ["MAT"] = [25,23,17,25,48,34,29,34,38,42,30,50,58,36,39,28,27,35,30,34,46,46,39,51,46,75,66,20],
        ["MRK"] = [45,28,35,41,43,56,37,38,50,52,33,44,37,72,47,20],
        ["LUK"] = [80,52,38,44,39,49,50,56,62,42,54,59,35,35,32,31,37,43,48,47,38,71,56,53],
        ["JHN"] = [51,25,36,54,47,71,53,59,41,42,57,50,38,31,27,33,26,40,42,31,25],
        ["ACT"] = [26,47,26,37,42,15,60,40,43,48,30,25,52,28,41,40,34,28,41,38,40,30,35,27,27,32,44,31],
        ["ROM"] = [32,29,31,25,21,23,25,39,33,21,36,21,14,23,33,27],
        ["1CO"] = [31,16,23,21,13,20,40,13,27,33,34,31,13,40,58,24],
        ["2CO"] = [24,17,18,18,21,18,16,24,15,18,33,21,14],
        ["GAL"] = [24,21,29,31,26,18],
        ["EPH"] = [23,22,21,32,33,24],
        ["PHP"] = [30,30,21,23],
        ["COL"] = [29,23,25,18],
        ["1TH"] = [10,20,13,18,28],
        ["2TH"] = [12,17,18],
        ["1TI"] = [20,15,16,16,25,21],
        ["2TI"] = [18,26,17,22],
        ["TIT"] = [16,15,15],
        ["PHM"] = [25],
        ["HEB"] = [14,18,19,16,14,20,28,13,28,39,40,29,25],
        ["JAS"] = [27,26,18,17,20],
        ["1PE"] = [25,25,22,19,14],
        ["2PE"] = [21,22,18],
        ["1JN"] = [10,29,24,21,21],
        ["2JN"] = [13],
        ["3JN"] = [15],
        ["JUD"] = [25],
        ["REV"] = [20,29,22,11,14,17,17,13,21,11,19,17,18,20,8,21,18,24,21,15,27,21]
    };

    // Phase 1.2 Catholic OT (current workflow books share the same verse map as Protestant for these books).
    private static readonly Dictionary<string, int[]> CatholicOtVerseCounts = CloneVerseCounts(ProtestantOtVerseCounts);

    // Phase 1.2 Orthodox OT (LXX-aware handling):
    // explicit Psalm versification table derived from Russian Orthodox source corpus.
    private static readonly int[] OrthodoxOtPsalmsVerseCounts =
    [
        6,12,8,8,12,10,17,9,38,7,8,5,7,5,11,15,50,14,9,13,31,6,10,22,12,14,9,11,12,24,11,22,22,28,12,40,22,13,17,13,11,5,26,17,11,9,14,20,23,20,10,6,8,23,13,11,11,17,13,8,12,11,10,13,19,7,35,36,5,24,20,28,23,10,12,20,72,13,19,16,8,18,12,13,17,6,18,52,16,16,16,5,23,11,13,12,9,9,5,8,28,22,35,45,48,43,13,31,7,10,10,9,26,9,10,2,29,176,7,8,9,4,8,5,6,5,6,8,8,3,18,3,3,21,26,9,8,24,13,10,7,12,15,21,10,11,9,14,9,6
    ];

    private static readonly Dictionary<string, int[]> OrthodoxOtVerseCounts = BuildOrthodoxOtVerseCounts();

    public static DocxScanResult Scan(string docxPath, CanonProfile canonProfile = CanonProfile.ProtestantOt)
    {
        var result = new DocxScanResult
        {
            CanonProfileUsed = canonProfile
        };
        var chapterCounts = GetChapterCounts(canonProfile);
        var versification = GetVersificationProfile(canonProfile);
        result.VersificationProfileUsed = versification.DisplayName;

        if (!File.Exists(docxPath))
        {
            result.Issues.Add(new ScanIssue("Error", "DOCX_NOT_FOUND", $"DOCX not found: {docxPath}"));
            return result;
        }

        XDocument? documentXml;
        using (var archive = ZipFile.OpenRead(docxPath))
        {
            var docEntry = archive.GetEntry("word/document.xml");
            if (docEntry is null)
            {
                result.Issues.Add(new ScanIssue("Error", "DOCX_ENTRY_MISSING", "word/document.xml is missing."));
                return result;
            }

            using var stream = docEntry.Open();
            documentXml = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }

        var body = documentXml.Root?.Element(W + "body");
        if (body is null)
        {
            result.Issues.Add(new ScanIssue("Error", "DOCX_BODY_MISSING", "w:body not found."));
            return result;
        }

        BookScanState? currentBook = null;
        var detectedBookOrder = new List<string>();

        foreach (var paragraph in body.Elements(W + "p"))
        {
            result.ParagraphCount++;

            var text = NormalizeWhitespace(ExtractText(paragraph)).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (IsIgnorablePreChapterHeaderLine(text))
            {
                continue;
            }

            if (TryResolveCanonicalBookTitle(text, canonProfile, out var resolvedBookId))
            {
                if (currentBook is not null
                    && string.Equals(currentBook.BookId, resolvedBookId, StringComparison.Ordinal))
                {
                    continue;
                }

                currentBook = new BookScanState(text, resolvedBookId);
                result.Books.Add(currentBook);
                if (!string.IsNullOrWhiteSpace(resolvedBookId))
                {
                    detectedBookOrder.Add(resolvedBookId);
                    result.RegisterDetectedBook(resolvedBookId);
                }
                continue;
            }
            else if (LooksLikeBookHeadingBoundary(text))
            {
                // Unmapped heading still marks a boundary; stop assigning upcoming chapters/verses to prior book.
                currentBook = null;
                result.Issues.Add(new ScanIssue("Info", "UNMAPPED_BOOK_HEADING", $"Detected unmapped book heading boundary: '{text}'"));
                continue;
            }

            var chapterMatch = ChapterRegex.Match(text);
            if (chapterMatch.Success)
            {
                if (currentBook is null)
                {
                    result.Issues.Add(new ScanIssue("Warning", "CHAPTER_BEFORE_BOOK", $"Found chapter before book: '{text}'"));
                    continue;
                }

                if (!TryParseScriptNumber(chapterMatch.Groups[1].Value, out var chapter) || chapter <= 0)
                {
                    result.Issues.Add(new ScanIssue("Warning", "CHAPTER_INVALID", $"Invalid chapter marker: '{text}'"));
                    continue;
                }

                currentBook.StartChapter(chapter, result.Issues);
                continue;
            }

            var verseMatch = VerseLeadRegex.Match(text);
            if (!verseMatch.Success)
            {
                continue;
            }

            if (currentBook is null)
            {
                result.Issues.Add(new ScanIssue("Warning", "VERSE_BEFORE_BOOK", $"Verse text before book heading: '{text}'"));
                continue;
            }

            if (!TryParseScriptNumber(verseMatch.Groups[1].Value, out var verse) || verse <= 0)
            {
                continue;
            }

            currentBook.AddVerse(verse, result.Issues);

            var embeddedMarkers = ExtractEmbeddedVerseMarkers(text, verse);
            if (embeddedMarkers.Count > 0)
            {
                foreach (var marker in embeddedMarkers)
                {
                    currentBook.AddVerse(marker, result.Issues);
                }

                result.Issues.Add(new ScanIssue(
                    "Info",
                    "VERSE_MERGED_MARKERS_DETECTED",
                    $"{currentBook.Title}: detected embedded verse markers in one paragraph ({string.Join(",", embeddedMarkers.Take(8))}{(embeddedMarkers.Count > 8 ? ",..." : string.Empty)})."));
            }
        }

        foreach (var book in result.Books)
        {
            book.FinalizeIntegrityChecks(result.Issues);

            if (!string.IsNullOrWhiteSpace(book.BookId)
                && chapterCounts.TryGetValue(book.BookId, out var expectedChapterCount)
                && book.ChapterCount != expectedChapterCount)
            {
                result.Issues.Add(new ScanIssue(
                    "Warning",
                    "CHAPTER_COUNT_MISMATCH",
                    $"{book.Title}: detected {book.ChapterCount} chapter(s), {versification.DisplayName} expects {expectedChapterCount}."));
            }

            ValidateCanonicalVerseCounts(book, result.Issues, versification);
        }

        ValidateOtOrder(detectedBookOrder, result.Issues);
        if (canonProfile is CanonProfile.CatholicOt or CanonProfile.OrthodoxOt)
        {
            result.Issues.Add(new ScanIssue(
                "Info",
                "CANON_PROFILE_SCOPE",
                $"{GetCanonDisplayName(canonProfile)} selected: canonical checks currently cover shared OT books in this workflow."));
        }
        else if (canonProfile is CanonProfile.CatholicNt or CanonProfile.OrthodoxNt)
        {
            result.Issues.Add(new ScanIssue(
                "Info",
                "CANON_PROFILE_SCOPE",
                $"{GetCanonDisplayName(canonProfile)} selected: chapter checks cover common NT books in this workflow."));
        }

        result.Issues.Add(new ScanIssue("Info", "CANON_SELECTED", $"Selected canon profile: {GetCanonDisplayName(canonProfile)}."));
        result.Issues.Add(new ScanIssue("Info", "VERSIFICATION_PROFILE_SELECTED", $"Versification profile: {versification.DisplayName}."));
        result.Issues.Add(new ScanIssue("Info", "BOOKS_DETECTED_OT", $"Detected OT books: {FormatBookList(result.DetectedOtBookIds)}"));
        result.Issues.Add(new ScanIssue("Info", "BOOKS_DETECTED_NT", $"Detected NT books: {FormatBookList(result.DetectedNtBookIds)}"));

        return result;
    }

    public static DocxStandardizeResult Standardize(
        string inputDocxPath,
        string outputDocxPath,
        CanonProfile canonProfile = CanonProfile.ProtestantOt,
        IReadOnlySet<string>? selectedBookIds = null,
        IReadOnlySet<string>? selectedBookTitles = null,
        bool inferMissingVerseMarkers = true)
    {
        var changedTextNodeCount = 0;
        var changedParagraphCount = 0;
        var versification = GetVersificationProfile(canonProfile);
        var selectedIds = selectedBookIds ?? new HashSet<string>(StringComparer.Ordinal);
        var selectedTitles = selectedBookTitles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filterByBook = selectedIds.Count > 0 || selectedTitles.Count > 0;

        var destinationDirectory = Path.GetDirectoryName(outputDocxPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(outputDocxPath))
        {
            File.Delete(outputDocxPath);
        }

        using (var source = ZipFile.OpenRead(inputDocxPath))
        using (var destination = ZipFile.Open(outputDocxPath, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var targetEntry = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var inputStream = entry.Open();
                using var outputStream = targetEntry.Open();

                if (!string.Equals(entry.FullName, "word/document.xml", StringComparison.OrdinalIgnoreCase))
                {
                    inputStream.CopyTo(outputStream);
                    continue;
                }

                var xml = XDocument.Load(inputStream, LoadOptions.PreserveWhitespace);
                var body = xml.Root?.Element(W + "body");
                if (body is not null)
                {
                    string? currentBookId = null;
                    string? currentBookTitle = null;
                    var currentChapter = 0;
                    var currentVerse = 0;
                    var expectedMaxVerse = 0;
                    var previousParagraphWasStandaloneVerseMarker = false;
                    foreach (var paragraph in body.Elements(W + "p"))
                    {
                        var paragraphText = NormalizeWhitespace(ExtractText(paragraph)).Trim();
                        if (IsIgnorablePreChapterHeaderLine(paragraphText))
                        {
                            continue;
                        }

                        if (TryResolveCanonicalBookTitle(paragraphText, canonProfile, out var resolvedBookId))
                        {
                            if (!string.Equals(currentBookId, resolvedBookId, StringComparison.Ordinal))
                            {
                                currentBookTitle = paragraphText;
                                currentBookId = resolvedBookId;
                                currentChapter = 0;
                                currentVerse = 0;
                                expectedMaxVerse = 0;
                            }
                        }
                        else if (LooksLikeBookHeadingBoundary(paragraphText))
                        {
                            // Boundary reset: avoid standardizing subsequent books when one selected book was chosen.
                            currentBookTitle = paragraphText;
                            currentBookId = null;
                            currentChapter = 0;
                            currentVerse = 0;
                            expectedMaxVerse = 0;
                        }

                        var chapterMatch = ChapterRegex.Match(paragraphText);
                        if (chapterMatch.Success && TryParseScriptNumber(chapterMatch.Groups[1].Value, out var parsedChapter) && parsedChapter > 0)
                        {
                            currentChapter = parsedChapter;
                            currentVerse = 0;
                            expectedMaxVerse = TryGetExpectedVerseCount(versification, currentBookId, currentChapter);
                            previousParagraphWasStandaloneVerseMarker = false;
                        }

                        if (filterByBook && !IsSelectedBook(currentBookId, currentBookTitle, selectedIds, selectedTitles))
                        {
                            previousParagraphWasStandaloneVerseMarker = false;
                            continue;
                        }

                        var paragraphChanged = false;
                        if (TryReadLooseLeadingVerse(paragraphText, out var looseVerse) && looseVerse > 0)
                        {
                            currentVerse = looseVerse;
                            if (TryNormalizeLeadingVerseMarker(paragraph, looseVerse))
                            {
                                paragraphText = NormalizeWhitespace(ExtractText(paragraph)).Trim();
                                changedTextNodeCount++;
                                paragraphChanged = true;
                            }
                        }

                        var verseMatch = VerseLeadRegex.Match(paragraphText);
                        if (verseMatch.Success && TryParseScriptNumber(verseMatch.Groups[1].Value, out var explicitVerse) && explicitVerse > 0)
                        {
                            currentVerse = explicitVerse;
                            previousParagraphWasStandaloneVerseMarker = IsStandaloneVerseMarkerParagraph(paragraphText);
                        }
                        else if (previousParagraphWasStandaloneVerseMarker)
                        {
                            // A standalone marker paragraph like "\v 1" means the following text paragraph
                            // already belongs to that verse, so do not inject a synthetic next marker.
                            previousParagraphWasStandaloneVerseMarker = false;
                        }
                        else if (inferMissingVerseMarkers && ShouldInferVerseMarker(paragraphText, currentChapter, currentVerse, expectedMaxVerse))
                        {
                            var inferredVerse = currentVerse + 1;
                            if (PrefixParagraphWithVerseMarker(paragraph, inferredVerse))
                            {
                                paragraphText = NormalizeWhitespace(ExtractText(paragraph)).Trim();
                                currentVerse = inferredVerse;
                                changedTextNodeCount++;
                                paragraphChanged = true;
                            }

                            previousParagraphWasStandaloneVerseMarker = false;
                        }
                        else
                        {
                            previousParagraphWasStandaloneVerseMarker = false;
                        }

                        foreach (var textNode in paragraph.Descendants(W + "t"))
                        {
                            var original = textNode.Value;
                            var normalized = NormalizeUserFacingText(original);
                            if (!string.Equals(original, normalized, StringComparison.Ordinal))
                            {
                                textNode.Value = normalized;
                                changedTextNodeCount++;
                                paragraphChanged = true;
                            }
                        }

                        if (paragraphChanged)
                        {
                            changedParagraphCount++;
                        }
                    }
                }

                xml.Save(outputStream);
            }
        }

        var postScan = Scan(outputDocxPath, canonProfile);
        var markerIssues = postScan.Issues
            .Where(issue => IsCanonicalMarkerIssue(issue.Code))
            .ToList();
        var canonReportPath = Path.Combine(
            Path.GetDirectoryName(outputDocxPath) ?? Directory.GetCurrentDirectory(),
            Path.GetFileNameWithoutExtension(outputDocxPath) + "_canon-highlights.txt");
        WriteCanonicalHighlightsReport(canonReportPath, canonProfile, markerIssues, postScan.VersificationProfileUsed);
        return new DocxStandardizeResult(
            outputDocxPath,
            changedParagraphCount,
            changedTextNodeCount,
            canonReportPath,
            markerIssues.Count);
    }

    private static bool ShouldInferVerseMarker(string paragraphText, int currentChapter, int currentVerse, int expectedMaxVerse)
    {
        if (currentChapter <= 0 || currentVerse <= 0)
        {
            return false;
        }

        if (expectedMaxVerse > 0 && currentVerse >= expectedMaxVerse)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(paragraphText))
        {
            return false;
        }

        if (ChapterRegex.IsMatch(paragraphText))
        {
            return false;
        }

        if (VerseLeadRegex.IsMatch(paragraphText))
        {
            return false;
        }

        if (LooksLikeBookHeadingBoundary(paragraphText))
        {
            return false;
        }

        return paragraphText.Length >= 3;
    }

    private static bool PrefixParagraphWithVerseMarker(XElement paragraph, int verseNumber)
    {
        var firstTextNode = paragraph.Descendants(W + "t").FirstOrDefault();
        if (firstTextNode is null)
        {
            return false;
        }

        var current = firstTextNode.Value ?? string.Empty;
        var trimmed = current.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (Regex.IsMatch(trimmed, "^(\\d{1,3})(?:[.)])?\\s+"))
        {
            return false;
        }

        var leadingCount = current.Length - trimmed.Length;
        var leading = leadingCount > 0 ? current[..leadingCount] : string.Empty;
        firstTextNode.Value = $"{leading}{verseNumber}. {trimmed}";
        return true;
    }

    private static bool IsStandaloneVerseMarkerParagraph(string paragraphText)
    {
        if (string.IsNullOrWhiteSpace(paragraphText))
        {
            return false;
        }

        return Regex.IsMatch(
            paragraphText.Trim(),
            @"^\\v\s+[\d\u0660-\u0669\u06F0-\u06F9]{1,3}\s*$",
            RegexOptions.CultureInvariant);
    }

    private static int TryGetExpectedVerseCount(VersificationProfile versification, string? bookId, int chapter)
    {
        if (string.IsNullOrWhiteSpace(bookId) || chapter <= 0)
        {
            return 0;
        }

        if (!versification.VerseCountsByBook.TryGetValue(bookId, out var chapterVerses))
        {
            return 0;
        }

        if (chapter > chapterVerses.Length)
        {
            return 0;
        }

        return chapterVerses[chapter - 1];
    }

    private static bool IsSelectedBook(
        string? bookId,
        string? bookTitle,
        IReadOnlySet<string> selectedBookIds,
        IReadOnlySet<string> selectedBookTitles)
    {
        if (!string.IsNullOrWhiteSpace(bookId) && selectedBookIds.Contains(bookId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(bookTitle) && selectedBookTitles.Contains(bookTitle.Trim()))
        {
            return true;
        }

        return false;
    }

    private static void ValidateOtOrder(IReadOnlyList<string> detectedBookIds, ICollection<ScanIssue> issues)
    {
        if (detectedBookIds.Count == 0)
        {
            return;
        }

        var canonicalOrder = new[]
        {
            "GEN","EXO","LEV","NUM","DEU","JOS","JDG","RUT","1SA","2SA","1KI","2KI","1CH","2CH","EZR","NEH","EST","JOB","PSA","PRO",
            "ECC","SNG","ISA","JER","LAM","EZK","DAN","HOS","JOL","AMO","OBA","JON","MIC","NAM","HAB","ZEP","HAG","ZEC","MAL"
        };

        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < canonicalOrder.Length; i++)
        {
            position[canonicalOrder[i]] = i;
        }

        var previous = -1;
        foreach (var bookId in detectedBookIds)
        {
            if (!position.TryGetValue(bookId, out var current))
            {
                continue;
            }

            if (previous > current)
            {
                issues.Add(new ScanIssue("Warning", "BOOK_ORDER_MISMATCH", $"Book order mismatch around {bookId}."));
                return;
            }

            previous = current;
        }
    }

    private static void ValidateCanonicalVerseCounts(
        BookScanState book,
        ICollection<ScanIssue> issues,
        VersificationProfile versification)
    {
        if (string.IsNullOrWhiteSpace(book.BookId))
        {
            return;
        }

        if (versification.RelaxedBooks.Contains(book.BookId))
        {
            issues.Add(new ScanIssue(
                "Info",
                "VERSIFICATION_RELAXED_BOOK",
                $"{book.Title}: strict verse-count checks skipped for {book.BookId} under {versification.DisplayName}."));
            return;
        }

        if (!versification.VerseCountsByBook.TryGetValue(book.BookId, out var expectedByChapter))
        {
            return;
        }

        for (var chapter = 1; chapter <= expectedByChapter.Length; chapter++)
        {
            var expectedMax = expectedByChapter[chapter - 1];
            if (!book.TryGetVerses(chapter, out var detectedVerses))
            {
                issues.Add(new ScanIssue(
                    "Warning",
                    "CANON_CHAPTER_MISSING",
                    $"{book.Title}: expected chapter {chapter} (max verse {expectedMax}) is missing."));
                continue;
            }

            for (var verse = 1; verse <= expectedMax; verse++)
            {
                if (!detectedVerses.Contains(verse))
                {
                    issues.Add(new ScanIssue(
                        "Warning",
                        "CANON_VERSE_MISSING",
                        $"{book.Title} {chapter}:{verse} missing ({versification.DisplayName} canonical check)."));
                }
            }

            foreach (var extra in detectedVerses.Where(v => v > expectedMax).OrderBy(v => v))
            {
                issues.Add(new ScanIssue(
                    "Warning",
                    "CANON_VERSE_EXTRA",
                    $"{book.Title} {chapter}:{extra} exceeds canonical max {expectedMax}."));
            }
        }

        foreach (var extraChapter in book.Chapters.Where(c => c > expectedByChapter.Length).OrderBy(c => c))
        {
            issues.Add(new ScanIssue(
                "Warning",
                "CANON_CHAPTER_EXTRA",
                $"{book.Title}: extra chapter {extraChapter} exceeds canonical chapter count {expectedByChapter.Length}."));
        }
    }

    private static IReadOnlyDictionary<string, int> GetChapterCounts(CanonProfile canonProfile)
    {
        return canonProfile switch
        {
            CanonProfile.ProtestantNt or CanonProfile.CatholicNt or CanonProfile.OrthodoxNt => CommonNtChapterCounts,
            _ => ProtestantOtChapterCounts
        };
    }

    internal static VersificationProfile GetVersificationProfile(CanonProfile canonProfile)
    {
        return canonProfile switch
        {
            CanonProfile.CatholicOt => new VersificationProfile(
                "catholic-ot-v1",
                "Catholic OT versification (v1)",
                CatholicOtVerseCounts,
                new HashSet<string>(StringComparer.Ordinal)),
            CanonProfile.OrthodoxOt => new VersificationProfile(
                "orthodox-ot-lxx-v1",
                "Orthodox OT versification (LXX-aware v2)",
                OrthodoxOtVerseCounts,
                new HashSet<string>(StringComparer.Ordinal)),
            CanonProfile.ProtestantNt => new VersificationProfile(
                "protestant-nt-v1",
                "Protestant NT versification (v1)",
                CommonNtVerseCounts,
                new HashSet<string>(StringComparer.Ordinal)),
            CanonProfile.CatholicNt => new VersificationProfile(
                "catholic-nt-v1",
                "Catholic NT versification (v1)",
                CommonNtVerseCounts,
                new HashSet<string>(StringComparer.Ordinal)),
            CanonProfile.OrthodoxNt => new VersificationProfile(
                "orthodox-nt-v1",
                "Orthodox NT versification (v1)",
                CommonNtVerseCounts,
                new HashSet<string>(StringComparer.Ordinal)),
            _ => new VersificationProfile(
                "protestant-ot-v1",
                "Protestant OT versification (v1)",
                ProtestantOtVerseCounts,
                new HashSet<string>(StringComparer.Ordinal))
        };
    }

    private static string GetCanonDisplayName(CanonProfile canonProfile)
    {
        return canonProfile switch
        {
            CanonProfile.CatholicOt => "Catholic OT",
            CanonProfile.OrthodoxOt => "Orthodox OT",
            CanonProfile.ProtestantNt => "Protestant NT",
            CanonProfile.CatholicNt => "Catholic NT",
            CanonProfile.OrthodoxNt => "Orthodox NT",
            _ => "Protestant OT"
        };
    }

    private static string FormatBookList(IReadOnlyList<string> ids)
    {
        return ids.Count == 0 ? "(none)" : string.Join(", ", ids);
    }

    private static bool IsCanonicalMarkerIssue(string code)
    {
        return code is "CHAPTER_COUNT_MISMATCH"
            or "CANON_CHAPTER_MISSING"
            or "CANON_VERSE_MISSING"
            or "CANON_VERSE_EXTRA"
            or "CANON_CHAPTER_EXTRA";
    }

    private static void WriteCanonicalHighlightsReport(
        string reportPath,
        CanonProfile canonProfile,
        IReadOnlyList<ScanIssue> markerIssues,
        string? versificationProfile = null)
    {
        var lines = new List<string>
        {
            "DOCX Canonical Marker Highlight Report",
            $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"Canon Profile: {GetCanonDisplayName(canonProfile)}",
            $"Versification Profile: {versificationProfile ?? "Protestant OT versification (v1)"}",
            $"Marker Issue Count: {markerIssues.Count}",
            string.Empty
        };

        if (markerIssues.Count == 0)
        {
            lines.Add("No canonical marker issues found.");
        }
        else
        {
            lines.Add("Issues:");
            foreach (var issue in markerIssues)
            {
                lines.Add($"[{issue.Severity}] {issue.Code}: {issue.Message}");
            }
        }

        File.WriteAllLines(reportPath, lines);
    }

    private static Dictionary<string, int[]> BuildOrthodoxOtVerseCounts()
    {
        var map = CloneVerseCounts(ProtestantOtVerseCounts);
        map["PSA"] = OrthodoxOtPsalmsVerseCounts.ToArray();
        return map;
    }

    private static Dictionary<string, int[]> CloneVerseCounts(IReadOnlyDictionary<string, int[]> source)
    {
        var copy = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var kvp in source)
        {
            copy[kvp.Key] = kvp.Value.ToArray();
        }

        return copy;
    }

    private static string NormalizeWhitespace(string value)
    {
        var normalized = value.Replace('\u00A0', ' ');
        if (ContainsArabicScript(normalized))
        {
            return ScripturePunctuationNormalizer.NormalizeArabicDerivedSpacing(normalized);
        }

        return Regex.Replace(normalized, "\\s+", " ");
    }

    private static string NormalizeUserFacingText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (ContainsArabicScript(value))
        {
            return ScripturePunctuationNormalizer.NormalizeArabicDerivedSpacing(value.Replace('\u00A0', ' '));
        }

        var result = value.Replace('\u00A0', ' ');
        result = Regex.Replace(result, "[ \t]{2,}", " ");
        result = Regex.Replace(result, "\\s+([,.;:!?])", "$1");
        result = Regex.Replace(result, "([,.;:!?])(?=[\\p{L}\\p{N}])", "$1 ");
        return result;
    }

    private static bool TryResolveCanonicalBookTitle(string text, CanonProfile canonProfile, out string? bookId)
    {
        bookId = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        if (ChapterRegex.IsMatch(text))
        {
            return false;
        }

        var canonBookIds = GetCanonBookIdSet(canonProfile);
        var inferred = InferBookId(text);
        var looksLikeBookTitle = BookTitleRegex.IsMatch(text)
                                 || StandaloneBookTitleRegex.IsMatch(text)
                                 || EquivalentBookTitleRegex.IsMatch(text)
                                 || IsNumericBookTitle(text)
                                 || (IsAliasOnlyBookHeadingCandidate(text) && !string.IsNullOrWhiteSpace(inferred));
        if (!looksLikeBookTitle)
        {
            return false;
        }

        bookId = inferred;
        if (string.IsNullOrWhiteSpace(bookId))
        {
            return false;
        }

        return canonBookIds.Contains(bookId);
    }

private static bool IsNumericBookTitle(string text)
{
    var normalized = NormalizeIndicDigits(text);
    var regex = new Regex(
        @"^(?:\d+|I|II|III|IV)\s*[.\-۔]?\s*(?:Книга\s+|Book\s+)?[\p{L}\p{M}][\p{L}\p{M}\s\-]{1,80}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    if (!regex.IsMatch(normalized))
    {
        return false;
    }

    if (text.IndexOfAny(new[] { '.', '!', '?', ':', ';', ',', '"' }) >= 0)
    {
        return false;
    }

    if (IsLikelyVerseText(text))
    {
        return false;
    }

    var words = Regex.Matches(text, "[\\p{L}\\p{M}]+").Count;
    if (words > 8)
    {
        return false;
    }

    var trimmed = normalized.TrimStart();
    var numericPrefix = Regex.Match(trimmed, "^(\\d+)");
    if (numericPrefix.Success
        && int.TryParse(numericPrefix.Groups[1].Value, out var numericBook)
        && numericBook > 4)
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(InferBookId(text)))
    {
        return false;
    }

    return true;
}

private static string ExtractText
(XElement paragraph)
    {
        return string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value));
    }

    private static string? InferBookId(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var t = NormalizeForAliasMatch(title);

        if (t.Contains("genesis") || t.Contains("бытие") || t.Contains("پیدائش")) return "GEN";
        if (t.Contains("exodus") || t.Contains("исход") || t.Contains("خروج")) return "EXO";
        if (t.Contains("leviticus") || t.Contains("левит") || t.Contains("احبار")) return "LEV";
        if (t.Contains("numbers") || t.Contains("числа") || t.Contains("گنتی")) return "NUM";
        if (t.Contains("deuteronomy") || t.Contains("второзаконие") || t.Contains("استثنا")) return "DEU";
        if (t.Contains("joshua") || t.Contains("иисусанавина") || t.Contains("یشوع")) return "JOS";
        if (t.Contains("judges") || t.Contains("судей") || t.Contains("قضاۃ")) return "JDG";
        if (t.Contains("ruth") || t.Contains("руфь") || t.Contains("روت")) return "RUT";
        if (t.Contains("1samuel") || t.Contains("1книгасамуила") || t.Contains("اولسموئیل") || t.Contains("پہلاسموئیل") || t.Contains("1سموئیل")) return "1SA";
        if (t.Contains("2samuel") || t.Contains("втораякнигацарств") || t.Contains("دومسموئیل") || t.Contains("دوسراسموئیل") || t.Contains("2سموئیل")) return "2SA";
        if (t.Contains("1kings") || t.Contains("3царств") || t.Contains("اولسلاطین") || t.Contains("پہلاسلاطین")) return "1KI";
        if (t.Contains("2kings") || t.Contains("4царств") || t.Contains("دومسلاطین") || t.Contains("دوسراسلاطین")) return "2KI";
        if (t.Contains("1chronicles") || t.Contains("1книгапаралипоменон") || t.Contains("اولتواریخ") || t.Contains("پہلاتواریخ")) return "1CH";
        if (t.Contains("2chronicles") || t.Contains("втораякнигапаралипоменон") || t.Contains("دومتواریخ") || t.Contains("دوسراتواریخ")) return "2CH";
        if (t.Contains("ezra") || t.Contains("ездры") || t.Contains("عزرا")) return "EZR";
        if (t.Contains("nehemiah") || t.Contains("неемии") || t.Contains("نحمیاہ")) return "NEH";
        if (t.Contains("esther") || t.Contains("эсфирь") || t.Contains("آستر")) return "EST";
        if (t.Contains("job") || t.Contains("иова") || t.Contains("ایوب")) return "JOB";
        if (t.Contains("psalm") || t.Contains("псалт") || t.Contains("زبور")) return "PSA";
        if (t.Contains("proverbs") || t.Contains("притчи") || t.Contains("امثال")) return "PRO";
        if (t.Contains("ecclesiastes") || t.Contains("qoheleth") || t.Contains("екклесиаст") || t.Contains("екклезиаст") || t.Contains("экклесиаст") || t.Contains("экклезиаст") || t.Contains("واعظ")) return "ECC";
        if (t.Contains("songofsongs") || t.Contains("songofsolomon") || t.Contains("песньпесней") || t.Contains("песняпесней") || t.Contains("غزلالغزلات")) return "SNG";
        if (t.Contains("isaiah") || t.Contains("исай") || t.Contains("یسعیاہ")) return "ISA";
        if (t.Contains("jeremiah") || t.Contains("иереми") || t.Contains("یرمیاہ") || t.Contains("یرمیا") || t.Contains("ارمیا") || t.Contains("اِرمیا") || t.Contains("ارمی") || t.Contains("اِرمی")) return "JER";
        if (t.Contains("lamentations") || t.Contains("плачиеремии") || t.Contains("نوحہ")) return "LAM";
        if (t.Contains("ezekiel") || t.Contains("иезекиил") || t.Contains("حزقیایل")) return "EZK";
        if (t.Contains("daniel") || t.Contains("даниил") || t.Contains("دانیایل")) return "DAN";
        if (t.Contains("hosea") || t.Contains("осии") || t.Contains("осия") || t.Contains("ہوسیع")) return "HOS";
        if (t.Contains("joel") || t.Contains("иоил") || t.Contains("یوایل")) return "JOL";
        if (t.Contains("amos") || t.Contains("амос") || t.Contains("عاموس")) return "AMO";
        if (t.Contains("obadiah") || t.Contains("авди") || t.Contains("عبدیاہ")) return "OBA";
        if (t.Contains("jonah") || t.Contains("ионы") || t.Contains("یونس")) return "JON";
        if (t.Contains("micah") || t.Contains("михе") || t.Contains("میکاہ")) return "MIC";
        if (t.Contains("nahum") || t.Contains("наум") || t.Contains("ناحوم")) return "NAM";
        if (t.Contains("habakkuk") || t.Contains("аввакум") || t.Contains("حبقوق")) return "HAB";
        if (t.Contains("zephaniah") || t.Contains("софони") || t.Contains("صفنیاہ")) return "ZEP";
        if (t.Contains("haggai") || t.Contains("агге") || t.Contains("حجی")) return "HAG";
        if (t.Contains("zechariah") || t.Contains("захари") || t.Contains("زکریاہ")) return "ZEC";
        if (t.Contains("malachi") || t.Contains("малахи") || t.Contains("ملاکی")) return "MAL";
        if (t.Contains("matthew") || t.Contains("матф") || t.Contains("متی")) return "MAT";
        if (t.Contains("mark") || t.Contains("марк") || t.Contains("مرقس")) return "MRK";
        if (t.Contains("luke") || t.Contains("лук") || t.Contains("لوقا")) return "LUK";
        if (t.Contains("john") || t.Contains("иоан") || t.Contains("отиоанна") || t.Contains("یوحنا")) return "JHN";
        if (t.Contains("acts") || t.Contains("деяния") || t.Contains("اعمال")) return "ACT";

        return null;
    }

    private static string NormalizeForAliasMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in NormalizeIndicDigits(value))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static bool LooksLikeBookHeadingBoundary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ChapterRegex.IsMatch(text))
        {
            return false;
        }

        if (text.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        if (IsLikelyVerseText(text))
        {
            return false;
        }

        return BookTitleRegex.IsMatch(text)
               || StandaloneBookTitleRegex.IsMatch(text)
               || EquivalentBookTitleRegex.IsMatch(text)
               || IsNumericBookTitle(text)
               || (IsAliasOnlyBookHeadingCandidate(text) && !string.IsNullOrWhiteSpace(InferBookId(text)));
    }

    private static bool IsAliasOnlyBookHeadingCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ChapterRegex.IsMatch(text) || IsLikelyVerseText(text))
        {
            return false;
        }

        if (text.IndexOfAny(new[] { '.', '!', '?', ':', ';', ',', '"', '۔', '\\' }) >= 0)
        {
            return false;
        }

        var words = Regex.Matches(text, @"[\p{L}\p{M}]+").Count;
        if (words is 0 or > 5)
        {
            return false;
        }

        return text.Trim().Length <= 40;
    }

    private static bool IsLikelyVerseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lead = Regex.Match(text, "^\\s*\\d{1,3}(?:[.)])?\\s+");
        if (!lead.Success)
        {
            return false;
        }

        var wordCount = Regex.Matches(text, "[\\p{L}\\p{M}]+").Count;
        if (wordCount >= 7)
        {
            return true;
        }

        // A common verse pattern is a number plus a full sentence.
        return text.Contains(' ') && text.Length > 40;
    }

    private static bool IsIgnorablePreChapterHeaderLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ChapterRegex.IsMatch(text) || VerseLeadRegex.IsMatch(text))
        {
            return false;
        }

        if (GenericTranslationLabelRegex.IsMatch(text))
        {
            return true;
        }

        var normalized = NormalizeForAliasMatch(text);
        if (normalized.Length <= 1)
        {
            return false;
        }

        if (normalized.Contains("سموئیل", StringComparison.Ordinal)
            || normalized.Contains("سمو", StringComparison.Ordinal)
            || normalized.Contains("samuel", StringComparison.Ordinal))
        {
            return false;
        }

        var wordCount = Regex.Matches(text, @"[\p{L}\p{M}]+").Count;
        if (wordCount is < 2 or > 6)
        {
            return false;
        }

        if (text.Contains(',') || text.Contains('،') || text.Contains('.') || text.Contains('۔') || text.Contains(':') || text.Contains(';'))
        {
            return false;
        }

        return true;
    }

    private static List<int> ExtractEmbeddedVerseMarkers(string text, int firstVerse)
    {
        var markers = new SortedSet<int>();
        foreach (Match match in EmbeddedVerseMarkerRegex.Matches(text))
        {
            if (!TryParseScriptNumber(match.Groups["n"].Value, out var number))
            {
                continue;
            }

            if (number <= firstVerse || number > 200)
            {
                continue;
            }

            markers.Add(number);
        }

        return markers.ToList();
    }

    private static bool TryReadLooseLeadingVerse(string text, out int verseNumber)
    {
        verseNumber = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = LooseLeadingVerseRegex.Match(text);
        if (!match.Success || !TryParseScriptNumber(match.Groups["n"].Value, out verseNumber))
        {
            return false;
        }

        return verseNumber > 0 && verseNumber <= 200;
    }

    private static bool TryNormalizeLeadingVerseMarker(XElement paragraph, int verseNumber)
    {
        if (verseNumber <= 0)
        {
            return false;
        }

        var firstTextNode = paragraph.Descendants(W + "t").FirstOrDefault();
        if (firstTextNode is null)
        {
            return false;
        }

        var original = firstTextNode.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(original))
        {
            return false;
        }

        var trimmed = original.TrimStart();
        var leadingCount = original.Length - trimmed.Length;
        var leading = leadingCount > 0 ? original[..leadingCount] : string.Empty;

        var match = LooseLeadingVerseRegex.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var tail = match.Groups["tail"].Value;
        // Drop marker separators/artifacts like ".", ")", replacement chars, and extra spaces.
        tail = Regex.Replace(tail, @"^\s*[\.\)\]:»”\uFFFD\-]*\s*", string.Empty);
        if (string.IsNullOrWhiteSpace(tail))
        {
            return false;
        }

        firstTextNode.Value = $"{leading}{verseNumber} {tail.TrimStart()}";
        return !string.Equals(original, firstTextNode.Value, StringComparison.Ordinal);
    }

    private static bool TryParseScriptNumber(string raw, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = NormalizeIndicDigits(raw.Trim());
        return int.TryParse(normalized, out number) && number > 0;
    }

    private static string NormalizeIndicDigits(string value)
    {
        var buffer = value.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            var ch = buffer[i];
            if (ch >= '\u0660' && ch <= '\u0669')
            {
                buffer[i] = (char)('0' + (ch - '\u0660'));
            }
            else if (ch >= '\u06F0' && ch <= '\u06F9')
            {
                buffer[i] = (char)('0' + (ch - '\u06F0'));
            }
        }

        return new string(buffer);
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

    private static IReadOnlySet<string> GetCanonBookIdSet(CanonProfile canonProfile)
    {
        return canonProfile switch
        {
            CanonProfile.ProtestantNt or CanonProfile.CatholicNt or CanonProfile.OrthodoxNt => NtBookIds,
            _ => OtBookIds
        };
    }
}

internal sealed class DocxScanResult
{
    public int ParagraphCount { get; set; }
    public List<BookScanState> Books { get; } = [];
    public List<ScanIssue> Issues { get; } = [];
    public CanonProfile CanonProfileUsed { get; set; } = CanonProfile.ProtestantOt;
    public string VersificationProfileUsed { get; set; } = "Protestant OT versification (v1)";
    public List<string> DetectedOtBookIds { get; } = [];
    public List<string> DetectedNtBookIds { get; } = [];

    public int TotalVerseCount => Books.Sum(b => b.TotalVerseCount);

    public void RegisterDetectedBook(string bookId)
    {
        if (DocxScanService.OtBookIds.Contains(bookId))
        {
            if (!DetectedOtBookIds.Contains(bookId, StringComparer.Ordinal))
            {
                DetectedOtBookIds.Add(bookId);
            }

            return;
        }

        if (DocxScanService.NtBookIds.Contains(bookId) && !DetectedNtBookIds.Contains(bookId, StringComparer.Ordinal))
        {
            DetectedNtBookIds.Add(bookId);
        }
    }

    public string BuildSummary()
    {
        var knownBooks = Books.Count(b => !string.IsNullOrWhiteSpace(b.BookId));
        var canon = CanonProfileUsed switch
        {
            CanonProfile.CatholicOt => "Catholic OT",
            CanonProfile.OrthodoxOt => "Orthodox OT",
            CanonProfile.ProtestantNt => "Protestant NT",
            CanonProfile.CatholicNt => "Catholic NT",
            CanonProfile.OrthodoxNt => "Orthodox NT",
            _ => "Protestant OT"
        };

        var ot = DetectedOtBookIds.Count == 0 ? "(none)" : string.Join(", ", DetectedOtBookIds);
        var nt = DetectedNtBookIds.Count == 0 ? "(none)" : string.Join(", ", DetectedNtBookIds);
        return $"Canon: {canon}. Versification: {VersificationProfileUsed}. Scanned {ParagraphCount} paragraph(s), detected {Books.Count} book heading(s) ({knownBooks} mapped), {Books.Sum(b => b.ChapterCount)} chapter(s), {TotalVerseCount} verse marker(s). OT detected: {ot}. NT detected: {nt}.";
    }
}

internal sealed class BookScanState
{
    public string Title { get; }
    public string? BookId { get; }

    private readonly Dictionary<int, HashSet<int>> _chapterVerses = new();
    private int _currentChapter;

    public BookScanState(string title, string? bookId)
    {
        Title = title;
        BookId = bookId;
    }

    public int ChapterCount => _chapterVerses.Count;
    public int TotalVerseCount => _chapterVerses.Sum(kvp => kvp.Value.Count);
    public IEnumerable<int> Chapters => _chapterVerses.Keys;

    public void StartChapter(int chapter, ICollection<ScanIssue> issues)
    {
        if (_currentChapter > 0 && chapter != _currentChapter + 1)
        {
            issues.Add(new ScanIssue("Warning", "CHAPTER_SEQUENCE_GAP", $"{Title}: chapter jump {_currentChapter} -> {chapter}."));
        }

        _currentChapter = chapter;
        if (!_chapterVerses.ContainsKey(chapter))
        {
            _chapterVerses[chapter] = [];
        }
    }

    public void AddVerse(int verse, ICollection<ScanIssue> issues)
    {
        if (_currentChapter <= 0)
        {
            issues.Add(new ScanIssue("Warning", "VERSE_WITHOUT_CHAPTER", $"{Title}: verse {verse} appears before chapter heading."));
            return;
        }

        var set = _chapterVerses[_currentChapter];
        if (!set.Add(verse))
        {
            issues.Add(new ScanIssue("Warning", "VERSE_DUPLICATE", $"{Title} {_currentChapter}:{verse} duplicated."));
        }
    }

    public void FinalizeIntegrityChecks(ICollection<ScanIssue> issues)
    {
        foreach (var chapter in _chapterVerses.OrderBy(k => k.Key))
        {
            if (chapter.Value.Count == 0)
            {
                issues.Add(new ScanIssue("Warning", "CHAPTER_WITHOUT_VERSES", $"{Title}: chapter {chapter.Key} has no verses."));
                continue;
            }

            var min = chapter.Value.Min();
            var max = chapter.Value.Max();
            if (min != 1)
            {
                issues.Add(new ScanIssue("Warning", "VERSE_START_NOT_ONE", $"{Title} {chapter.Key}: starts at verse {min}, expected 1."));
            }

            for (var v = min; v <= max; v++)
            {
                if (!chapter.Value.Contains(v))
                {
                    issues.Add(new ScanIssue("Warning", "VERSE_SEQUENCE_GAP", $"{Title} {chapter.Key}: missing verse {v}."));
                }
            }
        }
    }

    public bool TryGetVerses(int chapter, out HashSet<int> verses)
    {
        return _chapterVerses.TryGetValue(chapter, out verses!);
    }
}

internal sealed record ScanIssue(string Severity, string Code, string Message);

internal sealed record DocxStandardizeResult(
    string OutputPath,
    int ChangedParagraphCount,
    int ChangedTextNodeCount,
    string CanonHighlightReportPath,
    int CanonIssueCount);

internal sealed record VersificationProfile(
    string Id,
    string DisplayName,
    IReadOnlyDictionary<string, int[]> VerseCountsByBook,
    IReadOnlySet<string> RelaxedBooks);
