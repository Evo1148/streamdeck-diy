using StreamDeckDIY.App.Services;
using StreamDeckDIY.App.ViewModels;
using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Core.Profiles.Automation;
using StreamDeckDIY.Protocol.Models;

internal static class AutomationTests
{
    public static async Task RunAsync()
    {
        Check(ProcessNameNormalizer.Normalize("devenv") ==
              ProcessNameNormalizer.Normalize("devenv.exe"),
            "Process normalization treats name and .exe as equivalent");

        var profiles = new AutomationProfileService();
        var watcher = new AutomationWatcher();
        var store = new AutomationStore();
        await using var service = new AutoProfileService(store, profiles, watcher);
        await service.InitializeAsync();
        await service.CreateRuleAsync("DeVeNv.ExE", 2, true);
        await service.CreateRuleAsync("spotify", 3, true);
        await service.SetEnabledAsync(true);

        profiles.ResetTraffic();
        await service.ObserveAsync(new ForegroundApplication(10, "DEVENV"));
        Check(profiles.ActiveProfile.Id == 2 && profiles.SwitchCalls == 1,
            "Matching is case-insensitive and applies the correct profile");

        await service.ObserveAsync(new ForegroundApplication(11, "unknown.exe"));
        Check(profiles.SwitchCalls == 1,
            "Process without rule does not change profile");
        await service.ObserveAsync(new ForegroundApplication(10, "devenv.exe"));
        await service.ObserveAsync(new ForegroundApplication(10, "DEVENV"));
        Check(profiles.SwitchCalls == 1,
            "Already active and repeated process do not reapply profile");

        await service.ObserveAsync(new ForegroundApplication(12, "spotify.exe"));
        Check(profiles.ActiveProfile.Id == 3 && profiles.SwitchCalls == 2,
            "Application A to B applies their corresponding profiles");

        await service.SetEnabledAsync(false);
        await service.ObserveAsync(new ForegroundApplication(10, "devenv.exe"));
        Check(profiles.ActiveProfile.Id == 3,
            "AutoSwitch OFF does not change profile");
        watcher.Current = new ForegroundApplication(10, "devenv.exe");
        await service.SetEnabledAsync(true);
        Check(profiles.ActiveProfile.Id == 2,
            "OFF to ON evaluates the current foreground application");

        await ThrowsAsync<InvalidOperationException>(
            () => service.CreateRuleAsync("DEVENV", 3, true),
            "Duplicate enabled normalized rule is rejected");

        var missingStore = new AutomationStore(new ProfileAutomationSettings(
            true, 2, [new ProfileActivationRule(1, "missing.exe", 999, true)]));
        var missingProfiles = new AutomationProfileService();
        await using (var missingService = new AutoProfileService(
            missingStore, missingProfiles, new AutomationWatcher()))
        {
            await missingService.InitializeAsync();
            var feedbackCount = 0;
            missingService.FeedbackAvailable += (_, _) => feedbackCount++;
            await missingService.ObserveAsync(
                new ForegroundApplication(1, "missing.exe"));
            await missingService.ObserveAsync(
                new ForegroundApplication(1, "MISSING"));
            Check(missingProfiles.SwitchCalls == 0 && feedbackCount == 1,
                "Missing ProfileId is safe and feedback is not repeated");
        }

        await TestLatestForegroundWinsAsync();
        await TestFailedSwitchAsync();
        await TestRealProfileServiceRegressionAsync();
        await TestPersistenceAsync();
        await TestWatcherLifecycleAsync();
    }

    private static async Task TestLatestForegroundWinsAsync()
    {
        var profiles = new AutomationProfileService { GateSwitch = true };
        var store = new AutomationStore(new ProfileAutomationSettings(true, 3,
        [
            new ProfileActivationRule(1, "app-a.exe", 2, true),
            new ProfileActivationRule(2, "app-c.exe", 3, true),
        ]));
        await using var service = new AutoProfileService(
            store, profiles, new AutomationWatcher());
        await service.InitializeAsync();
        var first = service.ObserveAsync(new ForegroundApplication(1, "app-a"));
        await profiles.SwitchEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var latest = service.ObserveAsync(new ForegroundApplication(3, "app-c.exe"));
        Check(profiles.MaxConcurrentSwitches == 1,
            "Rapid changes never start concurrent profile switches");
        profiles.ReleaseSwitch.TrySetResult();
        await Task.WhenAll(first, latest);
        Check(profiles.ActiveProfile.Id == 3 && profiles.SwitchCalls == 2 &&
              profiles.MaxConcurrentSwitches == 1,
            "Rapid changes finish on the latest valid target");
    }

    private static async Task TestFailedSwitchAsync()
    {
        var profiles = new AutomationProfileService { FailSwitch = true };
        var store = new AutomationStore(new ProfileAutomationSettings(true, 2,
            [new ProfileActivationRule(1, "broken.exe", 2, true)]));
        await using var service = new AutoProfileService(
            store, profiles, new AutomationWatcher());
        await service.InitializeAsync();
        await service.ObserveAsync(new ForegroundApplication(1, "broken"));
        Check(profiles.ActiveProfile.Id == 1,
            "Failed SwitchProfileAsync does not alter active profile");
        profiles.FailSwitch = false;
        await service.ObserveAsync(new ForegroundApplication(1, "BROKEN.EXE"));
        Check(profiles.ActiveProfile.Id == 2 && profiles.SwitchCalls == 2,
            "Failed automatic switch is not recorded and can be retried");
    }

    private static async Task TestRealProfileServiceRegressionAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"StreamDeckDIY.AutoRegression.{Guid.NewGuid():N}");
        try
        {
            var device = new TransactionDevice();
            var button = new ControlId(ControlType.Button, 0);
            var volumeUp = new DeviceAction(
                ActionType.ConsumerControl,
                ConsumerControl: ConsumerControlAction.VolumeUp);
            var mute = new DeviceAction(
                ActionType.ConsumerControl,
                ConsumerControl: ConsumerControlAction.Mute);
            device.SetRuntime(button, volumeUp);
            var profileService = new ProfileService(
                new ProfileStore(Path.Combine(directory, "profiles.json")), device);
            await profileService.InitializeAsync();
            await profileService.SynchronizeWithDeviceAsync();
            var defaultProfile = profileService.ActiveProfile;
            var programming = await profileService.CreateAsync("Programacion");
            await profileService.SwitchProfileAsync(programming.Id);
            await profileService.SaveBindingAsync(programming.Id, button, mute);
            await profileService.SwitchProfileAsync(defaultProfile.Id);

            var profileViewModel = new ProfileViewModel(
                profileService, new NoProfileInteraction());
            await profileViewModel.InitializeAsync(CancellationToken.None);
            profileViewModel.SelectedProfile = profileViewModel.Profiles.Single(
                profile => profile.Id == defaultProfile.Id);

            var autoStore = new AutomationStore(new ProfileAutomationSettings(
                true, 2,
                [new ProfileActivationRule(1, "devenv.exe", programming.Id, true)]));
            await using var auto = new AutoProfileService(
                autoStore, profileService, new AutomationWatcher());
            await auto.InitializeAsync();
            var successAfterCommit = false;
            auto.FeedbackAvailable += (_, args) =>
            {
                successAfterCommit = args.Message.Contains("activado automáticamente") &&
                    profileService.ActiveProfile.Id == programming.Id &&
                    device.Log.LastOrDefault() == "Commit";
            };
            auto.ProfileActivated += (_, _) => profileViewModel.Refresh();
            device.Log.Clear();

            await auto.ObserveAsync(new ForegroundApplication(10, "devenv.exe"));
            Check(device.Log.Count == 17 && device.Log[0] == "Begin" &&
                  device.Log.Skip(1).Take(15).All(entry => entry.StartsWith("Set:")) &&
                  device.Log[^1] == "Commit",
                "Automatic path uses real ProfileService transaction exactly once");
            Check(profileService.ActiveProfile.Id == programming.Id &&
                  device.Runtime(button) == mute && successAfterCommit,
                "Success is published only after Commit and ActiveProfileId update");
            Check(profileViewModel.ActiveProfile?.Id == programming.Id &&
                  profileViewModel.SelectedProfile?.Id == defaultProfile.Id,
                "Automatic activation refreshes ActiveProfile without changing SelectedProfile");

            await profileService.SwitchProfileAsync(defaultProfile.Id);
            device.Log.Clear();
            await auto.ObserveAsync(new ForegroundApplication(20, "other.exe"));
            await auto.ObserveAsync(new ForegroundApplication(10, "DEVENV"));
            Check(profileService.ActiveProfile.Id == programming.Id &&
                  device.Runtime(button) == mute && device.Log.Count == 17,
                "Manual switch away does not suppress a later automatic reapplication");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task TestPersistenceAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"StreamDeckDIY.AutomationTests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profile-automation.json");
        try
        {
            var store = new ProfileAutomationStore(path);
            await store.InitializeAsync();
            Check(!store.Settings.AutoSwitchEnabled && store.Settings.Rules.Length == 0,
                "Missing automation file loads safe defaults");
            await store.SaveAsync(new ProfileAutomationSettings(true, 2,
                [new ProfileActivationRule(1, "spotify.exe", 3, true)]));
            var reloaded = new ProfileAutomationStore(path);
            await reloaded.InitializeAsync();
            Check(reloaded.Settings.AutoSwitchEnabled &&
                  reloaded.Settings.Rules.Single().ProcessName == "spotify.exe" &&
                  File.ReadAllText(path).Contains("\"version\": 1"),
                "Automation rules persist and reload from versioned JSON");

            await File.WriteAllTextAsync(path, "{bad-json");
            var corrupt = new ProfileAutomationStore(path);
            await corrupt.InitializeAsync();
            Check(!corrupt.Settings.AutoSwitchEnabled &&
                  corrupt.Settings.Rules.Length == 0 &&
                  !string.IsNullOrEmpty(corrupt.LoadWarning),
                "Corrupt JSON recovers to safe defaults with feedback");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task TestWatcherLifecycleAsync()
    {
        var provider = new SequenceForegroundProvider();
        await using var watcher = new ForegroundApplicationWatcher(
            provider, TimeSpan.FromMilliseconds(10));
        var changes = 0;
        watcher.ApplicationChanged += (_, _) => changes++;
        await watcher.StartAsync();
        await watcher.StartAsync();
        await Task.Delay(45);
        Check(watcher.IsRunning && changes == 1,
            "Watcher starts once and suppresses repeated process observations");
        await watcher.StopAsync();
        var callsAfterStop = provider.Calls;
        await Task.Delay(25);
        Check(!watcher.IsRunning && provider.Calls == callsAfterStop,
            "Watcher stops cleanly without a leaked polling loop");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action, string name)
        where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Test failed: {name}");
    }

    private sealed class AutomationStore : IProfileAutomationStore
    {
        public AutomationStore(ProfileAutomationSettings? settings = null) =>
            Settings = settings ?? new ProfileAutomationSettings(false, 1, []);
        public ProfileAutomationSettings Settings { get; private set; }
        public string? LoadWarning => null;
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SynchronizeWithDeviceAsync(
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(ProfileAutomationSettings settings,
                              CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class AutomationWatcher : IForegroundApplicationWatcher
    {
        public event EventHandler<ForegroundApplicationChangedEventArgs>? ApplicationChanged
        {
            add { }
            remove { }
        }
        public bool IsRunning { get; private set; }
        public ForegroundApplication? Current { get; set; }
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }
        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
        public Task<ForegroundApplication?> GetCurrentAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AutomationProfileService : IProfileService
    {
        private readonly StreamDeckProfile[] profiles;
        private uint activeId = 1;
        private int concurrentSwitches;

        public AutomationProfileService()
        {
            var bindings = ProfileControls.All.Select(control =>
                new BindingInfo(control, new DeviceAction(ActionType.None))).ToArray();
            profiles =
            [
                new(1, "General", bindings),
                new(2, "Programación", bindings),
                new(3, "Multimedia", bindings),
            ];
        }

        public IReadOnlyList<StreamDeckProfile> Profiles => profiles;
        public StreamDeckProfile ActiveProfile =>
            profiles.Single(profile => profile.Id == activeId);
        public bool IsHome => false;
        public int SwitchCalls { get; private set; }
        public int MaxConcurrentSwitches { get; private set; }
        public bool FailSwitch { get; set; }
        public bool GateSwitch { get; init; }
        public TaskCompletionSource SwitchEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSwitch { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ResetTraffic() => SwitchCalls = 0;
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task SynchronizeWithDeviceAsync(
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<StreamDeckProfile> CreateAsync(
            string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RenameAsync(uint id, string name,
                                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(uint id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task SwitchProfileAsync(
            uint targetId, CancellationToken cancellationToken = default)
        {
            SwitchCalls++;
            var concurrent = Interlocked.Increment(ref concurrentSwitches);
            MaxConcurrentSwitches = Math.Max(MaxConcurrentSwitches, concurrent);
            try
            {
                if (GateSwitch && SwitchCalls == 1)
                {
                    SwitchEntered.TrySetResult();
                    await ReleaseSwitch.Task.WaitAsync(cancellationToken);
                }
                if (FailSwitch) throw new IOException("Simulated switch failure.");
                activeId = targetId;
            }
            finally
            {
                Interlocked.Decrement(ref concurrentSwitches);
            }
        }

        public Task<ProfileBindingSaveResult> SaveBindingAsync(
            uint profileId, ControlId control, DeviceAction action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProfilePage> AddPageAsync(uint profileId, string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task NavigateAsync(uint profileId, SystemNavigation navigation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SelectPageAsync(uint profileId, uint pageId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SequenceForegroundProvider : IForegroundApplicationProvider
    {
        public int Calls { get; private set; }
        public Task<ForegroundApplication?> GetForegroundApplicationAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<ForegroundApplication?>(
                new ForegroundApplication(1, "devenv.exe"));
        }
    }

    private sealed class NoProfileInteraction : IProfileInteraction
    {
        public Task<string?> RequestNameAsync(string title, string initialValue) =>
            Task.FromResult<string?>(null);
        public Task<bool> ConfirmDeleteAsync(string profileName) =>
            Task.FromResult(false);
    }

    private sealed class TransactionDevice : IDeviceService
    {
        private Dictionary<ControlId, DeviceAction> runtime =
            ProfileControls.All.ToDictionary(control => control,
                _ => new DeviceAction(ActionType.None));
        private Dictionary<ControlId, DeviceAction>? staging;

        public event EventHandler<HostActionTriggeredEventArgs>? HostActionTriggered
        {
            add { }
            remove { }
        }
        public bool IsConnected => true;
        public List<string> Log { get; } = [];
        public void SetRuntime(ControlId control, DeviceAction action) =>
            runtime[control] = action;
        public DeviceAction Runtime(ControlId control) => runtime[control];
        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public void Disconnect() { }
        public Task<StreamDeckDeviceInfo> GetDeviceInfoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BindingInfo> GetBindingAsync(
            ControlId control, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BindingInfo(control, runtime[control]));
        public Task SetBindingAsync(
            ControlId control, DeviceAction action,
            CancellationToken cancellationToken = default)
        {
            Log.Add($"Set:{control.Type}:{control.Index}");
            (staging ?? runtime)[control] = action;
            return Task.CompletedTask;
        }
        public Task ExecuteActionTestAsync(
            DeviceAction action, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task BeginConfigUpdateAsync(CancellationToken cancellationToken = default)
        {
            Log.Add("Begin");
            staging = new(runtime);
            return Task.CompletedTask;
        }
        public Task CommitConfigUpdateAsync(CancellationToken cancellationToken = default)
        {
            Log.Add("Commit");
            runtime = staging ?? throw new InvalidOperationException();
            staging = null;
            return Task.CompletedTask;
        }
        public Task CancelConfigUpdateAsync(CancellationToken cancellationToken = default)
        {
            Log.Add("Cancel");
            staging = null;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
