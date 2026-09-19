#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace streamdeck::actions {
class ActionEngine;
class IHostActionSink;
}
namespace streamdeck::config { class ConfigManager; }
namespace streamdeck::usb { class Device; }
namespace streamdeck::display { class DisplayRuntime; }
namespace streamdeck::system { class IBootloaderRequest; }

namespace streamdeck::protocol {
inline constexpr uint16_t PACKET_MAGIC = 0x4453; // "SD" in little-endian.
inline constexpr std::size_t PACKET_SIZE = 64;
inline constexpr std::size_t HEADER_SIZE = 8;
inline constexpr std::size_t MAX_PAYLOAD_SIZE = PACKET_SIZE - HEADER_SIZE;

enum class MessageType : uint8_t {
    GetDeviceInfo = 0x01,
    SetBinding = 0x02,
    GetBinding = 0x03,
    ExecuteActionTest = 0x04,
    BeginConfigUpdate = 0x05,
    CommitConfigUpdate = 0x06,
    CancelConfigUpdate = 0x07,
    EnterBootloader = 0x08,
    Ack = 0x70,
    Nack = 0x71,
    DeviceInfo = 0x81,
    BindingInfo = 0x82,
    HostActionTriggered = 0x90, // Asynchronous device-to-host event.
    DisplayLinkCommand = 0x20,
    DisplayLinkResponse = 0x83,
    DisplayLinkEvent = 0x91,
};

enum class NackReason : uint8_t {
    InvalidPacketSize = 1,
    InvalidMagic,
    UnsupportedVersion,
    UnknownMessageType,
    InvalidPayloadLength,
    UnsupportedMessage,
    InvalidControl,
    InvalidAction,
    ActionBusy,
    UnsupportedAction,
    StorageError,
    InvalidState,
};

struct Services {
    config::ConfigManager* config{};
    actions::ActionEngine* actions{};
    usb::Device* usb{};
    actions::IHostActionSink* host_actions{};
    display::DisplayRuntime* display{};
    uint32_t now_ms{};
    system::IBootloaderRequest* bootloader{};
};

using Packet = std::array<uint8_t, PACKET_SIZE>;

class Protocol {
public:
    void handle(const uint8_t* request, std::size_t length, Packet& response,
                Services services = {}) const;
    void make_host_action_triggered(uint32_t action_id, uint16_t sequence,
                                    Packet& packet) const;
    bool make_display_link_event(const uint8_t* payload,
                                 std::size_t payload_length,
                                 uint16_t sequence, Packet& packet) const;
};
}
