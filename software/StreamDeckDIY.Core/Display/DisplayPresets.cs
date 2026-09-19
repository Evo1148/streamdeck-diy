namespace StreamDeckDIY.Core.Display;

public static class DisplaySceneFactory
{
    public static DisplayScene CreateHome(
        DisplayConfiguration configuration,
        string profileName,
        bool connected) =>
        CreatePage(configuration, profileName, "Principal", connected);

    public static DisplayScene CreatePage(
        DisplayConfiguration configuration,
        string profileName,
        string pageName,
        bool connected = false)
    {
        var width = configuration.Width;
        var height = configuration.Height;
        return new(
        "home",
        [
            new DisplayRectangleElement(
                "header", width * 0.04, height * 0.05,
                width * 0.92, height * 0.19, "#1E1830", 8, 0),
            new DisplayRectangleElement(
                "accent", width * 0.04, height * 0.05,
                width * 0.018, height * 0.19, "#7C3AED", 8, 1),
            new DisplayTextElement(
                "title", width * 0.085, height * 0.08,
                width * 0.80, height * 0.125, "StreamDeck DIY", height * 0.088,
                DisplayTextAlignment.Left, "#E8F5FF", 2),
            new DisplayTextElement(
                "profile-label", width * 0.075, height * 0.34,
                width * 0.85, height * 0.09, "PERFIL ACTIVO", height * 0.045,
                DisplayTextAlignment.Left, "#A78BFA", 3),
            new DisplayTextElement(
                "profile", width * 0.075, height * 0.43,
                width * 0.85, height * 0.17, profileName, height * 0.105,
                DisplayTextAlignment.Left, "#FFFFFF", 4),
            new DisplayRectangleElement(
                "page-chip", width * 0.075, height * 0.64,
                width * 0.48, height * 0.12, "#2B2142", 7, 5),
            new DisplayTextElement(
                "page", width * 0.095, height * 0.66,
                width * 0.44, height * 0.08, pageName, height * 0.045,
                DisplayTextAlignment.Left, "#DDD6FE", 6),
            new DisplayRectangleElement(
                "status-dot", width * 0.62, height * 0.825,
                height * 0.05, height * 0.05,
                connected ? "#4ADE80" : "#F87171", height * 0.025, 7),
            new DisplayTextElement(
                "status", width * 0.68, height * 0.79,
                width * 0.27, height * 0.12,
                connected ? "Conectado" : "Desconectado", height * 0.058,
                DisplayTextAlignment.Left, "#C8D8E6", 8),
        ],
        "#0C0B10");
    }
}

public sealed record ProfileVisualFeedback(
    DisplayScene Scene,
    VisualEffectDefinition Effect);

public interface IProfileVisualEffectProvider
{
    ProfileVisualFeedback CreateActivated(string profileName);
}

public sealed class HackerMatrixProfileVisualEffectProvider(
    DisplayConfiguration configuration)
    : IProfileVisualEffectProvider
{
    private static readonly string[] MatrixLines =
    [
        "01 10 11 00", "SYS::READY", "101101", "// PROFILE",
        "00101101", "ACCESS OK", "1100 0101", "STREAM::DECK",
    ];

    public ProfileVisualFeedback CreateActivated(string profileName)
    {
        var width = configuration.Width;
        var height = configuration.Height;
        var elements = new List<DisplayElement>
        {
            new DisplayRectangleElement(
                "matrix-background", 0, 0, width, height, "#020805", 0, 100,
                Animations: [
                    new(DisplayAnimationKind.FadeIn),
                    new(DisplayAnimationKind.FadeOut),
                ]),
        };

        for (var index = 0; index < MatrixLines.Length; index++)
        {
            elements.Add(new DisplayTextElement(
                $"matrix-line-{index}",
                width * (0.025 + index * 0.122),
                -height * (0.15 + (index % 3) * 0.23),
                width * 0.12,
                height * 1.08,
                MatrixLines[index],
                height * 0.046,
                DisplayTextAlignment.Center,
                index % 2 == 0 ? "#36FF79" : "#159447",
                101,
                Animations:
                [
                    new(DisplayAnimationKind.Slide, height * 1.38, 1),
                    new(DisplayAnimationKind.Pulse, 0.22, 2 + index % 3),
                    new(DisplayAnimationKind.FadeIn),
                    new(DisplayAnimationKind.FadeOut),
                ],
                FontFamily: "Consolas"));
        }

        elements.Add(new DisplayRectangleElement(
            "profile-panel", width * 0.078, height * 0.346,
            width * 0.844, height * 0.325, "#CC06120A", 10, 110,
            Animations:
            [
                new(DisplayAnimationKind.FadeIn),
                new(DisplayAnimationKind.FadeOut),
            ]));
        elements.Add(new DisplayTextElement(
            "profile-activated", width * 0.11, height * 0.383,
            width * 0.78, height * 0.092, "PERFIL ACTIVADO", height * 0.05,
            DisplayTextAlignment.Center, "#7CFF9F", 111,
            Animations: [new(DisplayAnimationKind.FadeIn)],
            FontFamily: "Consolas"));
        elements.Add(new DisplayTextElement(
            "profile-name", width * 0.11, height * 0.483,
            width * 0.78, height * 0.158, profileName, height * 0.104,
            DisplayTextAlignment.Center, "#D5FFE0", 112,
            Animations:
            [
                new(DisplayAnimationKind.FadeIn),
                new(DisplayAnimationKind.FadeOut),
                new(DisplayAnimationKind.Pulse, 0.12, 2),
            ],
            FontFamily: "Consolas"));

        return new ProfileVisualFeedback(
            new DisplayScene("profile-activated-hacker-matrix", elements),
            new VisualEffectDefinition(
                VisualEffectKind.HackerMatrix, TimeSpan.FromMilliseconds(1250)));
    }
}

public sealed class ProfileVisualFeedbackController(
    IDisplayEngine displayEngine,
    IProfileVisualEffectProvider effectProvider)
{
    public Task ShowActivatedAsync(
        string profileName,
        CancellationToken cancellationToken = default)
    {
        var feedback = effectProvider.CreateActivated(profileName);
        return displayEngine.ShowOverlayAsync(
            feedback.Scene, feedback.Effect, cancellationToken);
    }
}
