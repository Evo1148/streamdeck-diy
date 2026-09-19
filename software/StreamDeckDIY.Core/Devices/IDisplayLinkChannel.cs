using StreamDeckDIY.Protocol.DisplayLink;
namespace StreamDeckDIY.Core.Devices;
public interface IDisplayLinkChannel
{
 event EventHandler<DisplayTouchEvent>? DisplayTouchReceived;
 event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged
 {
  add { }
  remove { }
 }
 bool IsConnected{get;}
 Task<bool> ReconnectDisplayLinkAsync(CancellationToken cancellationToken=default);
 Task<DisplayLinkResponse> ExchangeDisplayLinkAsync(DisplayLinkOpcode opcode,uint generation,
     ReadOnlyMemory<byte> body=default,DisplayLinkFlags flags=DisplayLinkFlags.AckRequired,
     CancellationToken cancellationToken=default);
 Task SendDisplayLinkOneWayAsync(DisplayLinkOpcode opcode,uint generation,ReadOnlyMemory<byte> body,
     CancellationToken cancellationToken=default);
}
