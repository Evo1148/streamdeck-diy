using System.Windows.Input;
using StreamDeckDIY.Core.HostActions;
using StreamDeckDIY.Protocol.Models;

namespace StreamDeckDIY.App.ViewModels;

public static class BindingPresentation
{
    public static string Describe(DeviceAction action, Func<uint, string?> hostActionName) =>
        action.Type switch
        {
            ActionType.None => "Sin asignar",
            ActionType.Keyboard => KeyName(action.KeyCode),
            ActionType.KeyboardShortcut => ShortcutName(action),
            ActionType.ConsumerControl => action.ConsumerControl switch
            {
                ConsumerControlAction.VolumeUp => "Volumen +",
                ConsumerControlAction.VolumeDown => "Volumen −",
                ConsumerControlAction.Mute => "Silenciar audio",
                ConsumerControlAction.PlayPause => "Reproducir / pausa",
                _ => "Control multimedia",
            },
            ActionType.HostAction => hostActionName(action.HostActionId) ??
                "Acción no disponible",
            _ => "Sin asignar",
        };

    public static string Describe(
        DeviceAction action,
        Func<uint, HostActionDefinition?> hostAction) => action.Type == ActionType.HostAction
            ? DescribeHostAction(hostAction(action.HostActionId))
            : Describe(action, (uint _) => (string?)null);

    public static string IconGlyph(ActionType type) => type switch
    {
        ActionType.Keyboard => "\uE765",
        ActionType.KeyboardShortcut => "\uE8A7",
        ActionType.ConsumerControl => "\uE767",
        ActionType.HostAction => "\uE945",
        _ => "\uE80A",
    };

    public static string IconGlyph(
        DeviceAction action,
        Func<uint, HostActionDefinition?> hostAction)
    {
        if (action.Type != ActionType.HostAction) return IconGlyph(action.Type);
        return hostAction(action.HostActionId)?.Kind switch
        {
            HostActionKind.LaunchApplication => "\uE8A5",
            HostActionKind.OpenUrl => "\uE774",
            HostActionKind.Macro => "\uE8FD",
            HostActionKind.SetAudioOutput => "\uE995",
            HostActionKind.ToggleAudioOutput => "\uE767",
            _ => "\uE945",
        };
    }

    public static string FriendlyType(ActionType type) => type switch
    {
        ActionType.None => "Sin asignar",
        ActionType.Keyboard => "Tecla",
        ActionType.KeyboardShortcut => "Atajo de teclado",
        ActionType.ConsumerControl => "Control multimedia",
        ActionType.HostAction => "Acción del PC",
        _ => "Sin asignar",
    };

    public static bool IsSelected(ControlId item, ControlId selected) => item == selected;

    public static int EncoderOrder(ControlType type) => type switch
    {
        ControlType.EncoderCounterClockwise => 0,
        ControlType.EncoderPress => 1,
        ControlType.EncoderClockwise => 2,
        _ => 3,
    };

    public static string FriendlyHostActionName(HostActionDefinition action) =>
        DescribeHostAction(action);

    public static IReadOnlyList<BindingIconOption> IconOptions { get; } =
    [
        new("Automático", null, "\uE8D4"),
        new("Volumen", "volume", "\uE767"),
        new("Auriculares", "headphones", "\uE7F6"),
        new("Micrófono", "microphone", "\uE720"),
        new("Música", "music", "\uE8D6"),
        new("Reproducir", "play", "\uE768"),
        new("Pausa", "pause", "\uE769"),
        new("Internet", "web", "\uE774"),
        new("Enlace", "link", "\uE71B"),
        new("Teclado", "keyboard", "\uE765"),
        new("Aplicación", "application", "\uE8A5"),
        new("Carpeta", "folder", "\uE8B7"),
        new("Archivo", "file", "\uE8A5"),
        new("Energía", "power", "\uE7E8"),
        new("Ajustes", "settings", "\uE713"),
        new("Herramientas", "tools", "\uE90F"),
        new("Lista", "sequence", "\uE8FD"),
        new("Izquierda", "left", "\uE72B"),
        new("Derecha", "right", "\uE72A"),
        new("Arriba", "up", "\uE74A"),
        new("Abajo", "down", "\uE74B"),
        new("Mensaje", "message", "\uE8BD"),
        new("Persona", "person", "\uE77B"),
        new("Aviso", "alert", "\uE7BA"),
        new("Correcto", "success", "\uE73E"),
    ];

    public static string ResolveIcon(
        string? iconKey,
        DeviceAction action,
        Func<uint, HostActionDefinition?> hostAction) =>
        IconOptions.FirstOrDefault(option => option.Key == iconKey)?.Glyph ??
        IconGlyph(action, hostAction);

    private static string ShortcutName(DeviceAction action)
    {
        var parts = new List<string>();
        if (action.Modifiers.HasFlag(ActionModifiers.Control)) parts.Add("Ctrl");
        if (action.Modifiers.HasFlag(ActionModifiers.Shift)) parts.Add("Shift");
        if (action.Modifiers.HasFlag(ActionModifiers.Alt)) parts.Add("Alt");
        if (action.Modifiers.HasFlag(ActionModifiers.Gui)) parts.Add("Win");
        parts.Add(KeyName(action.KeyCode));
        return string.Join(" + ", parts);
    }

    private static string DescribeHostAction(HostActionDefinition? action) => action switch
    {
        null => "Acción no disponible",
        { Kind: HostActionKind.SetAudioOutput } => "Cambiar salida de audio",
        { Kind: HostActionKind.ToggleAudioOutput } => "Alternar salida de audio",
        _ => action.Name,
    };

    private static string KeyName(byte keyCode) => keyCode switch
    {
        >= 0x04 and <= 0x1D => ((char)('A' + keyCode - 0x04)).ToString(),
        >= 0x1E and <= 0x26 => (keyCode - 0x1D).ToString(),
        0x27 => "0",
        0x28 => "Enter",
        0x29 => "Escape",
        0x2B => "Tab",
        0x2C => "Espacio",
        _ => $"Tecla 0x{keyCode:X2}",
    };
}

public sealed record ActionTypeOption(string Name, ActionType Type)
{
    public override string ToString() => Name;
}

public sealed class ControlPresentationItem : ObservableObject
{
    private string number = string.Empty;
    private string name = string.Empty;
    private string summary = "Sin asignar";
    private string iconGlyph = "\uE80A";
    private string controlGlyph = "\uE80A";
    private bool isSelected;

    public ControlPresentationItem(ControlId control, ICommand selectCommand)
    {
        Control = control;
        SelectCommand = selectCommand;
    }

    public ControlId Control { get; }
    public ICommand SelectCommand { get; }
    public string Number { get => number; private set => SetProperty(ref number, value); }
    public string Name { get => name; private set => SetProperty(ref name, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public string IconGlyph { get => iconGlyph; private set => SetProperty(ref iconGlyph, value); }
    public string ControlGlyph { get => controlGlyph; private set => SetProperty(ref controlGlyph, value); }
    public bool IsSelected { get => isSelected; private set { if (SetProperty(ref isSelected, value)) OnPropertyChanged(nameof(SelectionOpacity)); } }
    public double SelectionOpacity => IsSelected ? 1d : 0d;

    public void Update(
        string number,
        string name,
        string summary,
        string iconGlyph,
        string controlGlyph,
        bool isSelected)
    {
        Number = number;
        Name = name;
        Summary = summary;
        IconGlyph = iconGlyph;
        ControlGlyph = controlGlyph;
        IsSelected = isSelected;
    }
}

public sealed record BindingIconOption(string Name, string? Key, string Glyph)
{
    public bool IsAutomatic => Key is null;
    public override string ToString() => Name;
}

public sealed record BindingHostActionOption(
    HostActionDefinition Definition,
    string DisplayName,
    string Detail)
{
    public override string ToString() => DisplayName;
}
