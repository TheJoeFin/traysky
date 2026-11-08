using System;
using System.ComponentModel;

namespace Traysky.Services;

/// <summary>
/// What view models need from navigation: go somewhere, go back, and know where we are. The
/// Frame and transition types stay behind <see cref="NavigationService"/>, so a view model that
/// depends on this can be exercised without a window.
/// </summary>
public interface INavigationService : INotifyPropertyChanged
{
    bool CanGoBack { get; }

    /// <summary>The page type currently shown, or null before the first navigation.</summary>
    Type? CurrentPageType { get; }

    /// <summary>Raised after every navigation, back or forward included.</summary>
    event EventHandler? NavigationChanged;

    bool Navigate(Type pageType, object? parameter = null);

    void GoBack();

    void ResetTo(Type pageType, object? parameter = null);

    void ClearBackStack();
}
