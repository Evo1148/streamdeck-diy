namespace StreamDeckDIY.Core.Profiles.Automation;

public sealed class AutoProfileService : IAutoProfileService
{
    private readonly IProfileAutomationStore store;
    private readonly IProfileService profiles;
    private readonly IForegroundApplicationWatcher watcher;
    private readonly SemaphoreSlim settingsLock = new(1, 1);
    private readonly object processingSync = new();
    private ProfileAutomationSettings settings = new(false, 1, []);
    private ForegroundApplication? pendingApplication;
    private bool hasPendingApplication;
    private Task processingTask = Task.CompletedTask;
    private bool processing;
    private string? lastFeedbackKey;

    public AutoProfileService(
        IProfileAutomationStore store,
        IProfileService profiles,
        IForegroundApplicationWatcher watcher)
    {
        this.store = store;
        this.profiles = profiles;
        this.watcher = watcher;
        watcher.ApplicationChanged += OnApplicationChanged;
    }

    public event EventHandler<AutoProfileFeedbackEventArgs>? FeedbackAvailable;
    public event EventHandler? ProfileActivated;
    public bool AutoSwitchEnabled => settings.AutoSwitchEnabled;
    public IReadOnlyList<ProfileActivationRule> Rules => settings.Rules.ToArray();
    public string? LoadWarning => store.LoadWarning;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        settings = store.Settings;
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        watcher.StartAsync(cancellationToken);

    public Task StopAsync() => watcher.StopAsync();

    public async Task SetEnabledAsync(
        bool enabled, CancellationToken cancellationToken = default)
    {
        await settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (settings.AutoSwitchEnabled == enabled) return;
            var updated = settings with { AutoSwitchEnabled = enabled };
            await store.SaveAsync(updated, cancellationToken);
            settings = updated;
            lastFeedbackKey = null;
        }
        finally
        {
            settingsLock.Release();
        }
        if (enabled) await EvaluateCurrentAsync(cancellationToken);
    }

    public async Task<ProfileActivationRule> CreateRuleAsync(
        string processName, uint profileId, bool enabled,
        CancellationToken cancellationToken = default)
    {
        await settingsLock.WaitAsync(cancellationToken);
        try
        {
            var canonical = ProcessNameNormalizer.Canonicalize(processName);
            EnsureProfileExists(profileId);
            EnsureNoDuplicate(canonical, enabled, null);
            var rule = new ProfileActivationRule(
                settings.NextId, canonical, profileId, enabled);
            var updated = settings with
            {
                NextId = settings.NextId + 1,
                Rules = [.. settings.Rules, rule],
            };
            await store.SaveAsync(updated, cancellationToken);
            settings = updated;
            return rule;
        }
        finally
        {
            settingsLock.Release();
        }
    }

    public async Task UpdateRuleAsync(
        ProfileActivationRule rule,
        CancellationToken cancellationToken = default)
    {
        await settingsLock.WaitAsync(cancellationToken);
        try
        {
            var index = Array.FindIndex(settings.Rules, value => value.Id == rule.Id);
            if (index < 0) throw new KeyNotFoundException("La regla no existe.");
            var canonical = ProcessNameNormalizer.Canonicalize(rule.ProcessName);
            EnsureProfileExists(rule.ProfileId);
            EnsureNoDuplicate(canonical, rule.Enabled, rule.Id);
            var rules = settings.Rules.ToArray();
            rules[index] = rule with { ProcessName = canonical };
            var updated = settings with { Rules = rules };
            await store.SaveAsync(updated, cancellationToken);
            settings = updated;
        }
        finally
        {
            settingsLock.Release();
        }
    }

    public async Task DeleteRuleAsync(
        uint id, CancellationToken cancellationToken = default)
    {
        await settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (settings.Rules.All(rule => rule.Id != id)) return;
            var updated = settings with
            {
                Rules = settings.Rules.Where(rule => rule.Id != id).ToArray(),
            };
            await store.SaveAsync(updated, cancellationToken);
            settings = updated;
        }
        finally
        {
            settingsLock.Release();
        }
    }

    public async Task EvaluateCurrentAsync(CancellationToken cancellationToken = default)
    {
        var application = await watcher.GetCurrentAsync(cancellationToken);
        if (application is null) return;
        await ObserveAsync(application);
    }

    public Task ObserveAsync(ForegroundApplication application)
    {
        lock (processingSync)
        {
            pendingApplication = application;
            hasPendingApplication = true;
            if (!processing)
            {
                processing = true;
                processingTask = ProcessPendingAsync();
            }
            return processingTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        watcher.ApplicationChanged -= OnApplicationChanged;
        await watcher.DisposeAsync();
        Task pending;
        lock (processingSync) pending = processingTask;
        await pending;
        settingsLock.Dispose();
    }

    private async Task ProcessPendingAsync()
    {
        await Task.Yield();
        while (true)
        {
            ForegroundApplication application;
            lock (processingSync)
            {
                if (!hasPendingApplication || pendingApplication is null)
                {
                    processing = false;
                    return;
                }
                application = pendingApplication;
                pendingApplication = null;
                hasPendingApplication = false;
            }
            try
            {
                await ApplyAsync(application);
            }
            catch (Exception exception)
            {
                PublishOnce("evaluation:failed",
                    $"No se pudo evaluar el cambio automático: {exception.Message}");
            }
        }
    }

    private async Task ApplyAsync(ForegroundApplication application)
    {
        if (!settings.AutoSwitchEnabled) return;
        var process = ProcessNameNormalizer.Normalize(application.ProcessName);
        var rule = settings.Rules.FirstOrDefault(value => value.Enabled &&
            ProcessNameNormalizer.Normalize(value.ProcessName) == process);
        if (rule is null) return;
        var matchKey = $"{process}:{rule.ProfileId}";
        var target = profiles.Profiles.FirstOrDefault(
            profile => profile.Id == rule.ProfileId);
        if (target is null)
        {
            PublishOnce(matchKey + ":missing",
                $"Regla no válida para {rule.ProcessName}: el perfil ya no existe.");
            return;
        }
        if (profiles.ActiveProfile.Id == target.Id)
            return;

        try
        {
            await profiles.SwitchProfileAsync(target.Id);
            lastFeedbackKey = null;
            FeedbackAvailable?.Invoke(this, new AutoProfileFeedbackEventArgs(
                $"Perfil '{target.Name}' activado automáticamente por {rule.ProcessName}."));
            ProfileActivated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            PublishOnce(matchKey + ":failed",
                $"No se pudo activar '{target.Name}' automáticamente: {exception.Message}");
        }
    }

    private void EnsureProfileExists(uint profileId)
    {
        if (profiles.Profiles.All(profile => profile.Id != profileId))
            throw new ArgumentException("El perfil seleccionado no existe.", nameof(profileId));
    }

    private void EnsureNoDuplicate(string processName, bool enabled, uint? ignoredId)
    {
        if (!enabled) return;
        var normalized = ProcessNameNormalizer.Normalize(processName);
        if (settings.Rules.Any(rule => rule.Enabled && rule.Id != ignoredId &&
            ProcessNameNormalizer.Normalize(rule.ProcessName) == normalized))
        {
            throw new InvalidOperationException(
                "Ya existe una regla habilitada para ese proceso.");
        }
    }

    private void PublishOnce(string key, string message)
    {
        if (lastFeedbackKey == key) return;
        lastFeedbackKey = key;
        FeedbackAvailable?.Invoke(this, new AutoProfileFeedbackEventArgs(message));
    }

    private void OnApplicationChanged(
        object? sender, ForegroundApplicationChangedEventArgs eventArgs) =>
        _ = ObserveAsync(eventArgs.Application);
}
