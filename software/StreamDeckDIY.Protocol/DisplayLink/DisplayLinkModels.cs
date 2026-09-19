namespace StreamDeckDIY.Protocol.DisplayLink;

[Flags] public enum DisplayLinkFlags : byte { None=0, AckRequired=1, ResponseError=0x80 }
public enum DisplayLinkOpcode : byte
{
    GetInfo=0x01,GetStatus=0x02,BeginSync=0x03,CommitSync=0x04,CancelSync=0x05,
    BeginUpdate=0x06,CommitUpdate=0x07,DefineNode=0x10,PatchNode=0x11,DeleteNode=0x12,
    SetStringFragment=0x13,DefineTouchRegion=0x20,DeleteTouchRegion=0x21,
    DefineAnimation=0x30,DefineKeyframe=0x31,PlayAnimation=0x32,StopAnimation=0x33,
    DeleteAnimation=0x34,AssetBegin=0x40,AssetChunk=0x41,AssetCommit=0x42,
    AssetRelease=0x43,ClockSync=0x50,Heartbeat=0x51,TouchEvent=0x80,AnimationEvent=0x81,
}
public enum DisplayLinkError : byte
{
    None=0,UnsupportedVersion=1,UnsupportedOpcode=2,UnsupportedProperty=3,
    InvalidGeneration=4,InvalidNode=5,InvalidParent=6,InvalidRegion=7,OutOfBounds=8,
    UnsupportedPrimitive=9,UnsupportedIcon=10,UnsupportedFormat=11,InvalidString=12,
    AssetTooLarge=13,AssetPoolFull=14,TransferNotFound=15,CrcMismatch=16,NoMemory=17,
    Busy=18,TouchUnavailable=19,InvalidState=20,InvalidAnimation=21,TooManyNodes=22,
    TooManyRegions=23,TooManyAnimations=24,
}
public enum DisplayBackendType : byte { Null=0,Physical=1 }
public enum DisplayPixelFormat : byte { Rgb565=1 }
public enum DisplayMode : byte { Offline=0,Dashboard=1,Scene=2 }
public sealed record DisplayLinkEnvelope(byte Major,byte Minor,DisplayLinkOpcode Opcode,DisplayLinkFlags Flags,uint Generation);
public sealed record DisplayLinkResponse(DisplayLinkEnvelope Envelope,byte[] Body);
public sealed record DisplayLinkInfo(byte Major,byte Minor,ushort Width,ushort Height,DisplayPixelFormat PixelFormat,
    DisplayBackendType Backend,bool PhysicalDisplayReady,bool TouchReady,uint PrimitiveCapabilities,
    uint AnimationCapabilities,ushort IconCatalogVersion,ushort FontCatalogVersion,ushort MaxNodes,
    ushort MaxTouchRegions,ushort MaxAnimations,byte MaxKeyframes,ushort MaxStringBytes,
    uint AssetPoolBytes,uint MaxSingleAssetBytes,uint BootSessionId);
public sealed record DisplayLinkStatus(uint ActiveGeneration,uint StagingGeneration,DisplayMode Mode,bool HostOnline,
    ushort NodeCount,ushort TouchRegionCount,ushort AnimationCount,uint AssetBytesUsed,byte ActiveTransfers,
    DisplayLinkError LastError,byte BackendState,uint SceneCrc);
public sealed record DisplayTouchEvent(uint Generation,uint EventId,ushort RegionId,byte Gesture,short X,short Y,uint Timestamp);
public sealed class DisplayLinkException(DisplayLinkError error,string message):Exception(message){public DisplayLinkError Error{get;}=error;}
