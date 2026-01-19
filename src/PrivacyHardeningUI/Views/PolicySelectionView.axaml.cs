using Avalonia.Controls;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Views;

public partial class PolicySelectionView : UserControl
{
    public PolicySelectionView()
    {
        InitializeComponent();
    }

    private void OnProfileChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string profile && DataContext is PolicySelectionViewModel vm)
        {
            vm.SelectProfileCommand.Execute(profile);
            
            // Reset combo box so it can be selected again
            if (sender is ComboBox cb)
            {
                cb.SelectedItem = null;
            }
        }
    }
}
