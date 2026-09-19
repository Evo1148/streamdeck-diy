#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include "display/display_backend.hpp"
#include "display/st7796_config.hpp"
#include "display/display_rasterizer.hpp"

namespace streamdeck::display {
struct St7796Performance {
    uint32_t renders{};
    uint32_t full_redraws{};
    uint32_t partial_redraws{};
    uint32_t skipped_renders{};
    uint32_t regions{};
    uint64_t total_render_us{};
    uint64_t max_render_us{};
    uint64_t max_spi_block_us{};
    uint64_t pixel_bytes{};
    uint32_t chip_select_violations{};
};
class St7796DisplayBackend final : public IDisplayBackend {
public:
    using ServiceCallback = void (*)();
    bool init();
    bool set_address_window(uint16_t x, uint16_t y, uint16_t width, uint16_t height);
    void write_pixels(const uint16_t* pixels, std::size_t count);
    void fill(uint16_t color);
    void fill_rectangle(uint16_t x, uint16_t y, uint16_t width,
                        uint16_t height, uint16_t color);
    void draw_crosshair(uint16_t x, uint16_t y, uint16_t color);
    void run_bringup_test();
    void show_waiting_screen();
    void restore_waiting_region(const Rect& region);
    void present(const Scene&, const AssetPool&, bool, const Rect*, std::size_t) override;
    uint8_t state() const override { return initialized_ ? 1 : 0; }
    bool physical() const override { return true; }
    void set_service_callback(ServiceCallback callback) { service_callback_ = callback; }
    void set_render_enabled(bool enabled) { render_enabled_ = enabled; }
    bool render_enabled() const { return render_enabled_; }
    const St7796Performance& performance() const { return performance_; }
private:
    void render_region(const Scene&, const AssetPool&, const Rect&);
    void hardware_reset();
    void command(uint8_t value);
    void command(uint8_t value, const uint8_t* data, std::size_t length);
    void data(const uint8_t* values, std::size_t length);
    void service() const;
    void begin_tft_transfer();
    std::array<uint8_t, st7796::transfer_pixels * 2> transfer_buffer_{};
    std::array<uint16_t, DisplayRasterizer::width * DisplayRasterizer::strip_height> render_strip_{};
    DisplayRasterizer rasterizer_{};
    bool initialized_{};
    bool render_enabled_{true};
    ServiceCallback service_callback_{};
    St7796Performance performance_{};
};
}  // namespace streamdeck::display
