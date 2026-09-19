namespace StreamDeckDIY.App.Services;

public interface IProfileInteraction
{
    Task<string?> RequestNameAsync(string title, string initialValue);
    Task<bool> ConfirmDeleteAsync(string profileName);
}
