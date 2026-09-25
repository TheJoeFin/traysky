using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.ApplicationModel.DataTransfer;

namespace Traysky.Controls;

/// <summary>
/// One post in a list. Self-contained: likes and reposts go through
/// <see cref="PostInteractions"/>; a reply shows in the card's own inline compose box, a quote
/// opens <see cref="Pages.ComposePage"/>.
/// </summary>
public sealed partial class PostCard : UserControl
{
    public static readonly DependencyProperty PostProperty = DependencyProperty.Register(
        nameof(Post), typeof(PostItem), typeof(PostCard), new PropertyMetadata(null, OnPostChanged));

    /// <summary>Whether tapping the card's body asks to open the post's thread. Off for the post a thread page is about.</summary>
    public static readonly DependencyProperty IsOpenableProperty = DependencyProperty.Register(
        nameof(IsOpenable), typeof(bool), typeof(PostCard), new PropertyMetadata(true));

    /// <summary>
    /// A tap that was not on a button, link, avatar or embed. Anything wanting to fall back to
    /// the browser, or ignore it, is expected to not subscribe.
    /// </summary>
    public event EventHandler<PostItem>? ThreadRequested;

    // The post text is selectable. A click that only clears a selection shouldn't also open the
    // thread, and by the time Tapped arrives the selection is already gone, so remember whether
    // there was one just before the last change.
    private bool _textHasSelection;
    private bool _textHadSelection;
    private DateTime _textSelectionChangedUtc;

    public PostCard()
    {
        InitializeComponent();

        PostText.SelectionChanged += (_, _) =>
        {
            _textHadSelection = _textHasSelection;
            _textHasSelection = PostText.SelectedText.Length > 0;
            _textSelectionChangedUtc = DateTime.UtcNow;
        };

        // Subscribed only while in the tree. Subscribing in the constructor leaked every card
        // that was created but never loaded (so never got an Unloaded) - and with it, through
        // ThreadRequested, the whole PostPage it was on - into the singleton for good. It also
        // left a card recycled by list virtualization deaf to ReplyTo after its first Unloaded.
        Loaded += (_, _) =>
        {
            ComposeViewModel.Instance.PropertyChanged -= OnComposeStateChanged;
            ComposeViewModel.Instance.PropertyChanged += OnComposeStateChanged;
            UpdateInlineReplyVisibility();
        };
        Unloaded += (_, _) => ComposeViewModel.Instance.PropertyChanged -= OnComposeStateChanged;
    }

    /// <summary>
    /// Drives the inline reply box's Visibility from code, not x:Bind: x:Bind's change tracking on
    /// a function bound to two different sources (the shared ComposeViewModel's ReplyTo plus this
    /// card's own Post) doesn't reliably fire on ReplyTo alone, which left the box
    /// visible-but-collapsed-small after Cancel instead of fully hiding.
    /// </summary>
    private void OnComposeStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComposeViewModel.ReplyTo))
            UpdateInlineReplyVisibility();
    }

    private void UpdateInlineReplyVisibility()
    {
        bool isReplyingHere = Post is not null && ReferenceEquals(ComposeViewModel.Instance.ReplyTo, Post);

        // The box is x:Load="False" and stays unrealized until this card is first replied to.
        // Realized here - before BeginReply raises FocusRequested - so the new box subscribes
        // in time to take focus once it loads.
        if (isReplyingHere)
        {
            if (InlineReply is null)
                FindName(nameof(InlineReply));

            InlineReply!.Visibility = Visibility.Visible;
        }
        else if (InlineReply is not null)
        {
            InlineReply.Visibility = Visibility.Collapsed;
        }
    }

    public PostItem? Post
    {
        get => (PostItem?)GetValue(PostProperty);
        set => SetValue(PostProperty, value);
    }

    public bool IsOpenable
    {
        get => (bool)GetValue(IsOpenableProperty);
        set => SetValue(IsOpenableProperty, value);
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (Post is null)
            return;

        // A tap on the avatar opens the author's profile, never the thread - checked here as
        // well as in Avatar_Tapped, since the avatar's own Tapped handler doesn't reliably run
        // (and mark the tap handled) before it bubbles up to this one.
        if (IsWithinAvatar(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            OpenProfile();
            return;
        }

        if (!IsOpenable)
            return;

        // Always swallow here, same as Avatar_Tapped/EmbedPresenter's own handlers - leaving
        // this unhandled would let the tap keep bubbling into the virtualizing ListView above,
        // which can disrupt an in-flight layout/animation on the item (e.g. the inline reply
        // box's own hide animation, leaving it stuck visible after Cancel).
        e.Handled = true;

        // A tap that landed on (or inside) a control that already acts on taps itself - a
        // button, the inline reply box's text field - shouldn't also open the thread. Click and
        // text-editing controls don't reliably mark the underlying Tapped gesture handled the
        // way a plain element with its own Tapped handler does, so without this check, e.g.
        // clicking Like or clicking into a reply draft races its own action against the thread
        // opening underneath it a beat later.
        if (IsWithinInteractiveControl(e.OriginalSource as DependencyObject))
            return;

        if (IsWithin(e.OriginalSource as DependencyObject, PostText) && IsSelectingText())
            return;

        // Hyperlinks inside the RichTextBlock are Inlines, not UIElements, so they can't be
        // caught by the walk above and their Click can land after this Tapped. Decide a beat
        // later so a link click opens the link and nothing else.
        PostItem post = Post;
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(120);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            // A double-click that selects a word lands here too; its selection is in by now.
            if (!RichTextBuilder.WasLinkClickedRecently() && PostText.SelectedText.Length == 0)
                ThreadRequested?.Invoke(this, post);
        };
        timer.Start();
    }

    /// <summary>Walks up from the tap's actual source to <see cref="Root"/>, looking for a
    /// control that consumes its own taps - a button (Reply/Repost/Like/More, and the inline
    /// reply box's Post/Cancel) or a text box (the inline reply box's editor).</summary>
    private bool IsWithinInteractiveControl(DependencyObject? source)
    {
        for (DependencyObject? node = source; node is not null && node != Root; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or TextBox)
                return true;
        }
        return false;
    }

    private bool IsWithinAvatar(DependencyObject? source) => IsWithin(source, Avatar);

    private bool IsWithin(DependencyObject? source, DependencyObject target)
    {
        for (DependencyObject? node = source; node is not null && node != Root; node = VisualTreeHelper.GetParent(node))
        {
            if (node == target)
                return true;
        }
        return false;
    }

    /// <summary>True when the text has a selection, or this tap has just cleared one.</summary>
    private bool IsSelectingText() =>
        _textHasSelection
        || (_textHadSelection && DateTime.UtcNow - _textSelectionChangedUtc < TimeSpan.FromMilliseconds(500));

    private static void OnPostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        PostCard card = (PostCard)d;
        card._textHasSelection = card._textHadSelection = false;
        if (e.NewValue is PostItem post)
            RichTextBuilder.Populate(card.PostText, post.Segments);
        else
            card.PostText.Blocks.Clear();

        // A recycled list container rebinding Post needs its inline reply box re-evaluated too.
        card.UpdateInlineReplyVisibility();
    }

    public Brush LikeBrush(bool liked) => liked
        ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    public Brush RepostBrush(bool reposted) => reposted
        ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
        : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private void Reply_Click(object sender, RoutedEventArgs e)
    {
        if (Post is not null)
            ComposeViewModel.Instance.BeginReply(Post);
    }

    private void Quote_Click(object sender, RoutedEventArgs e)
    {
        if (Post is null)
            return;

        ComposeViewModel.Instance.BeginQuote(Post);
        NavigationService.Instance.Navigate(typeof(Pages.ComposePage));
    }

    private void Repost_Click(object sender, RoutedEventArgs e)
    {
        if (Post is not null)
            _ = PostInteractions.ToggleRepostAsync(Post);
    }

    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if (Post is not null)
            _ = PostInteractions.ToggleLikeAsync(Post);
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e) => CopyToClipboard(Post?.WebUrl);

    private void CopyText_Click(object sender, RoutedEventArgs e) => CopyToClipboard(Post?.Text);

    private void OpenInBrowser_Click(object sender, RoutedEventArgs e) => _ = RichTextBuilder.OpenAsync(Post?.WebUrl);

    private void OpenProfile_Click(object sender, RoutedEventArgs e) => OpenProfile();

    /// <summary>A thread page's main post (the only non-openable card) gets the likes/reposts button.</summary>
    public Visibility EngagementVisibility(bool isOpenable) => isOpenable ? Visibility.Collapsed : Visibility.Visible;

    private void Engagement_Click(object sender, RoutedEventArgs e)
    {
        if (Post is not null)
            Pages.PostEngagementPage.Open(Post);
    }

    private void Avatar_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        OpenProfile();
    }

    private void OpenProfile()
    {
        if (Post is not null)
            Pages.ProfilePage.Open(Post.AuthorDid.Length > 0 ? Post.AuthorDid : Post.AuthorHandle);
    }

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            DataPackage package = new();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (System.Exception ex)
        {
            LogService.Warn("Clipboard", ex.Message);
        }
    }
}
