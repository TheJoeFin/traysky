using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// An account's profile and recent posts. Navigation parameter is a DID or handle.
/// </summary>
public sealed partial class ProfilePage : Page
{
    private ScrollViewer? _scrollViewer;

    public ProfileViewModel ViewModel { get; } = new();

    public ProfilePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Navigates to a profile. Shared by avatars, "View profile", mentions and follow notifications.</summary>
    public static void Open(string didOrHandle)
    {
        if (string.IsNullOrWhiteSpace(didOrHandle))
            return;
        NavigationService.Instance.Navigate(typeof(ProfilePage), didOrHandle.Trim().TrimStart('@'));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string actor)
            _ = ViewModel.LoadAsync(actor);
    }

    public bool AnyLoading(bool a, bool b) => a || b;

    public Style FollowButtonStyle(bool following) =>
        (Style)Application.Current.Resources[following ? "DefaultButtonStyle" : "AccentButtonStyle"];

    private void PostCard_ThreadRequested(object? sender, PostItem post) => PostPage.Open(post);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(PostList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnViewChanged;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer is null || e.IsIntermediate)
            return;

        double remaining = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        if (remaining < _scrollViewer.ViewportHeight * 2 && ViewModel.HasMore)
            _ = ViewModel.LoadMoreAsync();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            ScrollViewer? found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
    }
}
