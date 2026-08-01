using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UsfmIntegrityStudio.Views;

public partial class LanguageCueConfirmWindow : Window
{
    private readonly ObservableCollection<LanguageCueOverrideRow> _rows = [];

    public LanguageCueConfirmWindow()
    {
        InitializeComponent();
        OverrideItems.ItemsSource = _rows;
    }

    public LanguageCueConfirmWindow(
        string summary,
        string details,
        IEnumerable<LanguageCueOverrideSeed>? overrideSeeds = null)
    {
        InitializeComponent();
        SummaryText.Text = summary;
        DetailText.Text = details;

        if (overrideSeeds is not null)
        {
            foreach (var seed in overrideSeeds)
            {
                _rows.Add(new LanguageCueOverrideRow(seed));
            }
        }

        OverrideItems.ItemsSource = _rows;
    }

    private void Confirm_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var overrides = _rows
            .Where(row => row.SelectedChoice is not null
                          && !string.Equals(row.SelectedChoice.BookId, row.CurrentBookId, System.StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                row => row.Title,
                row => row.SelectedChoice!.BookId ?? string.Empty,
                System.StringComparer.OrdinalIgnoreCase);

        Close(new LanguageCueDecision(true, overrides));
    }

    private void Reject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(new LanguageCueDecision(false, new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)));
    }
}

public sealed record LanguageCueDecision(bool Confirmed, IReadOnlyDictionary<string, string> OverridesByTitle);

public sealed record LanguageCueOverrideSeed(string Title, string? CurrentBookId, string CurrentLabel, IReadOnlyList<LanguageCueBookChoice> Choices);

public sealed record LanguageCueBookChoice(string? BookId, string Label)
{
    public override string ToString() => Label;
}

public partial class LanguageCueOverrideRow : ObservableObject
{
    public LanguageCueOverrideRow(LanguageCueOverrideSeed seed)
    {
        Title = seed.Title;
        CurrentBookId = seed.CurrentBookId;
        CurrentLabel = seed.CurrentLabel;
        Choices = seed.Choices.ToList();
        SelectedChoice = Choices.FirstOrDefault(choice => string.Equals(choice.BookId, CurrentBookId, System.StringComparison.OrdinalIgnoreCase))
                         ?? Choices.FirstOrDefault();
    }

    public string Title { get; }
    public string? CurrentBookId { get; }
    public string CurrentLabel { get; }
    public IReadOnlyList<LanguageCueBookChoice> Choices { get; }

    [ObservableProperty]
    private LanguageCueBookChoice? selectedChoice;
}
