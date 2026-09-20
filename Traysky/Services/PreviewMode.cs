using System;
using System.Collections.Generic;
using idunno.AtProto;
using idunno.AtProto.Repo;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Debug-only sample data so the timeline, notifications and compose UI can be checked
/// without an account or network. Set <c>TRAYSKY_PREVIEW=1</c> in the environment before
/// launching a Debug build, or - since a packaged app launched from the shell inherits no
/// environment - drop an empty <c>preview.flag</c> file in the package's LocalState folder.
/// Never compiled into Release.
/// </summary>
public static class PreviewMode
{
#if DEBUG
    public static bool IsEnabled { get; } = Detect();

    private static bool Detect()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TRAYSKY_PREVIEW"), "1", StringComparison.Ordinal))
            return true;

        try
        {
            return System.IO.File.Exists(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "preview.flag"));
        }
        catch
        {
            return false;
        }
    }
#else
    public static bool IsEnabled => false;
#endif

    public const string Handle = "preview.bsky.social";

    private static StrongReference FakeRef(int n) =>
        new(new AtUri($"at://did:plc:preview{n}/app.bsky.feed.post/3k{n:d6}"), new Cid("bafyreigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi"));

    public static List<PostItem> SamplePosts()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        string t1 = "Shipping a tiny Bluesky client that lives in the Windows tray 🦋 Built on idunno.Bluesky and the bones of Traydio. #WinUI #dotnet";
        string t2 = "Handles do not start with an @ sign, that's just how the official app displays them. Read the docs: https://bluesky.idunno.dev/";
        string t3 = "Reply-with-a-picture test. Alt text matters.";
        string t4 = "Quoting this because the point stands.";

        return
        [
            new PostItem
            {
                AtUri = "at://did:plc:preview1/app.bsky.feed.post/3k000001", Cid = "bafy1", Reference = FakeRef(1),
                AuthorDid = "did:plc:preview1", AuthorHandle = "joe.bsky.social", AuthorDisplayName = "Joe Finney",
                Text = t1,
                Segments = FacetSegmenter.Segment(t1,
                [
                    new FacetInput(Utf8Index(t1, "#WinUI"), Utf8Index(t1, "#WinUI") + 6, FacetKind.Tag, "WinUI"),
                    new FacetInput(Utf8Index(t1, "#dotnet"), Utf8Index(t1, "#dotnet") + 7, FacetKind.Tag, "dotnet")
                ]),
                CreatedAt = now.AddMinutes(-4), LikeCount = 12, RepostCount = 3, ReplyCount = 2,
                WebUrl = "https://bsky.app/profile/joe.bsky.social/post/3k000001"
            },
            new PostItem
            {
                AtUri = "at://did:plc:preview2/app.bsky.feed.post/3k000002", Cid = "bafy2", Reference = FakeRef(2),
                AuthorDid = "did:plc:preview2", AuthorHandle = "blowdart.bsky.social", AuthorDisplayName = "Barry Dorrans",
                Text = t2,
                Segments = FacetSegmenter.Segment(t2,
                [
                    new FacetInput(Utf8Index(t2, "https://"), Utf8Index(t2, "https://") + "https://bluesky.idunno.dev/".Length, FacetKind.Link, "https://bluesky.idunno.dev/")
                ]),
                CreatedAt = now.AddHours(-2), LikeCount = 48, RepostCount = 9, ReplyCount = 5, IsLiked = true, LikeUri = "at://x/app.bsky.feed.like/1",
                RepostedBy = "Someone You Follow",
                Embed = new EmbedItem
                {
                    Kind = EmbedKind.External,
                    ExternalUri = new Uri("https://bluesky.idunno.dev/"),
                    ExternalHost = "bluesky.idunno.dev",
                    ExternalTitle = "idunno.Bluesky — a .NET library for Bluesky",
                    ExternalDescription = "Getting started, connecting, posting, timelines, notifications and more.",
                    OpenUrl = "https://bluesky.idunno.dev/"
                },
                WebUrl = "https://bsky.app/profile/blowdart.bsky.social/post/3k000002"
            },
            new PostItem
            {
                AtUri = "at://did:plc:preview3/app.bsky.feed.post/3k000003", Cid = "bafy3", Reference = FakeRef(3),
                AuthorDid = "did:plc:preview3", AuthorHandle = "photos.bsky.social", AuthorDisplayName = "Photo Person",
                Text = t3, Segments = FacetSegmenter.Segment(t3, null),
                CreatedAt = now.AddDays(-1), LikeCount = 3, ReplyingTo = "joe.bsky.social",
                Embed = new EmbedItem
                {
                    Kind = EmbedKind.Images,
                    Images =
                    [
                        new EmbedImage(new Uri("ms-appx:///Assets/Wide310x150Logo.scale-200.png"), new Uri("ms-appx:///Assets/Wide310x150Logo.scale-200.png"), "Wide logo", 620.0 / 300),
                        new EmbedImage(new Uri("ms-appx:///Assets/Square150x150Logo.scale-200.png"), new Uri("ms-appx:///Assets/Square150x150Logo.scale-200.png"), "Square logo", 1)
                    ]
                },
                WebUrl = "https://bsky.app/profile/photos.bsky.social/post/3k000003"
            },
            new PostItem
            {
                AtUri = "at://did:plc:preview4/app.bsky.feed.post/3k000004", Cid = "bafy4", Reference = FakeRef(4),
                AuthorDid = "did:plc:preview4", AuthorHandle = "quoter.bsky.social", AuthorDisplayName = "Quote Person",
                Text = t4, Segments = FacetSegmenter.Segment(t4, null),
                CreatedAt = now.AddDays(-9), LikeCount = 0, IsReposted = true, RepostUri = "at://x/app.bsky.feed.repost/1", RepostCount = 1,
                Embed = new EmbedItem
                {
                    Kind = EmbedKind.Quote,
                    QuoteAuthorName = "Joe Finney",
                    QuoteAuthorHandle = "joe.bsky.social",
                    QuoteText = t1,
                    QuoteWebUrl = "https://bsky.app/profile/joe.bsky.social/post/3k000001",
                    QuoteAtUri = "at://did:plc:preview1/app.bsky.feed.post/3k000001",
                    OpenUrl = "https://bsky.app/profile/joe.bsky.social/post/3k000001"
                },
                WebUrl = "https://bsky.app/profile/quoter.bsky.social/post/3k000004"
            }
        ];
    }

    public static List<NotificationItem> SampleNotifications()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<PostItem> posts = SamplePosts();

        return
        [
            new NotificationItem
            {
                Kind = NotificationKind.Reply, Headline = "Photo Person replied to you", AuthorHandle = "photos.bsky.social", AuthorDisplayName = "Photo Person", AuthorDid = "did:plc:preview3",
                BodyText = posts[2].Text, IndexedAt = now.AddMinutes(-10), IsUnread = true, Post = posts[2], SubjectPost = posts[2], WebUrl = posts[2].WebUrl
            },
            new NotificationItem
            {
                Kind = NotificationKind.Like, Headline = "Barry Dorrans, Photo Person and 3 others liked your post", AuthorHandle = "blowdart.bsky.social", AuthorDisplayName = "Barry Dorrans", AuthorDid = "did:plc:preview2",
                BodyText = posts[0].Text, IndexedAt = now.AddHours(-1), IsUnread = true, SubjectPost = posts[0], WebUrl = posts[0].WebUrl
            },
            new NotificationItem
            {
                Kind = NotificationKind.Follow, Headline = "Quote Person and Someone Else followed you", AuthorHandle = "quoter.bsky.social", AuthorDisplayName = "Quote Person", AuthorDid = "did:plc:preview4",
                IndexedAt = now.AddHours(-5), IsUnread = false, WebUrl = "https://bsky.app/profile/quoter.bsky.social"
            },
            new NotificationItem
            {
                Kind = NotificationKind.Repost, Headline = "Quote Person reposted your post", AuthorHandle = "quoter.bsky.social", AuthorDisplayName = "Quote Person", AuthorDid = "did:plc:preview4",
                BodyText = posts[0].Text, IndexedAt = now.AddDays(-2), IsUnread = false, SubjectPost = posts[0], WebUrl = posts[0].WebUrl
            }
        ];
    }

    /// <summary>A handful of pinned feeds so the tab row has something to show without a network call.</summary>
    public static List<FeedTabItem> SampleFeedTabs() =>
    [
        new FeedTabItem { Kind = FeedTabKind.Timeline, DisplayName = "Following" },
        new FeedTabItem
        {
            Kind = FeedTabKind.Feed,
            DisplayName = "Discover",
            Uri = new AtUri("at://did:plc:preview-discover/app.bsky.feed.generator/discover")
        },
        new FeedTabItem
        {
            Kind = FeedTabKind.Feed,
            DisplayName = "News",
            Uri = new AtUri("at://did:plc:preview-news/app.bsky.feed.generator/news")
        }
    ];

    private static long Utf8Index(string text, string needle) =>
        System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, text.IndexOf(needle, StringComparison.Ordinal)));
}
