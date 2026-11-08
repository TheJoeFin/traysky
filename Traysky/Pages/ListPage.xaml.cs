using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Traysky.Helpers;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// One list and its members. Navigation parameter is a <see cref="ListItem"/>; tapping a member
/// opens their profile. The list's creator also gets edit, delete, add-member and remove.
/// </summary>
public sealed partial class ListPage : Page
{
    private ScrollViewer? _scrollViewer;

    public ListViewModel ViewModel { get; } = new();

    public ListPage()
    {
        ViewModel.Dialogs = new Services.XamlDialogService(() => XamlRoot);
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ListViewModel.IsDeleted) && ViewModel.IsDeleted)
                NavigationService.Instance.GoBack();
        };
    }

    /// <summary>Navigates to a list's member page.</summary>
    public static void Open(ListItem list)
    {
        if (list.Uri.Length == 0)
            return;
        NavigationService.Instance.Navigate(typeof(ListPage), list);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ListItem list)
            _ = ViewModel.LoadAsync(list);
    }

    private void MemberList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem member })
            _ = ViewModel.RemoveAsync(member);
    }

    private void AddBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _ = UpdateSuggestionsAsync(sender);
    }

    private async System.Threading.Tasks.Task UpdateSuggestionsAsync(AutoSuggestBox box)
    {
        await ViewModel.UpdateSuggestionsAsync(box.Text);
        box.ItemsSource = ViewModel.Suggestions.Count > 0 ? ViewModel.Suggestions : null;
    }

    private async void AddBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not ActorItem actor)
            return;

        sender.Text = string.Empty;
        sender.ItemsSource = null;
        await ViewModel.AddAsync(actor);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(MemberList);
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
