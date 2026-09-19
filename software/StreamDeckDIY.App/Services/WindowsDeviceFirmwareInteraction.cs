using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsDeviceFirmwareInteraction(Window window)
    : IDeviceFirmwareInteraction
{
    public async Task<bool> ConfirmEnterBootloaderAsync()
    {
        if (window.Content is not FrameworkElement content) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = content.XamlRoot,
            Title = "Entrar en modo actualización",
            Content = "El StreamDeck se desconectará y aparecerá en Windows como " +
                      "la unidad RPI-RP2. Después podrás copiar manualmente el UF2.",
            PrimaryButtonText = "Continuar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowBootloaderReadyAsync()
    {
        if (window.Content is not FrameworkElement content) return;
        var dialog = new ContentDialog
        {
            XamlRoot = content.XamlRoot,
            Title = "Modo actualización solicitado",
            Content = "El dispositivo ha confirmado la orden. Espera a que aparezca " +
                      "RPI-RP2 y copia allí el nuevo archivo UF2.",
            CloseButtonText = "Entendido",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }
}
