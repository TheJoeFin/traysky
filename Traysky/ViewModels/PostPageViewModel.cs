using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky.Feed;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// One post with its thread: ancestors above, replies below. The seed post renders at once
/// from what the caller already had; the thread fetch then replaces it with the hydrated
/// version and fills in the rest.
/// </summary>
public sealed partial class PostPageViewModel : ObservableObject
{
    private const int ReplyDepth = 3;

    public ObservableCollection<ThreadRow> Rows { get; } = [];

    [ObservableProperty]
    public partial PostItem? Post { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    public partial string? Context { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool HasNoReplies { get; private set; }

    public bool HasContext => !string.IsNullOrEmpty(Context);

    public bool HasError => !string.IsNullOrEmpty(Error);

    public async Task LoadAsync(PostPageArgs args)
    {
        _lastArgs = args;
        Post = args.Post;
        Context = args.Context;
        Error = null;
        HasNoReplies = false;

        Rows.Clear();
        if (args.Post is not null)
            Rows.Add(new ThreadRow { Post = args.Post, Depth = 0, IsMain = true });

        string? atUri = args.EffectiveAtUri;
        if (atUri is null)
        {
            Error = "This post is no longer available.";
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn || PreviewMode.IsEnabled)
        {
            HasNoReplies = args.Post?.ReplyCount == 0;
            return;
        }

        IsLoading = true;
        try
        {
            AtProtoHttpResult<PostThread> result = await BlueskySessionService.Instance.Agent.GetPostThread(
                new AtUri(atUri),
                depth: ReplyDepth,
                parentHeight: 10);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Thread", $"GetPostThread failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = result.StatusCode switch
                {
                    System.Net.HttpStatusCode.NotFound => "This post has been deleted.",
                    0 => "Couldn't reach Bluesky. Check your connection.",
                    _ => "Couldn't load the thread."
                };
                return;
            }

            List<ThreadRow> rows = FeedMapper.FlattenThread(result.Result, ReplyDepth);
            if (rows.Count == 0)
            {
                Error = "This post is no longer available.";
                return;
            }

            // Carry the seed's optimistic like/repost state forward if the server view is
            // older than a click the user just made on the card that opened this page.
            foreach (ThreadRow row in rows)
            {
                if (row.IsMain)
                {
                    Post = row.Post;
                    break;
                }
            }

            Rows.Clear();
            foreach (ThreadRow row in rows)
                Rows.Add(row);

            HasNoReplies = rows.Count == 1 || !rows.Exists(r => r.Depth > 0);
        }
        catch (Exception ex)
        {
            LogService.Error("Thread", "GetPostThread threw", ex);
            Error = "Couldn't load the thread.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task Retry() => _lastArgs is null ? Task.CompletedTask : LoadAsync(_lastArgs);

    private PostPageArgs? _lastArgs;

    [RelayCommand]
    private Task OpenInBrowser() => RichTextBuilder.OpenAsync(Post?.WebUrl);
}
