using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;
using Windows.System;
using Windows.UI.Core;

namespace Traysky.Controls;

/// <summary>
/// The compose box: the one on <see cref="Pages.ComposePage"/> (a fresh post, a quote, or a
/// reply to a post that isn't shown anywhere), or the one a PostCard keeps hidden and pops
/// visible for an inline reply. One line until focused; Ctrl+Enter posts.
/// </summary>
public sealed partial class ComposeBox : UserControl
{
    public ComposeViewModel ViewModel { get; } = ComposeViewModel.Instance;

    public ComposeBox()
    {
        InitializeComponent();
        ViewModel.FocusRequested += OnFocusRequested;

        // A PostCard's own inline box is created fresh with every thread page visit (PostPage
        // isn't cached); without this, each one leaks its subscription to the shared singleton.
        Unloaded += (_, _) => ViewModel.FocusRequested -= OnFocusRequested;
    }

    private void OnFocusRequested(object? sender, System.EventArgs e) => FocusEditor();

    public void FocusEditor()
    {
        // The request can arrive before the control is in the tree (tray "Compose" action
        // while the popup is still opening); Loaded catches that case.
        if (IsLoaded)
        {
            Editor.Focus(FocusState.Programmatic);
            Editor.SelectionStart = Editor.Text.Length;
            return;
        }

        void OnLoaded(object s, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            Editor.Focus(FocusState.Programmatic);
            Editor.SelectionStart = Editor.Text.Length;
        }
        Loaded += OnLoaded;
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
