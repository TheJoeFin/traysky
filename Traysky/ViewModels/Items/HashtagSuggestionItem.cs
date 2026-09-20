namespace Traysky.ViewModels.Items;

/// <summary>One '#' suggestion row in ComposeBox's RichSuggestBox popup: either a previously-used tag or the tag exactly as typed.</summary>
public sealed class HashtagSuggestionItem
{
    public string Tag { get; init; } = string.Empty;
    public bool IsRecent { get; init; }

    public string Display => "#" + Tag;
}
