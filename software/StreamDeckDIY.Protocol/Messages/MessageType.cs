namespace StreamDeckDIY.Protocol.Messages;

public enum MessageType : byte
{
    GetDeviceInfo = 0x01,
    SetBinding = 0x02,
    GetBinding = 0x03,
    ExecuteActionTest = 0x04,
    BeginConfigUpdate = 0x05,
    CommitConfigUpdate = 0x06,
    CancelConfigUpdate = 0x07,
    EnterBootloader = 0x08,
    DisplayLinkCommand = 0x20,
    Ack = 0x70,
    Nack = 0x71,
    DeviceInfo = 0x81,
    BindingInfo = 0x82,
    DisplayLinkResponse = 0x83,
    HostActionTriggered = 0x90,
    DisplayLinkEvent = 0x91,
}
