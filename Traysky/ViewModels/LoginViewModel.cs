using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;

namespace Traysky.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private CancellationTokenSource? _browserSignIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInWithBrowserCommand))]
    public partial string Handle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string AppPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AuthCode { get; set; } = string.Empty;

    /// <summary>Shown after the server asks for an emailed sign-in code.</summary>
    [ObservableProperty]
    public partial bool NeedsAuthCode { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignInWithBrowserCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>True while the browser is open on Bluesky's sign-in page and the app waits for the redirect.</summary>
    [ObservableProperty]
    public partial bool IsWaitingForBrowser { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    private bool CanSignIn() => !IsBusy && !string.IsNullOrWhiteSpace(Handle) && !string.IsNullOrEmpty(AppPassword);

    private bool CanSignInWithBrowser() => !IsBusy && !string.IsNullOrWhiteSpace(Handle);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        Error = null;

        try
        {
            (LoginOutcome outcome, string? error) = await BlueskySessionService.Instance.LoginAsync(
                Handle,
                AppPassword,
                NeedsAuthCode ? AuthCode : null);

            switch (outcome)
            {
                case LoginOutcome.Success:
                    AppPassword = string.Empty;
                    AuthCode = string.Empty;
                    NeedsAuthCode = false;
                    break;

                case LoginOutcome.NeedsAuthFactor:
                    NeedsAuthCode = true;
                    Error = "Check your email for a sign-in code and enter it below.";
                    break;

                default:
                    Error = error ?? "Sign in failed.";
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignInWithBrowser))]
    private async Task SignInWithBrowserAsync()
    {
        IsBusy = true;
        IsWaitingForBrowser = true;
        Error = null;

        using CancellationTokenSource cts = new();
        _browserSignIn = cts;
        try
        {
            (LoginOutcome outcome, string? error) = await BlueskySessionService.Instance.LoginWithBrowserAsync(Handle, cts.Token);

            if (outcome == LoginOutcome.Failed)
                Error = error ?? "Sign in failed.";
        }
        finally
        {
            _browserSignIn = null;
            IsWaitingForBrowser = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelBrowserSignIn() => _browserSignIn?.Cancel();

    [RelayCommand]
    private Task OpenAppPasswords() => RichTextBuilder.OpenAsync(BlueskyLinks.AppPasswordsUrl);
}
