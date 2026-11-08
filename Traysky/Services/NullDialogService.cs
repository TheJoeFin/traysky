using idunno.Bluesky.Moderation;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>An <see cref="IDialogService"/> with nobody to ask: every question is answered "cancelled".</summary>
public sealed class NullDialogService : IDialogService
{
    public static NullDialogService Instance { get; } = new();

    private NullDialogService()
    {
    }

    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    public Task<bool> ConfirmAsync(string title, string message, string button) => Task.FromResult(false);
    public Task<bool> ConfirmDetachQuoteAsync(string handle) => Task.FromResult(false);
    public Task<bool> ConfirmBlockAsync(string handle) => Task.FromResult(false);
    public Task<bool> ConfirmDeletePostAsync() => Task.FromResult(false);
    public Task<(ReportType Reason, string Details)?> AskReportAsync(string what) => Task.FromResult<(ReportType Reason, string Details)?>(null);
    public Task<bool> EditPostGatesAsync(PostItem post) => Task.FromResult(false);
    public Task<(string Name, string Description, ListKind Kind)?> AskListAsync(ListItem? existing) => Task.FromResult<(string Name, string Description, ListKind Kind)?>(null);
    public Task<(Uri Link, string Title, int Minutes)?> AskGoLiveAsync() => Task.FromResult<(Uri Link, string Title, int Minutes)?>(null);
    public Task<bool> ConfirmDeleteListAsync(string name) => Task.FromResult(false);
    public Task<bool> ConfirmBlockListAsync(string name) => Task.FromResult(false);
    public Task ShowListMembershipAsync(string did, string handle) => Task.CompletedTask;
    public Task<string?> AskGroupNameAsync(string current) => Task.FromResult<string?>(null);
    public Task<List<ActorItem>?> AskGroupMembersAsync(int roomLeft, IReadOnlySet<string> alreadyIn) => Task.FromResult<List<ActorItem>?>(null);
    public Task<(string Name, List<ActorItem> Members)?> AskNewGroupAsync() => Task.FromResult<(string Name, List<ActorItem> Members)?>(null);
    public Task JoinGroupAsync(string? code = null) => Task.CompletedTask;
}
