using StreamDeckDIY.Core.Devices;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Protocol.Models;
using StreamDeckDIY.App.Services;
using StreamDeckDIY.App.ViewModels;
using System.Text.Json;

internal static class ProfileTests
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"StreamDeckDIY.ProfileTests.{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "profiles.json");
        try
        {
            var device = new ProfileTestDevice();
            device.SetRuntime(new ControlId(ControlType.Button, 0),
                new DeviceAction(ActionType.HostAction, HostActionId: 77));
            var store = new ProfileStore(path);
            var service = new ProfileService(store, device);
            await service.InitializeAsync();
            await service.SynchronizeWithDeviceAsync();

            Check(service.Profiles.Count == 1 &&
                  service.ActiveProfile.Name == "Predeterminado" &&
                  service.ActiveProfile.Bindings.Length == 15,
                "First start imports 15 runtime bindings into Predeterminado");
            Check(service.ActiveProfile.Bindings.First(binding =>
                      binding.Control == new ControlId(ControlType.Button, 0))
                  .Action.HostActionId == 77,
                "Profiles retain HostAction references by ID");
            await ThrowsAsync<InvalidOperationException>(
                () => service.DeleteAsync(service.ActiveProfile.Id),
                "Only active profile cannot be deleted");

            var firstId = service.ActiveProfile.Id;
            var copy = await service.CreateAsync("  Programación  ");
            var secondCopy = await service.CreateAsync("General");
            Check(copy.Id != firstId && secondCopy.Id != copy.Id,
                "Created profiles have unique IDs");
            Check(copy.Name == "Programación" &&
                  copy.Bindings.SequenceEqual(service.ActiveProfile.Bindings),
                "New profile trims its name and copies active bindings");

            var copyBindings = copy.Bindings.ToArray();
            await service.RenameAsync(copy.Id, "  Desarrollo  ");
            var renamed = service.Profiles.Single(profile => profile.Id == copy.Id);
            Check(renamed.Name == "Desarrollo" &&
                  renamed.Bindings.SequenceEqual(copyBindings),
                "Rename preserves ID and bindings");
            await ThrowsAsync<ArgumentException>(
                () => service.RenameAsync(copy.Id, "   "),
                "Whitespace profile name is rejected");
            await ThrowsAsync<InvalidOperationException>(
                () => service.DeleteAsync(firstId),
                "Active profile cannot be deleted");

            var reloadedStore = new ProfileStore(path);
            await reloadedStore.InitializeAsync();
            Check(reloadedStore.GetAll().Count == 3 &&
                  reloadedStore.ActiveProfileId == firstId &&
                  File.ReadAllText(path).Contains("\"version\": 2"),
                "profiles.json persists version, active ID and profiles");

            var dashboardFlushCount = 0;
            uint? profileDuringDashboardFlush = null;
            device.Log.Clear();
            service.SetBeforeProfileSwitch(_ =>
                throw new IOException("Simulated dashboard flush failure."));
            device.Log.Clear();
            await ThrowsAsync<IOException>(
                () => service.SwitchProfileAsync(copy.Id),
                "Failed dashboard flush blocks profile switching");
            Check(service.ActiveProfile.Id == firstId && device.Log.Count == 0,
                "Profile switch cannot overtake a failed persistence flush");

            service.SetBeforeProfileSwitch(_ =>
            {
                dashboardFlushCount++;
                profileDuringDashboardFlush = service.ActiveProfile.Id;
                return Task.CompletedTask;
            });
            await service.SwitchProfileAsync(copy.Id);
            Check(device.Log.Count == 17 && device.Log[0] == "Begin" &&
                  device.Log.Skip(1).Take(15).All(value => value.StartsWith("Set:")) &&
                  device.Log[^1] == "Commit",
                "Profile switch sends Begin, 15 SetBinding and Commit");
            Check(service.ActiveProfile.Id == copy.Id && device.CommitCount == 1,
                "Active profile changes only after one successful commit");
            Check(dashboardFlushCount == 1 && profileDuringDashboardFlush == firstId,
                "Dashboard flush completes while the departing profile is still active");

            device.Log.Clear();
            device.FailSetNumber = 4;
            await ThrowsAsync<IOException>(
                () => service.SwitchProfileAsync(secondCopy.Id),
                "Intermediate SetBinding failure is propagated");
            Check(device.Log[^1] == "Cancel" &&
                  service.ActiveProfile.Id == copy.Id,
                "Intermediate failure cancels and preserves active profile");
            device.FailSetNumber = null;

            device.CommitSucceeds = false;
            await ThrowsAsync<IOException>(
                () => service.SwitchProfileAsync(secondCopy.Id),
                "Commit failure is propagated");
            Check(service.ActiveProfile.Id == copy.Id && device.Log[^1] == "Cancel",
                "Active profile remains unchanged when Commit fails");
            device.CommitSucceeds = true;

            var control = new ControlId(ControlType.Button, 2);
            var action = new DeviceAction(ActionType.Keyboard, KeyCode: 0x06);
            var synchronized = await service.SaveBindingAsync(
                copy.Id, control, action);
            Check(synchronized.DeviceSynchronized &&
                  service.ActiveProfile.Bindings.Single(
                      binding => binding.Control == control).Action == action,
                "Successful SaveBinding persists locally and synchronizes the active profile");
            device.FailNormalSet = true;
            var rejected = new DeviceAction(ActionType.KeyboardShortcut, 0x19,
                ActionModifiers.Control | ActionModifiers.Shift);
            var pending = await service.SaveBindingAsync(
                copy.Id, control, rejected);
            Check(pending.DeviceSyncPending &&
                  service.ActiveProfile.Bindings.Single(
                      binding => binding.Control == control).Action == rejected,
                "Device rejection preserves the local binding and reports pending synchronization");
            device.FailNormalSet = false;

            var inactiveControl = new ControlId(ControlType.Button, 0);
            var inactiveAction = new DeviceAction(ActionType.Keyboard, KeyCode: 0x1B);
            var inactiveSave = await service.SaveBindingAsync(
                secondCopy.Id, inactiveControl, inactiveAction);
            Check(!inactiveSave.DeviceSynchronized && !inactiveSave.DeviceSyncPending &&
                  service.ActiveProfile.Id == copy.Id &&
                  service.Profiles.Single(profile => profile.Id == secondCopy.Id)
                      .Bindings.Single(binding => binding.Control == inactiveControl)
                      .Action == inactiveAction,
                "Saving SelectedProfile does not change or synchronize ActiveProfile");

            var persisted = new ProfileStore(path);
            await persisted.InitializeAsync();
            Check(persisted.GetById(copy.Id)!.Bindings.Single(
                      binding => binding.Control == control).Action == rejected &&
                  persisted.GetById(secondCopy.Id)!.Bindings.Single(
                      binding => binding.Control == inactiveControl).Action == inactiveAction,
                "Shortcut and inactive-profile bindings survive a real JSON reload");

            await service.DeleteAsync(secondCopy.Id);
            Check(service.Profiles.All(profile => profile.Id != secondCopy.Id),
                "Inactive profile can be deleted without changing active profile");

            await TestProfileViewModelInteractionAsync();
            await TestLocalInitializationWithoutDeviceAsync(directory);
            await TestRepeatedInitializationAsync(directory);
            await TestActiveProfileFallbackAsync(directory);
            await TestPagingAndMigrationAsync(directory);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task TestPagingAndMigrationAsync(string directory)
    {
        var path = Path.Combine(directory, "paging-profiles.json");
        var bindings = ProfileControls.All.Select(control => new BindingInfo(
            control, control.Type == ControlType.Button
                ? new DeviceAction(ActionType.Keyboard, KeyCode: (byte)(4 + control.Index))
                : new DeviceAction(ActionType.None))).ToArray();
        var legacyDocument = new
        {
            version = 1,
            activeProfileId = 1u,
            nextId = 2u,
            profiles = new[] { new { id = 1u, name = "Legacy", bindings } },
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            legacyDocument, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { WriteIndented = true }));

        var store = new ProfileStore(path);
        var device = new ProfileTestDevice();
        var service = new ProfileService(store, device);
        await service.InitializeAsync();
        var migrated = service.ActiveProfile;
        Check(migrated.Pages.Length == 1 && migrated.ActivePage.Bindings.Length == 9 &&
              migrated.ActivePage.Bindings.Select(value => value.Control.Index)
                  .SequenceEqual(Enumerable.Range(0, 9).Select(value => (byte)value)),
            "v1 migration creates one 3x3 page and preserves bindings 0-8");
        Check(migrated.LegacyBottomRowBindings.Length == 3 &&
              migrated.LegacyBottomRowBindings.Select(value => value.Control.Index)
                  .SequenceEqual(new byte[] { 9, 10, 11 }) &&
              migrated.Bindings.Where(value => value.Control.Type == ControlType.Button &&
                  value.Control.Index >= 9).All(value => value.Action.Type == ActionType.None),
            "v1 migration preserves old 9-11 for recovery and clears runtime system controls");

        device.Log.Clear();
        await service.NavigateAsync(1, SystemNavigation.Next);
        Check(service.ActiveProfile.ActivePageId == 1 && device.Log.Count == 0,
            "Previous and Next are both disabled for a single-page profile");

        var page2 = await service.AddPageAsync(1, "Página 2");
        Check(service.ActiveProfile.Pages.Length == 2 && page2.Bindings.Length == 9,
            "Profile supports multiple pages with exactly nine bindings each");
        device.Log.Clear();
        await service.NavigateAsync(1, SystemNavigation.Previous);
        Check(service.ActiveProfile.ActivePageId == 1 && device.Log.Count == 0,
            "Previous is disabled at the first-page boundary");
        await service.NavigateAsync(1, SystemNavigation.Next);
        Check(service.ActiveProfile.ActivePageId == page2.Id && device.Log.Count == 17,
            "Next activates the following page through one device transaction");
        device.Log.Clear();
        await service.NavigateAsync(1, SystemNavigation.Next);
        Check(service.ActiveProfile.ActivePageId == page2.Id && device.Log.Count == 0,
            "Next is disabled at the last-page boundary");
        await service.NavigateAsync(1, SystemNavigation.Home);
        Check(service.IsHome && device.RuntimeButtons(0, 9).All(action =>
                  action.Type == ActionType.None),
            "Home is a special state and does not execute page bindings");
        await service.NavigateAsync(1, SystemNavigation.Previous);
        Check(!service.IsHome && service.ActiveProfile.ActivePageId == 1,
            "Navigation from Home returns deterministically to the default page");

        var isolated = await service.CreateAsync("Isolated");
        await service.AddPageAsync(isolated.Id, "Only there");
        Check(service.ActiveProfile.Pages.Length == 2 &&
              service.Profiles.Single(profile => profile.Id == isolated.Id).Pages.Length == 3,
            "Page changes remain isolated by profile");
    }

    private static async Task TestProfileViewModelInteractionAsync()
    {
        var service = new ProfileViewModelService();
        var interaction = new ProfileViewModelInteraction
        {
            RequestedName = "Programación renombrada",
        };
        var viewModel = new ProfileViewModel(service, interaction);
        var successfulActivationEvents = 0;
        viewModel.ActiveProfileChanged += (_, _) => successfulActivationEvents++;
        await viewModel.InitializeAsync(CancellationToken.None);

        var inactive = viewModel.Profiles.Single(profile => profile.Id == 2);
        viewModel.SelectedProfile = inactive;
        Check(viewModel.SelectedProfile?.Id == 2 &&
              viewModel.ActiveProfile?.Id == 1 &&
              service.SwitchCount == 0 && service.DeviceTrafficCount == 0,
            "Selecting inactive profile for management does not activate or send traffic");

        service.SetActiveExternally(3);
        viewModel.Refresh();
        Check(viewModel.ActiveProfile?.Id == 3 && viewModel.SelectedProfile?.Id == 2,
            "Automatic active profile refresh preserves SelectedProfile");
        service.SetActiveExternally(1);
        viewModel.Refresh();

        await viewModel.RenameSelectedAsync();
        Check(service.Profiles.Single(profile => profile.Id == 2).Name ==
              "Programación renombrada" && service.ActiveProfile.Id == 1 &&
              service.DeviceTrafficCount == 0,
            "Renaming inactive profile preserves active ID and sends no traffic");

        await viewModel.DeleteSelectedAsync();
        Check(service.Profiles.All(profile => profile.Id != 2) &&
              service.ActiveProfile.Id == 1 && service.DeviceTrafficCount == 0,
            "Selected inactive profile can be deleted without device traffic");

        viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Id == 1);
        await viewModel.DeleteSelectedAsync();
        Check(service.Profiles.Any(profile => profile.Id == 1) &&
              service.ActiveProfile.Id == 1,
            "Deleting active profile remains rejected");

        var target = viewModel.Profiles.Single(profile => profile.Id == 3);
        service.FailSwitch = true;
        await viewModel.ActivateProfileAsync(target);
        Check(successfulActivationEvents == 0 && service.ActiveProfile.Id == 1,
            "Failed profile activation emits no success event for visual feedback");
        service.FailSwitch = false;
        await viewModel.ActivateProfileAsync(target);
        Check(service.ActiveProfile.Id == 3 && viewModel.ActiveProfile?.Id == 3 &&
              service.SwitchCount == 1 && service.DeviceTrafficCount == 17 &&
              successfulActivationEvents == 1,
            "Explicit active profile change uses SwitchProfileAsync");
        Check(!viewModel.IsBusy && viewModel.CanInteract,
            "Profile spinner stops after a failed and successful device operation");
    }

    private static async Task TestLocalInitializationWithoutDeviceAsync(string directory)
    {
        var path = Path.Combine(directory, "offline-profiles.json");
        var seed = new ProfileStore(path);
        await seed.InitializeAsync();
        var bindings = ProfileControls.All.Select(control =>
            new BindingInfo(control, new DeviceAction(ActionType.None))).ToArray();
        await seed.CreateInitialAsync("General", bindings);
        await seed.CreateCopyAsync("Programación", 1);

        var unavailable = new ProfileTestDevice { IsAvailable = false };
        var service = new ProfileService(new ProfileStore(path), unavailable);
        var viewModel = new ProfileViewModel(service, new ProfileViewModelInteraction());
        await viewModel.InitializeAsync(CancellationToken.None);

        Check(viewModel.Profiles.Count == 2 && viewModel.ActiveProfile?.Name == "General" &&
              viewModel.SelectedProfile?.Id == viewModel.ActiveProfile?.Id,
            "Valid profiles.json loads locally without an available device");
        Check(!viewModel.IsBusy && viewModel.CanInteract && unavailable.GetBindingCount == 0,
            "Local profile initialization finishes without HID traffic");

        await ThrowsAsync<IOException>(
            () => service.SynchronizeWithDeviceAsync(),
            "Device synchronization failure remains separate from local loading");
        Check(service.Profiles.Count == 2 && service.ActiveProfile.Name == "General" &&
              !viewModel.IsBusy,
            "Synchronization failure preserves local profiles and idle UI state");

        viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Id == 2);
        Check(viewModel.ActiveProfile?.Id == 1 && viewModel.SelectedProfile?.Id == 2,
            "Offline management selection remains separate from active profile");

        var available = new ProfileTestDevice();
        var onlineService = new ProfileService(new ProfileStore(path), available);
        await onlineService.InitializeAsync();
        await onlineService.SynchronizeWithDeviceAsync();
        Check(onlineService.Profiles.Count == 2 && onlineService.ActiveProfile.Name == "General" &&
              available.GetBindingCount == 0 && available.CommitCount == 1 &&
              available.Log.Count == 17,
            "Existing local profiles are pushed to the device without being overwritten by it");

        var offlineControl = new ControlId(ControlType.Button, 1);
        var offlineAction = new DeviceAction(ActionType.KeyboardShortcut, 0x19,
            ActionModifiers.Control | ActionModifiers.Shift);
        var offlineSave = await service.SaveBindingAsync(
            2, offlineControl, offlineAction);
        Check(!offlineSave.DeviceSynchronized && !offlineSave.DeviceSyncPending,
            "Inactive profile saves locally without requiring the device");
        var offlineReload = new ProfileStore(path);
        await offlineReload.InitializeAsync();
        Check(offlineReload.GetById(2)!.Bindings.Single(
                  binding => binding.Control == offlineControl).Action == offlineAction &&
              offlineReload.ActiveProfileId == 1,
            "Offline SelectedProfile edit survives store recreation without changing ActiveProfileId");
    }

    private static async Task TestRepeatedInitializationAsync(string directory)
    {
        var path = Path.Combine(directory, "repeated-profiles.json");
        var store = new ProfileStore(path);
        await store.InitializeAsync();
        var bindings = ProfileControls.All.Select(control =>
            new BindingInfo(control, new DeviceAction(ActionType.None))).ToArray();
        await store.CreateInitialAsync("General", bindings);
        var service = new ProfileService(new ProfileStore(path), new ProfileTestDevice());
        var viewModel = new ProfileViewModel(service, new ProfileViewModelInteraction());
        await Task.WhenAll(
            viewModel.InitializeAsync(CancellationToken.None),
            viewModel.InitializeAsync(CancellationToken.None));
        await viewModel.InitializeAsync(CancellationToken.None);
        Check(service.Profiles.Count == 1 && service.ActiveProfile.Name == "General" &&
              viewModel.Profiles.Count == 1 && !viewModel.IsBusy,
            "Concurrent and repeated initialization is idempotent, finite and preserves profiles");
    }

    private static async Task TestActiveProfileFallbackAsync(string directory)
    {
        var path = Path.Combine(directory, "fallback-profiles.json");
        var bindings = ProfileControls.All.Select(control =>
            new BindingInfo(control, new DeviceAction(ActionType.None))).ToArray();
        var seed = new ProfileStore(path);
        await seed.InitializeAsync();
        await seed.CreateInitialAsync("General", bindings);
        await seed.CreateCopyAsync("Programación", 1);

        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path,
            json.Replace("\"activeProfileId\": 1", "\"activeProfileId\": 999"));
        var repaired = new ProfileStore(path);
        await repaired.InitializeAsync();
        Check(repaired.ActiveProfileId == repaired.GetAll()[0].Id &&
              File.ReadAllText(path).Contains("\"activeProfileId\": 1"),
            "Missing persisted active profile falls back deterministically and is repaired");

        var emptyPath = Path.Combine(directory, "empty-profiles.json");
        await File.WriteAllTextAsync(emptyPath,
            "{\"version\":1,\"activeProfileId\":0,\"nextId\":1,\"profiles\":[]}");
        var empty = new ProfileStore(emptyPath);
        await empty.InitializeAsync();
        Check(empty.ActiveProfileId is null && empty.GetAll().Count == 0,
            "Zero profiles keeps a null active profile");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action, string name)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Test failed: {name}");
    }

    private sealed class ProfileTestDevice : IDeviceService
    {
        private Dictionary<ControlId, DeviceAction> runtime =
            ProfileControls.All.ToDictionary(control => control,
                _ => new DeviceAction(ActionType.None));
        private Dictionary<ControlId, DeviceAction>? staging;
        private int transactionSetCount;

        public event EventHandler<HostActionTriggeredEventArgs>? HostActionTriggered
        {
            add { }
            remove { }
        }
        public bool IsConnected => IsAvailable;
        public bool IsAvailable { get; set; } = true;
        public int GetBindingCount { get; private set; }
        public List<string> Log { get; } = [];
        public int CommitCount { get; private set; }
        public int? FailSetNumber { get; set; }
        public bool FailNormalSet { get; set; }
        public bool CommitSucceeds { get; set; } = true;

        public void SetRuntime(ControlId control, DeviceAction action) =>
            runtime[control] = action;

        public IEnumerable<DeviceAction> RuntimeButtons(int start, int count) =>
            Enumerable.Range(start, count).Select(index =>
                runtime[new ControlId(ControlType.Button, (byte)index)]);

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public void Disconnect() { }
        public Task<StreamDeckDeviceInfo> GetDeviceInfoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BindingInfo> GetBindingAsync(
            ControlId control, CancellationToken cancellationToken = default)
        {
            GetBindingCount++;
            if (!IsAvailable) throw new IOException("Simulated unavailable device.");
            return Task.FromResult(new BindingInfo(control, runtime[control]));
        }

        public Task SetBindingAsync(
            ControlId control, DeviceAction action,
            CancellationToken cancellationToken = default)
        {
            if (!IsAvailable) throw new IOException("Simulated unavailable device.");
            Log.Add($"Set:{control.Type}:{control.Index}");
            if (staging is not null)
            {
                transactionSetCount++;
                if (FailSetNumber == transactionSetCount)
                    throw new IOException("Simulated SET_BINDING failure.");
                staging[control] = action;
            }
            else
            {
                if (FailNormalSet)
                    throw new IOException("Simulated SET_BINDING failure.");
                runtime[control] = action;
            }
            return Task.CompletedTask;
        }

        public Task ExecuteActionTestAsync(
            DeviceAction action, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task BeginConfigUpdateAsync(
            CancellationToken cancellationToken = default)
        {
            if (!IsAvailable) throw new IOException("Simulated unavailable device.");
            Log.Add("Begin");
            transactionSetCount = 0;
            staging = new(runtime);
            return Task.CompletedTask;
        }

        public Task CommitConfigUpdateAsync(
            CancellationToken cancellationToken = default)
        {
            Log.Add("Commit");
            if (!CommitSucceeds)
                throw new IOException("Simulated COMMIT failure.");
            runtime = staging ?? throw new InvalidOperationException();
            staging = null;
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task CancelConfigUpdateAsync(
            CancellationToken cancellationToken = default)
        {
            Log.Add("Cancel");
            staging = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ProfileViewModelInteraction : IProfileInteraction
    {
        public string? RequestedName { get; init; }
        public Task<string?> RequestNameAsync(string title, string initialValue) =>
            Task.FromResult(RequestedName);
        public Task<bool> ConfirmDeleteAsync(string profileName) =>
            Task.FromResult(true);
    }

    private sealed class ProfileViewModelService : IProfileService
    {
        private readonly List<StreamDeckProfile> profiles;
        private uint activeId = 1;

        public ProfileViewModelService()
        {
            var bindings = ProfileControls.All.Select(control =>
                new BindingInfo(control, new DeviceAction(ActionType.None))).ToArray();
            profiles =
            [
                new(1, "General", bindings.ToArray()),
                new(2, "Programación", bindings.ToArray()),
                new(3, "Juegos", bindings.ToArray()),
            ];
        }

        public IReadOnlyList<StreamDeckProfile> Profiles => profiles.ToArray();
        public StreamDeckProfile ActiveProfile =>
            profiles.Single(profile => profile.Id == activeId);
        public bool IsHome => false;
        public int SwitchCount { get; private set; }
        public int DeviceTrafficCount { get; private set; }
        public bool FailSwitch { get; set; }

        public void SetActiveExternally(uint id) => activeId = id;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SynchronizeWithDeviceAsync(
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<StreamDeckProfile> CreateAsync(
            string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(
            uint id, string name, CancellationToken cancellationToken = default)
        {
            var index = profiles.FindIndex(profile => profile.Id == id);
            profiles[index] = profiles[index] with { Name = name };
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            uint id, CancellationToken cancellationToken = default)
        {
            if (id == activeId)
                throw new InvalidOperationException("Cannot delete active profile.");
            profiles.RemoveAll(profile => profile.Id == id);
            return Task.CompletedTask;
        }

        public Task SwitchProfileAsync(
            uint targetId, CancellationToken cancellationToken = default)
        {
            if (FailSwitch)
                throw new IOException("Simulated profile activation failure.");
            SwitchCount++;
            DeviceTrafficCount += 17;
            activeId = targetId;
            return Task.CompletedTask;
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
}
