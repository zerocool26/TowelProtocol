using System;
using PrivacyHardeningUI.ViewModels;

namespace PrivacyHardeningUI.Services;

/// <summary>
/// UI-only navigation coordinator.
/// Keeps ViewModels decoupled from MainViewModel while supporting task-based workflow jumps.
/// </summary>
public sealed class NavigationService
{
    public event Action<AppPage>? NavigateRequested;

    public void Navigate(AppPage page)
    {
        NavigateRequested?.Invoke(page);
    }
}
