using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.ApplicationModel.DataTransfer;
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
        ViewModel.Dialogs = new XamlDialogService(() => XamlRoot);
        InitializeComponent();
        Loaded += OnLoaded;

        ViewModel.MessagesAppended += OnMessagesAppended;
        ViewModel.Closed += OnConversationClosed;

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

    /// <summary>Shows the react button while the pointer is over a message, hides it otherwise.</summary>
    private void Message_PointerEntered(object sender, PointerRoutedEventArgs e) => ShowReactButton(sender, true);

    private void Message_PointerExited(object sender, PointerRoutedEventArgs e) => ShowReactButton(sender, false);

    private static void ShowReactButton(object sender, bool over)
    {
        if (sender is FrameworkElement row && row.FindName("ReactButton") is Button button)
        {
            // Keep it showing while its flyout is open, even if the pointer moves onto the flyout.
            button.Opacity = over || button.Flyout?.IsOpen == true ? 1 : 0.45;
        }
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MessageItem { HasText: true } message } button)
        {
            DataPackage package = new();
            package.SetText(message.Text);
            Clipboard.SetContent(package);
            CloseOpenFlyouts(button);
        }
    }

    private static void CloseOpenFlyouts(DependencyObject from)
    {
        if (from is not FrameworkElement { XamlRoot: { } root })
            return;

        foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child is FlyoutPresenter)
                popup.IsOpen = false;
        }
    }

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MessageItem message, Content: string emoji } button)
        {
            // The picker is the react button's flyout; close it as the reaction goes out.
            CloseOpenFlyouts(button);
            _ =ViewModel.ToggleReactionAsync(message, emoji);
        }
    }

    private void DeleteForMe_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MessageItem message } button)
        {
            CloseOpenFlyouts(button);
            _ = ViewModel.DeleteForMeAsync(message);
        }
    }

    private void ConversationMenu_Opening(object? sender, object e)
    {
        if (ViewModel.Conversation is not { } conversation)
            return;

        ChatConversationMenu menu = ChatConversationMenuPolicy.For(conversation.IsRequest, conversation.IsMuted, conversation.IsGroup);
        ConversationMenu.Items.Clear();

        if (menu.ShowAccept)
        {
            MenuFlyoutItem accept = new() { Text = "Accept request", Icon = new FontIcon { Glyph = "" } };
            accept.Click += (_, _) => ViewModel.AcceptCommand.Execute(null);
            ConversationMenu.Items.Add(accept);
        }

        if (menu.ShowGroupInfo)
        {
            MenuFlyoutItem info = new() { Text = "Group info and members", Icon = new FontIcon { Glyph = "" } };
            info.Click += (_, _) => GroupInfoPage.Open(conversation);
            ConversationMenu.Items.Add(info);
        }

        if (menu.ShowMute || menu.ShowUnmute)
        {
            MenuFlyoutItem mute = new()
            {
                Text = menu.ShowMute ? "Mute conversation" : "Unmute conversation",
                Icon = new FontIcon { Glyph = menu.ShowMute ? "" : "" }
            };
            mute.Click += (_, _) => ViewModel.ToggleMuteCommand.Execute(null);
            ConversationMenu.Items.Add(mute);
        }

        MenuFlyoutItem leave = new() { Text = menu.LeaveLabel, Icon = new FontIcon { Glyph = "" } };
        leave.Click += (_, _) => ViewModel.ConfirmLeaveCommand.Execute(null);
        ConversationMenu.Items.Add(leave);
    }

    private void OnConversationClosed(object? sender, EventArgs e)
    {
        if (NavigationService.Instance.CanGoBack)
            NavigationService.Instance.GoBack();
    }

    private void Header_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // The options button lives in the header; its taps are not "open the profile".
        for (DependencyObject? node = e.OriginalSource as DependencyObject; node is not null && !ReferenceEquals(node, Header); node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button)
                return;
        }

        if (ViewModel.Conversation is { IsGroup: false, OtherProfileKey.Length: > 0 } conversation)
            ProfilePage.Open(conversation.OtherProfileKey);
        else if (ViewModel.Conversation is { IsGroup: true, IsRequest: false } group)
            GroupInfoPage.Open(group);
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
