#pragma once
#include "core/events.hpp"

namespace streamdeck {
class LedSelfTest {
public:
    bool poll(uint32_t now, Event& event) {
        // Unsigned subtraction also works across the millisecond counter wrap.
        if (started_ && static_cast<uint32_t>(now - last_) < 700) return false;
        started_ = true;
        last_ = now;
        event = {EventType::LedTestStep, step_, now};
        step_ = static_cast<uint8_t>((step_ + 1) % 4);
        return true;
    }
private:
    bool started_{};
    uint32_t last_{};
    uint8_t step_{};
};
}
