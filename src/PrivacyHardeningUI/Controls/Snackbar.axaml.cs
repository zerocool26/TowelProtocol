using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Controls;

public partial class Snackbar : UserControl
{
    private Border? _border;

    public Snackbar()
    {
        AvaloniaXamlLoader.Load(this);
        this.DataContextChanged += Snackbar_DataContextChanged;
    }

    private void Snackbar_DataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SnackbarVisible))
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.SnackbarVisible) _ = RunShowAnimationAsync();
                else _ = RunHideAnimationAsync();
            }
        }
    }

    private async Task RunShowAnimationAsync()
    {
        if (_border == null)
        {
            _border = this.FindControl<Border?>("PART_Border");
            if (_border == null) return;
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _border.Opacity = 0;
                if (_border.RenderTransform is Avalonia.Media.TranslateTransform tt) tt.Y = 24;
                _border.IsVisible = true;
            });

            int steps = 8;
            for (int i = 1; i <= steps; i++)
            {
                await Task.Delay(28);
                double t = i / (double)steps;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _border.Opacity = 0.95 * t;
                    if (_border.RenderTransform is Avalonia.Media.TranslateTransform tt) tt.Y = 24 * (1 - t);
                });
            }
        }
        catch { }
    }

    private async Task RunHideAnimationAsync()
    {
        if (_border == null) return;
        try
        {
            int steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                await Task.Delay(30);
                double t = 1 - (i / (double)steps);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _border.Opacity = 0.95 * t;
                    if (_border.RenderTransform is Avalonia.Media.TranslateTransform tt) tt.Y = 24 * (1 - t);
                });
            }

            await Dispatcher.UIThread.InvokeAsync(() => _border.IsVisible = false);
        }
        catch { }
    }
}
