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
using Traysky.Services;
using Traysky.ViewModels.Items;
using ChatMessages = idunno.Bluesky.Chat.Messages;

namespace Traysky.ViewModels;

/// <summary>
/// One conversation: its messages oldest to newest, older pages on demand, new messages by
/// polling while the page is open (Bluesky chat has no push for third-party clients), and
/// sending. One instance per <see cref="Pages.ConversationPage"/>.
/// </summary>
public sealed partial class ConversationViewModel : DialogViewModel
{
    private const int PageSize = 40;
    private const int PollSize = 20;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private string? _olderCursor;

    public ObservableCollection<MessageItem> Messages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend), nameof(CanType))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial ConversationItem? Conversation { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingOlder { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasOlder => !string.IsNullOrEmpty(_olderCursor);

    public bool CanSend => !IsSending && CanType && ChatThreadPolicy.CanSend(Draft);

    /// <summary>False in a locked group, where nobody can send.</summary>
    public bool CanType => Conversation is not { IsLocked: true };

    /// <summary>Raised after messages were added at the bottom (loaded, polled or sent), so the page can follow them.</summary>
    public event EventHandler<bool>? MessagesAppended;

    public async Task LoadAsync(ConversationItem conversation)
    {
        Conversation = conversation;
        IsLoading = true;
        Error = null;

        try
        {
            if (PreviewMode.IsEnabled)
            {
                Append(PreviewMode.SampleMessages(conversation), newestFirst: false);
                IsEmpty = Messages.Count == 0;
                MessagesAppended?.Invoke(this, true);
                return;
            }

            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
            {
                Error = session.AuthenticationError;
                return;
            }

            AtProtoHttpResult<ChatMessages> result = await session.Agent.GetMessages(conversation.Id, limit: PageSize);
            if (!result.Succeeded || result.Result is null)
            {
                Error = result.StatusCode == 0 ? "Couldn't reach Bluesky. Check your connection." : "Couldn't load this conversation.";
                LogService.Warn("Chat", $"GetMessages failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return;
            }

            Append(Map(result.Result), newestFirst: true);
            _olderCursor = result.Result.Cursor;
            IsEmpty = Messages.Count == 0;
            MessagesAppended?.Invoke(this, true);

            await MarkReadAsync();
        }
        catch (Exception ex)
        {
            LogService.Error("Chat", "Loading a conversation threw", ex);
            Error = "Couldn't load this conversation.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadOlderAsync()
    {
        if (Conversation is null || !HasOlder || PreviewMode.IsEnabled)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        IsLoadingOlder = true;
        try
        {
            AtProtoHttpResult<ChatMessages> result = await BlueskySessionService.Instance.Agent.GetMessages(Conversation.Id, limit: PageSize, cursor: _olderCursor);
            if (!result.Succeeded || result.Result is null)
                return;

            // Newest first from the server, so inserting each at the top leaves them in order.
            foreach (MessageItem message in Map(result.Result))
            {
                if (_known.Add(message.Id))
                    Messages.Insert(0, message);
            }
            _olderCursor = result.Result.Cursor;
            UpdateRuns();
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"Loading older messages threw: {ex.Message}");
        }
        finally
        {
            IsLoadingOlder = false;
            _gate.Release();
        }
    }

    /// <summary>Picks up anything new since the last look, and marks it read - the conversation is on screen.</summary>
    public async Task PollAsync()
    {
        if (Conversation is null || IsLoading || PreviewMode.IsEnabled || !BlueskySessionService.Instance.IsSignedIn)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        try
        {
            if (!await BlueskySessionService.Instance.EnsureAuthenticatedAsync())
                return;

            // The log says exactly what changed. Until it has given us a cursor to resume from
            // (or when it can't be reached) read the latest messages directly instead.
            IReadOnlyList<LogBase>? log = await ChatService.Instance.SyncLogAsync();
            if (log is null || !ChatService.Instance.HasLogCursor)
            {
                await PollMessagesAsync(refreshReactions: false);
                return;
            }

            if (log.Count > 0)
                await ApplyLogAsync(log);
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"Polling a conversation threw: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The conversation was left or declined, here or on another device: the page should go back.</summary>
    public event EventHandler? Closed;

    private async Task ApplyLogAsync(IReadOnlyList<LogBase> log)
    {
        ConversationItem conversation = Conversation!;
        ChatLogDelta delta = ChatThreadPolicy.Reduce(log.Select(ChatMapper.ToLogEntry), conversation.Id, _known);

        if (delta.InboxChanged)
            MessagesViewModel.Instance.Invalidate();

        if (delta.Muted is bool muted)
            conversation.IsMuted = muted;

        if (delta.GroupChanged && conversation.IsGroup)
            await RefreshGroupAsync(conversation);

        if (delta.LeftConversation)
        {
            MessagesViewModel.Instance.Remove(conversation.Id);
            Closed?.Invoke(this, EventArgs.Empty);
            return;
        }

        foreach (string id in delta.DeletedMessageIds)
        {
            MessageItem? gone = Messages.FirstOrDefault(m => m.Id == id);
            if (gone is not null)
                Messages.Remove(gone);
        }
        if (delta.DeletedMessageIds.Count > 0)
        {
            UpdateRuns();
            IsEmpty = Messages.Count == 0;
        }

        if (delta.NewMessageIds.Count > 0)
        {
            Dictionary<string, MessageViewBase> created = new(StringComparer.Ordinal);
            foreach (CreateMessage entry in log.OfType<CreateMessage>())
            {
                if (entry.ConversationId == conversation.Id && ChatMapper.MessageIdOf(entry.Message) is { } id)
                    created.TryAdd(id, entry.Message);
            }

            List<MessageItem> fresh = Map(delta.NewMessageIds.Where(created.ContainsKey).Select(id => created[id]));
            if (fresh.Count > 0)
                await AppendFreshAsync(fresh);
        }

        if (delta.ReactionsChanged)
            await PollMessagesAsync(refreshReactions: true);
    }

    /// <summary>A member, the name, the lock or the join link of the open group changed: pick up the new name, size and lock state.</summary>
    private async Task RefreshGroupAsync(ConversationItem conversation)
    {
        GroupDetail? detail = await GroupChatService.GetDetailAsync(conversation.Id);
        if (detail is null)
            return;

        conversation.Title = detail.Conversation.Title;
        conversation.Subtitle = detail.Conversation.Subtitle;
        conversation.MemberCount = detail.Conversation.MemberCount;
        if (conversation.IsLocked != detail.Conversation.IsLocked)
        {
            conversation.IsLocked = detail.Conversation.IsLocked;
            OnPropertyChanged(nameof(CanType));
            OnPropertyChanged(nameof(CanSend));
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Reads the latest page of messages: anything new is added, and reactions on known ones can be refreshed.</summary>
    private async Task PollMessagesAsync(bool refreshReactions)
    {
        AtProtoHttpResult<ChatMessages> result = await BlueskySessionService.Instance.Agent.GetMessages(Conversation!.Id, limit: PollSize);
        if (!result.Succeeded || result.Result is null)
            return;

        List<MessageItem> mapped = Map(result.Result);

        if (refreshReactions)
        {
            foreach (MessageItem latest in mapped)
            {
                MessageItem? shown = Messages.FirstOrDefault(m => m.Id == latest.Id);
                if (shown is not null)
                    shown.ReplaceReactions(latest.Reactions);
            }
        }

        List<MessageItem> fresh = mapped.Where(m => !_known.Contains(m.Id)).ToList();
        if (fresh.Count > 0)
            await AppendFreshAsync(fresh, newestFirst: true);
    }

    private async Task AppendFreshAsync(List<MessageItem> fresh, bool newestFirst = false)
    {
        Append(fresh, newestFirst);
        IsEmpty = false;
        MessagesAppended?.Invoke(this, false);

        MessageItem newest = Messages[^1];
        string text = newest.Text.ReplaceLineEndings(" ");
        Conversation!.LastMessagePreview = newest.IsMine ? "You: " + text : text;
        Conversation.LastMessageAt = newest.SentAt;

        // New messages from them are unread on the server until we say otherwise.
        if (fresh.Any(m => !m.IsMine))
            await MarkReadAsync(evenIfReadLocally: true);
    }

    /// <summary>Hides a message for this account only; the other side keeps it. Gone at once, put back if the server refuses.</summary>
    public async Task DeleteForMeAsync(MessageItem message)
    {
        if (Conversation is null)
            return;

        int index = Messages.IndexOf(message);
        if (index < 0)
            return;

        Messages.RemoveAt(index);
        UpdateRuns();
        IsEmpty = Messages.Count == 0;
        if (PreviewMode.IsEnabled)
            return;

        void Restore(string error)
        {
            Messages.Insert(Math.Min(index, Messages.Count), message);
            UpdateRuns();
            IsEmpty = false;
            Error = error;
        }

        try
        {
            if (!await BlueskySessionService.Instance.EnsureAuthenticatedAsync())
            {
                Restore(BlueskySessionService.Instance.AuthenticationError);
                return;
            }
            var result = await BlueskySessionService.Instance.Agent.DeleteMessageForSelf(Conversation.Id, message.Id);
            if (!result.Succeeded)
            {
                Restore(result.StatusCode == 0 ? "Couldn't reach Bluesky. The message wasn't deleted." : "The message wasn't deleted.");
                LogService.Warn("Chat", $"DeleteMessageForSelf failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
        }
        catch (Exception ex)
        {
            Restore("The message wasn't deleted.");
            LogService.Warn("Chat", $"DeleteMessageForSelf threw: {ex.Message}");
        }
    }

    /// <summary>Mutes or unmutes the conversation. Shows at once, undone if the server refuses.</summary>
    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        if (Conversation is null)
            return;

        ConversationItem conversation = Conversation;
        bool mute = !conversation.IsMuted;
        conversation.IsMuted = mute;
        if (PreviewMode.IsEnabled)
            return;

        try
        {
            if (!await BlueskySessionService.Instance.EnsureAuthenticatedAsync())
            {
                conversation.IsMuted = !mute;
                Error = BlueskySessionService.Instance.AuthenticationError;
                return;
            }
            bool ok;
            System.Net.HttpStatusCode status;
            AtErrorDetail? detail;
            if (mute)
            {
                var result = await BlueskySessionService.Instance.Agent.MuteConversation(conversation.Id);
                (ok, status, detail) = (result.Succeeded, result.StatusCode, result.AtErrorDetail);
            }
            else
            {
                var result = await BlueskySessionService.Instance.Agent.UnmuteConversation(conversation.Id);
                (ok, status, detail) = (result.Succeeded, result.StatusCode, result.AtErrorDetail);
            }

            if (!ok)
            {
                conversation.IsMuted = !mute;
                Error = status == 0 ? "Couldn't reach Bluesky. The change wasn't saved." : "The change wasn't saved.";
                LogService.Warn("Chat", $"{(mute ? "MuteConversation" : "UnmuteConversation")} failed: {(int)status} {detail?.Error}");
            }
            else
            {
                ChatService.Instance.MuteChanged(conversation);
            }
        }
        catch (Exception ex)
        {
            conversation.IsMuted = !mute;
            Error = "The change wasn't saved.";
            LogService.Warn("Chat", $"Muting threw: {ex.Message}");
        }
    }

    /// <summary>Leaves (or, for a request, declines) the conversation and drops it from the list.</summary>
    /// <summary>Asks whether to leave (or delete) this conversation, worded for its kind, then does it.</summary>
    [RelayCommand]
    private async Task ConfirmLeave()
    {
        if (Conversation is not { } conversation)
            return;

        ChatConversationMenu menu = ChatConversationMenuPolicy.For(conversation.IsRequest, conversation.IsMuted, conversation.IsGroup);
        if (await Dialogs.ConfirmAsync(menu.LeaveLabel + "?", menu.LeavePrompt, menu.LeaveButton))
            await LeaveAsync();
    }

    public async Task LeaveAsync()
    {
        if (Conversation is null)
            return;

        ConversationItem conversation = Conversation;
        if (!PreviewMode.IsEnabled)
        {
            try
            {
                if (!await BlueskySessionService.Instance.EnsureAuthenticatedAsync())
                {
                    Error = BlueskySessionService.Instance.AuthenticationError;
                    return;
                }
                var result = await BlueskySessionService.Instance.Agent.LeaveConversation(conversation.Id);
                if (!result.Succeeded)
                {
                    Error = result.StatusCode == 0 ? "Couldn't reach Bluesky. You're still in this conversation." : "You're still in this conversation.";
                    LogService.Warn("Chat", $"LeaveConversation failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Error = "You're still in this conversation.";
                LogService.Warn("Chat", $"LeaveConversation threw: {ex.Message}");
                return;
            }
        }

        MessagesViewModel.Instance.Remove(conversation.Id);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (Conversation is null)
            return;

        string text = Draft.Trim();
        IsSending = true;
        Error = null;

        try
        {
            MessageItem? sent;
            if (PreviewMode.IsEnabled)
            {
                sent = new MessageItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SenderDid = BlueskySessionService.Instance.Did ?? PreviewMode.SelfDid,
                    IsMine = true,
                    Text = text,
                    Segments = FacetSegmenter.Segment(text, null),
                    SentAt = DateTimeOffset.UtcNow
                };
            }
            else
            {
                if (!await BlueskySessionService.Instance.EnsureAuthenticatedAsync())
                {
                    Error = BlueskySessionService.Instance.AuthenticationError;
                    return;
                }

                AtProtoHttpResult<MessageView> result = await BlueskySessionService.Instance.Agent.SendMessage(Conversation.Id, text, extractFacets: true);
                if (!result.Succeeded || result.Result is null)
                {
                    Error = result.StatusCode == 0
                        ? "Couldn't reach Bluesky. Your message wasn't sent."
                        : result.AtErrorDetail?.Message is { Length: > 0 } message ? message : "Your message wasn't sent.";
                    LogService.Warn("Chat", $"SendMessage failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    return;
                }

                sent = ChatMapper.ToMessageItem(result.Result, BlueskySessionService.Instance.Did, Conversation.Members, Conversation.IsGroup);
            }

            Draft = string.Empty;

            if (sent is not null && _known.Add(sent.Id))
            {
                Messages.Add(sent);
                UpdateRuns();
                IsEmpty = false;
                MessagesAppended?.Invoke(this, true);
            }

            // Replying accepts a request, same as the official app.
            Conversation.IsRequest = false;
            Conversation.LastMessagePreview = "You: " + text.ReplaceLineEndings(" ");
            Conversation.LastMessageAt = sent?.SentAt ?? DateTimeOffset.UtcNow;
            MessagesViewModel.Instance.BringToTop(Conversation);
        }
        catch (Exception ex)
        {
            LogService.Error("Chat", "SendMessage threw", ex);
            Error = "Your message wasn't sent.";
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>Adds my <paramref name="emoji"/> to a message, or takes it back if it's already there. Shows at once, undone if the server refuses.</summary>
    public async Task ToggleReactionAsync(MessageItem message, string emoji)
    {
        if (Conversation is null || message.IsDeleted)
            return;

        string self = BlueskySessionService.Instance.Did ?? PreviewMode.SelfDid;
        bool remove = message.HasReacted(self, emoji);

        void Apply(bool removing)
        {
            if (removing)
                message.RemoveReaction(self, emoji);
            else
                message.AddReaction(self, emoji);
        }

        Apply(remove);
        if (PreviewMode.IsEnabled)
            return;

        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
            {
                Apply(!remove);
                Error = session.AuthenticationError;
                return;
            }

            bool succeeded;
            System.Net.HttpStatusCode status;
            AtErrorDetail? detail;
            if (remove)
            {
                var result = await session.Agent.RemoveReaction(Conversation.Id, message.Id, emoji);
                (succeeded, status, detail) = (result.Succeeded, result.StatusCode, result.AtErrorDetail);
            }
            else
            {
                var result = await session.Agent.AddReaction(Conversation.Id, message.Id, emoji);
                (succeeded, status, detail) = (result.Succeeded, result.StatusCode, result.AtErrorDetail);
            }

            if (!succeeded)
            {
                Apply(!remove);
                Error = status == 0
                    ? "Couldn't reach Bluesky. Your reaction wasn't saved."
                    : detail?.Message is { Length: > 0 } text ? text : "Your reaction wasn't saved.";
                LogService.Warn("Chat", $"{(remove ? "RemoveReaction" : "AddReaction")} failed: {(int)status} {detail?.Error}");
            }
        }
        catch (Exception ex)
        {
            Apply(!remove);
            Error = "Your reaction wasn't saved.";
            LogService.Warn("Chat", $"Reacting threw: {ex.Message}");
        }
    }

    /// <summary>Moves a request into the inbox without replying.</summary>
    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (Conversation is null)
            return;

        if (PreviewMode.IsEnabled)
        {
            Conversation.IsRequest = false;
            return;
        }

        try
        {
            var result = await BlueskySessionService.Instance.Agent.AcceptConversation(Conversation.Id);
            if (result.Succeeded)
                Conversation.IsRequest = false;
            else
                LogService.Warn("Chat", $"AcceptConversation failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"AcceptConversation threw: {ex.Message}");
        }
    }

    private async Task MarkReadAsync(bool evenIfReadLocally = false)
    {
        if (Conversation is null || (!Conversation.IsUnread && !evenIfReadLocally))
            return;

        if (Conversation.IsUnread)
        {
            ChatService.Instance.ConversationRead(Conversation);
            Conversation.UnreadCount = 0;
        }

        if (PreviewMode.IsEnabled)
            return;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.UpdateRead(Conversation.Id);
            if (!result.Succeeded)
                LogService.Warn("Chat", $"UpdateRead failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"UpdateRead threw: {ex.Message}");
        }
    }

    private List<MessageItem> Map(IEnumerable<MessageViewBase> views)
    {
        string? self = BlueskySessionService.Instance.Did;
        ConversationItem conversation = Conversation!;
        List<MessageItem> items = [];
        foreach (MessageViewBase view in views)
        {
            if (ChatMapper.ToMessageItem(view, self, conversation.Members, conversation.IsGroup) is MessageItem item)
                items.Add(item);
        }
        return items;
    }

    private void Append(IEnumerable<MessageItem> messages, bool newestFirst)
    {
        IEnumerable<MessageItem> ordered = newestFirst ? messages.Reverse() : messages;
        foreach (MessageItem message in ordered)
        {
            if (_known.Add(message.Id))
                Messages.Add(message);
        }
        UpdateRuns();
    }

    private void UpdateRuns()
    {
        MessageItem? previous = null;
        foreach (MessageItem message in Messages)
        {
            bool starts = ChatThreadPolicy.StartsRun(previous?.SenderDid, previous?.SentAt, message.SenderDid, message.SentAt);
            if (message.StartsRun != starts)
                message.StartsRun = starts;
            previous = message;
        }
    }
}
