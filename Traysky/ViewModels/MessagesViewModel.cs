using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky.Chat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Pages;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The Messages page: every conversation the account is in, newest activity first, requests
/// included (and labelled) so nothing waits unseen.
/// </summary>
public sealed partial class MessagesViewModel : DialogViewModel
{
    private static readonly Lazy<MessagesViewModel> _instance = new(() => new MessagesViewModel());

    public static MessagesViewModel Instance => _instance.Value;

    private const int PageSize = 30;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cursor;
    private DateTimeOffset _loadedAtUtc = DateTimeOffset.MinValue;

    private MessagesViewModel()
    {
        BlueskySessionService.Instance.SignedOut += (_, _) => Clear();
        ChatService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatService.UnreadCount))
                OnPropertyChanged(nameof(CanMarkAllRead));
        };
        Items.CollectionChanged += (_, _) => RefreshDerived();
    }

    public ObservableCollection<ConversationItem> Items { get; } = [];

    /// <summary>The main list, or just the message requests (loaded through their own endpoint).</summary>
    public ChatListMode Mode { get; private set; } = ChatListMode.All;

    public bool IsRequestsMode => Mode == ChatListMode.Requests;

    public string EmptyText => ChatReadPolicy.EmptyText(Mode);

    /// <summary>The Requests tab's label, with how many requests are known.</summary>
    public string RequestsLabel => ChatReadPolicy.RequestsLabel(
        IsRequestsMode ? Items.Count : Items.Count(i => i.IsRequest), IsRequestsMode && HasMore);

    public bool CanMarkAllRead => ChatReadPolicy.CanMarkAllRead(
        Mode, ChatService.Instance.UnreadCount, Items.Count(i => i.IsUnread && !i.IsRequest && !i.IsMuted));

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(CanMarkAllRead));
        OnPropertyChanged(nameof(RequestsLabel));
    }

    /// <summary>Switches between the main list and the requests, reloading from the top.</summary>
    public Task SetModeAsync(ChatListMode mode)
    {
        if (Mode == mode)
            return Task.CompletedTask;

        Mode = mode;
        _cursor = null;
        Items.Clear();
        IsEmpty = false;
        Error = null;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsRequestsMode));
        OnPropertyChanged(nameof(EmptyText));
        RefreshDerived();
        return RefreshAsync();
    }

    private sealed record Page(bool Ok, System.Net.HttpStatusCode Status, AtErrorDetail? Detail, IReadOnlyList<ConversationView> Views, string? Cursor);

    /// <summary>One page of the current list. Requests come from their own endpoint, which also types its rows more loosely.</summary>
    private async Task<Page> FetchAsync(BlueskySessionService session, string? cursor)
    {
        if (Mode == ChatListMode.Requests)
        {
            var requests = await session.Agent.ListConversationRequests(PageSize, cursor);
            return requests.Succeeded && requests.Result is { } r
                ? new Page(true, requests.StatusCode, requests.AtErrorDetail, r.OfType<ConversationView>().ToList(), r.Cursor)
                : new Page(false, requests.StatusCode, requests.AtErrorDetail, [], null);
        }

        var all = await session.Agent.ListConversations(limit: PageSize, cursor: cursor);
        return all.Succeeded && all.Result is { } c
            ? new Page(true, all.StatusCode, all.AtErrorDetail, c.ToList(), c.Cursor)
            : new Page(false, all.StatusCode, all.AtErrorDetail, [], null);
    }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public Task RefreshIfStaleAsync()
    {
        if (Items.Count > 0 && !ChatService.Instance.HasUnread && DateTimeOffset.UtcNow - _loadedAtUtc < StaleAfter)
            return Task.CompletedTask;

        return RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        if (!session.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoading = Items.Count == 0;
        Error = null;

        try
        {
            if (PreviewMode.IsEnabled)
            {
                Replace(PreviewMode.SampleConversations().Where(c => Mode == ChatListMode.All || c.IsRequest));
                _cursor = null;
                return;
            }

            if (!await session.EnsureAuthenticatedAsync())
            {
                Error = session.AuthenticationError;
                return;
            }

            ChatListMode requested = Mode;
            Page result = await FetchAsync(session, null);
            if (requested != Mode)
                return;

            if (!result.Ok)
            {
                Error = result.Status switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
                    0 => "Couldn't reach Bluesky. Check your connection.",
                    _ => "Couldn't load your messages."
                };
                LogService.Warn("Chat", $"Conversation list failed: {(int)result.Status} {result.Detail?.Error}");
                return;
            }

            Replace(result.Views.Select(c => ChatMapper.ToConversationItem(c, session.Did)));
            _cursor = result.Cursor;
            RefreshDerived();
        }
        catch (Exception ex)
        {
            LogService.Error("Chat", "Conversation list threw", ex);
            Error = "Couldn't load your messages.";
        }
        finally
        {
            IsLoading = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        if (!HasMore || !session.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoadingMore = true;
        try
        {
            ChatListMode requested = Mode;
            Page result = await FetchAsync(session, _cursor);
            if (!result.Ok || requested != Mode)
                return;

            HashSet<string> known = Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (ConversationView view in result.Views)
            {
                if (known.Add(view.Id))
                    Items.Add(ChatMapper.ToConversationItem(view, session.Did));
            }
            _cursor = result.Cursor;
            RefreshDerived();
        }
        catch (Exception ex)
        {
            LogService.Error("Chat", "Conversation list load more threw", ex);
        }
        finally
        {
            IsLoadingMore = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Moves a conversation to the top after a message was sent from it, or adds it when it
    /// was just started from a profile and isn't in the list yet.
    /// </summary>
    public void BringToTop(ConversationItem conversation)
    {
        ConversationItem? existing = Items.FirstOrDefault(i => i.Id == conversation.Id);
        if (existing is not null)
        {
            int index = Items.IndexOf(existing);
            if (!ReferenceEquals(existing, conversation))
            {
                existing.LastMessagePreview = conversation.LastMessagePreview;
                existing.LastMessageAt = conversation.LastMessageAt;
                existing.UnreadCount = conversation.UnreadCount;
                existing.IsRequest = conversation.IsRequest;
            }
            if (index > 0)
                Items.Move(index, 0);
            return;
        }

        Items.Insert(0, conversation);
        IsEmpty = false;
    }

    /// <summary>Something changed in chat that the loaded list doesn't show; the next visit reloads it.</summary>
    public void Invalidate() => _loadedAtUtc = DateTimeOffset.MinValue;

    /// <summary>Drops a conversation that was left or declined.</summary>
    public void Remove(string conversationId)
    {
        ConversationItem? item = Items.FirstOrDefault(i => i.Id == conversationId);
        if (item is null)
            return;

        ChatService.Instance.ConversationRead(item);
        Items.Remove(item);
        IsEmpty = Items.Count == 0;
    }

    /// <summary>Accepts a request without opening it. Shows at once, undone if the server refuses.</summary>
    [RelayCommand]
    private async Task AcceptRequestAsync(ConversationItem? conversation)
    {
        if (conversation is null || !conversation.IsRequest)
            return;

        // In the Requests list an accepted conversation leaves it; it is in the main list now.
        int index = Items.IndexOf(conversation);
        conversation.IsRequest = false;
        if (IsRequestsMode && index >= 0)
            Items.RemoveAt(index);
        UpdateBadge();
        IsEmpty = Items.Count == 0;
        if (PreviewMode.IsEnabled)
            return;

        if (!await ChatCall(async agent =>
            {
                var r = await agent.AcceptConversation(conversation.Id);
                return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
            }, "Accept"))
        {
            conversation.IsRequest = true;
            if (IsRequestsMode && index >= 0)
                Items.Insert(Math.Min(index, Items.Count), conversation);
            UpdateBadge();
            IsEmpty = Items.Count == 0;
        }
        else
        {
            Invalidate();
        }
    }

    /// <summary>
    /// Marks every accepted conversation read. The badge and rows clear at once; a refusal puts the counts back.
    /// </summary>
    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        if (!CanMarkAllRead)
            return;

        List<(ConversationItem Item, long Count)> before = Items
            .Where(i => i.IsUnread && !i.IsRequest)
            .Select(i => (i, i.UnreadCount))
            .ToList();
        int badgeBefore = ChatService.Instance.UnreadCount;

        foreach ((ConversationItem item, _) in before)
            item.UnreadCount = 0;
        ChatService.Instance.UpdateUnreadFrom(Items);
        RefreshDerived();
        if (PreviewMode.IsEnabled)
            return;

        if (await ChatCall(async agent =>
            {
                var r = await agent.UpdateAllRead(ConversationStatus.Accepted);
                return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
            }, "Mark all read"))
        {
            // The server's count is the truth (muted chats, the 100 cap), so ask again.
            _ = ChatService.Instance.CheckAsync(force: true);
            return;
        }

        foreach ((ConversationItem item, long count) in before)
            item.UnreadCount = count;
        ChatService.Instance.UpdateUnreadFrom(Items);
        if (ChatService.Instance.UnreadCount < badgeBefore)
            _ = ChatService.Instance.CheckAsync(force: true);
        RefreshDerived();
    }

    private void UpdateBadge()
    {
        if (ChatReadPolicy.UpdatesBadge(Mode))
            ChatService.Instance.UpdateUnreadFrom(Items);
        RefreshDerived();
    }

    /// <summary>Declines a request: it leaves the list at once and comes back if the server refuses.</summary>
    /// <summary>Asks first, then declines the request.</summary>
    [RelayCommand]
    private async Task ConfirmDeclineRequest(ConversationItem? conversation)
    {
        if (conversation is null)
            return;

        if (await Dialogs.ConfirmAsync("Decline this request?", $"The request from {conversation.Title} is removed.", "Decline"))
            await DeclineRequestAsync(conversation);
    }

    [RelayCommand]
    private Task JoinGroup() => Dialogs.JoinGroupAsync();

    [RelayCommand]
    private async Task NewGroup()
    {
        if (await Dialogs.AskNewGroupAsync() is not { } answer)
            return;

        ConversationItem? created;
        if (PreviewMode.IsEnabled)
        {
            created = PreviewMode.SampleNewGroup(answer.Name, answer.Members.Count);
        }
        else
        {
            (created, string? error) = await GroupChatService.CreateAsync(answer.Name, answer.Members.Select(m => m.Did).ToList());
            if (error is not null)
            {
                await Dialogs.ShowMessageAsync("Couldn't create the group", error);
                return;
            }
        }

        if (created is null)
        {
            // The group exists but the server didn't hand it back; the list will show it.
            Invalidate();
            _ = RefreshAsync();
            return;
        }

        BringToTop(created);
        ConversationPage.Open(created);
    }

    [RelayCommand]
    private async Task DeclineRequestAsync(ConversationItem? conversation)
    {
        if (conversation is null || !conversation.IsRequest)
            return;

        int index = Items.IndexOf(conversation);
        if (index < 0)
            return;

        Items.RemoveAt(index);
        IsEmpty = Items.Count == 0;
        if (PreviewMode.IsEnabled)
            return;

        if (!await ChatCall(async agent =>
            {
                var r = await agent.LeaveConversation(conversation.Id);
                return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
            }, "Decline"))
        {
            Items.Insert(Math.Min(index, Items.Count), conversation);
            IsEmpty = false;
        }
    }

    private async Task<bool> ChatCall(Func<idunno.Bluesky.BlueskyAgent, Task<(bool Ok, System.Net.HttpStatusCode Status, AtErrorDetail? Detail)>> call, string what)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
            {
                Error = session.AuthenticationError;
                return false;
            }
            (bool ok, System.Net.HttpStatusCode status, AtErrorDetail? detail) = await call(session.Agent);
            if (ok)
                return true;

            Error = status == 0 ? "Couldn't reach Bluesky. Nothing was changed." : $"{what} didn't go through.";
            LogService.Warn("Chat", $"{what} failed: {(int)status} {detail?.Error}");
        }
        catch (Exception ex)
        {
            Error = $"{what} didn't go through.";
            LogService.Warn("Chat", $"{what} threw: {ex.Message}");
        }
        return false;
    }

    private void Replace(IEnumerable<ConversationItem> conversations)
    {
        Items.Clear();
        foreach (ConversationItem c in conversations)
            Items.Add(c);

        _loadedAtUtc = DateTimeOffset.UtcNow;
        IsEmpty = Items.Count == 0;
        UpdateBadge();
    }

    private void Clear()
    {
        Mode = ChatListMode.All;
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsRequestsMode));
        OnPropertyChanged(nameof(EmptyText));
        Items.Clear();
        _cursor = null;
        _loadedAtUtc = DateTimeOffset.MinValue;
        Error = null;
        IsEmpty = false;
    }
}
