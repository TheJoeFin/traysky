using Traysky.ViewModels.Items;

namespace Traysky.Models;

/// <summary>
/// What the post page opens with: the post itself when already in hand (so it renders before
/// the thread loads), or just its AT URI when it still has to be fetched, plus an optional
/// one-line context such as "Barry Dorrans liked this post".
/// </summary>
public sealed record PostPageArgs(PostItem? Post, string? Context = null, string? AtUri = null)
{
    public string? EffectiveAtUri => Post?.AtUri ?? AtUri;
}
