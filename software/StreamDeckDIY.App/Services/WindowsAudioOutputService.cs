using System.Runtime.InteropServices;
using StreamDeckDIY.Core.Audio;

namespace StreamDeckDIY.App.Services;

public sealed class WindowsAudioOutputService : IAudioOutputService
{
    private readonly ICoreAudioEndpointApi endpointApi;

    public WindowsAudioOutputService() : this(new CoreAudioEndpointApi()) { }

    internal WindowsAudioOutputService(ICoreAudioEndpointApi endpointApi) =>
        this.endpointApi = endpointApi;

    public Task<IReadOnlyList<AudioOutputDevice>> GetActiveOutputsAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(endpointApi.GetActiveOutputs, cancellationToken);

    public async Task<AudioOutputDevice?> GetDefaultOutputAsync(
        CancellationToken cancellationToken = default)
    {
        var defaultId = await Task.Run(
            endpointApi.GetDefaultOutputId, cancellationToken);
        if (string.IsNullOrWhiteSpace(defaultId)) return null;
        var devices = await GetActiveOutputsAsync(cancellationToken);
        return devices.FirstOrDefault(device => string.Equals(
                   device.Id, defaultId, StringComparison.OrdinalIgnoreCase)) ??
               new AudioOutputDevice(defaultId, defaultId);
    }

    public async Task SetDefaultOutputAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var devices = await GetActiveOutputsAsync(cancellationToken);
        if (!devices.Any(device => string.Equals(
                device.Id, deviceId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AudioOutputUnavailableException(
                "El dispositivo de audio configurado ya no está disponible.");
        }
        await Task.Run(() => endpointApi.SetDefaultOutput(deviceId), cancellationToken);
    }
}

internal interface ICoreAudioEndpointApi
{
    IReadOnlyList<AudioOutputDevice> GetActiveOutputs();
    string? GetDefaultOutputId();
    void SetDefaultOutput(string deviceId);
}

internal sealed class CoreAudioEndpointApi : ICoreAudioEndpointApi
{
    private static readonly Guid DeviceEnumeratorClassId =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid PolicyConfigClientClassId =
        new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private static PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public IReadOnlyList<AudioOutputDevice> GetActiveOutputs()
    {
        var enumerator = CreateDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(
                EDataFlow.Render, DeviceState.Active, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var devices = new List<AudioOutputDevice>((int)count);
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                    var id = GetId(device);
                    devices.Add(new AudioOutputDevice(id, GetFriendlyName(device) ?? id));
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }
            return devices
                .OrderBy(device => device.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            ReleaseComObject(collection);
            ReleaseComObject(enumerator);
        }
    }

    public string? GetDefaultOutputId()
    {
        var enumerator = CreateDeviceEnumerator();
        IMMDevice? device = null;
        try
        {
            var result = enumerator.GetDefaultAudioEndpoint(
                EDataFlow.Render, ERole.Console, out device);
            if (result == HResultNotFound) return null;
            Marshal.ThrowExceptionForHR(result);
            return GetId(device);
        }
        finally
        {
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    public void SetDefaultOutput(string deviceId)
    {
        object? policyObject = null;
        try
        {
            // Windows has no public setter for the default endpoint. Keep the
            // commonly used PolicyConfig COM interop isolated in this class.
            var policyType = Type.GetTypeFromCLSID(PolicyConfigClientClassId, true)!;
            policyObject = Activator.CreateInstance(policyType);
            var policy = (IPolicyConfig)policyObject!;
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.Console));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.Multimedia));
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Windows no pudo cambiar la salida de audio predeterminada.", exception);
        }
        finally
        {
            ReleaseComObject(policyObject);
        }
    }

    private static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        var type = Type.GetTypeFromCLSID(DeviceEnumeratorClassId, true)!;
        return (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
    }

    private static string GetId(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out var id));
        return id;
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        var value = default(PropVariant);
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out store));
            Marshal.ThrowExceptionForHR(store.GetValue(ref FriendlyNameKey, out value));
            return value.ValueType == VariantType.String
                ? Marshal.PtrToStringUni(value.PointerValue) : null;
        }
        finally
        {
            PropVariantClear(ref value);
            ReleaseComObject(store);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    private const int HResultNotFound = unchecked((int)0x80070490);

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [Flags]
    private enum DeviceState : uint { Active = 0x00000001 }
    private enum VariantType : ushort { String = 31 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public VariantType ValueType;
        [FieldOffset(8)] public IntPtr PointerValue;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, IntPtr activationParams, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(int accessMode, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out DeviceState state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(IntPtr deviceId, IntPtr format);
        int GetDeviceFormat(IntPtr deviceId, int defaultFormat, IntPtr format);
        int ResetDeviceFormat(IntPtr deviceId);
        int SetDeviceFormat(IntPtr deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod(IntPtr deviceId, int defaultPeriod, IntPtr period, IntPtr minimumPeriod);
        int SetProcessingPeriod(IntPtr deviceId, IntPtr period);
        int GetShareMode(IntPtr deviceId, IntPtr mode);
        int SetShareMode(IntPtr deviceId, IntPtr mode);
        int GetPropertyValue(IntPtr deviceId, IntPtr propertyKey, IntPtr propertyValue);
        int SetPropertyValue(IntPtr deviceId, IntPtr propertyKey, IntPtr propertyValue);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        int SetEndpointVisibility(IntPtr deviceId, int visible);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
