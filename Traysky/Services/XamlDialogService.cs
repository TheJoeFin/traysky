using idunno.Bluesky.Moderation;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Traysky.Helpers;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// <see cref="IDialogService"/> over ContentDialogs. Each view hands in the way to find its own
/// <see cref="XamlRoot"/> (a ContentDialog must be shown in the root of the window it belongs
/// to - the tray popup and the contained profile window have different ones), looked up when a
/// dialog is shown, not when this is created, because a control has no root until it is loaded.
/// </summary>
public sealed class XamlDialogService : IDialogService
{
    private readonly Func<XamlRoot?> _root;

    public XamlDialogService(Func<XamlRoot?> root) => _root = root;

    public Task ShowMessageAsync(string title, string message) =>
        _root() is { } root ? ModerationDialogs.ShowMessageAsync(root, title, message) : Task.CompletedTask;

    public Task<bool> ConfirmDetachQuoteAsync(string handle) =>
        _root() is { } root ? ModerationDialogs.ConfirmDetachQuoteAsync(root, handle) : Task.FromResult(false);

    public Task<bool> ConfirmBlockAsync(string handle) =>
        _root() is { } root ? ModerationDialogs.ConfirmBlockAsync(root, handle) : Task.FromResult(false);

    public Task<bool> ConfirmDeletePostAsync() =>
        _root() is { } root ? ModerationDialogs.ConfirmDeletePostAsync(root) : Task.FromResult(false);

    public Task<(ReportType Reason, string Details)?> AskReportAsync(string what) =>
        _root() is { } root
            ? ModerationDialogs.AskReportAsync(root, what)
            : Task.FromResult<(ReportType Reason, string Details)?>(null);

    public Task<bool> EditPostGatesAsync(PostItem post) =>
        _root() is { } root ? GateDialogs.EditPostAsync(root, post) : Task.FromResult(false);

    public Task<bool> ConfirmAsync(string title, string message, string button) =>
        _root() is { } root ? ChatDialogs.ConfirmAsync(root, title, message, button) : Task.FromResult(false);

    public Task<(string Name, string Description, ListKind Kind)?> AskListAsync(ListItem? existing) =>
        _root() is { } root
            ? ListDialogs.AskListAsync(root, existing)
            : Task.FromResult<(string Name, string Description, ListKind Kind)?>(null);

    public Task<bool> ConfirmDeleteListAsync(string name) =>
        _root() is { } root ? ListDialogs.ConfirmDeleteAsync(root, name) : Task.FromResult(false);

    public Task<bool> ConfirmBlockListAsync(string name) =>
        _root() is { } root ? ListDialogs.ConfirmBlockAsync(root, name) : Task.FromResult(false);

    public Task ShowListMembershipAsync(string did, string handle) =>
        _root() is { } root ? ListDialogs.ShowMembershipAsync(root, did, handle) : Task.CompletedTask;

    public Task<string?> AskGroupNameAsync(string current) =>
        _root() is { } root ? GroupDialogs.AskNameAsync(root, current) : Task.FromResult<string?>(null);

    public Task<List<ActorItem>?> AskGroupMembersAsync(int roomLeft, IReadOnlySet<string> alreadyIn) =>
        _root() is { } root ? GroupDialogs.AskMembersAsync(root, roomLeft, alreadyIn) : Task.FromResult<List<ActorItem>?>(null);

    public Task<(string Name, List<ActorItem> Members)?> AskNewGroupAsync() =>
        _root() is { } root
            ? GroupDialogs.AskNewGroupAsync(root)
            : Task.FromResult<(string Name, List<ActorItem> Members)?>(null);

    public Task JoinGroupAsync(string? code = null) =>
        _root() is { } root ? GroupDialogs.JoinAsync(root, code) : Task.CompletedTask;
}
