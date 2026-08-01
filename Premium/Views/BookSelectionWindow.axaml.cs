using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using UsfmIntegrityStudio.Models;

namespace UsfmIntegrityStudio.Views;

public partial class BookSelectionWindow : Window
{
    private readonly ObservableCollection<BookSelectionOption> _options;

    public BookSelectionWindow()
    {
        InitializeComponent();
        _options = new ObservableCollection<BookSelectionOption>();
        DataContext = _options;
    }

    public BookSelectionWindow(IEnumerable<BookSelectionOption> options)
    {
        InitializeComponent();
        _options = new ObservableCollection<BookSelectionOption>(options);
        DataContext = _options;
    }

    public BookSelectionWindow(
        IEnumerable<BookSelectionOption> options,
        string title,
        string heading,
        string recommendation,
        string confirmButtonText)
    {
        InitializeComponent();
        _options = new ObservableCollection<BookSelectionOption>(options);
        DataContext = _options;

        Title = title;
        HeadingText.Text = heading;
        RecommendationText.Text = recommendation;
        ConfirmButton.Content = confirmButtonText;
    }

    private void SelectAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var option in _options)
        {
            option.IsSelected = true;
        }

        BooksList.ItemsSource = null;
        BooksList.ItemsSource = _options;
    }

    private void ClearAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var option in _options)
        {
            option.IsSelected = false;
        }

        BooksList.ItemsSource = null;
        BooksList.ItemsSource = _options;
    }

    private void Confirm_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selected = _options.Where(o => o.IsSelected).ToList();
        Close(selected);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
