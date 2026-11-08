using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Traysky.Services;

namespace Traysky.Helpers;

/// <summary>The "Go live" dialog: where people watch, what to call it, and for how long.</summary>
public static class LiveStatusDialogs
{
    public static async Task<(Uri Link, string Title, int Minutes)?> AskGoLiveAsync(XamlRoot root)
    {
        TextBox link = new() { Header = "Stream link", PlaceholderText = "twitch.tv/yourname" };
        TextBox title = new()
        {
            Header = "Title (optional)",
            PlaceholderText = "Shown with your LIVE badge",
            MaxLength = LiveStatusPolicy.TitleMaxLength
        };

        ComboBox duration = new() { Header = "Stay live for", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (int minutes in LiveStatusPolicy.PresetMinutes)
            duration.Items.Add(LiveStatusPolicy.PresetLabel(minutes));
        duration.SelectedIndex = Math.Max(0, Array.IndexOf(LiveStatusPolicy.PresetMinutes, LiveStatusPolicy.DefaultMinutes));

        TextBlock hint = new()
        {
            Text = "Your profile shows a LIVE badge until the time is up, or until you end it.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        StackPanel panel = new() { Spacing = 12, MinWidth = 300 };
        panel.Children.Add(link);
        panel.Children.Add(title);
        panel.Children.Add(duration);
        panel.Children.Add(hint);

        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = "Go live",
            Content = panel,
            PrimaryButtonText = "Go live",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        link.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = LiveStatusPolicy.TryNormalizeLink(link.Text, out _);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || !LiveStatusPolicy.TryNormalizeLink(link.Text, out Uri? uri))
            return null;

        int selected = Math.Clamp(duration.SelectedIndex, 0, LiveStatusPolicy.PresetMinutes.Length - 1);
        return (uri, LiveStatusPolicy.ResolveTitle(title.Text, uri), LiveStatusPolicy.PresetMinutes[selected]);
    }
}
