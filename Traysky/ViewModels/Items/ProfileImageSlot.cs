using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// The avatar or the banner in the profile editor: what the account has now, plus the pending
/// change (keep, replace with a chosen picture, or remove). The view shows the result from
/// <see cref="CurrentUri"/> and <see cref="UploadBytes"/>; this class holds no UI types.
/// </summary>
public sealed partial class ProfileImageSlot : ObservableObject
{
    private Uri? _current;

    public ProfileImageChange Change { get; private set; }

    /// <summary>The picture to upload; set only while <see cref="Change"/> is <see cref="ProfileImageChange.Replace"/>.</summary>
    public ProfileImageUpload? Upload { get; private set; }

    /// <summary>The picture to show while nothing is chosen: the account's own, or null when removed or absent.</summary>
    [ObservableProperty]
    public partial Uri? CurrentUri { get; private set; }

    /// <summary>The chosen replacement's bytes for previewing; null unless a picture was chosen.</summary>
    [ObservableProperty]
    public partial byte[]? UploadBytes { get; private set; }

    public bool HasImage => Change switch
    {
        ProfileImageChange.Replace => true,
        ProfileImageChange.Remove => false,
        _ => _current is not null
    };

    public bool IsChanged => Change != ProfileImageChange.Keep;

    /// <summary>Resets to the account's current picture (null when it has none).</summary>
    public void Load(Uri? current)
    {
        _current = current;
        Reset();
    }

    public void Replace(ProfileImageUpload upload)
    {
        Change = ProfileImageChange.Replace;
        Upload = upload;
        UploadBytes = upload.Bytes;
        CurrentUri = null;
        Notify();
    }

    public void Remove()
    {
        Change = ProfileImageChange.Remove;
        Upload = null;
        UploadBytes = null;
        CurrentUri = null;
        Notify();
    }

    /// <summary>Drops any pending change and shows the account's current picture again.</summary>
    public void Reset()
    {
        Change = ProfileImageChange.Keep;
        Upload = null;
        UploadBytes = null;
        CurrentUri = _current;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(IsChanged));
    }
}
