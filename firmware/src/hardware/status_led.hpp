#pragma once
#include <cstdint>
#include "hardware/pio.h"

namespace streamdeck::hardware {
class StatusLed {
public:
    void init();
    void show_test_step(uint8_t step);
private:
    PIO pio_ = pio0;
    uint sm_{};
};
}
