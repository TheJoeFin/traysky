using Microsoft.UI.Xaml;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One post in a flattened thread: ancestors above the opened post, then the post itself,
/// then replies indented by depth.
/// </summary>
public sealed class ThreadRow
{
    public PostItem Post { get; init; } = null!;

    /// <summary>0 for ancestors and the opened post, 1 for direct replies, 2 for replies to those, …</summary>
    public int Depth { get; init; }

    /// <summary>The post the page was opened for; drawn with a highlight and not tappable.</summary>
    public bool IsMain { get; init; }

    public Thickness Indent => new(Depth * 16, 0, 0, 0);
}
