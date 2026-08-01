using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace UsfmIntegrityStudio.Models;

public sealed record CleanerJobIdentity(
    string InputType,
    string BookId,
    string BookName,
    string DisplayTitle,
    string Language,
    string Resource);

public static class CleanerJobIdentityService
{
    private const string Unknown = "Not declared";

    public static CleanerJobIdentity Read(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        return Path.GetExtension(inputPath).ToLowerInvariant() switch
        {
            ".tstudio" => ReadTstudio(inputPath),
            ".usfm" or ".txt" => ReadUsfm(inputPath),
            _ => throw new InvalidDataException("The selected file is not a supported USFM or BTTW project file.")
        };
    }

    private static CleanerJobIdentity ReadTstudio(string inputPath)
    {
        using var archive = ZipFile.OpenRead(inputPath);
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(entry.Open());
            var root = document.RootElement;
            if (!root.TryGetProperty("project", out var project) ||
                project.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var prefix = entry.FullName[..^"manifest.json".Length];
            var titleEntry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.FullName, prefix + "front/title.txt", StringComparison.OrdinalIgnoreCase));

            var bookId = ReadJsonString(project, "id");
            var bookName = ReadJsonString(project, "name");
            var title = titleEntry is null ? string.Empty : ReadEntryText(titleEntry).Trim();
            var language = ReadObjectIdentity(root, "target_language");
            var resource = ReadObjectIdentity(root, "resource");

            return new CleanerJobIdentity(
                "BTTW Project (.tstudio)",
                ValueOrUnknown(bookId),
                ValueOrUnknown(bookName),
                ValueOrFallback(title, bookName),
                ValueOrUnknown(language),
                ValueOrUnknown(resource));
        }

        throw new InvalidDataException("No inner BTTW project manifest was found in the selected .tstudio file.");
    }

    private static CleanerJobIdentity ReadUsfm(string inputPath)
    {
        var bookId = string.Empty;
        var bookName = string.Empty;
        var title = string.Empty;

        foreach (var line in File.ReadLines(inputPath, Encoding.UTF8))
        {
            if (TryReadMarker(line, "id", out var idValue))
            {
                var parts = idValue.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                bookId = parts.FirstOrDefault() ?? string.Empty;
                bookName = parts.Length > 1 ? parts[1] : string.Empty;
            }
            else if (TryReadMarker(line, "h", out var header))
            {
                title = header;
            }
            else if (string.IsNullOrWhiteSpace(title) && TryReadMarker(line, "toc1", out var toc1))
            {
                title = toc1;
            }
            else if (string.IsNullOrWhiteSpace(title) && TryReadMarker(line, "mt", out var mainTitle))
            {
                title = mainTitle;
            }

            if (line.TrimStart().StartsWith("\\c ", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(bookId))
        {
            throw new InvalidDataException("The selected USFM file has no readable \\id header.");
        }

        return new CleanerJobIdentity(
            "USFM File",
            bookId,
            ValueOrFallback(bookName, title),
            ValueOrFallback(title, bookName),
            Unknown,
            Unknown);
    }

    private static bool TryReadMarker(string line, string marker, out string value)
    {
        var trimmed = line.TrimStart();
        var prefix = "\\" + marker;
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length > prefix.Length && !char.IsWhiteSpace(trimmed[prefix.Length])))
        {
            value = string.Empty;
            return false;
        }

        value = trimmed[prefix.Length..].Trim();
        return true;
    }

    private static string ReadObjectIdentity(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var id = ReadJsonString(value, "id");
        var name = ReadJsonString(value, "name");
        return string.IsNullOrWhiteSpace(id)
            ? name
            : string.IsNullOrWhiteSpace(name)
                ? id
                : string.Equals(id, name, StringComparison.OrdinalIgnoreCase)
                    ? name
                : $"{name} ({id})";
    }

    private static string ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ValueOrUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? Unknown : value;

    private static string ValueOrFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? ValueOrUnknown(fallback) : value;
}
