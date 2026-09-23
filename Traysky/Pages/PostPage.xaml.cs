using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>
/// A post with its thread. Opened from a notification ("Alice liked this post"), from a tap on
/// a timeline post, or from a reply inside another thread. Not cached: each visit is a fresh
/// page, and the back stack holds the trail.
/// </summary>
public sealed partial class PostPage : Page
{
    public PostPageViewModel ViewModel { get; } = new();

    public PostPage()
    {
        InitializeComponent();

        // A reply (now inline under its post - see PostCard) or a quote posted from this page
        // won't show up until the thread is re-fetched. Not cached (see class remarks), so the
        // subscription is tied to Loaded/Unloaded - never the constructor, where a page that
        // was created but never loaded would be left rooted by the singleton.
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

        if (e.Parameter is PostPageArgs args)
            _ = ViewModel.LoadAsync(args);
    }

    private void OnPosted(object? sender, EventArgs e)
    {
        if (ViewModel.RetryCommand.CanExecute(null))
            ViewModel.RetryCommand.Execute(null);
    }

    /// <summary>Navigates to a post's thread page. Shared by every list that shows post cards.</summary>
    public static void Open(PostItem post, string? context = null)
    {
        NavigationService.Instance.Navigate(typeof(PostPage), new PostPageArgs(post, context));
    }

    /// <summary>Opens a post that has not been fetched yet; the page loads it by AT URI.</summary>
    public static void Open(string atUri, string? context = null)
    {
        NavigationService.Instance.Navigate(typeof(PostPage), new PostPageArgs(null, context, atUri));
    }

    private void PostCard_ThreadRequested(object? sender, PostItem post)
    {
        // A reply inside this thread opens as its own page, so its replies can be explored;
        // the current page stays on the back stack.
        Open(post);
    }
}
