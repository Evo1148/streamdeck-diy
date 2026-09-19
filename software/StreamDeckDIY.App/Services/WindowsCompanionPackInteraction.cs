using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsCompanionPackInteraction(Window window) : ICompanionPackInteraction
{
    public async Task<string?> PickArchiveAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<bool> ConfirmDeleteAsync(string displayName)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = window.Content.XamlRoot,
            Title = "Eliminar Companion Pack",
            Content = $"¿Eliminar '{displayName}' de la biblioteca local?",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
