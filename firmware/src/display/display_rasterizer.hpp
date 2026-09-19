#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>

#include "display/display_assets.hpp"
#include "display/display_scene.hpp"

namespace streamdeck::display {

class DisplayRasterizer {
public:
    static constexpr uint16_t width = 480;
    static constexpr uint16_t height = 320;
    static constexpr uint16_t strip_height = 8;

    void render_strip(const Scene& scene, const AssetPool* assets,
                      const Rect& strip, uint16_t* pixels,
                      std::size_t pixel_count);
    void render_waiting_strip(const Rect& strip, uint16_t* pixels,
                              std::size_t pixel_count) const;

    static Rect clip_to_screen(const Rect& rect);
    static bool intersects(const Rect& left, const Rect& right);
    static bool icon_supported(uint16_t icon_id);
    static constexpr int32_t font_scale(uint8_t font_size) {
        return std::max<int32_t>(1, (font_size + 6) / 7);
    }
    static constexpr int32_t glyph_advance(uint8_t font_size) {
        return 6 * font_scale(font_size);
    }
    static constexpr std::size_t text_capacity(int16_t width, uint8_t font_size) {
        return width > 0 ? std::size_t((width + font_scale(font_size)) /
            glyph_advance(font_size)) : 0;
    }
    struct ResolvedNode {
        const Node* node{};
        Rect bounds{};
        Rect clip{};
        int32_t z{};
        uint8_t opacity{};
        int16_t rotation{};
        bool visible{};
    };
private:
    std::array<ResolvedNode, MAX_NODES> resolved_{};
};

}  // namespace streamdeck::display
