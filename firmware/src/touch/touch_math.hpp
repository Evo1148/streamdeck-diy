#pragma once

#include <cstdint>

#include "touch/touch_calibration.hpp"

namespace streamdeck::touch {
constexpr uint16_t unpack_xpt2046_12bit(uint8_t high, uint8_t low) {
    return static_cast<uint16_t>(
        (((uint16_t(high) << 8) | low) >> 3) & 0x0FFFu);
}

constexpr bool xpt2046_no_response(uint8_t rx0, uint8_t rx1,
                                   uint8_t rx2) {
    return rx0 == 0 && rx1 == 0 && rx2 == 0;
}

constexpr bool irq_active(bool gpio_level_high) {
    return !gpio_level_high;
}

class IrqDebouncer {
public:
    static constexpr uint32_t debounce_ms = 15;

    void reset(bool gpio_level_high, uint32_t now_ms);
    bool update(bool gpio_level_high, uint32_t now_ms, bool& active);

private:
    bool stable_level_high_{true};
    bool candidate_level_high_{true};
    uint32_t candidate_since_ms_{};
};

class TapDetector {
public:
    static constexpr uint32_t maximum_duration_ms = 500;
    static constexpr uint16_t maximum_movement_pixels = 15;

    void begin(ScreenPoint point, uint32_t timestamp_ms);
    void update(ScreenPoint point);
    bool end(uint32_t timestamp_ms, ScreenPoint& point);
    void cancel();
    bool active() const { return active_; }

private:
    ScreenPoint start_{};
    ScreenPoint last_{};
    uint32_t started_ms_{};
    bool moved_too_far_{};
    bool active_{};
};
}
