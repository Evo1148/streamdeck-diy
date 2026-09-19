using StreamDeckDIY.Core.Dashboard;

internal static class SystemStatsTests
{
    public static async Task RunAsync()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var initial = SystemStatsState.Unavailable;
        Assert(initial.ProviderStatus is not null &&
               initial.ProviderStatus.Availability == SystemStatsAvailability.Unavailable &&
               initial.CpuUsagePercent is null && initial.GpuUsagePercent is null &&
               initial.RamUsagePercent is null && initial.CpuTemperatureC is null &&
               initial.GpuTemperatureC is null && initial.NetworkDownloadBytesPerSecond is null &&
               initial.DiskActivityPercent is null,
            "initial state is fully unavailable and has a safe provider status");
        Assert(SystemStatsPresentation.Detail(initial) == "CPU --   GPU --   RAM --" &&
               SystemStatsPresentation.HardwareStatus(initial) == "No disponible" &&
               SystemStatsPresentation.CpuSensorStatus(initial) == "No disponible" &&
               SystemStatsPresentation.GpuSensorStatus(initial) == "No disponible" &&
               SystemStatsPresentation.TemperatureSensorStatus(initial) == "No disponible" &&
               !string.IsNullOrWhiteSpace(SystemStatsPresentation.Diagnostic(initial)),
            "dashboard getters are safe before the first statistics sample");

        var native = new FakeWindowsSource(new WindowsSystemStatsSnapshot(
            24, 8UL << 30, 16UL << 30, 2048, 1024, 31,
            300UL << 30, 500UL << 30, timestamp));
        var sensors = new FakeHardwareProvider(new HardwareSensorSnapshot(
            "CPU", 58, 4700, "GPU", 36, 62, 6UL << 30, 16UL << 30));
        var service = new SystemStatsService(native, sensors, TimeSpan.FromHours(1));
        Assert(ReferenceEquals(service.Current, SystemStatsState.Unavailable) &&
               service.Current.ProviderStatus is not null,
            "service exposes a safe state before initialization");
        await service.InitializeAsync();
        var all = service.Current;
        Assert(!ReferenceEquals(all, initial),
            "first sample replaces the initial unavailable state");
        Assert(all.CpuUsagePercent == 24 && all.CpuTemperatureC == 58 &&
               all.GpuUsagePercent == 36 && all.GpuTemperatureC == 62 &&
               all.GpuMemoryUsagePercent == 37.5 && all.RamUsagePercent == 50 &&
               all.NetworkDownloadBytesPerSecond == 2048 && all.DiskActivityPercent == 31 &&
               all.ProviderStatus is { Availability: SystemStatsAvailability.Active },
            "complete snapshot aggregates native and hardware metrics");
        await service.DisposeAsync();
        Assert(native.DisposeCount == 1 && sensors.DisposeCount == 1,
            "provider lifecycle disposes every source exactly once");

        await using (var noHardware = new SystemStatsService(
            new FakeWindowsSource(native.Sample), new FakeHardwareProvider(new()),
            TimeSpan.FromHours(1)))
        {
            await noHardware.InitializeAsync();
            Assert(noHardware.Current.CpuUsagePercent == 24 &&
                   noHardware.Current.GpuUsagePercent is null &&
                   noHardware.Current.ProviderStatus.Availability == SystemStatsAvailability.Partial,
                "native fallback survives unavailable hardware provider");
        }

        await using (var partialHardware = new SystemStatsService(
            new FakeWindowsSource(native.Sample),
            new FakeHardwareProvider(new HardwareSensorSnapshot(
                CpuName: "CPU", CpuTemperatureC: 57)), TimeSpan.FromHours(1)))
        {
            await partialHardware.InitializeAsync();
            Assert(partialHardware.Current.CpuTemperatureC == 57 &&
                   partialHardware.Current.GpuUsagePercent is null &&
                   partialHardware.Current.ProviderStatus.Temperatures == TemperatureAvailability.Partial,
                "partial sensor availability remains explicit");
        }

        await using (var brokenHardware = new SystemStatsService(
            new FakeWindowsSource(native.Sample), new FakeHardwareProvider(new(), throwOnRead: true),
            TimeSpan.FromHours(1)))
        {
            await brokenHardware.InitializeAsync();
            Assert(brokenHardware.Current.RamUsagePercent == 50 &&
                   brokenHardware.Current.GpuTemperatureC is null &&
                   brokenHardware.Current.ProviderStatus.Diagnostic?.Contains("sensor roto") == true &&
                   SystemStatsPresentation.HardwareStatus(brokenHardware.Current) == "Parcial" &&
                   SystemStatsPresentation.GpuSensorStatus(brokenHardware.Current) == "No disponible",
                "hardware exception is isolated from native statistics");
        }

        await using (var invalidCpuTemperature = new SystemStatsService(
            new FakeWindowsSource(native.Sample),
            new FakeHardwareProvider(new HardwareSensorSnapshot(
                CpuName: "CPU", CpuTemperatureC: 0,
                GpuName: "GPU", GpuUsagePercent: 13, GpuTemperatureC: 48,
                GpuMemoryUsedBytes: 6UL << 30, GpuMemoryTotalBytes: 16UL << 30)),
            TimeSpan.FromHours(1)))
        {
            await invalidCpuTemperature.InitializeAsync();
            var state = invalidCpuTemperature.Current;
            Assert(state.CpuTemperatureC is null &&
                   SystemStatsFormatting.FormatTemperature(state.CpuTemperatureC) == "--" &&
                   state.GpuUsagePercent == 13 && state.GpuTemperatureC == 48 &&
                   state.GpuMemoryUsedBytes == (6UL << 30) &&
                   state.GpuMemoryTotalBytes == (16UL << 30),
                "aggregation rejects invalid CPU temperature without affecting GPU or VRAM");
        }

        var changingSensors = new SequenceHardwareProvider(
            new HardwareSensorSnapshot(CpuName: "AMD Ryzen 7 7800X3D", CpuTemperatureC: 0),
            new HardwareSensorSnapshot(CpuName: "AMD Ryzen 7 7800X3D", CpuTemperatureC: 58));
        await using (var changingTemperature = new SystemStatsService(
            new FakeWindowsSource(native.Sample), changingSensors, TimeSpan.FromMilliseconds(10)))
        {
            await changingTemperature.InitializeAsync();
            Assert(changingTemperature.Current.CpuTemperatureC is null,
                "zero CPU sensor remains unavailable on its initial sample");
            await WaitUntilAsync(() => changingTemperature.Current.CpuTemperatureC == 58);
            Assert(changingTemperature.Current.CpuTemperatureC == 58 &&
                   changingSensors.InitializeCount == 1 && changingSensors.ReadCount >= 2,
                "later valid CPU reading appears without recreating the hardware provider");
        }

        var zeroCpuDevices = new HardwareDeviceReading[]
        {
            new(HardwareDeviceKind.Cpu, "AMD Ryzen 7 7800X3D",
                [new("Core (Tctl/Tdie)", HardwareSensorKind.Temperature, 0, 0, 0)])
        };
        var firstDiagnostic = HardwareSensorDiagnostics.CpuTemperature(zeroCpuDevices);
        var repeatedDiagnostic = HardwareSensorDiagnostics.CpuTemperature(zeroCpuDevices);
        Assert(firstDiagnostic == "CPU temp: sensor detectado, lectura inválida (0 °C)." &&
               repeatedDiagnostic == firstDiagnostic &&
               !firstDiagnostic.Contains("candidates", StringComparison.OrdinalIgnoreCase),
            "invalid CPU reading produces a compact stable diagnostic without per-sample dumps");
        Assert(HardwareSensorDiagnostics.CpuTemperature([
                   new(HardwareDeviceKind.Cpu, "CPU",
                       [new("CPU Package", HardwareSensorKind.Temperature, 60)])]) is null,
            "valid CPU temperature clears the invalid-reading diagnostic");

        var cancellingSensors = new CancellationHardwareProvider();
        var cancellingService = new SystemStatsService(
            new FakeWindowsSource(native.Sample), cancellingSensors, TimeSpan.FromMilliseconds(5));
        await cancellingService.InitializeAsync();
        await cancellingSensors.SecondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cancellingService.DisposeAsync();
        Assert(cancellingSensors.CancellationObserved && cancellingSensors.DisposeCount == 1 &&
               cancellingService.Current.ProviderStatus.Diagnostic?.StartsWith(
                   "Cierre de estadísticas:", StringComparison.Ordinal) != true,
            "expected sampling cancellation is consumed as normal shutdown");

        var partialState = new SystemStatsState
        {
            CpuUsagePercent = 42,
            RamUsedBytes = 4UL << 30,
            RamTotalBytes = 8UL << 30,
            ProviderStatus = new(SystemStatsAvailability.Partial, true, false, false, false,
                TemperatureAvailability.Unavailable),
        };
        Assert(SystemStatsPresentation.Detail(partialState) == "CPU 42%   GPU --   RAM 50%" &&
               SystemStatsPresentation.GpuSensorStatus(partialState) == "No disponible" &&
               SystemStatsPresentation.TemperatureSensorStatus(partialState) == "No disponible",
            "partial states keep unavailable GPU and temperatures safe");
        var nullStatusState = new SystemStatsState { ProviderStatus = null! };
        Assert(nullStatusState.ProviderStatus is not null &&
               nullStatusState.ProviderStatus.Availability == SystemStatsAvailability.Unavailable,
            "provider status cannot become null");

        var cpu = new HardwareDeviceReading(HardwareDeviceKind.Cpu, "Generic CPU",
        [
            new("Core #1", HardwareSensorKind.Temperature, 70),
            new("CPU Package", HardwareSensorKind.Temperature, 61),
            new("Core Average", HardwareSensorKind.Temperature, 65),
            new("Core Distance to TjMax", HardwareSensorKind.Temperature, 30),
        ]);
        var integrated = new HardwareDeviceReading(HardwareDeviceKind.GpuIntel, "Integrated",
        [
            new("GPU Core", HardwareSensorKind.Load, 80),
            new("D3D Shared Memory Used", HardwareSensorKind.SmallData, 900),
        ]);
        var discrete = new HardwareDeviceReading(HardwareDeviceKind.GpuNvidia, "Discrete",
        [
            new("GPU Core", HardwareSensorKind.Load, 40),
            new("GPU Core", HardwareSensorKind.Temperature, 64),
            new("GPU Memory Used", HardwareSensorKind.SmallData, 6144),
            new("GPU Memory Total", HardwareSensorKind.SmallData, 16384),
        ]);
        var selected = HardwareSensorSelector.Select([cpu, integrated, discrete]);
        Assert(selected.CpuTemperatureC == 61,
            "CPU package temperature wins over arbitrary core readings");
        Assert(selected.GpuName == "Discrete" && selected.GpuUsagePercent == 40 &&
               selected.GpuTemperatureC == 64 &&
               selected.GpuMemoryUsedBytes == (6UL << 30) &&
               selected.GpuMemoryTotalBytes == (16UL << 30),
            "GPU selection prefers discrete dedicated-memory device and converts VRAM MB to bytes");

        var tctl = new HardwareDeviceReading(HardwareDeviceKind.Cpu, "AMD CPU",
        [
            new("CPU Tctl/Tdie", HardwareSensorKind.Temperature, 59),
            new("Core Average", HardwareSensorKind.Temperature, 55),
        ]);
        Assert(HardwareSensorSelector.SelectCpuTemperature(tctl) == 59,
            "Tctl/Tdie is preferred when CPU Package is unavailable");

        var multipleCpuTrees = HardwareSensorSelector.Select([
            new(HardwareDeviceKind.Cpu, "CPU root",
                [new("CPU Package", HardwareSensorKind.Temperature, null,
                    Path: "CPU root")]),
            new(HardwareDeviceKind.Cpu, "CPU subhardware",
                [new("CPU Tctl", HardwareSensorKind.Temperature, 57,
                    Min: 51, Max: 64, Path: "CPU root/temperature source")]),
            new(HardwareDeviceKind.GpuNvidia, "GPU",
                [new("CPU Package", HardwareSensorKind.Temperature, 88)])
        ]);
        Assert(multipleCpuTrees.CpuTemperatureC == 57 &&
               multipleCpuTrees.CpuName == "CPU root",
            "CPU candidates across discovered CPU hardware paths are combined without using GPU sensors");

        var invalidPackage = new HardwareDeviceReading(HardwareDeviceKind.Cpu, "CPU",
        [
            new("CPU Package", HardwareSensorKind.Temperature, 0),
            new("CPU Tctl/Tdie", HardwareSensorKind.Temperature, 58),
            new("Core Average", HardwareSensorKind.Temperature, 54),
        ]);
        Assert(HardwareSensorSelector.SelectCpuTemperature(invalidPackage) == 58,
            "invalid zero Package reading falls through to the next valid priority");

        var unavailableTemperatures = new HardwareDeviceReading(HardwareDeviceKind.Cpu, "CPU",
        [
            new("CPU Package", HardwareSensorKind.Temperature, 0),
            new("CPU Tctl/Tdie", HardwareSensorKind.Temperature, double.NaN),
            new("Package", HardwareSensorKind.Temperature, double.PositiveInfinity),
            new("Core Average", HardwareSensorKind.Temperature, 0),
        ]);
        Assert(HardwareSensorSelector.SelectCpuTemperature(unavailableTemperatures) is null &&
               SystemStatsFormatting.FormatTemperature(
                   HardwareSensorSelector.SelectCpuTemperature(unavailableTemperatures)) == "--",
            "all invalid CPU temperatures remain unavailable rather than zero");
        Assert(HardwareSensorSelector.SelectCpuTemperature(new(
                   HardwareDeviceKind.Cpu, "CPU",
                   [new("Unknown temperature", HardwareSensorKind.Temperature, 45),
                    new("CPU Package", HardwareSensorKind.Temperature, 130)])) is null,
            "unreliable names and physically implausible CPU values are not published");

        var rate = new NetworkRateCalculator();
        Assert(rate.Add(1000, 500, timestamp) == (null, null),
            "first network counter sample is unavailable rather than zero");
        var calculated = rate.Add(5096, 2548, timestamp.AddSeconds(2));
        Assert(calculated.Download == 2048 && calculated.Upload == 1024,
            "network throughput uses byte counter deltas and elapsed time");
        Assert(SystemStatsFormatting.FormatBytes(6UL << 30) == "6 GB" &&
               SystemStatsFormatting.FormatTransferRate(2 * 1024 * 1024).EndsWith("MB/s") &&
               SystemStatsFormatting.FormatTemperature(null) == "--",
            "VRAM, transfer and unavailable units format consistently");
        Assert(all.DiskActivityPercent == 31 && all.DiskUsagePercent == 60,
            "disk state exposes activity and system-volume usage independently");
        Assert(SystemStatsState.Unavailable.CpuUsagePercent is null &&
               SystemStatsFormatting.FormatPercent(SystemStatsState.Unavailable.CpuUsagePercent) == "--" &&
               !SystemStatsThresholds.IsCritical(SystemStatsState.Unavailable),
            "unavailable is distinct from zero and cannot alert the mascot");
        var alert = new SystemStatsAlertGate();
        Assert(!alert.Update(SystemStatsState.Unavailable) &&
               !alert.Update(new SystemStatsState { CpuTemperatureC = 96 }) &&
               alert.Update(new SystemStatsState { CpuTemperatureC = 96 }) &&
               alert.Update(SystemStatsState.Unavailable) &&
               alert.Update(new SystemStatsState { CpuTemperatureC = 70 }) &&
               alert.Update(new SystemStatsState { CpuTemperatureC = 70 }) &&
               !alert.Update(new SystemStatsState { CpuTemperatureC = 70 }),
            "mascot alert ignores unavailable data and applies entry/recovery hysteresis");

        var options = new DashboardWidgetOptions(ShowCpu: true, ShowGpu: false,
            ShowRam: true, ShowTemperatures: false, ShowNetwork: true, ShowDisk: true);
        Assert(options.ShowCpu && !options.ShowGpu && options.ShowRam &&
               !options.ShowTemperatures && options.ShowNetwork && options.ShowDisk &&
               DashboardLayoutEngine.Build(DashboardConfiguration.CreateDefault(1) with
                   { PresetId = DashboardPresets.System.Id }).Any(widget =>
                       widget.Kind == DashboardWidgetKind.SystemStats),
            "System preset and metric visibility configuration remain renderable");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(1);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
                throw new TimeoutException("Timed out waiting for a System Stats sample.");
            await Task.Delay(10);
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Test failed: {name}");
    }

    private sealed class FakeWindowsSource(WindowsSystemStatsSnapshot sample) : IWindowsSystemStatsSource
    {
        public WindowsSystemStatsSnapshot Sample { get; } = sample;
        public int DisposeCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask<WindowsSystemStatsSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Sample);
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    private sealed class SequenceHardwareProvider(params HardwareSensorSnapshot[] samples)
        : IHardwareSensorProvider
    {
        public int InitializeCount { get; private set; }
        public int ReadCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCount++;
            return Task.CompletedTask;
        }
        public ValueTask<HardwareSensorSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            var index = Math.Min(ReadCount++, samples.Length - 1);
            return ValueTask.FromResult(samples[index]);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellationHardwareProvider : IHardwareSensorProvider
    {
        private int readCount;
        public TaskCompletionSource SecondReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public int DisposeCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public async ValueTask<HardwareSensorSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref readCount) == 1)
                return new(CpuName: "CPU", CpuTemperatureC: 55);
            SecondReadStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHardwareProvider(HardwareSensorSnapshot sample, bool throwOnRead = false)
        : IHardwareSensorProvider
    {
        public int DisposeCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask<HardwareSensorSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            throwOnRead ? ValueTask.FromException<HardwareSensorSnapshot>(new InvalidOperationException("sensor roto"))
                        : ValueTask.FromResult(sample);
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
