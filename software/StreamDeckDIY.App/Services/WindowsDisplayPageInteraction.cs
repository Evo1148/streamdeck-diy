using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace StreamDeckDIY.App.Services;

public interface IDisplayPageInteraction
{
    Task<string?> RequestNameAsync(string title, string initialValue);
    Task<bool> ConfirmDeleteAsync(string pageName);
}

public sealed class WindowsDisplayPageInteraction(Window window) : IDisplayPageInteraction
{
    public async Task<string?> RequestNameAsync(string title, string initialValue)
    {
        var input = new TextBox
        {
            Text = initialValue,
            SelectionStart = initialValue.Length,
            MinWidth = 280,
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
            ? input.Text : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string pageName)
    {
        var dialog = new ContentDialog
        {
            Title = "Eliminar página",
            Content = $"¿Eliminar la página '{pageName}'?",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = window.Content.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
