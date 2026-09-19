#include <array>
#include <cassert>
#include <cstdint>
#include <cstring>

#include "display/display_rasterizer.hpp"
#include "display/display_icon_catalog.hpp"

using namespace streamdeck::display;

namespace {
uint32_t crc32(const uint8_t* data, std::size_t length) {
    uint32_t value = 0xFFFFFFFFu;
    for (std::size_t i = 0; i < length; ++i) {
        value ^= data[i];
        for (int bit = 0; bit < 8; ++bit)
            value = (value >> 1) ^ (0xEDB88320u & uint32_t(0 - int(value & 1)));
    }
    return value ^ 0xFFFFFFFFu;
}

Node node(uint16_t id, NodeKind kind, Rect bounds, int16_t z, uint16_t fill,
          uint16_t parent = 0) {
    Node value{};
    value.id = id; value.parent = parent; value.kind = kind; value.bounds = bounds;
    value.z = z; value.fill = fill; value.flags = 1; value.opacity = 255;
    return value;
}
}

int main() {
    Scene scene;
    scene.clear(1, Mode::Dashboard);
    assert(scene.define_node(node(1, NodeKind::Rectangle, {0, 0, 480, 320}, -100, 0x001F)) == Error::None);
    auto panel = node(2, NodeKind::Rectangle, {10, 10, 40, 30}, 1, 0xF800);
    panel.corner_radius = 8; panel.stroke = 0xFFFF; panel.stroke_width = 2;
    assert(scene.define_node(panel) == Error::None);
    assert(scene.define_node(node(3, NodeKind::Circle, {60, 10, 30, 30}, 2, 0x07E0)) == Error::None);
    auto line = node(4, NodeKind::Line, {100, 10, 31, 21}, 3, 0xFFE0);
    line.stroke = 0xFFE0; line.stroke_width = 2;
    assert(scene.define_node(line) == Error::None);
    auto progress = node(5, NodeKind::Progress, {10, 50, 100, 10}, 4, 0xF81F);
    progress.progress = progress.progress_start = progress.progress_target = 500;
    progress.stroke = 0x2104; progress.corner_radius = 3;
    assert(scene.define_node(progress) == Error::None);
    assert(scene.define_node(node(6, NodeKind::Group, {140, 20, 80, 50}, 10, 0)) == Error::None);
    assert(scene.define_node(node(7, NodeKind::Rectangle, {5, 5, 20, 20}, 1, 0x07FF, 6)) == Error::None);
    auto text = node(8, NodeKind::Text, {10, 70, 180, 24}, 20, 0xFFFF);
    text.font_size = 10; text.font_id = 2;
    assert(scene.define_node(text) == Error::None);
    const char message[] = u8"Español: áéíóú ¿¡";
    assert(scene.set_string(8, 0, sizeof(message) - 1,
        reinterpret_cast<const uint8_t*>(message), sizeof(message) - 1) == Error::None);
    auto icon = node(9, NodeKind::Icon, {200, 70, 32, 32}, 21, 0xFFFF);
    icon.icon_id = 99;
    assert(scene.define_node(icon) == Error::None);

    AssetPool assets;
    const uint8_t image[] = {0x00,0xF8, 0xE0,0x07, 0x1F,0x00, 0xFF,0xFF};
    assert(assets.begin(1, 12, AssetFormat::Rgb565Raw, 2, 2, sizeof(image), crc32(image, sizeof(image))) == Error::None);
    assert(assets.chunk(1, 0, image, sizeof(image)) == Error::None);
    assert(assets.commit(1) == Error::None);
    auto image_node = node(10, NodeKind::Image, {240, 70, 20, 20}, 22, 0);
    image_node.asset_id = 12;
    assert(scene.define_node(image_node) == Error::None);

    DisplayRasterizer renderer;
    std::array<uint16_t, 480 * 8> strip{};
    renderer.render_strip(scene, &assets, {0, 8, 480, 8}, strip.data(), strip.size());
    auto pixel = [&](int x, int y) { return strip[std::size_t(y - 8) * 480 + x]; };
    assert(pixel(0, 8) == 0x001F);                       // clipping/background
    assert(pixel(10, 10) == 0x001F);                     // rounded corner
    assert(pixel(25, 14) == 0xF800);                     // rectangle fill

    renderer.render_strip(scene, &assets, {0, 20, 480, 8}, strip.data(), strip.size());
    assert(strip[10] == 0xFFFF);                          // rectangle stroke
    assert(strip[std::size_t(25 - 20) * 480 + 75] == 0x07E0); // circle
    assert(strip[115] == 0xFFE0);                         // line

    renderer.render_strip(scene, &assets, {0, 48, 480, 8}, strip.data(), strip.size());
    auto progress_pixel = [&](int x, int y) { return strip[std::size_t(y - 48) * 480 + x]; };
    assert(progress_pixel(20, 54) == 0xF81F);
    assert(progress_pixel(90, 54) == 0x2104);

    renderer.render_strip(scene, &assets, {0, 24, 480, 8}, strip.data(), strip.size());
    auto group_pixel = [&](int x, int y) { return strip[std::size_t(y - 24) * 480 + x]; };
    assert(group_pixel(146, 26) == 0x07FF);               // group local offset
    assert(group_pixel(139, 26) == 0x001F);

    renderer.render_strip(scene, &assets, {0, 72, 480, 8}, strip.data(), strip.size());
    bool text_drawn = false, fallback_icon_drawn = false;
    for (int y = 72; y < 80; ++y) {
        for (int x = 10; x < 190; ++x) if (strip[std::size_t(y - 72) * 480 + x] == 0xFFFF) text_drawn = true;
        for (int x = 200; x < 232; ++x) if (strip[std::size_t(y - 72) * 480 + x] == 0xFFFF) fallback_icon_drawn = true;
    }
    assert(text_drawn);
    assert(fallback_icon_drawn);
    assert(strip[195] == 0x001F);                        // text clipping

    renderer.render_strip(scene, &assets, {240, 70, 20, 8}, strip.data(), strip.size());
    assert(strip[0] == 0xF800);                           // RGB565 asset lookup
    for (uint16_t id = static_cast<uint16_t>(IconId::Volume);
         id <= static_cast<uint16_t>(IconId::Press); ++id)
        assert(DisplayRasterizer::icon_supported(id) && is_known_icon(id));
    assert(!DisplayRasterizer::icon_supported(0));
    assert(!DisplayRasterizer::icon_supported(99));
    assert(DisplayRasterizer::width == 480 && DisplayRasterizer::height == 320);
    assert(DisplayRasterizer::font_scale(7) == 1 &&
           DisplayRasterizer::font_scale(8) == 2 &&
           DisplayRasterizer::font_scale(14) == 2);
    assert(DisplayRasterizer::glyph_advance(14) == 12 &&
           DisplayRasterizer::text_capacity(84, 7) == 14);

    renderer.render_strip(scene, &assets, {240, 80, 20, 10}, strip.data(), strip.size());
    assert(strip[0] == 0x001F);                            // image Fill keeps source aspect policy
    assert(strip[19] == 0xFFFF);

    // A higher-Z overlay replaces the lower rectangle at the same pixel.
    assert(scene.define_node(node(11, NodeKind::Rectangle, {20, 20, 10, 10}, 50, 0xFFFF)) == Error::None);
    renderer.render_strip(scene, &assets, {0, 20, 480, 8}, strip.data(), strip.size());
    assert(strip[20] == 0xFFFF);
}
