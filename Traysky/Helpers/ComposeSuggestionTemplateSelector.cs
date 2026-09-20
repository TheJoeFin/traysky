using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>Picks the avatar-and-handle row for '@' results or the tag row for '#' results in ComposeBox's RichSuggestBox popup.</summary>
public sealed class ComposeSuggestionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MentionTemplate { get; set; }
    public DataTemplate? HashtagTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        MentionSuggestionItem => MentionTemplate,
        HashtagSuggestionItem => HashtagTemplate,
        _ => base.SelectTemplateCore(item)
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
