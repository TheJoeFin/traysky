using Microsoft.UI.Xaml.Media;
using System;
using Traysky.Helpers;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One '@' suggestion row in ComposeBox's RichSuggestBox popup, projected once from
/// <c>ProfileViewBasic</c> (see <see cref="Services.FeedMapper.ToMentionSuggestion"/>) so the
/// popup binds to plain properties instead of the wire type.
/// </summary>
public sealed class MentionSuggestionItem
{
    public string Did { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public Uri? AvatarUri { get; init; }

    public string NameOrHandle => DisplayName.Length > 0 ? DisplayName : Handle;

    public string HandleDisplay => "@" + Handle;

    public ImageSource? AvatarImage => BindHelpers.ImageFromUri(AvatarUri);
}
