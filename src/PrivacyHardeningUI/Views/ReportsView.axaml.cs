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

    private async Task SaveTextAsync(string suggestedName, string text)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }
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
}
