using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// One group's details: its members (with remove for the owner), the owner's rename / add-people
/// / join link / join request controls, and the group's size and lock state. Everyone in the
/// group sees the member list; the rest is for the owner (see <see cref="GroupChatPolicy.PermissionsFor"/>).
/// One instance per <see cref="Pages.GroupInfoPage"/>.
/// </summary>
public sealed partial class GroupInfoViewModel : DialogViewModel
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConversationItem? _conversation;
    private string? _cursor;
    private bool _syncing;

    public ObservableCollection<ActorItem> Members { get; } = [];

    /// <summary>People waiting for the owner to let them in through the join link.</summary>
    public ObservableCollection<ActorItem> Requests { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRename), nameof(CanAddMembers), nameof(CanManageLink))]
    public partial bool IsOwner { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddMembers))]
    public partial bool IsLocked { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    /// <summary>The group has a join link (on or off).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLinkCode))]
    public partial bool HasLink { get; private set; }

    /// <summary>Two-way bound to the link's switch; changing it from the page calls the server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLinkCode))]
    public partial bool LinkEnabled { get; set; }

    [ObservableProperty]
    public partial bool LinkRequireApproval { get; set; } = true;

    [ObservableProperty]
    public partial bool LinkFollowedOnly { get; set; }

    [ObservableProperty]
    public partial string LinkCode { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LinkSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRequests { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public bool CanRename => GroupChatPolicy.PermissionsFor(IsOwner, IsLocked).CanRename;

    public bool CanAddMembers => GroupChatPolicy.PermissionsFor(IsOwner, IsLocked).CanAddMembers;

    public bool CanManageLink => GroupChatPolicy.PermissionsFor(IsOwner, IsLocked).CanManageLink;

    public bool ShowLinkCode => HasLink && LinkEnabled;

    /// <summary>The ids of everyone loaded so far, so the add dialog can skip people who are already in.</summary>
    public IReadOnlySet<string> MemberDids => Members.Select(m => m.Did).ToHashSet(StringComparer.Ordinal);

    public long MemberCount => _conversation?.MemberCount ?? Members.Count;

    public long MemberLimit => _conversation?.MemberLimit ?? 0;

    public string? ConversationId => _conversation?.Id;

    // ---- Loading -------------------------------------------------------------------------

    public async Task LoadAsync(ConversationItem conversation)
    {
        _conversation = conversation;
        Show(conversation);
        IsLoading = true;
        Error = null;

        try
        {
            if (PreviewMode.IsEnabled)
            {
                Members.Clear();
                foreach (GroupMember member in PreviewMode.SampleGroupMembers())
                    Members.Add(Row(member));
                ApplyLink(new GroupJoinLink("preview-code", true, true, false));
                Requests.Clear();
                foreach (ActorItem actor in PreviewMode.SampleSearchActors().Take(1))
                    Requests.Add(actor);
                HasRequests = Requests.Count > 0;
                return;
            }

            Task<GroupDetail?> detailTask = GroupChatService.GetDetailAsync(conversation.Id);
            GroupMemberPage? page = await GroupChatService.GetMembersAsync(conversation.Id, null);
            GroupDetail? detail = await detailTask;

            if (detail is not null)
            {
                Show(detail.Conversation);
                CopyOnto(conversation, detail.Conversation);
                ApplyLink(detail.Link);
            }

            if (page is null)
            {
                Error = "Couldn't load the members of this group.";
                return;
            }

            Members.Clear();
            foreach (GroupMember member in page.Members)
                Members.Add(Row(member));
            _cursor = page.Cursor;

            if (IsOwner && HasLink)
                await LoadRequestsAsync();
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"Loading group info threw: {ex.Message}");
            Error = "Couldn't load this group.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMoreAsync()
    {
        if (_conversation is null || !HasMore || PreviewMode.IsEnabled || !await _gate.WaitAsync(0))
            return;

        try
        {
            GroupMemberPage? page = await GroupChatService.GetMembersAsync(_conversation.Id, _cursor);
            if (page is null)
                return;

            HashSet<string> known = MemberDids.ToHashSet(StringComparer.Ordinal);
            foreach (GroupMember member in page.Members)
            {
                if (known.Add(member.Actor.Did))
                    Members.Add(Row(member));
            }
            _cursor = page.Cursor;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadRequestsAsync()
    {
        if (_conversation is null)
            return;

        List<ActorItem>? people = await GroupChatService.GetJoinRequestsAsync(_conversation.Id);
        if (people is null)
            return;

        Requests.Clear();
        foreach (ActorItem person in people)
            Requests.Add(person);
        HasRequests = Requests.Count > 0;
    }

    private void Show(ConversationItem conversation)
    {
        Name = conversation.Title;
        Subtitle = conversation.Subtitle;
        IsOwner = conversation.IsOwner;
        IsLocked = conversation.IsLocked;
        OnPropertyChanged(nameof(MemberCount));
        OnPropertyChanged(nameof(MemberLimit));
    }

    /// <summary>The conversation page and Messages list hold their own copy of the group; keep their name and size current.</summary>
    private static void CopyOnto(ConversationItem target, ConversationItem fresh)
    {
        target.Title = fresh.Title;
        target.Subtitle = fresh.Subtitle;
        target.MemberCount = fresh.MemberCount;
    }

    private ActorItem Row(GroupMember member)
    {
        string self = BlueskySessionService.Instance.Did ?? PreviewMode.SelfDid;
        bool removable = IsOwner && !member.IsOwner && !string.Equals(member.Actor.Did, self, StringComparison.Ordinal);
        return new ActorItem
        {
            Did = member.Actor.Did,
            Handle = member.Actor.Handle,
            DisplayName = member.Actor.DisplayName,
            AvatarUri = member.Actor.AvatarUri,
            ManagedActionLabel = removable ? "Remove" : string.Empty,
            ManagedDetail = member.IsOwner ? "Owner" : string.Empty
        };
    }

    private void ApplyLink(GroupJoinLink? link)
    {
        _syncing = true;
        try
        {
            HasLink = link is not null;
            LinkEnabled = link?.Enabled == true;
            LinkRequireApproval = link?.RequireApproval ?? LinkRequireApproval;
            LinkFollowedOnly = link?.FollowedByOwnerOnly ?? LinkFollowedOnly;
            LinkCode = link?.Code ?? string.Empty;
            LinkSummary = link is null
                ? "No join link yet. Turn it on to let people join with a link."
                : GroupChatPolicy.JoinLinkSummary(link.Enabled, link.RequireApproval, link.FollowedByOwnerOnly);
            OnPropertyChanged(nameof(ShowLinkCode));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SetCount(long count)
    {
        if (_conversation is null)
            return;

        _conversation.MemberCount = Math.Max(0, count);
        _conversation.Subtitle = GroupChatPolicy.MemberCountText(_conversation.MemberCount, _conversation.MemberLimit);
        Subtitle = _conversation.Subtitle;
        OnPropertyChanged(nameof(MemberCount));
    }

    // ---- Owner actions -------------------------------------------------------------------

    /// <summary>Renames the group. False when the server refused (the reason is in <see cref="Error"/>).</summary>
    public async Task<bool> RenameAsync(string name)
    {
        if (_conversation is null || !CanRename)
            return false;

        Error = null;
        string? error = PreviewMode.IsEnabled ? null : await GroupChatService.RenameAsync(_conversation.Id, name);
        if (error is not null)
        {
            Error = error;
            return false;
        }

        Name = name;
        _conversation.Title = name;
        return true;
    }

    /// <summary>Adds people and re-reads the member list. False when the server refused.</summary>
    public async Task<bool> AddAsync(IReadOnlyList<ActorItem> people)
    {
        if (_conversation is null || !CanAddMembers || people.Count == 0)
            return false;

        Error = null;
        if (PreviewMode.IsEnabled)
        {
            foreach (ActorItem person in people)
                Members.Add(Row(new GroupMember(person, false)));
            SetCount(MemberCount + people.Count);
            return true;
        }

        string? error = await GroupChatService.AddMembersAsync(_conversation.Id, people.Select(p => p.Did).ToList());
        if (error is not null)
        {
            Error = error;
            return false;
        }

        await LoadAsync(_conversation);
        return true;
    }

    /// <summary>Takes someone out of the group. Gone at once, put back if the server refuses.</summary>
    public async Task RemoveAsync(ActorItem member)
    {
        if (_conversation is null || !IsOwner)
            return;

        int index = Members.IndexOf(member);
        if (index < 0)
            return;

        Error = null;
        Members.RemoveAt(index);
        SetCount(MemberCount - 1);
        if (PreviewMode.IsEnabled)
            return;

        string? error = await GroupChatService.RemoveMemberAsync(_conversation.Id, member.Did);
        if (error is not null)
        {
            Members.Insert(Math.Min(index, Members.Count), member);
            SetCount(MemberCount + 1);
            Error = error;
        }
    }

    [RelayCommand]
    private async Task RenameGroup()
    {
        if (await Dialogs.AskGroupNameAsync(Name) is { } name)
            await RenameAsync(name);
    }

    [RelayCommand]
    private async Task AddPeople()
    {
        int room = GroupChatPolicy.RoomLeft((int)MemberCount, (int)MemberLimit);
        if (room == 0 && MemberLimit > 0)
        {
            await Dialogs.ShowMessageAsync("This group is full", $"A group holds at most {MemberLimit} people.");
            return;
        }

        List<ActorItem>? people = await Dialogs.AskGroupMembersAsync(room == 0 ? int.MaxValue : room, MemberDids);
        if (people is not null)
            await AddAsync(people);
    }

    /// <summary>Asks, then takes the member out of the group.</summary>
    [RelayCommand]
    private async Task RemoveMember(ActorItem? member)
    {
        if (member is null)
            return;

        if (await Dialogs.ConfirmAsync($"Remove {member.NameOrHandle}?", "They are taken out of the group and can't read new messages.", "Remove"))
            await RemoveAsync(member);
    }

    partial void OnLinkEnabledChanged(bool value)
    {
        if (!_syncing)
            _ = ToggleLinkAsync(value);
    }

    partial void OnLinkRequireApprovalChanged(bool value)
    {
        if (!_syncing && HasLink)
            _ = EditLinkAsync();
    }

    partial void OnLinkFollowedOnlyChanged(bool value)
    {
        if (!_syncing && HasLink)
            _ = EditLinkAsync();
    }

    private async Task ToggleLinkAsync(bool on)
    {
        if (_conversation is null || !CanManageLink)
            return;

        Error = null;
        if (PreviewMode.IsEnabled)
        {
            ApplyLink(new GroupJoinLink("preview-code", on, LinkRequireApproval, LinkFollowedOnly));
            return;
        }

        string? error = HasLink
            ? await GroupChatService.SetLinkEnabledAsync(_conversation.Id, on)
            : on ? await GroupChatService.CreateLinkAsync(_conversation.Id, LinkRequireApproval, LinkFollowedOnly) : null;

        if (error is not null)
        {
            Error = error;
            _syncing = true;
            LinkEnabled = !on;
            _syncing = false;
            OnPropertyChanged(nameof(ShowLinkCode));
            return;
        }

        await RefreshLinkAsync();
    }

    private async Task EditLinkAsync()
    {
        if (_conversation is null || !CanManageLink)
            return;

        Error = null;
        if (PreviewMode.IsEnabled)
        {
            ApplyLink(new GroupJoinLink(LinkCode, LinkEnabled, LinkRequireApproval, LinkFollowedOnly));
            return;
        }

        string? error = await GroupChatService.EditLinkAsync(_conversation.Id, LinkRequireApproval, LinkFollowedOnly);
        if (error is not null)
            Error = error;

        await RefreshLinkAsync();
    }

    private async Task RefreshLinkAsync()
    {
        if (_conversation is null)
            return;

        GroupDetail? detail = await GroupChatService.GetDetailAsync(_conversation.Id);
        if (detail is not null)
            ApplyLink(detail.Link);
    }

    /// <summary>Lets a waiting person in. Gone from the list at once, put back if the server refuses.</summary>
    public Task ApproveAsync(ActorItem person) => ResolveRequestAsync(person, approve: true);

    public Task RejectAsync(ActorItem person) => ResolveRequestAsync(person, approve: false);

    private async Task ResolveRequestAsync(ActorItem person, bool approve)
    {
        if (_conversation is null || !IsOwner)
            return;

        int index = Requests.IndexOf(person);
        if (index < 0)
            return;

        Error = null;
        Requests.RemoveAt(index);
        HasRequests = Requests.Count > 0;
        if (PreviewMode.IsEnabled)
            return;

        string? error = approve
            ? await GroupChatService.ApproveRequestAsync(_conversation.Id, person.Did)
            : await GroupChatService.RejectRequestAsync(_conversation.Id, person.Did);

        if (error is not null)
        {
            Requests.Insert(Math.Min(index, Requests.Count), person);
            HasRequests = true;
            Error = error;
            return;
        }

        if (approve)
            await LoadAsync(_conversation);
    }
}
