#pragma once
#include <cstdint>

namespace streamdeck::controls {
enum class ControlType : uint8_t {
    Button,
    EncoderClockwise,
    EncoderCounterClockwise,
    EncoderPress,
};

struct Control {
    ControlType type{ControlType::Button};
    uint8_t index{};
};

inline constexpr uint8_t BUTTON_COUNT = 12;
inline constexpr uint8_t ENCODER_COUNT = 1;

constexpr bool is_valid(Control control) {
    switch (control.type) {
    case ControlType::Button:
        return control.index < BUTTON_COUNT;
    case ControlType::EncoderClockwise:
    case ControlType::EncoderCounterClockwise:
    case ControlType::EncoderPress:
        return control.index < ENCODER_COUNT;
    }
    return false;
}
}
