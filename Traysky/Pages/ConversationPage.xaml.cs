using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Traysky.Pages;

/// <summary>
/// One direct message conversation: the messages, newest at the bottom, and a box to reply.
/// Navigation parameter is the <see cref="ConversationItem"/>.
/// </summary>
public sealed partial class ConversationPage : Page
{
    /// <summary>How often an open conversation checks for new messages.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Within this many pixels of the bottom counts as "reading the latest", so new messages scroll in.</summary>
    private const double FollowThreshold = 80;

    private readonly DispatcherQueueTimer _pollTimer;
    private ScrollViewer? _scrollViewer;

    public ConversationViewModel ViewModel { get; } = new();

    public ConversationPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        ViewModel.MessagesAppended += OnMessagesAppended;

        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = PollInterval;
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += (_, _) =>
        {
            // The flyout is hidden: nobody is reading, and reopening it goes back to the timeline anyway.
            if (XamlRoot?.IsHostVisible == true)
                _ = ViewModel.PollAsync();
        };
    }

    public static void Open(ConversationItem conversation) =>
        NavigationService.Instance.Navigate(typeof(ConversationPage), conversation);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ConversationItem conversation && ViewModel.Conversation is null)
            _ = ViewModel.LoadAsync(conversation);
        _pollTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _pollTimer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DraftBox.Focus(FocusState.Programmatic);

        if (_scrollViewer is not null)
            return;

        _scrollViewer = FindScrollViewer(MessageList);
        if (_scrollViewer is not null)
            _scrollViewer.ViewChanged += OnViewChanged;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer is null || e.IsIntermediate || ViewModel.IsLoading)
            return;

        if (_scrollViewer.VerticalOffset < _scrollViewer.ViewportHeight / 2 && ViewModel.HasOlder)
            _ = ViewModel.LoadOlderAsync();
    }

    /// <summary>
    /// Follows new messages down, but only if the reader was already at the bottom (or it was
    /// their own message, or the first load) - scrolled up reading history, they stay put.
    /// </summary>
    private void OnMessagesAppended(object? sender, bool force)
    {
        bool atBottom = _scrollViewer is null || _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset < FollowThreshold;
        if (!force && !atBottom)
            return;

        // After layout, so the new rows exist to scroll to.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (ViewModel.Messages.Count > 0)
                MessageList.ScrollIntoView(ViewModel.Messages[^1], ScrollIntoViewAlignment.Leading);
        });
    }

    private void DraftBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (shift)
            return;

        e.Handled = true;
        if (ViewModel.SendCommand.CanExecute(null))
            ViewModel.SendCommand.Execute(null);
    }

    private void Header_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.Conversation is { IsGroup: false, OtherProfileKey.Length: > 0 } conversation)
            ProfilePage.Open(conversation.OtherProfileKey);
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
