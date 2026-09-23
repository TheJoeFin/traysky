using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// Who liked and who reposted a post, one tab each. Opened from the info button on the main
/// post of a thread page; tapping a person opens their profile.
/// </summary>
public sealed partial class PostEngagementPage : Page
{
    private ScrollViewer? _scrollViewer;

    public PostEngagementViewModel ViewModel { get; } = new();

    public PostEngagementPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        ViewModel.Likes.CollectionChanged += OnListChanged;
        ViewModel.Reposts.CollectionChanged += OnListChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ShowSelectedList();
    }

    /// <summary>Navigates to a post's likes/reposts page.</summary>
    public static void Open(PostItem post) => NavigationService.Instance.Navigate(typeof(PostEngagementPage), post);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is PostItem post)
        {
            if (post.LikeCount == 0 && post.RepostCount > 0)
                RepostsTab.IsSelected = true;
            _ = ViewModel.LoadAsync(post);
        }
    }

    private bool ShowingReposts => Tabs.SelectedItem == RepostsTab;

    private ObservableCollection<ActorItem> SelectedList => ShowingReposts ? ViewModel.Reposts : ViewModel.Likes;

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowSelectedList();

    private void ShowSelectedList()
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the list exists.
        if (PeopleList is null || EmptyText is null)
            return;

        PeopleList.ItemsSource = SelectedList;
        _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
        UpdateEmptyText();
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyText();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PostEngagementViewModel.IsLoading) or nameof(PostEngagementViewModel.HasError))
            UpdateEmptyText();
    }

    private void UpdateEmptyText()
    {
        EmptyText.Text = ShowingReposts ? "No reposts yet" : "No likes yet";
        EmptyText.Visibility = !ViewModel.IsLoading && !ViewModel.HasError && SelectedList.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PeopleList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(PeopleList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnViewChanged;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer is null || e.IsIntermediate)
            return;

        double remaining = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        if (remaining < _scrollViewer.ViewportHeight * 2)
            _ = ShowingReposts ? ViewModel.LoadMoreRepostsAsync() : ViewModel.LoadMoreLikesAsync();
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
