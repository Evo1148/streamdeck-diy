using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsProfileInteraction(Window window) : IProfileInteraction
{
    public async Task<string?> RequestNameAsync(string title, string initialValue)
    {
        var input = new TextBox
        {
            Text = initialValue,
            SelectionStart = initialValue.Length,
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = "Aceptar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = window.Content.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? input.Text
            : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string profileName)
    {
        var dialog = new ContentDialog
        {
            Title = "Eliminar perfil",
            Content = $"¿Eliminar el perfil '{profileName}'?",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = window.Content.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
