#pragma once

#include <cstdint>

namespace streamdeck::input {
enum class SystemNavigation : uint8_t { Previous = 9, Home = 10, Next = 11 };

constexpr bool system_navigation_from_button(
    uint8_t index, SystemNavigation& navigation) {
    if (index < static_cast<uint8_t>(SystemNavigation::Previous) ||
        index > static_cast<uint8_t>(SystemNavigation::Next)) {
        return false;
    }
    navigation = static_cast<SystemNavigation>(index);
    return true;
}

constexpr uint16_t system_navigation_region(SystemNavigation navigation) {
    return static_cast<uint16_t>(100 + static_cast<uint8_t>(navigation));
}
}  // namespace streamdeck::input
