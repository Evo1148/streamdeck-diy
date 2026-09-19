using System.Security;
using LibreHardwareMonitor.Hardware;
using StreamDeckDIY.Core.Dashboard;

namespace StreamDeckDIY.App.Services;

public sealed class LibreHardwareMonitorSensorProvider : IHardwareSensorProvider
{
    private Computer? computer;
    private bool permissionLimited;
    private string? diagnostic;
    private bool warmupPending = true;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            computer.Open();
            UpdateHardware();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            permissionLimited = true;
            diagnostic = "Algunos sensores requieren permisos elevados.";
            computer?.Close();
            computer = null;
        }
    }, cancellationToken);

    public async ValueTask<HardwareSensorSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        if (computer is null)
            return new(PermissionLimited: permissionLimited, Diagnostic: diagnostic);
        var devices = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateHardware();
            var readings = new List<HardwareDeviceReading>();
            foreach (var item in computer.Hardware)
            {
                try
                {
                    if (MapDevice(item) is { } device) readings.Add(device);
                }
                catch (Exception exception)
                { diagnostic = $"Sensor '{item.Name}': {exception.Message}"; }
            }
            return readings;
        }, cancellationToken);
        var selected = HardwareSensorSelector.Select(devices, permissionLimited,
            HardwareSensorDiagnostics.CpuTemperature(devices, diagnostic));
        if (warmupPending && selected.CpuName is not null && selected.CpuTemperatureC is null)
        {
            warmupPending = false;
            await Task.Delay(150, cancellationToken);
            return await ReadAsync(cancellationToken);
        }
        warmupPending = false;
        return selected;
    }

    public ValueTask DisposeAsync()
    {
        computer?.Close();
        computer = null;
        return ValueTask.CompletedTask;
    }

    private void UpdateHardware()
    {
        if (computer is null) return;
        foreach (var hardware in computer.Hardware)
        {
            try { UpdateRecursive(hardware); }
            catch (Exception exception)
            { diagnostic = $"Sensor '{hardware.Name}': {exception.Message}"; }
        }
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var child in hardware.SubHardware) UpdateRecursive(child);
    }

    private static HardwareDeviceReading? MapDevice(IHardware hardware)
    {
        var kind = hardware.HardwareType switch
        {
            HardwareType.Cpu => HardwareDeviceKind.Cpu,
            HardwareType.GpuAmd => HardwareDeviceKind.GpuAmd,
            HardwareType.GpuNvidia => HardwareDeviceKind.GpuNvidia,
            HardwareType.GpuIntel => HardwareDeviceKind.GpuIntel,
            _ => (HardwareDeviceKind?)null,
        };
        if (kind is null) return null;
        var sensors = EnumerateSensors(hardware, hardware.Name)
            .Select(entry => new HardwareSensorReading(entry.Sensor.Name,
                MapKind(entry.Sensor.SensorType), entry.Sensor.Value,
                entry.Sensor.Min, entry.Sensor.Max, entry.Path)).ToArray();
        return new HardwareDeviceReading(kind.Value, hardware.Name, sensors);
    }

    private static IEnumerable<(ISensor Sensor, string Path)> EnumerateSensors(
        IHardware hardware, string path)
    {
        foreach (var sensor in hardware.Sensors) yield return (sensor, path);
        foreach (var child in hardware.SubHardware)
            foreach (var sensor in EnumerateSensors(child, $"{path}/{child.Name}")) yield return sensor;
    }


    private static HardwareSensorKind MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => HardwareSensorKind.Temperature,
        SensorType.Load => HardwareSensorKind.Load,
        SensorType.Clock => HardwareSensorKind.Clock,
        SensorType.SmallData => HardwareSensorKind.SmallData,
        _ => (HardwareSensorKind)(-1),
    };
}
