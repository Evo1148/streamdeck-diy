using StreamDeckDIY.Core.Dashboard;

internal static class CompanionRasterPolicyTests
{
    public static void Run()
    {
        FindsContentWithTransparentMargins();
        KeepsEdgeDetailsAndSmoke();
        UsesOneStableFrameAcrossTallAndWideMoods();
        FitsInsideSafeAreaWithoutDistortion();
        CompositesAgainstTheRequestedBackground();
        CacheIdentityIncludesEveryRasterInput();
        LatestCompanionRequestWins();
        KeepsMemoryInsideFirmwareLimits();
        UsesProductMicrocopy();
    }

    private static void FindsContentWithTransparentMargins()
    {
        var pixels = Canvas(100, 100);
        FillAlpha(pixels, 100, 40, 20, 20, 60, 255);
        var bounds = CompanionRasterPolicy.FindAlphaBounds(pixels, 0, 400, 100, 100);
        Assert(bounds == new CompanionPixelBounds(38, 18, 24, 64),
            "Alpha bounds remove large transparent margins and retain safety pixels");
    }

    private static void KeepsEdgeDetailsAndSmoke()
    {
        var pixels = Canvas(100, 80);
        FillAlpha(pixels, 100, 0, 8, 30, 50, 255);
        FillAlpha(pixels, 100, 86, 0, 14, 12, 90);
        var bounds = CompanionRasterPolicy.FindAlphaBounds(pixels, 0, 400, 100, 80);
        Assert(bounds.X == 0 && bounds.Y == 0 && bounds.Right == 100 &&
               bounds.Bottom >= 60,
            "Content on an edge and translucent smoke remain inside the safe bounds");
    }

    private static void UsesOneStableFrameAcrossTallAndWideMoods()
    {
        var tall = new CompanionSourceGeometry(1000, 1000,
            new CompanionPixelBounds(300, 50, 400, 900));
        var wide = new CompanionSourceGeometry(1000, 1000,
            new CompanionPixelBounds(50, 300, 900, 400));
        var profile = CompanionRasterPolicy.CreatePackProfile([tall, wide]);
        var tallCrop = CompanionRasterPolicy.CalculateCrop(profile, tall);
        var wideCrop = CompanionRasterPolicy.CalculateCrop(profile, wide);
        Assert(tallCrop.Width == wideCrop.Width && tallCrop.Height == wideCrop.Height &&
               Contains(tallCrop, tall.ContentBounds) && Contains(wideCrop, wide.ContentBounds),
            "All moods in a pack use the same normalized frame without clipping");
    }

    private static void FitsInsideSafeAreaWithoutDistortion()
    {
        foreach (var crop in new[]
                 {
                     new CompanionCrop(0, 0, 300, 900),
                     new CompanionCrop(0, 0, 900, 300),
                     new CompanionCrop(0, 0, 700, 700),
                 })
        {
            var fit = CompanionRasterPolicy.Fit(crop);
            Assert(fit.X >= CompanionRasterPolicy.SafePadding &&
                   fit.Y >= CompanionRasterPolicy.SafePadding &&
                   fit.X + fit.Width <= CompanionRasterPolicy.RenderSize -
                       CompanionRasterPolicy.SafePadding &&
                   fit.Y + fit.Height <= CompanionRasterPolicy.RenderSize -
                       CompanionRasterPolicy.SafePadding,
                "Hero sprite stays inside its motion-safe area");
            var sourceRatio = crop.Width / (double)crop.Height;
            var targetRatio = fit.Width / (double)fit.Height;
            Assert(Math.Abs(sourceRatio - targetRatio) / sourceRatio < .02,
                "Hero scaling preserves the sprite aspect ratio");
        }
    }

    private static void CompositesAgainstTheRequestedBackground()
    {
        const ushort hikari = 0xF79C;
        const ushort neon = 0x0842;
        Assert(CompanionRasterPolicy.CompositePremultiplied(0, 0, 0, 0, hikari) ==
               CompanionRasterPolicy.Background(hikari),
            "COMPANION-BG-01: fully transparent Hikari pixels become the Hikari background");
        Assert(CompanionRasterPolicy.CompositePremultiplied(0, 0, 0, 0, neon) ==
               CompanionRasterPolicy.Background(neon),
            "COMPANION-BG-02: fully transparent Neon pixels become the Neon background");
        var opaque = CompanionRasterPolicy.CompositePremultiplied(17, 61, 143, 255, neon);
        Assert(opaque == new CompanionBgr(17, 61, 143),
            "Opaque sprite pixels remain unchanged after background compositing");
        var blended = CompanionRasterPolicy.CompositePremultiplied(60, 20, 100, 128, hikari);
        Assert(blended.Red > 100 && blended.Green > 20 && blended.Blue > 60,
            "Premultiplied translucent edges blend with the actual background instead of black");
    }

    private static void CacheIdentityIncludesEveryRasterInput()
    {
        var hikari = DashboardVisualTokenResolver.CompanionBackground(
            VisualIdentity.Hikari, DashboardVisualOptions.Default);
        var neon = DashboardVisualTokenResolver.CompanionBackground(
            VisualIdentity.Neon, DashboardVisualOptions.Default);
        var hikariKey = CompanionRasterPolicy.ProcessingKey(
            "neko:happy:12345678", VisualIdentity.Hikari, hikari);
        var neonKey = CompanionRasterPolicy.ProcessingKey(
            "neko:happy:12345678", VisualIdentity.Neon, neon);
        Assert(hikariKey != neonKey && hikariKey.Contains("hero-v3") &&
               hikariKey.Contains("size=96") && hikariKey.Contains("padding=7"),
            "COMPANION-CACHE-01: identity, background, size and raster policy participate in cache identity");

        var beforeStats = CompanionRasterPolicy.RequestKey(
            "neko", VisualIdentity.Neon, CompanionMood.Happy, neon);
        var afterStats = CompanionRasterPolicy.RequestKey(
            "neko", VisualIdentity.Neon, CompanionMood.Happy, neon);
        var differentMood = CompanionRasterPolicy.RequestKey(
            "neko", VisualIdentity.Neon, CompanionMood.Busy, neon);
        Assert(beforeStats == afterStats && beforeStats != differentMood,
            "COMPANION-CACHE-02: stats and clock are absent from the key while mood invalidates it");
    }
    private static void LatestCompanionRequestWins()
    {
        var requests=new CompanionRequestCoordinator();
        var beganHikari = requests.TryBegin("Hikari:Happy", out var hikari);
        var beganNeon = requests.TryBegin("Neon:Happy", out var neon);
        Assert(beganHikari && beganNeon &&
               !requests.IsCurrent(hikari) && requests.IsCurrent(neon),
            "A later theme request makes an earlier asynchronous result stale");
        requests.Complete(hikari);
        Assert(requests.IsCurrent(neon),
            "A stale completion cannot displace the latest Companion request");
        requests.Complete(neon);
        Assert(!requests.TryBegin("Neon:Happy",out _),
            "Stats updates coalesce after the current Companion asset is applied");
        requests.Invalidate();
        Assert(requests.TryBegin("Neon:Happy",out _),
            "Explicit pack/profile invalidation permits the same request to be rebuilt");
    }
    private static void KeepsMemoryInsideFirmwareLimits()
    {
        const int pool = 64 * 1024;
        const int maximumSingleAsset = 32 * 1024;
        var oldCompanion = 64 * 64 * 2;
        var newCompanion = CompanionRasterPolicy.RenderSize *
            CompanionRasterPolicy.RenderSize * 2;
        var artwork = 64 * 64 * 2;
        Assert(oldCompanion == 8192 && newCompanion == 18432,
            "Companion memory change is measured exactly");
        Assert(newCompanion < maximumSingleAsset &&
               newCompanion + artwork < pool &&
               newCompanion + artwork + artwork < pool,
            "96x96 Companion plus artwork and replacement headroom fit the 64 KiB pool");
    }

    private static void UsesProductMicrocopy()
    {
        foreach (var mood in Enum.GetValues<CompanionMood>())
        {
            var copy = CompanionMicrocopy.For(mood);
            Assert(!string.IsNullOrWhiteSpace(copy) &&
                   !string.Equals(copy, mood.ToString(), StringComparison.OrdinalIgnoreCase),
                "Companion states use deterministic product microcopy rather than enum names");
        }
    }

    private static byte[] Canvas(int width, int height) => new byte[width * height * 4];

    private static void FillAlpha(byte[] pixels, int stridePixels,
        int x, int y, int width, int height, byte alpha)
    {
        for (var row = y; row < y + height; row++)
            for (var column = x; column < x + width; column++)
                pixels[(row * stridePixels + column) * 4 + 3] = alpha;
    }

    private static bool Contains(CompanionCrop crop, CompanionPixelBounds bounds) =>
        crop.X <= bounds.X && crop.Y <= bounds.Y &&
        crop.X + crop.Width >= bounds.Right &&
        crop.Y + crop.Height >= bounds.Bottom;

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }
}

