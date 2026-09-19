namespace StreamDeckDIY.App.Display;

public sealed record DisplayEditorRenderState(
    bool IsEditing,
    bool ShowGrid,
    double GridSize,
    string? SelectedId);
