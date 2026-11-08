using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky.Graph;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// One list: its details and members, paged from <c>GetList</c>. The creator can edit or delete it,
/// add people (found with the account typeahead) and remove them; removal leaves the row at once
/// and puts it back if the server refuses.
/// </summary>
public sealed partial class ListViewModel : DialogViewModel
{
    private const int PageSize = 50;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ListItem? _list;
    private string? _cursor;
    private CancellationTokenSource? _typeahead;

    public ObservableCollection<ActorItem> Members { get; } = [];

    /// <summary>Typeahead matches for the add-member box.</summary>
    public ObservableCollection<ActorItem> Suggestions { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string Description { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; private set; }

    /// <summary>The signed-in account made this list: edit, delete, add and remove are offered.</summary>
    [ObservableProperty]
    public partial bool CanManage { get; private set; }

    /// <summary>Set after a delete so the page can leave.</summary>
    [ObservableProperty]
    public partial bool IsDeleted { get; private set; }

    /// <summary>A curation or moderation list: the mute button is offered, whoever made it.</summary>
    [ObservableProperty]
    public partial bool CanMute { get; private set; }

    /// <summary>A moderation list: the block button is offered too.</summary>
    [ObservableProperty]
    public partial bool CanBlock { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteLabel))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatus))]
    [NotifyPropertyChangedFor(nameof(HasSubscriptionStatus))]
    public partial bool IsMuted { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlockLabel))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatus))]
    [NotifyPropertyChangedFor(nameof(HasSubscriptionStatus))]
    public partial bool IsBlocked { get; private set; }

    /// <summary>A mute / block call is in flight; both buttons wait.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleSubscription))]
    public partial bool IsSubscriptionBusy { get; private set; }

    public bool CanToggleSubscription => !IsSubscriptionBusy;

    public string MuteLabel => MuteTargetPolicy.MuteLabel(_list?.Kind ?? ListKind.Moderation, IsMuted);

    public string BlockLabel => ListPolicy.BlockLabel(IsBlocked);

    public string SubscriptionStatus => ListPolicy.SubscriptionStatus(IsMuted, IsBlocked);

    public bool HasSubscriptionStatus => SubscriptionStatus.Length > 0;

    public bool HasDescription => Description.Length > 0;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsEmpty => HasLoaded && !IsLoading && Members.Count == 0 && !HasError;

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    /// <summary>The list as last loaded or edited (the dialog starts from it).</summary>
    public ListItem? CurrentList => _list;

    public string EmptyText => ListPolicy.EmptyMembersText(CanManage);

    public async Task LoadAsync(ListItem list)
    {
        _list = list;
        ShowHeader(list);
        CanManage = list.IsMine;
        CanMute = MuteTargetPolicy.CanMute(list.Kind);
        CanBlock = MuteTargetPolicy.CanBlock(list.Kind);
        IsMuted = list.IsMuted;
        IsBlocked = list.IsBlocked;
        OnPropertyChanged(nameof(MuteLabel));
        OnPropertyChanged(nameof(EmptyText));
        Members.Clear();
        Suggestions.Clear();
        _cursor = null;
        Error = null;
        HasLoaded = false;

        if (PreviewMode.IsEnabled)
        {
            foreach (ActorItem actor in PreviewMode.SampleSearchActors())
                Members.Add(WithRemove(actor, "at://preview/app.bsky.graph.listitem/" + actor.Did));
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
        {
            HasLoaded = true;
            return;
        }

        await LoadPageAsync(reset: true);
    }

    public Task LoadMoreAsync() => HasMore ? LoadPageAsync(reset: false) : Task.CompletedTask;

    private void ShowHeader(ListItem list)
    {
        Name = list.Name;
        Description = list.Description;
        Subtitle = list.Subtitle;
    }

    private async Task LoadPageAsync(bool reset)
    {
        if (_list is null || !await _gate.WaitAsync(0))
            return;

        IsLoading = true;
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            var result = await BlueskySessionService.Instance.Agent.GetList(
                new AtUri(_list.Uri),
                limit: PageSize,
                cursor: reset ? null : _cursor,
                subscribedLabelers: LabelService.Instance.Subscribed);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("List", $"GetList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                if (reset)
                {
                    Error = result.StatusCode == 0
                        ? "Couldn't reach Bluesky. Check your connection."
                        : "Couldn't load this list.";
                }
                return;
            }

            // The list may have been muted or blocked elsewhere since the row that opened this page was read.
            if (reset && result.Result.List.Viewer is { } viewer && !IsSubscriptionBusy)
            {
                IsMuted = viewer.Muted == true;
                IsBlocked = viewer.Blocked is not null;
                _list = Copy(_list, _list.Name, _list.Description, _list.MemberCount);
            }

            foreach (ListItemView item in result.Result)
                Members.Add(WithRemove(FeedMapper.ToActorItem(item.Subject), item.Uri.ToString()));

            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("List", $"GetList threw: {ex.Message}");
            if (reset)
                Error = "Couldn't load this list.";
        }
        finally
        {
            IsLoading = false;
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            _gate.Release();
        }
    }

    private ActorItem WithRemove(ActorItem actor, string listItemUri) => new()
    {
        Did = actor.Did,
        Handle = actor.Handle,
        DisplayName = actor.DisplayName,
        AvatarUri = actor.AvatarUri,
        ListItemUri = listItemUri,
        ManagedActionLabel = CanManage ? "Remove" : string.Empty
    };

    /// <summary>Takes <paramref name="member"/> off the list. The row leaves at once and returns if the server refuses.</summary>
    public async Task RemoveAsync(ActorItem member)
    {
        int index = Members.IndexOf(member);
        if (index < 0 || !CanManage || member.ListItemUri is null)
            return;

        Error = null;
        Members.RemoveAt(index);
        OnPropertyChanged(nameof(IsEmpty));

        bool ok = PreviewMode.IsEnabled || await ListsService.RemoveMemberAsync(member.ListItemUri);
        if (ok)
        {
            AdjustCount(-1);
            return;
        }

        Members.Insert(ManagedAccountsPolicy.RestoreIndex(index, Members.Count), member);
        Error = $"Couldn't remove @{member.Handle}. Try again.";
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Puts <paramref name="actor"/> on the list and shows them at the top. False when they are already on it or the server refused.</summary>
    public async Task<bool> AddAsync(ActorItem actor)
    {
        if (_list is null || !CanManage)
            return false;

        foreach (ActorItem existing in Members)
        {
            if (string.Equals(existing.Did, actor.Did, StringComparison.Ordinal))
            {
                Error = $"@{actor.Handle} is already on this list.";
                return false;
            }
        }

        Error = null;
        string? uri = PreviewMode.IsEnabled ? "at://preview/app.bsky.graph.listitem/" + actor.Did : await ListsService.AddMemberAsync(_list.Uri, actor.Did);
        if (uri is null)
        {
            Error = $"Couldn't add @{actor.Handle}. Try again.";
            return false;
        }

        Members.Insert(0, WithRemove(actor, uri));
        AdjustCount(+1);
        OnPropertyChanged(nameof(IsEmpty));
        return true;
    }

    private void AdjustCount(int delta)
    {
        if (_list is null)
            return;

        _list = Copy(_list, _list.Name, _list.Description, Math.Max(0, _list.MemberCount + delta));
        Subtitle = _list.Subtitle;
    }

    private ListItem Copy(ListItem list, string name, string description, int memberCount) => new()
    {
        Uri = list.Uri,
        Name = name,
        Description = description,
        Kind = list.Kind,
        AvatarUri = list.AvatarUri,
        MemberCount = memberCount,
        CreatorDid = list.CreatorDid,
        CreatorHandle = list.CreatorHandle,
        IsMuted = IsMuted,
        IsBlocked = IsBlocked
    };

    /// <summary>Mutes or unmutes everyone on the list. Shows the new state at once and puts it back if the server refuses.</summary>
    [RelayCommand]
    public Task ToggleMutedAsync() => SetSubscriptionAsync(block: false);

    /// <summary>Blocks everyone on the list after asking (undoing it needs no confirmation).</summary>
    [RelayCommand]
    private async Task BlockList()
    {
        // Blocking sweeps in a whole list of people, so ask first.
        if (!IsBlocked && !await Dialogs.ConfirmBlockListAsync(Name))
            return;

        await ToggleBlockedAsync();
    }

    [RelayCommand]
    private async Task EditList()
    {
        if (CurrentList is not { } list)
            return;

        if (await Dialogs.AskListAsync(list) is not { } edit)
            return;

        if (!await EditAsync(edit.Name, edit.Description))
            await Dialogs.ShowMessageAsync("Couldn't save the list", "That didn't go through. Try again in a moment.");
    }

    [RelayCommand]
    private async Task DeleteList()
    {
        if (!await Dialogs.ConfirmDeleteListAsync(Name))
            return;

        if (!await DeleteAsync())
            await Dialogs.ShowMessageAsync("Couldn't delete the list", "That didn't go through. Try again in a moment.");
    }

    /// <summary>Blocks or unblocks everyone on the list. Shows the new state at once and puts it back if the server refuses.</summary>
    public Task ToggleBlockedAsync() => SetSubscriptionAsync(block: true);

    private async Task SetSubscriptionAsync(bool block)
    {
        if (_list is null || !(block ? CanBlock : CanMute) || IsSubscriptionBusy)
            return;

        bool turnOn = !(block ? IsBlocked : IsMuted);
        IsSubscriptionBusy = true;
        Error = null;
        SetState(block, turnOn);

        bool ok = PreviewMode.IsEnabled
            || await (block ? ListsService.SetListBlockedAsync(_list.Uri, turnOn) : ListsService.SetListMutedAsync(_list.Uri, turnOn, _list.Kind));
        if (ok)
            _list = Copy(_list, _list.Name, _list.Description, _list.MemberCount);
        else
        {
            SetState(block, !turnOn);
            Error = ListPolicy.SubscriptionFailureText(block, turnOn);
        }

        IsSubscriptionBusy = false;
    }

    private void SetState(bool block, bool value)
    {
        if (block)
            IsBlocked = value;
        else
            IsMuted = value;
    }

    /// <summary>Saves a new name / description. False when the server refused.</summary>
    public async Task<bool> EditAsync(string name, string description)
    {
        if (_list is null || !CanManage)
            return false;

        if (!PreviewMode.IsEnabled && !await ListsService.UpdateAsync(_list.Uri, name, description))
            return false;

        _list = Copy(_list, name, description, _list.MemberCount);
        ShowHeader(_list);
        return true;
    }

    /// <summary>Deletes the list. False when the server refused.</summary>
    public async Task<bool> DeleteAsync()
    {
        if (_list is null || !CanManage)
            return false;

        if (!PreviewMode.IsEnabled && !await ListsService.DeleteAsync(_list.Uri))
            return false;

        IsDeleted = true;
        return true;
    }

    /// <summary>Refreshes the add-member dropdown after a short pause in typing.</summary>
    public async Task UpdateSuggestionsAsync(string? text)
    {
        _typeahead?.Cancel();
        Suggestions.Clear();

        string query = text?.Trim() ?? string.Empty;
        if (query.Length < 2 || !CanManage)
            return;

        if (PreviewMode.IsEnabled)
        {
            foreach (ActorItem actor in PreviewMode.SampleSearchActors())
                Suggestions.Add(actor);
            return;
        }

        var cts = _typeahead = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            var result = await BlueskySessionService.Instance.Agent.SearchActorsTypeahead(
                query,
                limit: 6,
                subscribedLabelers: LabelService.Instance.Subscribed,
                cancellationToken: cts.Token);

            if (cts.IsCancellationRequested || !result.Succeeded || result.Result is null)
                return;

            foreach (var profile in result.Result)
                Suggestions.Add(FeedMapper.ToActorItem(profile));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogService.Warn("List", $"Typeahead threw: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task Retry() => _list is null ? Task.CompletedTask : LoadAsync(_list);

    [RelayCommand]
    private Task OpenInBrowser() => _list is null || _list.WebUrl.Length == 0
        ? Task.CompletedTask
        : RichTextBuilder.OpenAsync(_list.WebUrl);
}
