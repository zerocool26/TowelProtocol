using Avalonia.Controls;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Views;

public partial class MainWindow : Window
{
    public MainViewModel? ViewModel { get; }

    // Parameterless constructor required for XAML runtime loader / designer
    public MainWindow()
    {
        InitializeComponent();
    }

    // Primary constructor used by DI at runtime
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        InitializeComponent();
    }
}
