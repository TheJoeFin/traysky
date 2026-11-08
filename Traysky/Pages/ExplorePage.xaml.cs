using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// Discovery: trending topics, suggested accounts and suggested feeds, one tab each.
/// </summary>
public sealed partial class ExplorePage : Page
{
    public ExploreViewModel ViewModel { get; } = new();

    public ExplorePage()
    {
        ViewModel.Dialogs = new XamlDialogService(() => XamlRoot);
        InitializeComponent();
        ShowSelectedTab();
    }

    public static void Open() => NavigationService.Instance.Navigate(typeof(ExplorePage));

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadSelectedTab();
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // IsSelected in XAML can raise SelectionChanged mid-InitializeComponent, before the lists exist.
        if (FeedList is null)
            return;

        ShowSelectedTab();
        LoadSelectedTab();
    }

    private void ShowSelectedTab()
    {
        if (FeedList is null)
            return;

        TrendList.Visibility = Tabs.SelectedItem == TrendingTab ? Visibility.Visible : Visibility.Collapsed;
        PeopleList.Visibility = Tabs.SelectedItem == PeopleTab ? Visibility.Visible : Visibility.Collapsed;
        FeedList.Visibility = Tabs.SelectedItem == FeedsTab ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSelectedTab()
    {
        if (Tabs.SelectedItem == PeopleTab)
            _ = ViewModel.LoadPeopleAsync();
        else if (Tabs.SelectedItem == FeedsTab)
            _ = ViewModel.LoadFeedsAsync();
        else
            _ = ViewModel.LoadTrendsAsync();
    }

    private void TrendList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not TrendItem trend)
            return;

        // Topic can be an opaque id rather than the words; like bsky.app, search for what's displayed.
        if (trend.DisplayName.Length > 0)
            SearchPage.Open(trend.DisplayName);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (SearchQueryPolicy.Normalize(args.QueryText).Length > 0)
            SearchPage.Open(args.QueryText);
    }

    private void PeopleList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    // Row templates can't reach the page's view model with x:Bind, so this only forwards the row.
    private void PinFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FeedSuggestionItem feed })
            ViewModel.PinFeedCommand.Execute(feed);
    }

    private void FeedList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FeedSuggestionItem feed)
            _ = RichTextBuilder.OpenAsync(feed.WebUrl);
    }
}
