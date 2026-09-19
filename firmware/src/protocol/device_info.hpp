#pragma once
#include <cstdint>

namespace streamdeck::protocol {
inline constexpr uint8_t PROTOCOL_VERSION = 1;
inline constexpr uint8_t FIRMWARE_VERSION_MAJOR = 0;
inline constexpr uint8_t FIRMWARE_VERSION_MINOR = 1;
inline constexpr uint8_t FIRMWARE_VERSION_PATCH = 0;

enum class Capability : uint16_t {
    KeyboardHid = 1u << 0,
    ConsumerHid = 1u << 1,
    VendorControl = 1u << 2,
    Display = 1u << 3,
    Touch = 1u << 4,
};

constexpr Capability operator|(Capability left, Capability right) {
    return static_cast<Capability>(static_cast<uint16_t>(left) |
                                   static_cast<uint16_t>(right));
}

struct DeviceInfo {
    uint8_t protocol_version{PROTOCOL_VERSION};
    uint8_t firmware_major{FIRMWARE_VERSION_MAJOR};
    uint8_t firmware_minor{FIRMWARE_VERSION_MINOR};
    uint8_t firmware_patch{FIRMWARE_VERSION_PATCH};
    uint8_t button_count{12};
    uint8_t encoder_count{1};
    Capability capabilities{Capability::KeyboardHid |
                            Capability::ConsumerHid |
                            Capability::VendorControl};
};

inline constexpr uint16_t DEVICE_INFO_PAYLOAD_SIZE = 8;
}
