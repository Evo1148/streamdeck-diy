#include "hardware/status_led.hpp"
#include "hardware/hardware_map.hpp"
#include "pico/stdlib.h"
#include "ws2812.pio.h"

static_assert(PICO_DEFAULT_WS2812_PIN == streamdeck::hardware::rgb_led);

namespace streamdeck::hardware {
void StatusLed::init() {
    sm_ = pio_claim_unused_sm(pio_, true);
    const uint offset = pio_add_program(pio_, &ws2812_program);
    ws2812_program_init(pio_, sm_, offset, rgb_led, 800000, false);
}

void StatusLed::show_test_step(uint8_t step) {
    // GRB order, modest brightness (32/255). Calls are spaced by 700 ms,
    // leaving ample low time to latch each WS2812 frame.
    constexpr uint32_t colors[]{0x002000, 0x200000, 0x000020, 0};
    pio_sm_put_blocking(pio_, sm_, colors[step % 4] << 8u);
}
}
