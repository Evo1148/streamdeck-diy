#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace streamdeck::hardware {
// GPIO numbers, not physical header positions. Agreed map v1.0.
inline constexpr uint8_t touch_irq = 0;
inline constexpr uint8_t backlight = 1;
inline constexpr std::array<uint8_t, 4> rows{2, 3, 4, 5};
inline constexpr std::array<uint8_t, 3> columns{6, 7, 8};
inline constexpr uint8_t touch_cs = 9;
inline constexpr uint8_t spi_sck = 10;
inline constexpr uint8_t spi_mosi = 11;
inline constexpr uint8_t spi_miso = 12;
inline constexpr uint8_t tft_cs = 13;
inline constexpr uint8_t tft_dc = 14;
inline constexpr uint8_t tft_reset = 15;
inline constexpr uint8_t rgb_led = 16;
inline constexpr uint8_t encoder_a = 26;
inline constexpr uint8_t encoder_b = 27;
inline constexpr uint8_t encoder_switch = 28;
inline constexpr uint8_t free_pin = 29;
inline constexpr unsigned spi_bus = 1;
inline constexpr std::size_t key_count = rows.size() * columns.size();

constexpr bool valid_map() {
    constexpr std::array<uint8_t, 21> pins{
        touch_irq, backlight, rows[0], rows[1], rows[2], rows[3],
        columns[0], columns[1], columns[2], touch_cs, spi_sck, spi_mosi,
        spi_miso, tft_cs, tft_dc, tft_reset, rgb_led, encoder_a,
        encoder_b, encoder_switch, free_pin};
    for (std::size_t i = 0; i < pins.size(); ++i) {
        if (pins[i] > 29) return false;
        for (std::size_t j = i + 1; j < pins.size(); ++j)
            if (pins[i] == pins[j]) return false;
    }
    return true;
}
static_assert(valid_map(), "GPIO map contains invalid or duplicate pins");
static_assert(key_count == 12);
}
