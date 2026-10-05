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
public sealed partial class ConversationViewModel : ObservableObject
{
    private const int PageSize = 40;
    private const int PollSize = 20;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private string? _olderCursor;

    public ObservableCollection<MessageItem> Messages { get; } = [];

    [ObservableProperty]
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

    public bool CanSend => !IsSending && ChatThreadPolicy.CanSend(Draft);

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
            await session.EnsureAuthenticatedAsync();

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

            AtProtoHttpResult<ChatMessages> result = await BlueskySessionService.Instance.Agent.GetMessages(Conversation.Id, limit: PollSize);
            if (!result.Succeeded || result.Result is null)
                return;

            List<MessageItem> fresh = Map(result.Result).Where(m => !_known.Contains(m.Id)).ToList();
            if (fresh.Count == 0)
                return;

            Append(fresh, newestFirst: true);
            IsEmpty = false;
            MessagesAppended?.Invoke(this, false);

            MessageItem newest = Messages[^1];
            string text = newest.Text.ReplaceLineEndings(" ");
            Conversation.LastMessagePreview = newest.IsMine ? "You: " + text : text;
            Conversation.LastMessageAt = newest.SentAt;

            // New messages from them are unread on the server until we say otherwise.
            if (fresh.Any(m => !m.IsMine))
                await MarkReadAsync(evenIfReadLocally: true);
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
                await BlueskySessionService.Instance.EnsureAuthenticatedAsync();

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
            await session.EnsureAuthenticatedAsync();

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
