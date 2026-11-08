using CommunityToolkit.Mvvm.ComponentModel;
using Traysky.Services;

namespace Traysky.ViewModels;

/// <summary>
/// Base for view models whose commands ask the user something. The page that shows the view
/// model assigns <see cref="Dialogs"/> (an <see cref="XamlDialogService"/> over its own
/// XamlRoot); until then, and in tests, questions go to <see cref="NullDialogService"/>, which
/// answers "cancelled" to everything.
/// </summary>
public abstract class DialogViewModel : ObservableObject
{
    public IDialogService Dialogs { get; set; } = NullDialogService.Instance;
}
