using System;
using System.Collections.Generic;
using System.Linq;

namespace UsfmIntegrityStudio.Models;

internal static class BttwCanonicalChunkMap
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<int, int[]>>> Parsed =
        new(Parse, isThreadSafe: true);

    internal static bool ContainsBook(string bookId)
    {
        return Parsed.Value.ContainsKey(NormalizeBookId(bookId));
    }

    internal static bool TryGetChapterStarts(string bookId, int chapter, out int[] starts)
    {
        starts = [];
        if (!Parsed.Value.TryGetValue(NormalizeBookId(bookId), out var chapters)
            || !chapters.TryGetValue(chapter, out var found))
        {
            return false;
        }

        starts = found;
        return true;
    }

    internal static bool IsCanonicalStart(string bookId, int chapter, int verse)
    {
        return TryGetChapterStarts(bookId, chapter, out var starts)
            && Array.BinarySearch(starts, verse) >= 0;
    }

    internal static bool TryFindChunkStart(string bookId, int chapter, int verse, out int chunkStart)
    {
        chunkStart = 0;
        if (!TryGetChapterStarts(bookId, chapter, out var starts) || starts.Length == 0)
        {
            return false;
        }

        var index = Array.BinarySearch(starts, verse);
        if (index < 0)
        {
            index = ~index - 1;
        }

        if (index < 0)
        {
            return false;
        }

        chunkStart = starts[index];
        return true;
    }

    internal static bool TryGetChunkEndExclusive(string bookId, int chapter, int chunkStart, out int endExclusive)
    {
        endExclusive = int.MaxValue;
        if (!TryGetChapterStarts(bookId, chapter, out var starts))
        {
            return false;
        }

        var index = Array.BinarySearch(starts, chunkStart);
        if (index < 0)
        {
            return false;
        }

        if (index + 1 < starts.Length)
        {
            endExclusive = starts[index + 1];
        }

        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<int, int[]>> Parse()
    {
        var books = new Dictionary<string, IReadOnlyDictionary<int, int[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (bookId, encoded) in BttwCanonicalChunkData.Encoded)
        {
            var chapters = new Dictionary<int, int[]>();
            foreach (var chapterEntry in encoded.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = chapterEntry.IndexOf(':');
                if (separator <= 0
                    || !int.TryParse(chapterEntry[..separator], out var chapter)
                    || chapter <= 0)
                {
                    throw new InvalidOperationException($"Invalid canonical BTTW chunk-map entry for {bookId}: {chapterEntry}");
                }

                var starts = chapterEntry[(separator + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
                if (starts.Length == 0)
                {
                    throw new InvalidOperationException($"Canonical BTTW chunk-map chapter has no starts: {bookId} {chapter}");
                }

                chapters[chapter] = starts;
            }

            books[NormalizeBookId(bookId)] = chapters;
        }

        return books;
    }

    private static string NormalizeBookId(string bookId)
    {
        return (bookId ?? string.Empty).Trim().ToUpperInvariant();
    }
}
