using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Traysky.ViewModels;
using Windows.System;

namespace Traysky.Pages;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; } = new();

    public LoginPage()
    {
        InitializeComponent();
        Loaded += (_, _) => HandleBox.Focus(FocusState.Programmatic);
    }

    public bool IsNotBusy(bool busy) => !busy;

    public Visibility HiddenWhenBusy(bool busy) => busy ? Visibility.Collapsed : Visibility.Visible;

    private void Field_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        if (ViewModel.SignInCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.SignInCommand.Execute(null);
        }
    }
}
