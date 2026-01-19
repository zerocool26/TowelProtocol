using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private async Task SaveTextAsync(string suggestedName, string text, string extension = "json", string filterName = "JSON")
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(filterName) { Patterns = new[] { $"*.{extension}" } }
            }
        });

        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
    }

    private async void ExportAuditJson(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        await SaveTextAsync($"audit_{DateTime.Now:yyyyMMdd_HHmmss}.json", vm.ExportAuditJson());
    }

    private async void ExportPreviewJson(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        await SaveTextAsync($"preview_{DateTime.Now:yyyyMMdd_HHmmss}.json", vm.ExportPreviewJson());
    }

    private async void ExportHistoryJson(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        await SaveTextAsync($"history_{DateTime.Now:yyyyMMdd_HHmmss}.json", vm.ExportHistoryJson());
    }

    private async void GenerateFullReport(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        var text = await vm.GenerateFullReportAsync();
        await SaveTextAsync($"PrivacyReport_{DateTime.Now:yyyyMMdd}.md", text, "md", "Markdown");
    }
}
