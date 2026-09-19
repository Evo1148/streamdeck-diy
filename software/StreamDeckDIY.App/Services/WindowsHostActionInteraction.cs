using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamDeckDIY.Core.HostActions;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsHostActionInteraction(Window window) : IHostActionInteraction
{
    public async Task<string?> PickExecutableAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<bool> ConfirmDeleteAsync(
        HostActionDefinition definition,
        int macroUsageCount)
    {
        if (window.Content is not FrameworkElement content) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = content.XamlRoot,
            Title = $"Eliminar '{definition.Name}'",
            Content = (macroUsageCount > 0
                ? $"Esta acción está utilizada por {macroUsageCount} macro(s). "
                : string.Empty) +
                      "Los botones que tengan esta acción asignada conservarán su ID, " +
                      "pero la acción dejará de poder ejecutarse hasta que vuelva a existir " +
                      "una definición correspondiente.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
