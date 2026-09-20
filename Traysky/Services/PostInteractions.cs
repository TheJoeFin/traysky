using idunno.AtProto;
using idunno.AtProto.Repo;
using System;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Like / repost toggles with optimistic UI: the card flips immediately, and flips back if
/// the request fails. Shared by every list that shows a <see cref="PostItem"/>.
/// </summary>
public static class PostInteractions
{
    public static async Task ToggleLikeAsync(PostItem post)
    {
        if (post.IsBusy || !BlueskySessionService.Instance.IsSignedIn)
            return;

        post.IsBusy = true;
        bool wasLiked = post.IsLiked;
        string? previousLikeUri = post.LikeUri;

        post.IsLiked = !wasLiked;
        post.LikeCount = Math.Max(0, post.LikeCount + (wasLiked ? -1 : 1));

        try
        {
            if (wasLiked)
            {
                if (previousLikeUri is null)
                    return; // nothing to undo server-side; local state was already wrong

                AtProtoHttpResult<Commit> result = await BlueskySessionService.Instance.Agent.DeleteLike(new AtUri(previousLikeUri));
                if (result.Succeeded)
                {
                    post.LikeUri = null;
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Unlike failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
            else
            {
                AtProtoHttpResult<CreateRecordResult> result = await BlueskySessionService.Instance.Agent.Like(post.Reference);
                if (result.Succeeded && result.Result is not null)
                {
                    post.LikeUri = result.Result.Uri.ToString();
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Like failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
        }
        catch (Exception ex)
        {
            Revert();
            LogService.Warn("Interact", $"Like threw: {ex.Message}");
        }
        finally
        {
            post.IsBusy = false;
        }

        void Revert()
        {
            post.IsLiked = wasLiked;
            post.LikeUri = previousLikeUri;
            post.LikeCount = Math.Max(0, post.LikeCount + (wasLiked ? 1 : -1));
        }
    }

    public static async Task ToggleRepostAsync(PostItem post)
    {
        if (post.IsBusy || !BlueskySessionService.Instance.IsSignedIn)
            return;

        post.IsBusy = true;
        bool wasReposted = post.IsReposted;
        string? previousRepostUri = post.RepostUri;

        post.IsReposted = !wasReposted;
        post.RepostCount = Math.Max(0, post.RepostCount + (wasReposted ? -1 : 1));

        try
        {
            if (wasReposted)
            {
                if (previousRepostUri is null)
                    return;

                AtProtoHttpResult<Commit> result = await BlueskySessionService.Instance.Agent.DeleteRepost(new AtUri(previousRepostUri));
                if (result.Succeeded)
                {
                    post.RepostUri = null;
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Un-repost failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
            else
            {
                AtProtoHttpResult<CreateRecordResult> result = await BlueskySessionService.Instance.Agent.Repost(post.Reference);
                if (result.Succeeded && result.Result is not null)
                {
                    post.RepostUri = result.Result.Uri.ToString();
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Repost failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
        }
        catch (Exception ex)
        {
            Revert();
            LogService.Warn("Interact", $"Repost threw: {ex.Message}");
        }
        finally
        {
            post.IsBusy = false;
        }

        void Revert()
        {
            post.IsReposted = wasReposted;
            post.RepostUri = previousRepostUri;
            post.RepostCount = Math.Max(0, post.RepostCount + (wasReposted ? 1 : -1));
        }
    }
}
