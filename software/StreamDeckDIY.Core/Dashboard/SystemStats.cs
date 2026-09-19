namespace StreamDeckDIY.Core.Dashboard;

public enum SystemStatsAvailability { Active, Partial, Unavailable }
public enum TemperatureAvailability { Available, Partial, Unavailable }

public sealed record SystemStatsProviderStatus(
    SystemStatsAvailability Availability,
    bool NativeAvailable,
    bool HardwareAvailable,
    bool CpuSensorAvailable,
    bool GpuSensorAvailable,
    TemperatureAvailability Temperatures,
    bool PermissionLimited = false,
    string? Diagnostic = null,
    string? CpuName = null,
    string? GpuName = null);

public sealed record SystemStatsState
{
    private SystemStatsProviderStatus providerStatus = CreateUnavailableStatus();

    public double? CpuUsagePercent { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? CpuClockMhz { get; init; }
    public double? GpuUsagePercent { get; init; }
    public double? GpuTemperatureC { get; init; }
    public ulong? GpuMemoryUsedBytes { get; init; }
    public ulong? GpuMemoryTotalBytes { get; init; }
    public ulong? RamUsedBytes { get; init; }
    public ulong? RamTotalBytes { get; init; }
    public double? NetworkDownloadBytesPerSecond { get; init; }
    public double? NetworkUploadBytesPerSecond { get; init; }
    public double? DiskActivityPercent { get; init; }
    public ulong? DiskUsedBytes { get; init; }
    public ulong? DiskTotalBytes { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public SystemStatsProviderStatus ProviderStatus
    {
        get => providerStatus;
        init => providerStatus = value ?? CreateUnavailableStatus();
    }

    public double? RamUsagePercent => Percentage(RamUsedBytes, RamTotalBytes);
    public double? GpuMemoryUsagePercent => Percentage(GpuMemoryUsedBytes, GpuMemoryTotalBytes);
    public double? DiskUsagePercent => Percentage(DiskUsedBytes, DiskTotalBytes);
    public static SystemStatsProviderStatus UnavailableStatus { get; } = CreateUnavailableStatus();
    public static SystemStatsState Unavailable { get; } = new();

    private static SystemStatsProviderStatus CreateUnavailableStatus() => new(
        SystemStatsAvailability.Unavailable, false, false, false, false,
        TemperatureAvailability.Unavailable, Diagnostic: "Estadísticas no inicializadas.");

    private static double? Percentage(ulong? used, ulong? total) =>
        used is ulong value && total is ulong capacity && capacity > 0
            ? Math.Clamp(value * 100d / capacity, 0, 100)
            : null;
}

public static class SystemStatsPresentation
{
    public static string Detail(SystemStatsState state) =>
        $"CPU {SystemStatsFormatting.FormatPercent(state.CpuUsagePercent)}   " +
        $"GPU {SystemStatsFormatting.FormatPercent(state.GpuUsagePercent)}   " +
        $"RAM {SystemStatsFormatting.FormatPercent(state.RamUsagePercent)}";

    public static string HardwareStatus(SystemStatsState state) => state.ProviderStatus.Availability switch
    {
        SystemStatsAvailability.Active => "Activo",
        SystemStatsAvailability.Partial => "Parcial",
        _ => "No disponible",
    };

    public static string CpuSensorStatus(SystemStatsState state) =>
        state.ProviderStatus.CpuSensorAvailable ? "Disponible" : "No disponible";

    public static string GpuSensorStatus(SystemStatsState state) =>
        state.ProviderStatus.GpuSensorAvailable ? "Disponible" : "No disponible";

    public static string TemperatureSensorStatus(SystemStatsState state) => state.ProviderStatus.Temperatures switch
    {
        TemperatureAvailability.Available => "Disponible",
        TemperatureAvailability.Partial => "Parcial",
        _ => "No disponible",
    };

    public static string Diagnostic(SystemStatsState state) => state.ProviderStatus.PermissionLimited
        ? "Algunos sensores requieren permisos elevados"
        : state.ProviderStatus.Diagnostic ?? string.Empty;
}

public sealed record WindowsSystemStatsSnapshot(
    double? CpuUsagePercent,
    ulong? RamUsedBytes,
    ulong? RamTotalBytes,
    double? NetworkDownloadBytesPerSecond,
    double? NetworkUploadBytesPerSecond,
    double? DiskActivityPercent,
    ulong? DiskUsedBytes,
    ulong? DiskTotalBytes,
    DateTimeOffset Timestamp,
    string? Diagnostic = null);

public sealed record HardwareSensorSnapshot(
    string? CpuName = null,
    double? CpuTemperatureC = null,
    double? CpuClockMhz = null,
    string? GpuName = null,
    double? GpuUsagePercent = null,
    double? GpuTemperatureC = null,
    ulong? GpuMemoryUsedBytes = null,
    ulong? GpuMemoryTotalBytes = null,
    bool PermissionLimited = false,
    string? Diagnostic = null);

public static class SystemStatsValueValidation
{
    public static double? Temperature(double? value) =>
        value is double number && double.IsFinite(number) && number > 0 && number <= 125
            ? number : null;
}

public static class HardwareSensorDiagnostics
{
    public static string? CpuTemperature(
        IReadOnlyCollection<HardwareDeviceReading> devices, string? existing = null)
    {
        var sensors = devices.Where(device => device.Kind == HardwareDeviceKind.Cpu)
            .SelectMany(device => device.Sensors)
            .Where(sensor => sensor.Kind == HardwareSensorKind.Temperature)
            .ToArray();
        if (sensors.Any(sensor =>
                SystemStatsValueValidation.Temperature(sensor.Value) is not null))
            return existing;

        var detail = sensors.Length == 0
            ? "CPU temp: sensor no detectado."
            : sensors.Any(sensor => sensor.Value is double value &&
                                    double.IsFinite(value) && value == 0)
                ? "CPU temp: sensor detectado, lectura inválida (0 °C)."
                : "CPU temp: sensor detectado, sin lectura válida.";
        return string.IsNullOrWhiteSpace(existing) ? detail : $"{existing} {detail}";
    }
}

public interface IWindowsSystemStatsSource : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    ValueTask<WindowsSystemStatsSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IHardwareSensorProvider : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    ValueTask<HardwareSensorSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public interface ISystemStatsProvider : IAsyncDisposable
{
    event EventHandler<SystemStatsState>? StateChanged;
    SystemStatsState Current { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemStatsService(
    IWindowsSystemStatsSource windows,
    IHardwareSensorProvider hardware,
    TimeSpan? sampleInterval = null) : ISystemStatsProvider
{
    private readonly TimeSpan interval = sampleInterval ?? TimeSpan.FromSeconds(1);
    private readonly CancellationTokenSource lifetime = new();
    private Task loop = Task.CompletedTask;
    private bool initialized;
    private string? nativeInitializationError;
    private string? hardwareInitializationError;

    public event EventHandler<SystemStatsState>? StateChanged;
    public SystemStatsState Current { get; private set; } = SystemStatsState.Unavailable;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;
        initialized = true;
        try { await windows.InitializeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { nativeInitializationError = exception.Message; }
        try { await hardware.InitializeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { hardwareInitializationError = exception.Message; }
        await SampleAsync(cancellationToken).ConfigureAwait(false);
        loop = RunAsync(lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        await loop.ConfigureAwait(false);
        Exception? disposalError = null;
        try { await hardware.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { disposalError = exception; }
        finally
        {
            try { await windows.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { disposalError ??= exception; }
            lifetime.Dispose();
        }
        if (disposalError is not null)
            Current = Current with { ProviderStatus = Current.ProviderStatus with
                { Diagnostic = $"Cierre de estadísticas: {disposalError.Message}" } };
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await SampleAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the statistics sampling loop is stopped.
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        WindowsSystemStatsSnapshot? native = null;
        HardwareSensorSnapshot? sensors = null;
        string? nativeError = nativeInitializationError;
        string? hardwareError = hardwareInitializationError;
        if (nativeInitializationError is null)
        {
            try { native = await windows.ReadAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { nativeError = exception.Message; }
        }
        if (hardwareInitializationError is null)
        {
            try { sensors = await hardware.ReadAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { hardwareError = exception.Message; }
        }

        var cpuTemperature = SystemStatsValueValidation.Temperature(sensors?.CpuTemperatureC);
        var gpuTemperature = SystemStatsValueValidation.Temperature(sensors?.GpuTemperatureC);
        var nativeAvailable = native is not null;
        var hardwareAvailable = sensors is not null && HasHardwareValue(sensors);
        var cpuSensor = cpuTemperature is not null || sensors?.CpuClockMhz is not null;
        var gpuSensor = sensors?.GpuUsagePercent is not null || gpuTemperature is not null ||
                        sensors?.GpuMemoryTotalBytes is not null;
        var temperatures = (cpuTemperature, gpuTemperature) switch
        {
            (not null, not null) => TemperatureAvailability.Available,
            (not null, null) or (null, not null) => TemperatureAvailability.Partial,
            _ => TemperatureAvailability.Unavailable,
        };
        var availability = nativeAvailable && hardwareAvailable ? SystemStatsAvailability.Active
            : nativeAvailable || hardwareAvailable ? SystemStatsAvailability.Partial
            : SystemStatsAvailability.Unavailable;
        var diagnostic = string.Join(" ", new[]
        {
            native?.Diagnostic, sensors?.Diagnostic,
            nativeError is null ? null : $"Windows: {nativeError}",
            hardwareError is null ? null : $"Sensores: {hardwareError}",
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Current = new SystemStatsState
        {
            CpuUsagePercent = native?.CpuUsagePercent,
            CpuTemperatureC = cpuTemperature,
            CpuClockMhz = sensors?.CpuClockMhz,
            GpuUsagePercent = sensors?.GpuUsagePercent,
            GpuTemperatureC = gpuTemperature,
            GpuMemoryUsedBytes = sensors?.GpuMemoryUsedBytes,
            GpuMemoryTotalBytes = sensors?.GpuMemoryTotalBytes,
            RamUsedBytes = native?.RamUsedBytes,
            RamTotalBytes = native?.RamTotalBytes,
            NetworkDownloadBytesPerSecond = native?.NetworkDownloadBytesPerSecond,
            NetworkUploadBytesPerSecond = native?.NetworkUploadBytesPerSecond,
            DiskActivityPercent = native?.DiskActivityPercent,
            DiskUsedBytes = native?.DiskUsedBytes,
            DiskTotalBytes = native?.DiskTotalBytes,
            Timestamp = native?.Timestamp ?? DateTimeOffset.UtcNow,
            ProviderStatus = new(availability, nativeAvailable, hardwareAvailable, cpuSensor, gpuSensor,
                temperatures, sensors?.PermissionLimited ?? false,
                string.IsNullOrWhiteSpace(diagnostic) ? null : diagnostic,
                sensors?.CpuName, sensors?.GpuName),
        };
        Publish(Current);
    }

    private void Publish(SystemStatsState state)
    {
        var handlers = StateChanged;
        if (handlers is null) return;
        string? subscriberError = null;
        foreach (EventHandler<SystemStatsState> handler in handlers.GetInvocationList())
        {
            try { handler(this, state); }
            catch (Exception exception) { subscriberError ??= exception.Message; }
        }
        if (subscriberError is not null)
            Current = Current with { ProviderStatus = Current.ProviderStatus with
                { Diagnostic = $"Suscriptor de estadísticas: {subscriberError}" } };
    }

    private static bool HasHardwareValue(HardwareSensorSnapshot value) =>
        SystemStatsValueValidation.Temperature(value.CpuTemperatureC) is not null ||
        value.CpuClockMhz is not null || value.GpuUsagePercent is not null ||
        SystemStatsValueValidation.Temperature(value.GpuTemperatureC) is not null ||
        value.GpuMemoryTotalBytes is not null;
}

public sealed class UnavailableSystemStatsProvider : ISystemStatsProvider
{
    public event EventHandler<SystemStatsState>? StateChanged { add { } remove { } }
    public SystemStatsState Current { get; } = SystemStatsState.Unavailable;
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public enum HardwareDeviceKind { Cpu, GpuAmd, GpuNvidia, GpuIntel }
public enum HardwareSensorKind { Temperature, Load, Clock, SmallData }
public sealed record HardwareSensorReading(string Name, HardwareSensorKind Kind, double? Value,
    double? Min = null, double? Max = null, string Path = "");
public sealed record HardwareDeviceReading(HardwareDeviceKind Kind, string Name,
    IReadOnlyList<HardwareSensorReading> Sensors);

public static class HardwareSensorSelector
{
    private const double Mebibyte = 1024d * 1024d;

    public static HardwareSensorSnapshot Select(IReadOnlyCollection<HardwareDeviceReading> devices,
        bool permissionLimited = false, string? diagnostic = null)
    {
        var cpuDevices = devices.Where(device => device.Kind == HardwareDeviceKind.Cpu).ToArray();
        var cpu = cpuDevices.FirstOrDefault();
        var cpuReadings = cpu is null ? null : new HardwareDeviceReading(
            HardwareDeviceKind.Cpu, cpu.Name,
            cpuDevices.SelectMany(device => device.Sensors).ToArray());
        var gpu = devices.Where(device => device.Kind != HardwareDeviceKind.Cpu)
            .OrderByDescending(GpuScore).FirstOrDefault();
        var gpuUsed = Find(gpu, HardwareSensorKind.SmallData,
            "GPU Memory Used", "D3D Dedicated Memory Used");
        var gpuTotal = Find(gpu, HardwareSensorKind.SmallData,
            "GPU Memory Total", "D3D Dedicated Memory Total");
        return new(
            CpuName: cpu?.Name,
            CpuTemperatureC: SelectCpuTemperature(cpuReadings),
            CpuClockMhz: Find(cpuReadings, HardwareSensorKind.Clock, "CPU Core Average", "Core Average"),
            GpuName: gpu?.Name,
            GpuUsagePercent: Find(gpu, HardwareSensorKind.Load,
                "GPU Core", "GPU Total", "D3D 3D", "GPU Render/Compute"),
            GpuTemperatureC: Find(gpu, HardwareSensorKind.Temperature,
                "GPU Core", "GPU Package", "GPU Hot Spot"),
            GpuMemoryUsedBytes: ToBytes(gpuUsed),
            GpuMemoryTotalBytes: ToBytes(gpuTotal),
            PermissionLimited: permissionLimited,
            Diagnostic: diagnostic);
    }

    public static double? SelectCpuTemperature(HardwareDeviceReading? cpu)
    {
        if (cpu is null) return null;
        var temperatures = cpu.Sensors.Where(sensor => sensor.Kind == HardwareSensorKind.Temperature &&
            !sensor.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase) &&
            SystemStatsValueValidation.Temperature(sensor.Value) is not null &&
            CpuTemperatureScore(sensor.Name) > 0).ToArray();
        return temperatures.OrderByDescending(sensor => CpuTemperatureScore(sensor.Name))
            .Select(sensor => sensor.Value).FirstOrDefault();
    }

    private static int CpuTemperatureScore(string name)
    {
        if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase)) return 95;
        if (name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 92;
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("Core Average", StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains("Core Max", StringComparison.OrdinalIgnoreCase)) return 70;
        return 0;
    }

    private static double GpuScore(HardwareDeviceReading gpu)
    {
        var discrete = gpu.Kind is HardwareDeviceKind.GpuAmd or HardwareDeviceKind.GpuNvidia ? 40 : 0;
        var usage = Find(gpu, HardwareSensorKind.Load, "GPU Core", "GPU Total", "D3D 3D") ?? 0;
        var dedicated = Find(gpu, HardwareSensorKind.SmallData,
            "GPU Memory Total", "D3D Dedicated Memory Total") is not null ? 25 : 0;
        return discrete + dedicated + usage / 4;
    }

    private static double? Find(HardwareDeviceReading? device, HardwareSensorKind kind,
        params string[] preferredNames)
    {
        if (device is null) return null;
        foreach (var preferred in preferredNames)
        {
            var exact = device.Sensors.FirstOrDefault(sensor => sensor.Kind == kind &&
                sensor.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (exact?.Value is { } exactValue) return exactValue;
        }
        foreach (var preferred in preferredNames)
        {
            var partial = device.Sensors.FirstOrDefault(sensor => sensor.Kind == kind &&
                sensor.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase));
            if (partial?.Value is { } partialValue) return partialValue;
        }
        return null;
    }

    private static ulong? ToBytes(double? mebibytes) => mebibytes is >= 0
        ? (ulong)Math.Round(mebibytes.Value * Mebibyte)
        : null;
}

public static class SystemStatsFormatting
{
    public static string FormatPercent(double? value) => value is double number ? $"{number:0}%" : "--";
    public static string FormatTemperature(double? value, bool compact = true) => value is double number
        ? compact ? $"{number:0}°" : $"{number:0} °C" : "--";
    public static string FormatBytes(ulong? bytes)
    {
        if (bytes is null) return "--";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
    }
    public static string FormatTransferRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null) return "--";
        var value = Math.Max(0, bytesPerSecond.Value);
        if (value >= 1024 * 1024) return $"{value / (1024 * 1024):0.0} MB/s";
        if (value >= 1024) return $"{value / 1024:0.0} KB/s";
        return $"{value:0} B/s";
    }
}

public sealed class NetworkRateCalculator
{
    private ulong? previousReceived;
    private ulong? previousSent;
    private DateTimeOffset previousTimestamp;

    public (double? Download, double? Upload) Add(
        ulong received, ulong sent, DateTimeOffset timestamp)
    {
        if (previousReceived is null || timestamp <= previousTimestamp)
        {
            Remember(received, sent, timestamp);
            return (null, null);
        }
        var seconds = (timestamp - previousTimestamp).TotalSeconds;
        double? download = received >= previousReceived ? (received - previousReceived.Value) / seconds : null;
        double? upload = sent >= previousSent ? (sent - previousSent.Value) / seconds : null;
        Remember(received, sent, timestamp);
        return (download, upload);
    }

    private void Remember(ulong received, ulong sent, DateTimeOffset timestamp)
    {
        previousReceived = received;
        previousSent = sent;
        previousTimestamp = timestamp;
    }
}

public static class SystemStatsThresholds
{
    public const double WarningTemperatureC = 85;
    public const double CriticalTemperatureC = 95;
    public static bool IsCritical(SystemStatsState state) =>
        state.CpuTemperatureC is >= CriticalTemperatureC ||
        state.GpuTemperatureC is >= CriticalTemperatureC;
}

public sealed class SystemStatsAlertGate(int criticalSamples = 2, int recoverySamples = 3)
{
    private int criticalCount;
    private int recoveryCount;
    public bool IsAlert { get; private set; }

    public bool Update(SystemStatsState state)
    {
        if (state.CpuTemperatureC is null && state.GpuTemperatureC is null)
            return IsAlert;
        if (SystemStatsThresholds.IsCritical(state))
        {
            recoveryCount = 0;
            if (++criticalCount >= criticalSamples) IsAlert = true;
        }
        else
        {
            criticalCount = 0;
            if (IsAlert && ++recoveryCount >= recoverySamples)
            { IsAlert = false; recoveryCount = 0; }
        }
        return IsAlert;
    }
}
