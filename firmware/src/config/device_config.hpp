#pragma once
#include <array>
#include <cstdint>
#include <type_traits>

#include "actions/action.hpp"

namespace streamdeck::config {
inline constexpr uint16_t CONFIG_VERSION = 1;

struct EncoderBindings {
    actions::Action clockwise{};
    actions::Action counterclockwise{};
    actions::Action press{};
};

struct DeviceConfig {
    uint16_t config_version{CONFIG_VERSION};
    uint8_t active_profile{};
    uint8_t active_page{};
    std::array<actions::Action, 12> buttons{};
    EncoderBindings encoder{};
};

static_assert(std::is_trivially_copyable_v<DeviceConfig>);
}
