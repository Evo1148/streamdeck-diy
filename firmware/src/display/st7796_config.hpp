#pragma once

#include <cstddef>
#include <cstdint>

namespace streamdeck::display::st7796 {
inline constexpr uint16_t logical_width = 480;
inline constexpr uint16_t logical_height = 320;
inline constexpr uint32_t spi_frequency_hz = 10'000'000;
inline constexpr uint8_t landscape_madctl = 0x28;  // MV | BGR
inline constexpr uint8_t rgb565_pixel_format = 0x55;
inline constexpr std::size_t transfer_pixels = 128;
inline constexpr uint16_t cooperative_rows = 1;
inline constexpr std::size_t frame_bytes =
    std::size_t(logical_width) * logical_height * 2;
constexpr uint64_t spi_wire_time_us(std::size_t bytes) {
    return (uint64_t(bytes) * 8u * 1'000'000u + spi_frequency_hz - 1u) /
           spi_frequency_hz;
}
constexpr bool chip_selects_exclusive(bool tft_high, bool touch_high) {
    return tft_high || touch_high;
}
constexpr std::size_t transfer_count(std::size_t pixels) {
    return (pixels + transfer_pixels - 1) / transfer_pixels;
}
struct TransferChunk { std::size_t offset; std::size_t count; };
class TransferCursor {
public:
    constexpr explicit TransferCursor(std::size_t pixels) : remaining_(pixels) {}
    constexpr bool next(TransferChunk& chunk) {
        if (remaining_ == 0) return false;
        const std::size_t count = remaining_ < transfer_pixels
                                    ? remaining_ : transfer_pixels;
        chunk = {offset_, count};
        offset_ += count;
        remaining_ -= count;
        return true;
    }
private:
    std::size_t offset_{};
    std::size_t remaining_{};
};

constexpr uint16_t rgb565(uint8_t red, uint8_t green, uint8_t blue) {
    return static_cast<uint16_t>(((red & 0xF8u) << 8) |
                                 ((green & 0xFCu) << 3) | (blue >> 3));
}
inline constexpr uint16_t black = rgb565(0, 0, 0);
inline constexpr uint16_t white = rgb565(255, 255, 255);
inline constexpr uint16_t red = rgb565(255, 0, 0);
inline constexpr uint16_t green = rgb565(0, 255, 0);
inline constexpr uint16_t blue = rgb565(0, 0, 255);

struct AddressWindow { uint16_t x0, y0, x1, y1; };

constexpr bool make_address_window(uint16_t x, uint16_t y,
                                   uint16_t width, uint16_t height,
                                   AddressWindow& result) {
    if (width == 0 || height == 0 || x >= logical_width ||
        y >= logical_height || width > logical_width - x ||
        height > logical_height - y) return false;
    result = {x, y, static_cast<uint16_t>(x + width - 1),
             static_cast<uint16_t>(y + height - 1)};
    return true;
}
}  // namespace streamdeck::display::st7796
