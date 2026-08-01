using Avalonia.Controls;

namespace UsfmIntegrityStudio.Views;

public partial class StandardizeResultWindow : Window
{
    public StandardizeResultWindow()
    {
        InitializeComponent();
    }

    public StandardizeResultWindow(
        int changedParagraphs,
        int changedTextNodes,
        string standardizedDocxPath,
        string canonReportPath)
    {
        InitializeComponent();
        SummaryText.Text =
            $"Updated {changedTextNodes} text node(s) across {changedParagraphs} paragraph(s).";
        PathText.Text =
            $"Standardized DOCX: {standardizedDocxPath}\n" +
            $"Canon highlight report: {canonReportPath}";
    }

    private void KeepOriginal_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void UseStandardized_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }
}
