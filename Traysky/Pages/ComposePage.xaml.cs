using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Traysky.Services;
using Traysky.ViewModels;

namespace Traysky.Pages;

/// <summary>
/// A whole page for writing a post: a fresh one (titlebar "New post" button), a quote (a
/// PostCard's Quote action), or a reply to a post that isn't shown anywhere on screen (e.g. a
/// notification's subject post). A reply to a post that *is* on screen shows inline under it
/// instead - see PostCard - and never opens this page. The caller sets up the draft's context on
/// <see cref="ComposeViewModel"/> (BeginNew/BeginReply/BeginQuote) before navigating here;
/// posting or backing out returns to whatever was showing before, via the shell's back button.
/// </summary>
public sealed partial class ComposePage : Page
{
    public ComposePage()
    {
        InitializeComponent();

        // Tied to Loaded, not the constructor, so a page that never loads can't be left rooted
        // by the singleton.
        Loaded += (_, _) =>
        {
            ComposeViewModel.Instance.Posted -= OnPosted;
            ComposeViewModel.Instance.Posted += OnPosted;
        };
        Unloaded += (_, _) => ComposeViewModel.Instance.Posted -= OnPosted;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Compose.FocusEditor();
    }

    private void OnPosted(object? sender, EventArgs e)
    {
        if (NavigationService.Instance.CanGoBack)
            NavigationService.Instance.GoBack();
    }
}
