using idunno.Bluesky.Moderation;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// The questions and notices a view model puts to the user. It says <em>what</em> to ask; the
/// implementation (<see cref="XamlDialogService"/>) owns the ContentDialog and the XamlRoot it
/// needs, so view models never touch either. A cancelled question answers false or null.
/// </summary>
public interface IDialogService
{
    Task ShowMessageAsync(string title, string message);

    /// <summary>A plain confirmation with a custom confirm button.</summary>
    Task<bool> ConfirmAsync(string title, string message, string button);

    Task<bool> ConfirmDetachQuoteAsync(string handle);

    Task<bool> ConfirmBlockAsync(string handle);

    Task<bool> ConfirmDeletePostAsync();

    /// <summary>Asks why <paramref name="what"/> is being reported; null when cancelled.</summary>
    Task<(ReportType Reason, string Details)?> AskReportAsync(string what);

    /// <summary>Edits a posted post's reply and quote settings and saves them; false when nothing changed or saving failed.</summary>
    Task<bool> EditPostGatesAsync(PostItem post);

    /// <summary>Asks for a list's name, description and (for a new list) purpose; null when cancelled.</summary>
    Task<(string Name, string Description, ListKind Kind)?> AskListAsync(ListItem? existing);

    /// <summary>Asks for a stream link, an optional title and how long to stay live; null when cancelled.</summary>
    Task<(Uri Link, string Title, int Minutes)?> AskGoLiveAsync();

    Task<bool> ConfirmDeleteListAsync(string name);

    Task<bool> ConfirmBlockListAsync(string name);

    /// <summary>The "Add to list" checkboxes for an account; changes are saved as they are ticked.</summary>
    Task ShowListMembershipAsync(string did, string handle);

    Task<string?> AskGroupNameAsync(string current);

    /// <summary>Picks people to add to a group; null when cancelled.</summary>
    Task<List<ActorItem>?> AskGroupMembersAsync(int roomLeft, IReadOnlySet<string> alreadyIn);

    Task<(string Name, List<ActorItem> Members)?> AskNewGroupAsync();

    /// <summary>Asks for a group's join code (or uses <paramref name="code"/>) and joins.</summary>
    Task JoinGroupAsync(string? code = null);
}
