namespace UsfmIntegrityStudio.Models;

public sealed class BookSelectionOption
{
    public string? BookId { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsSelected { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(BookId)
        ? Title
        : $"{BookId} - {Title}";
}
