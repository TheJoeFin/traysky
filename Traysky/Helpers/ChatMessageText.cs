using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>
/// Fills a <see cref="RichTextBlock"/> in a DataTemplate with a chat message's text, links and
/// mentions: <c>helpers:ChatMessageText.Message="{x:Bind}"</c>. A RichTextBlock's content isn't
/// bindable, so this is the template-friendly equivalent of what <see cref="Controls.PostCard"/>
/// does in code-behind.
/// </summary>
public static class ChatMessageText
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.RegisterAttached(
        "Message", typeof(MessageItem), typeof(ChatMessageText), new PropertyMetadata(null, OnMessageChanged));

    public static MessageItem? GetMessage(DependencyObject element) => (MessageItem?)element.GetValue(MessageProperty);

    public static void SetMessage(DependencyObject element, MessageItem? value) => element.SetValue(MessageProperty, value);

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBlock target)
            return;

        if (e.NewValue is not MessageItem message)
        {
            target.Blocks.Clear();
            return;
        }

        // Accent links would vanish on the accent-coloured bubble of your own messages.
        Brush? linkForeground = message.IsMine ? BindHelpers.BrushFromKey(message.TextBrushKey) : null;
        RichTextBuilder.Populate(target, message.Segments, linkForeground);
    }
}
