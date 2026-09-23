using System;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One account in a people list (e.g. who liked or reposted a post), projected once from
/// <c>ProfileViewBasic</c> (see <see cref="Services.FeedMapper.ToActorItem"/>).
/// </summary>
public sealed class ActorItem
{
    public string Did { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public Uri? AvatarUri { get; init; }

    public string NameOrHandle => DisplayName.Length > 0 ? DisplayName : Handle;

    public string HandleDisplay => "@" + Handle;

    /// <summary>What <see cref="Pages.ProfilePage.Open"/> takes: the DID when known, else the handle.</summary>
    public string ProfileKey => Did.Length > 0 ? Did : Handle;
}
