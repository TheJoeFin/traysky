using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One message in a conversation, projected from the library's <c>MessageView</c> or
/// <c>DeletedMessageView</c> (see <see cref="ChatMapper.ToMessageItem"/>).
/// </summary>
public sealed partial class MessageItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string SenderDid { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public Uri? SenderAvatarUri { get; init; }

    /// <summary>Sent by the signed-in account: drawn on the right, in the accent colour.</summary>
    public bool IsMine { get; init; }

    /// <summary>In a group, other people's runs are labelled with the sender's name.</summary>
    public bool IsInGroup { get; init; }

    public bool IsDeleted { get; init; }

    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<TextSegment> Segments { get; init; } = [];

    /// <summary>A shared post (or other record) attached to the message.</summary>
    public EmbedItem? Embed { get; init; }

    /// <summary>Reactions summarised for display, e.g. "❤️ 2  😂"; empty when there are none.</summary>
    public string ReactionsText { get; init; } = string.Empty;

    public DateTimeOffset SentAt { get; init; }

    /// <summary>First message of a burst (see <see cref="ChatThreadPolicy.StartsRun"/>); set by the list
    /// as messages arrive, and can change when older messages are loaded above it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunHeaderVisibility), nameof(SenderNameVisibility), nameof(RunMargin))]
    public partial bool StartsRun { get; set; } = true;

    public bool HasText => Text.Length > 0;
    public bool HasEmbed => Embed is not null;
    public bool HasReactions => ReactionsText.Length > 0;

    public HorizontalAlignment Alignment => IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public string BubbleBrushKey => IsMine ? "AccentFillColorDefaultBrush" : "CardBackgroundFillColorSecondaryBrush";

    public string TextBrushKey => IsMine ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush";

    public Visibility RunHeaderVisibility => StartsRun ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SenderNameVisibility => StartsRun && IsInGroup && !IsMine ? Visibility.Visible : Visibility.Collapsed;

    public Thickness RunMargin => StartsRun ? new Thickness(0, 10, 0, 0) : new Thickness(0, 2, 0, 0);

    public string TimeText => RelativeTimeFormatter.FormatAbsolute(SentAt);

    /// <summary>Above each run: just the time today, the date and time before that.</summary>
    public string RunTimeText
    {
        get
        {
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.CurrentCulture;
            DateTime local = SentAt.ToLocalTime().DateTime;
            string time = local.ToString("t", culture);
            return local.Date == DateTime.Today ? time : $"{local.ToString("MMM d", culture)}, {time}";
        }
    }
}
