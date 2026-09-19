namespace StreamDeckDIY.Transport.Hid;

internal static class StreamDeckHidIdentity
{
    public const ushort VendorId = 0xCAFE;
    public const ushort ProductId = 0x4004;
    public const ushort UsagePage = 0xFF00;
    public const ushort UsageId = 0x0001;
    public const int ReportSize = 64;
}
