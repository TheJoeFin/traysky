using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using Traysky.Services;

namespace Traysky.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
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
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    private bool CanSignIn() => !IsBusy && !string.IsNullOrWhiteSpace(Handle) && !string.IsNullOrEmpty(AppPassword);

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

    [RelayCommand]
    private Task OpenAppPasswords() => RichTextBuilder.OpenAsync(BlueskyLinks.AppPasswordsUrl);
}
