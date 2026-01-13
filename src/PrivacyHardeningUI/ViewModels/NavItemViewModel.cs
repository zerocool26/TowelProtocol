using CommunityToolkit.Mvvm.ComponentModel;

namespace PrivacyHardeningUI.ViewModels;

public sealed partial class NavItemViewModel : ObservableObject
{
    public AppPage Page { get; }
    public string Title { get; }
    public string Icon { get; }
    public object ViewModel { get; }

    public NavItemViewModel(AppPage page, string title, string icon, object viewModel)
    {
        Page = page;
        Title = title;
        Icon = icon;
        ViewModel = viewModel;
    }
}
