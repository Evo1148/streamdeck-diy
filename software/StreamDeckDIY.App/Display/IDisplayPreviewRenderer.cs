using StreamDeckDIY.Core.Display;

namespace StreamDeckDIY.App.Display;

public interface IDisplayPreviewRenderer
{
    void Render(DisplayState state, DisplayEditorRenderState editorState);
}
