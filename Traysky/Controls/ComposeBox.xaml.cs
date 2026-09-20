using CommunityToolkit.Common.Deferred;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.ComponentModel;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.System;
using Windows.UI.Core;

namespace Traysky.Controls;

/// <summary>
/// The compose box: the one on <see cref="Pages.ComposePage"/> (a fresh post, a quote, or a
/// reply to a post that isn't shown anywhere), or the one a PostCard keeps hidden and pops
/// visible for an inline reply. One line until focused; Ctrl+Enter posts. The editor is a
/// RichSuggestBox (CommunityToolkit) so typing '@' or '#' opens a live suggestion popup; see
/// <see cref="RichSuggestTextPolicy"/> for how its token markup is turned back into the plain
/// "@handle"/"#tag" text <see cref="ComposeViewModel.Text"/> and PostAsync's facet extraction
/// already expect.
/// </summary>
public sealed partial class ComposeBox : UserControl
{
    public ComposeViewModel ViewModel { get; } = ComposeViewModel.Instance;

    /// <summary>Set while pushing ViewModel.Text into the editor, so the resulting TextChanged doesn't bounce back.</summary>
    private bool _syncingFromViewModel;

    /// <summary>Bumped per '@' suggestion request so a slow response can't overwrite a newer one's results.</summary>
    private int _mentionRequestEpoch;

    public ComposeBox()
    {
        InitializeComponent();
        ViewModel.FocusRequested += OnFocusRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += ComposeBox_Loaded;

        // A PostCard's own inline box is created fresh with every thread page visit (PostPage
        // isn't cached); without this, each one leaks its subscription to the shared singleton.
        Unloaded += (_, _) =>
        {
            ViewModel.FocusRequested -= OnFocusRequested;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    private void ComposeBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Runs every time this instance (re)enters the visual tree, not just once: a PostCard's
        // inline box can be recycled by list virtualization, and the shared singleton VM's draft
        // may have changed to reflect a different post's reply while this instance was detached.
        if (!IsEditorReady)
            return;

        if (RichSuggestTextPolicy.StripTokenMarkup(CurrentEditorText()) != ViewModel.Text)
            SetEditorText(ViewModel.Text);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComposeViewModel.Text) || _syncingFromViewModel)
            return;

        // PostCard embeds its own ComposeBox for the inline reply box, so many instances of this
        // control can share the singleton VM at once; most sit collapsed and never load their
        // RichEditBox. A change from any one of them (including this one's own edit, echoed back
        // here) must not touch an instance whose TextDocument isn't ready yet - ComposeBox_Loaded
        // catches it up from the VM once it is.
        if (!IsEditorReady)
            return;

        // This instance's own edit already produced this value (Editor_TextChanged set it);
        // only a change from elsewhere - Post clearing the draft, or another ComposeBox instance
        // sharing the same singleton VM - needs to be pushed into this editor.
        if (RichSuggestTextPolicy.StripTokenMarkup(CurrentEditorText()) == ViewModel.Text)
            return;

        SetEditorText(ViewModel.Text);
    }

    /// <summary>RichEditBox doesn't create its TextDocument until it's loaded and templated; a collapsed/not-yet-loaded ComposeBox has neither.</summary>
    private bool IsEditorReady => IsLoaded && Editor.TextDocument is not null;

    private string CurrentEditorText()
    {
        Editor.TextDocument.GetText(TextGetOptions.NoHidden, out string text);
        return text;
    }

    private void SetEditorText(string text)
    {
        _syncingFromViewModel = true;
        try
        {
            Editor.TextDocument.SetText(TextSetOptions.None, text);
            Editor.TextDocument.Selection.StartPosition = int.MaxValue;
            Editor.TextDocument.Selection.EndPosition = int.MaxValue;
        }
        finally
        {
            _syncingFromViewModel = false;
        }
    }

    private void Editor_TextChanged(RichSuggestBox sender, RoutedEventArgs e)
    {
        if (_syncingFromViewModel)
            return;

        ViewModel.Text = RichSuggestTextPolicy.StripTokenMarkup(CurrentEditorText());
    }

    private async void Editor_SuggestionRequested(RichSuggestBox sender, SuggestionRequestedEventArgs args)
    {
        EventDeferral deferral = args.GetDeferral();
        try
        {
            string query = args.QueryText ?? string.Empty;

            if (args.Prefix == "@")
            {
                int epoch = ++_mentionRequestEpoch;
                IReadOnlyList<MentionSuggestionItem> results = await ViewModel.SearchMentionsAsync(query);
                if (epoch == _mentionRequestEpoch)
                    sender.ItemsSource = results;
            }
            else if (args.Prefix == "#")
            {
                sender.ItemsSource = ViewModel.SearchHashtags(query);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Editor_SuggestionChosen(RichSuggestBox sender, SuggestionChosenEventArgs args)
    {
        if (args.Prefix == "@" && args.SelectedItem is MentionSuggestionItem mention)
        {
            args.DisplayText = mention.Handle;
        }
        else if (args.Prefix == "#" && args.SelectedItem is HashtagSuggestionItem hashtag)
        {
            args.DisplayText = hashtag.Tag;
            ViewModel.RecordHashtagUsed(hashtag.Tag);
        }
        else
        {
            return;
        }

        // RichSuggestBox's default hyperlink color is a fixed RGB baked straight into the
        // document's character formatting, not a ThemeResource, so it doesn't adapt to dark
        // mode on its own (reads as illegibly dark against a dark background). A ThemeResource
        // brush lookup from code doesn't reliably track the live theme here either, so this
        // branches on the editor's own ActualTheme and stamps in an explicit, checked-for-
        // contrast color per theme instead.
        if (args.Format is ITextCharacterFormat format)
            format.ForegroundColor = TokenColor();
    }

    private Windows.UI.Color TokenColor() => Editor.ActualTheme switch
    {
        ElementTheme.Dark => Windows.UI.Color.FromArgb(0xFF, 0x6C, 0xB4, 0xFF),
        _ => Windows.UI.Color.FromArgb(0xFF, 0x00, 0x5F, 0xB8)
    };

    private void OnFocusRequested(object? sender, System.EventArgs e) => FocusEditor();

    public void FocusEditor()
    {
        // The request can arrive before the control is in the tree (tray "Compose" action
        // while the popup is still opening); Loaded catches that case.
        if (IsLoaded)
        {
            Editor.Focus(FocusState.Programmatic);
            MoveCaretToEndIfReady();
            return;
        }

        void OnLoaded(object s, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            Editor.Focus(FocusState.Programmatic);
            MoveCaretToEndIfReady();
        }
        Loaded += OnLoaded;
    }

    /// <summary>
    /// RichEditBox can report Loaded before it's laid out enough to have created its native
    /// TextDocument (seen on a PostCard's inline reply box, which starts life collapsed with no
    /// size) - in that case there's no caret to move yet, so this is a no-op rather than a crash.
    /// </summary>
    private void MoveCaretToEndIfReady()
    {
        if (Editor.TextDocument is null)
            return;

        Editor.TextDocument.Selection.StartPosition = int.MaxValue;
        Editor.TextDocument.Selection.EndPosition = int.MaxValue;
    }

    public double EditorMinHeight(bool expanded) => expanded ? 72 : 32;

    public Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public string PostLabel(PostItem? replyTo, PostItem? quoteOf) => replyTo is not null ? "Reply" : quoteOf is not null ? "Quote" : "Post";

    public string StatusText(string? uploadStatus) => uploadStatus ?? "Ctrl+Enter to post";

    public Brush CounterBrush(bool near, bool over)
    {
        string key = over ? "SystemFillColorCriticalBrush" : near ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush";
        return (Brush)Application.Current.Resources[key];
    }

    private void Editor_GotFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.IsExpanded = true;
    }

    private void Editor_LostFocus(object sender, RoutedEventArgs e)
    {
        // Clicking another control in this same box (Attach image/video, for example) also
        // fires the editor's LostFocus; only collapse once focus has actually left the whole
        // box, not just the text field, or attaching a photo before typing anything would
        // shrink the box out from under the picker. Deferred one tick since focus hasn't
        // necessarily settled on the new element yet when LostFocus raises.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsWithin(focused, this))
                return;

            ViewModel.CollapseIfEmpty();
        });
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (!ctrl)
            return;

        e.Handled = true;
        if (ViewModel.PostCommand.CanExecute(null))
            ViewModel.PostCommand.Execute(null);
    }
}
