using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using StreamDeckDIY.Core.Dashboard;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsSystemStatsProvider : IWindowsSystemStatsSource
{
    private readonly NetworkRateCalculator networkRates = new();
    private readonly WindowsDiskActivityCounter diskActivity = new();
    private NetworkInterface[] adapters = [];
    private ulong previousIdle;
    private ulong previousKernel;
    private ulong previousUser;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        adapters = DiscoverAdapters();
        _ = ReadCpuUsage();
        return Task.CompletedTask;
    }

    public ValueTask<WindowsSystemStatsSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timestamp = DateTimeOffset.UtcNow;
        var diagnostics = new List<string>();
        var cpu = ReadCpuUsage();
        if (cpu is null) diagnostics.Add("CPU nativa no disponible.");

        ulong? ramUsed = null;
        ulong? ramTotal = null;
        var memory = new MemoryStatusEx();
        if (GlobalMemoryStatusEx(memory))
        {
            ramTotal = memory.TotalPhysical;
            ramUsed = memory.TotalPhysical - Math.Min(memory.TotalPhysical, memory.AvailablePhysical);
        }
        else diagnostics.Add("Memoria nativa no disponible.");

        if (adapters.Length == 0) adapters = DiscoverAdapters();
        double? download = null;
        double? upload = null;
        try
        {
            if (adapters.Length == 0)
                diagnostics.Add("No hay adaptadores de red físicos activos.");
            else
            {
                ulong received = 0;
                ulong sent = 0;
                foreach (var adapter in adapters)
                {
                    var statistics = adapter.GetIPStatistics();
                    received += (ulong)Math.Max(0, statistics.BytesReceived);
                    sent += (ulong)Math.Max(0, statistics.BytesSent);
                }
                (download, upload) = networkRates.Add(received, sent, timestamp);
            }
        }
        catch (Exception exception)
        { diagnostics.Add($"Red: {exception.Message}"); }

        ulong? diskUsed = null;
        ulong? diskTotal = null;
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    diskTotal = (ulong)drive.TotalSize;
                    diskUsed = (ulong)(drive.TotalSize - drive.AvailableFreeSpace);
                }
            }
        }
        catch (IOException exception) { diagnostics.Add($"Disco: {exception.Message}"); }
        catch (UnauthorizedAccessException exception) { diagnostics.Add($"Disco: {exception.Message}"); }

        return ValueTask.FromResult(new WindowsSystemStatsSnapshot(
            cpu, ramUsed, ramTotal, download, upload, diskActivity.Read(),
            diskUsed, diskTotal, timestamp,
            diagnostics.Count == 0 ? null : string.Join(" ", diagnostics)));
    }

    public ValueTask DisposeAsync()
    {
        diskActivity.Dispose();
        return ValueTask.CompletedTask;
    }

    private double? ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var currentIdle = ToUInt64(idle);
        var currentKernel = ToUInt64(kernel);
        var currentUser = ToUInt64(user);
        double? result = null;
        if (previousKernel != 0 && currentKernel >= previousKernel &&
            currentUser >= previousUser && currentIdle >= previousIdle)
        {
            var total = currentKernel - previousKernel + currentUser - previousUser;
            var idleDelta = currentIdle - previousIdle;
            if (total > 0 && total >= idleDelta)
                result = Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
        }
        previousIdle = currentIdle;
        previousKernel = currentKernel;
        previousUser = currentUser;
        return result;
    }

    private static NetworkInterface[] DiscoverAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(adapter =>
            adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) &&
            !IsVirtual(adapter)).ToArray();

    private static bool IsVirtual(NetworkInterface adapter)
    {
        var identity = $"{adapter.Name} {adapter.Description}";
        string[] ignored = ["virtual", "vmware", "hyper-v", "vethernet", "loopback",
            "bluetooth", "npcap", "container", "tunnel"];
        return ignored.Any(value => identity.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}

internal sealed class WindowsDiskActivityCounter : IDisposable
{
    private const uint PdhFormatDouble = 0x00000200;
    private IntPtr query;
    private IntPtr counter;

    public WindowsDiskActivityCounter()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0 ||
            PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\% Disk Time",
                IntPtr.Zero, out counter) != 0)
        {
            Dispose();
            return;
        }
        _ = PdhCollectQueryData(query);
    }

    public double? Read()
    {
        if (query == IntPtr.Zero || counter == IntPtr.Zero || PdhCollectQueryData(query) != 0)
            return null;
        if (PdhGetFormattedCounterValue(counter, PdhFormatDouble, out _, out var value) != 0 ||
            value.Status != 0 || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return null;
        return Math.Clamp(value.Value, 0, 100);
    }

    public void Dispose()
    {
        if (query == IntPtr.Zero) return;
        _ = PdhCloseQuery(query);
        query = IntPtr.Zero;
        counter = IntPtr.Zero;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath,
        IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format,
        out uint type, out PdhFormattedCounterValue value);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValue
    {
        public uint Status;
        public double Value;
    }
}
