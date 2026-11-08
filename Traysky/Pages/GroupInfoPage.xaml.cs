using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using Traysky.Helpers;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.ApplicationModel.DataTransfer;

namespace Traysky.Pages;

/// <summary>
/// A group chat's details: who is in it, and for the owner the controls to rename it, add and
/// remove people and run the join link. Navigation parameter is the group's <see cref="ConversationItem"/>.
/// </summary>
public sealed partial class GroupInfoPage : Page
{
    private ScrollViewer? _scrollViewer;

    public GroupInfoViewModel ViewModel { get; } = new();

    public GroupInfoPage()
    {
        ViewModel.Dialogs = new XamlDialogService(() => XamlRoot);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public static void Open(ConversationItem group) =>
        NavigationService.Instance.Navigate(typeof(GroupInfoPage), group);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ConversationItem group && ViewModel.ConversationId is null)
            _ = ViewModel.LoadAsync(group);
    }

    private void MemberList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActorItem actor)
            ProfilePage.Open(actor.ProfileKey);
    }

    // Row templates can't reach the page's view model with x:Bind, so this only forwards the row.
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem member })
            ViewModel.RemoveMemberCommand.Execute(member);
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem person })
            _ = ViewModel.ApproveAsync(person);
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActorItem person })
            _ = ViewModel.RejectAsync(person);
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new();
        package.SetText(ViewModel.LinkCode);
        Clipboard.SetContent(package);
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
