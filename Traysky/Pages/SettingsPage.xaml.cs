using Microsoft.UI.Xaml.Controls;
using Traysky.ViewModels;

namespace Traysky.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
    }

    public string AtHandle(string? handle) => string.IsNullOrEmpty(handle) ? "Not signed in" : "@" + handle;

    public Microsoft.UI.Xaml.Visibility TextVisibility(string? text) => string.IsNullOrEmpty(text) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
}
