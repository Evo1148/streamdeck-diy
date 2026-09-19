#pragma once

#include <cstdint>

namespace streamdeck::display {

enum class IconId : uint16_t {
    Volume = 1,
    Web = 2,
    Settings = 3,
    Folder = 4,
    Keyboard = 5,
    Play = 6,
    Application = 7,
    Sequence = 8,
    Microphone = 9,
    Headphones = 10,
    Music = 11,
    Link = 12,
    Power = 13,
    Tools = 14,
    Left = 15,
    Right = 16,
    Up = 17,
    Down = 18,
    Message = 19,
    Person = 20,
    Alert = 21,
    Success = 22,
    Grid = 23,
    Press = 24,
};

inline constexpr bool is_known_icon(uint16_t value) {
    return value >= static_cast<uint16_t>(IconId::Volume) &&
           value <= static_cast<uint16_t>(IconId::Press);
}

}  // namespace streamdeck::display
