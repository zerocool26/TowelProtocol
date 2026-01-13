using Avalonia.Controls;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Views;

public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel vm)
    {
        ViewModel = vm;
        DataContext = ViewModel;
        InitializeComponent();
    }
}
