using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// What the "…" menu of one post card offers and does: which items show, and the mute, block,
/// report, delete, detach and settings actions with their confirmations. A card owns one and
/// points it at whichever post it currently shows (cards are recycled between posts).
/// </summary>
public sealed partial class PostActionsViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    public PostActionsViewModel(IDialogService dialogs) => _dialogs = dialogs;

    /// <summary>Raised after the post was deleted on the server, so the card showing it can hide.</summary>
    public event EventHandler<PostItem>? PostDeleted;

    [ObservableProperty]
    public partial PostItem? Post { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowModeration), nameof(ShowDelete), nameof(ShowInteractionSettings), nameof(ShowDetachQuote),
        nameof(ShowMuteThread), nameof(ShowDeleteSeparator), nameof(MuteThreadLabel))]
    public partial PostMenuState Menu { get; private set; }

    public bool ShowModeration => Menu.ShowAccountModeration;
    public bool ShowDelete => Menu.ShowDelete;
    public bool ShowInteractionSettings => Menu.ShowInteractionSettings;
    public bool ShowDetachQuote => Menu.ShowDetachQuote;
    public bool ShowMuteThread => Menu.ShowMuteThread;
    public bool ShowDeleteSeparator => Menu.ShowDelete || Menu.ShowMuteThread || Menu.ShowDetachQuote;
    public string MuteThreadLabel => Menu.MuteThreadLabel;

    public string MuteAuthorLabel => $"Mute @{Post?.AuthorHandle}";
    public string BlockAuthorLabel => $"Block @{Post?.AuthorHandle}";

    public bool IsOwnPost => Post is not null
        && string.Equals(Post.AuthorDid, BlueskySessionService.Instance.Did, StringComparison.Ordinal);

    partial void OnPostChanged(PostItem? value)
    {
        OnPropertyChanged(nameof(MuteAuthorLabel));
        OnPropertyChanged(nameof(BlockAuthorLabel));
        OnPropertyChanged(nameof(IsOwnPost));
        RefreshMenu();
    }

    /// <summary>Re-reads the session and the post; call when the menu opens, since either can change under a card.</summary>
    public void RefreshMenu()
    {
        if (Post is not { } post)
        {
            Menu = default;
            return;
        }

        Menu = PostMenuPolicy.For(
            post.AuthorDid, BlueskySessionService.Instance.Did, BlueskySessionService.Instance.IsSignedIn, post.IsThreadMuted,
            post.Embed is { Kind: EmbedKind.Quote } ? post.Embed.QuoteAtUri : null);
    }

    [RelayCommand]
    private async Task MuteThreadAsync()
    {
        if (Post is not { } post)
            return;

        bool wasMuted = post.IsThreadMuted;
        if (!await PostInteractions.ToggleThreadMuteAsync(post))
            await _dialogs.ShowMessageAsync(wasMuted ? "Couldn't unmute" : "Couldn't mute",
                "The thread's mute setting didn't change. Try again in a moment.");
    }

    [RelayCommand]
    private async Task EditInteractionSettingsAsync()
    {
        if (Post is not { } post || !IsOwnPost)
            return;

        if (!await _dialogs.EditPostGatesAsync(post))
            await _dialogs.ShowMessageAsync("Couldn't save", "The reply and quote settings didn't change. Try again in a moment.");
    }

    [RelayCommand]
    private async Task DetachQuoteAsync()
    {
        if (Post is not { Embed: { Kind: EmbedKind.Quote, QuoteAtUri: { } quotedUri } } post)
            return;
        if (!DetachQuotePolicy.CanDetach(quotedUri, BlueskySessionService.Instance.Did, BlueskySessionService.Instance.IsSignedIn))
            return;

        if (!await _dialogs.ConfirmDetachQuoteAsync(post.AuthorHandle))
            return;

        if (await GatesService.DetachQuoteAsync(quotedUri, post.AtUri))
            post.Embed = new EmbedItem { Kind = EmbedKind.Unavailable };
        else
            await _dialogs.ShowMessageAsync("Couldn't detach", "Your post wasn't detached from the quote. Try again in a moment.");
    }

    [RelayCommand]
    private async Task DeletePostAsync()
    {
        if (Post is not { } post || !IsOwnPost)
            return;

        if (!await _dialogs.ConfirmDeletePostAsync())
            return;

        if (await PostInteractions.DeletePostAsync(post))
            PostDeleted?.Invoke(this, post);
        else
            await _dialogs.ShowMessageAsync("Couldn't delete", "The post wasn't deleted. Try again in a moment.");
    }

    [RelayCommand]
    private async Task MuteAuthorAsync()
    {
        if (Post is not { } post)
            return;

        if (!await ModerationService.SetMutedAsync(post.AuthorDid, muted: true))
            await _dialogs.ShowMessageAsync("Couldn't mute", $"Muting @{post.AuthorHandle} didn't go through. Try again in a moment.");
    }

    [RelayCommand]
    private async Task BlockAuthorAsync()
    {
        if (Post is not { } post)
            return;

        string handle = post.AuthorHandle;
        string did = post.AuthorDid;
        if (!await _dialogs.ConfirmBlockAsync(handle))
            return;

        if (await ModerationService.BlockAsync(did) is null)
            await _dialogs.ShowMessageAsync("Couldn't block", $"Blocking @{handle} didn't go through. Try again in a moment.");
    }

    [RelayCommand]
    private async Task ReportPostAsync()
    {
        if (Post is not { } post)
            return;

        var reference = post.Reference;
        if (await _dialogs.AskReportAsync("post") is not { } report)
            return;

        bool sent = await ModerationService.ReportPostAsync(reference, report.Reason, report.Details);
        await _dialogs.ShowMessageAsync(
            sent ? "Report sent" : "Couldn't send report",
            sent ? "Thanks. Bluesky's moderators will take a look." : "The report didn't go through. Try again in a moment.");
    }
}
