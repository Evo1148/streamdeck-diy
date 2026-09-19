namespace StreamDeckDIY.App.Services;

public interface ICompanionPackInteraction
{
    Task<string?> PickArchiveAsync();
    Task<bool> ConfirmDeleteAsync(string displayName);
}
