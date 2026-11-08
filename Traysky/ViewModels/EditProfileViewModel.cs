using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Win32;

namespace Traysky.ViewModels;

/// <summary>
/// Edit the signed-in account's display name, bio, avatar and banner. Nothing is sent until Save;
/// the service re-reads the record then and changes only these fields.
/// </summary>
public sealed partial class EditProfileViewModel : ObservableObject
{
    private OwnProfile? _loaded;

    public ProfileImageSlot Avatar { get; } = new();

    public ProfileImageSlot Banner { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameCounter), nameof(ValidationMessage), nameof(CanSave))]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BioCounter), nameof(ValidationMessage), nameof(CanSave))]
    public partial string Bio { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>The fields are only usable once the current profile has been read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsReady { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsSaving { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string NameCounter => ProfileEditPolicy.Counter(DisplayName, ProfileEditPolicy.DisplayNameMaxGraphemes);

    public string BioCounter => ProfileEditPolicy.Counter(Bio, ProfileEditPolicy.BioMaxGraphemes);

    /// <summary>Shown under the fields while what's typed can't be saved; empty otherwise.</summary>
    public string ValidationMessage => ProfileEditPolicy.Validate(DisplayName, Bio) ?? string.Empty;

    public bool CanSave => IsReady && !IsSaving && ValidationMessage.Length == 0 && HasChanges;

    private bool HasChanges =>
        _loaded is not null
        && ProfileEditPolicy.HasChanges(_loaded.DisplayName, _loaded.Bio, DisplayName, Bio, Avatar.Change, Banner.Change);

    /// <summary>Raised on the UI thread after a successful save, so the page can leave.</summary>
    public event EventHandler? Saved;

    public async Task LoadAsync()
    {
        Error = null;
        IsLoading = true;
        try
        {
            OwnProfile? profile = await ProfileEditService.LoadAsync();
            if (profile is null)
            {
                Error = "Couldn't load your profile.";
                return;
            }

            _loaded = profile;
            DisplayName = profile.DisplayName;
            Bio = profile.Bio;
            Avatar.Load(profile.AvatarUri);
            Banner.Load(profile.BannerUri);
            IsReady = true;
            OnPropertyChanged(nameof(CanSave));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!CanSave)
            return;

        IsSaving = true;
        Error = null;
        try
        {
            (bool saved, string? error) = await ProfileEditService.SaveAsync(
                DisplayName, Bio, Avatar.Change, Avatar.Upload, Banner.Change, Banner.Upload);

            if (!saved)
            {
                Error = error ?? "Couldn't save your profile.";
                return;
            }

            await BlueskySessionService.Instance.RefreshProfileAsync();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private Task Retry() => LoadAsync();

    [RelayCommand]
    private Task ChooseAvatar() => ChooseImageAsync(Avatar, ProfileEditPolicy.AvatarSteps, "avatar");

    [RelayCommand]
    private Task ChooseBanner() => ChooseImageAsync(Banner, ProfileEditPolicy.BannerSteps, "banner");

    [RelayCommand]
    private void RemoveAvatar()
    {
        Avatar.Remove();
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private void RemoveBanner()
    {
        Banner.Remove();
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private void UndoAvatar()
    {
        Avatar.Reset();
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private void UndoBanner()
    {
        Banner.Reset();
        OnPropertyChanged(nameof(CanSave));
    }

    private async Task ChooseImageAsync(ProfileImageSlot slot, IReadOnlyList<ImageCompressionPolicy.Step> steps, string what)
    {
        Error = null;

        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        foreach (string extension in ComposeAttachmentPolicy.ImageExtensions)
            picker.FileTypeFilter.Add(extension);

        // Same reasoning as the compose box: the popup is the foreground window, and the picker's own
        // window would otherwise read as focus leaving the flyout.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, PInvoke.GetForegroundWindow());
        using IDisposable suppression = LightDismissSuppressionService.Suppress();

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        try
        {
            string extension = Path.GetExtension(file.Name);
            string? mimeType = ComposeAttachmentPolicy.MimeTypeFor(extension);
            if (mimeType is null || !ComposeAttachmentPolicy.IsImageExtension(extension))
            {
                Error = "Unsupported image type.";
                return;
            }

            byte[] bytes = await ComposeViewModel.ReadAllBytesAsync(file);

            // The profile record only takes PNG or JPEG within the blob limit; anything else, or anything
            // bigger, is re-encoded as a JPEG that fits.
            bool fits = bytes.Length <= ProfileEditPolicy.MaxImageBytes && (mimeType is "image/png" or "image/jpeg");
            if (!fits)
            {
                CompressedImage? compressed = await ImageCompressor.TryCompressAsync(bytes, steps, ProfileEditPolicy.MaxImageBytes);
                if (compressed is null)
                {
                    Error = $"Couldn't shrink that picture enough to use as your {what}.";
                    return;
                }

                bytes = compressed.Bytes;
                mimeType = "image/jpeg";
            }

            slot.Replace(new ProfileImageUpload(bytes, mimeType));
            OnPropertyChanged(nameof(CanSave));
        }
        catch (Exception ex)
        {
            LogService.Warn("ProfileEdit", $"Choosing the {what} threw: {ex.Message}");
            Error = "Couldn't open that picture.";
        }
    }
}
