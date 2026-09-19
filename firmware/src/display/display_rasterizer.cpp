#include "display/display_rasterizer.hpp"
#include "display/display_icon_catalog.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>

namespace streamdeck::display {
namespace {

constexpr uint16_t kBlack = 0x0000;
constexpr uint16_t kWhite = 0xFFFF;
constexpr uint16_t kFallback = 0x7BEF;
constexpr double kPi = 3.14159265358979323846;

using ResolvedNode = DisplayRasterizer::ResolvedNode;

struct Glyph {
    char value;
    std::array<uint8_t, 7> rows;
};

constexpr std::array<Glyph, 55> kGlyphs{{
    {' ', {0,0,0,0,0,0,0}}, {'!', {4,4,4,4,4,0,4}},
    {'"', {10,10,10,0,0,0,0}}, {'#', {10,31,10,10,31,10,0}},
    {'$', {4,15,20,14,5,30,4}},
    {'%', {17,2,4,8,17,0,0}}, {'&', {6,9,10,4,21,18,13}},
    {'\'', {4,4,8,0,0,0,0}}, {'(', {2,4,8,8,8,4,2}},
    {')', {8,4,2,2,2,4,8}}, {'*', {0,10,4,31,4,10,0}},
    {'+', {0,4,4,31,4,4,0}}, {',', {0,0,0,0,4,4,8}},
    {'-', {0,0,0,31,0,0,0}}, {'.', {0,0,0,0,0,12,12}},
    {'/', {1,2,4,8,16,0,0}}, {':', {0,12,12,0,12,12,0}},
    {';', {0,12,12,0,4,4,8}}, {'<', {2,4,8,16,8,4,2}},
    {'=', {0,31,0,31,0,0,0}}, {'>', {8,4,2,1,2,4,8}},
    {'?', {14,17,1,2,4,0,4}}, {'@', {14,17,23,21,23,16,14}},
    {'[', {14,8,8,8,8,8,14}}, {'\\', {16,8,4,2,1,0,0}},
    {']', {14,2,2,2,2,2,14}}, {'^', {4,10,17,0,0,0,0}},
    {'_', {0,0,0,0,0,0,31}}, {'`', {8,4,2,0,0,0,0}},
    {'{', {3,4,4,8,4,4,3}}, {'|', {4,4,4,4,4,4,4}},
    {'}', {24,4,4,2,4,4,24}}, {'~', {0,0,9,22,0,0,0}},
    {'0', {14,17,19,21,25,17,14}}, {'1', {4,12,4,4,4,4,14}},
    {'2', {14,17,1,2,4,8,31}}, {'3', {30,1,1,14,1,1,30}},
    {'4', {2,6,10,18,31,2,2}}, {'5', {31,16,16,30,1,1,30}},
    {'6', {14,16,16,30,17,17,14}}, {'7', {31,1,2,4,8,8,8}},
    {'8', {14,17,17,14,17,17,14}}, {'9', {14,17,17,15,1,1,14}},
    {'A', {14,17,17,31,17,17,17}}, {'B', {30,17,17,30,17,17,30}},
    {'C', {14,17,16,16,16,17,14}}, {'D', {30,17,17,17,17,17,30}},
    {'E', {31,16,16,30,16,16,31}}, {'F', {31,16,16,30,16,16,16}},
    {'G', {14,17,16,23,17,17,15}}, {'H', {17,17,17,31,17,17,17}},
    {'I', {14,4,4,4,4,4,14}}, {'J', {7,2,2,2,2,18,12}},
    {'K', {17,18,20,24,20,18,17}}, {'L', {16,16,16,16,16,16,31}},
}};

constexpr std::array<Glyph, 14> kMoreGlyphs{{
    {'M', {17,27,21,21,17,17,17}}, {'N', {17,25,21,19,17,17,17}},
    {'O', {14,17,17,17,17,17,14}}, {'P', {30,17,17,30,16,16,16}},
    {'Q', {14,17,17,17,21,18,13}}, {'R', {30,17,17,30,20,18,17}},
    {'S', {15,16,16,14,1,1,30}}, {'T', {31,4,4,4,4,4,4}},
    {'U', {17,17,17,17,17,17,14}}, {'V', {17,17,17,17,17,10,4}},
    {'W', {17,17,17,21,21,21,10}}, {'X', {17,17,10,4,10,17,17}},
    {'Y', {17,17,10,4,4,4,4}}, {'Z', {31,1,2,4,8,16,31}},
}};

Rect intersection(Rect a, Rect b) {
    const int32_t x0 = std::max<int32_t>(a.x, b.x);
    const int32_t y0 = std::max<int32_t>(a.y, b.y);
    const int32_t x1 = std::min<int32_t>(int32_t(a.x) + a.width,
                                         int32_t(b.x) + b.width);
    const int32_t y1 = std::min<int32_t>(int32_t(a.y) + a.height,
                                         int32_t(b.y) + b.height);
    if (x1 <= x0 || y1 <= y0) return {};
    return {int16_t(x0), int16_t(y0), int16_t(x1 - x0), int16_t(y1 - y0)};
}

uint16_t blend(uint16_t bottom, uint16_t top, uint8_t alpha) {
    if (alpha == 255) return top;
    if (alpha == 0) return bottom;
    const uint32_t br = (bottom >> 11) & 31, bg = (bottom >> 5) & 63, bb = bottom & 31;
    const uint32_t tr = (top >> 11) & 31, tg = (top >> 5) & 63, tb = top & 31;
    const uint32_t r = (br * (255 - alpha) + tr * alpha + 127) / 255;
    const uint32_t g = (bg * (255 - alpha) + tg * alpha + 127) / 255;
    const uint32_t b = (bb * (255 - alpha) + tb * alpha + 127) / 255;
    return uint16_t((r << 11) | (g << 5) | b);
}

uint16_t darken(uint16_t value) {
    return uint16_t(((value >> 1) & 0x7BEF));
}

bool resolve_node(const Scene& scene, const Node& node, ResolvedNode& out) {
    const Node* chain[9]{};
    std::size_t depth = 0;
    const Node* current = &node;
    while (current && depth < std::size(chain)) {
        chain[depth++] = current;
        current = current->parent ? scene.find_node(current->parent) : nullptr;
    }
    if (current || depth == 0) return false;
    int32_t x = 0, y = 0, scale_x = 1000, scale_y = 1000, z = 0;
    uint32_t opacity = 255;
    Rect clip{0, 0, DisplayRasterizer::width, DisplayRasterizer::height};
    bool visible = true;
    for (std::size_t i = depth; i-- > 0;) {
        const Node& part = *chain[i];
        x += (int32_t(part.bounds.x) * scale_x) / 1000;
        y += (int32_t(part.bounds.y) * scale_y) / 1000;
        const int32_t w = std::max<int32_t>(0, (int32_t(part.bounds.width) * scale_x * part.scale_x) / 1000000);
        const int32_t h = std::max<int32_t>(0, (int32_t(part.bounds.height) * scale_y * part.scale_y) / 1000000);
        Rect absolute{int16_t(x), int16_t(y), int16_t(std::min<int32_t>(w, 32767)), int16_t(std::min<int32_t>(h, 32767))};
        if (part.kind == NodeKind::Group) clip = intersection(clip, absolute);
        scale_x = (scale_x * part.scale_x) / 1000;
        scale_y = (scale_y * part.scale_y) / 1000;
        opacity = (opacity * part.opacity + 127) / 255;
        z += part.z;
        visible = visible && (part.flags & 1u) != 0;
    }
    const int32_t w = std::max<int32_t>(0, (int32_t(node.bounds.width) *
        (depth > 1 ? 1000 : node.scale_x)) / 1000);
    const int32_t h = std::max<int32_t>(0, (int32_t(node.bounds.height) *
        (depth > 1 ? 1000 : node.scale_y)) / 1000);
    (void)w; (void)h;
    out = {&node, {int16_t(x), int16_t(y),
            int16_t(std::max<int32_t>(0, (int32_t(node.bounds.width) * scale_x) / 1000)),
            int16_t(std::max<int32_t>(0, (int32_t(node.bounds.height) * scale_y) / 1000))},
           clip, z, uint8_t(opacity), node.rotation, visible};
    if (node.kind != NodeKind::Group) out.clip = intersection(out.clip, out.bounds);
    return true;
}

bool rounded_contains(int32_t x, int32_t y, int32_t w, int32_t h, int32_t radius) {
    if (x < 0 || y < 0 || x >= w || y >= h) return false;
    radius = std::min<int32_t>(radius, std::min(w, h) / 2);
    if (radius <= 0 || (x >= radius && x < w - radius) ||
        (y >= radius && y < h - radius)) return true;
    const int32_t cx = x < radius ? radius - 1 : w - radius;
    const int32_t cy = y < radius ? radius - 1 : h - radius;
    const int32_t dx = x - cx, dy = y - cy;
    return dx * dx + dy * dy <= radius * radius;
}

void inverse_rotate(const ResolvedNode& resolved, int32_t screen_x, int32_t screen_y,
                    int32_t& local_x, int32_t& local_y) {
    local_x = screen_x - resolved.bounds.x;
    local_y = screen_y - resolved.bounds.y;
    if (resolved.rotation == 0) return;
    const double radians = -double(resolved.rotation) * kPi / 180.0;
    const int32_t cosine = int32_t(std::cos(radians) * 32768.0);
    const int32_t sine = int32_t(std::sin(radians) * 32768.0);
    const int32_t cx = resolved.bounds.width / 2;
    const int32_t cy = resolved.bounds.height / 2;
    const int32_t dx = local_x - cx, dy = local_y - cy;
    local_x = cx + int32_t((int64_t(dx) * cosine - int64_t(dy) * sine) / 32768);
    local_y = cy + int32_t((int64_t(dx) * sine + int64_t(dy) * cosine) / 32768);
}

const std::array<uint8_t, 7>& glyph_rows(uint32_t codepoint) {
    static constexpr std::array<uint8_t, 7> fallback{14,17,1,2,4,0,4};
    if (codepoint >= 'a' && codepoint <= 'z') codepoint -= 32;
    switch (codepoint) {
        case 0x00C1: case 0x00E1: codepoint = 'A'; break;
        case 0x00C9: case 0x00E9: codepoint = 'E'; break;
        case 0x00CD: case 0x00ED: codepoint = 'I'; break;
        case 0x00D3: case 0x00F3: codepoint = 'O'; break;
        case 0x00DA: case 0x00FA: case 0x00DC: case 0x00FC: codepoint = 'U'; break;
        case 0x00D1: case 0x00F1: codepoint = 'N'; break;
        case 0x00BF: codepoint = '?'; break;
        case 0x00A1: codepoint = '!'; break;
        default: break;
    }
    for (const auto& glyph : kGlyphs) if (uint8_t(glyph.value) == codepoint) return glyph.rows;
    for (const auto& glyph : kMoreGlyphs) if (uint8_t(glyph.value) == codepoint) return glyph.rows;
    return fallback;
}

uint32_t next_codepoint(const uint8_t* text, std::size_t length, std::size_t& offset) {
    const uint8_t first = text[offset++];
    if (first < 0x80) return first;
    if ((first & 0xE0) == 0xC0 && offset < length)
        return uint32_t(first & 0x1F) << 6 | uint32_t(text[offset++] & 0x3F);
    if ((first & 0xF0) == 0xE0 && offset + 1 < length) {
        const uint32_t value = uint32_t(first & 0x0F) << 12 |
            uint32_t(text[offset] & 0x3F) << 6 | uint32_t(text[offset + 1] & 0x3F);
        offset += 2; return value;
    }
    while (offset < length && (text[offset] & 0xC0) == 0x80) ++offset;
    return '?';
}

std::size_t codepoint_count(const uint8_t* text, std::size_t length) {
    std::size_t count = 0, offset = 0;
    while (offset < length) { next_codepoint(text, length, offset); ++count; }
    return count;
}

void put_pixel(const Rect& strip, uint16_t* pixels, int32_t x, int32_t y,
               uint16_t color, uint8_t opacity) {
    if (x < strip.x || y < strip.y || x >= int32_t(strip.x) + strip.width ||
        y >= int32_t(strip.y) + strip.height) return;
    const std::size_t index = std::size_t(y - strip.y) * strip.width + (x - strip.x);
    pixels[index] = blend(pixels[index], color, opacity);
}

void draw_text(const Scene* scene, const ResolvedNode& resolved, const Rect& strip,
               uint16_t* pixels, const uint8_t* text, std::size_t length,
               uint16_t color, uint8_t font_size, uint8_t alignment, bool bold) {
    if (!text || length == 0) return;
    const int32_t scale = DisplayRasterizer::font_scale(font_size);
    const int32_t glyph_width = 5 * scale;
    const int32_t advance = DisplayRasterizer::glyph_advance(font_size);
    const std::size_t source_count = codepoint_count(text, length);
    const std::size_t capacity = DisplayRasterizer::text_capacity(
        resolved.bounds.width, font_size);
    if (capacity == 0) return;
    const bool ellipsis = source_count > capacity;
    const std::size_t count = std::min(source_count, capacity);
    const std::size_t plain_count = ellipsis
        ? (capacity > 3 ? capacity - 3 : 0) : count;
    const int32_t text_width = int32_t(count) * advance - scale;
    int32_t start_x = resolved.bounds.x;
    if (alignment == 1) start_x += (resolved.bounds.width - text_width) / 2;
    else if (alignment == 2) start_x += resolved.bounds.width - text_width;
    const int32_t start_y = resolved.bounds.y + std::max<int32_t>(0,
        (resolved.bounds.height - 7 * scale) / 2);
    std::size_t offset = 0;
    for (std::size_t index = 0; index < count; ++index) {
        const uint32_t codepoint = ellipsis && index >= plain_count
            ? uint32_t('.') : next_codepoint(text, length, offset);
        const auto& rows = glyph_rows(codepoint);
        const int32_t gx = start_x + int32_t(index) * advance;
        if (gx >= resolved.bounds.x + resolved.bounds.width) break;
        for (int32_t row = 0; row < 7; ++row) for (int32_t column = 0; column < 5; ++column) {
            if ((rows[row] & (1u << (4 - column))) == 0) continue;
            for (int32_t yy = 0; yy < scale; ++yy) for (int32_t xx = 0; xx < scale; ++xx) {
                const int32_t x = gx + column * scale + xx, y = start_y + row * scale + yy;
                if (x < resolved.clip.x || y < resolved.clip.y ||
                    x >= resolved.clip.x + resolved.clip.width || y >= resolved.clip.y + resolved.clip.height) continue;
                put_pixel(strip, pixels, x, y, color, resolved.opacity);
                if (bold && x + 1 < resolved.clip.x + resolved.clip.width)
                    put_pixel(strip, pixels, x + 1, y, color, resolved.opacity);
            }
        }
    }
    (void)scene; (void)glyph_width;
}

bool line_contains(int32_t x, int32_t y, int32_t width, int32_t height, int32_t thickness) {
    const int64_t vx = std::max<int32_t>(0, width - 1);
    const int64_t vy = std::max<int32_t>(0, height - 1);
    const int64_t length2 = vx * vx + vy * vy;
    if (length2 == 0) return x * x + y * y <= thickness * thickness;
    const int64_t projection = std::clamp<int64_t>(x * vx + y * vy, 0, length2);
    const int64_t px = (projection * vx) / length2, py = (projection * vy) / length2;
    const int64_t dx = x - px, dy = y - py;
    return dx * dx + dy * dy <= int64_t(thickness) * thickness;
}

void draw_icon(const ResolvedNode& resolved, const Rect& strip, uint16_t* pixels) {
    const uint16_t color = resolved.node->fill ? resolved.node->fill : kWhite;
    const int32_t w = resolved.bounds.width, h = resolved.bounds.height;
    const int32_t cx = resolved.bounds.x + w / 2, cy = resolved.bounds.y + h / 2;
    const int32_t radius = std::max<int32_t>(2, std::min(w, h) / 3);
    for (int32_t y = std::max<int32_t>(resolved.clip.y, strip.y);
         y < std::min<int32_t>(resolved.clip.y + resolved.clip.height, strip.y + strip.height); ++y) {
        for (int32_t x = std::max<int32_t>(resolved.clip.x, strip.x);
             x < std::min<int32_t>(resolved.clip.x + resolved.clip.width, strip.x + strip.width); ++x) {
            const int32_t dx = x - cx, dy = y - cy;
            bool on = false;
            switch (resolved.node->icon_id) {
                case static_cast<uint16_t>(IconId::Volume): on = (dx >= -radius && dx <= -radius / 2 && std::abs(dy) <= radius / 3) ||
                    (dx > -radius / 2 && dx <= 0 && std::abs(dy) <= radius - dx) ||
                    (dx > 1 && std::abs(dx * dx + dy * dy - radius * radius) <= radius * 2); break;
                case static_cast<uint16_t>(IconId::Web): on = std::abs(dx * dx + dy * dy - radius * radius) <= radius * 2 ||
                    std::abs(dx) <= 1 || std::abs(dy) <= 1; break;
                case static_cast<uint16_t>(IconId::Settings): on = std::abs(std::max(std::abs(dx), std::abs(dy)) - radius) <= 1 ||
                    dx * dx + dy * dy <= 4; break;
                case static_cast<uint16_t>(IconId::Folder): on = (std::abs(dy + radius / 2) <= 1 && std::abs(dx) <= radius) ||
                    (std::abs(dy - radius / 2) <= 1 && std::abs(dx) <= radius) ||
                    (std::abs(dx) == radius && std::abs(dy) <= radius / 2); break;
                case static_cast<uint16_t>(IconId::Keyboard): on = std::abs(std::max(std::abs(dx), std::abs(dy)) - radius) <= 1 ||
                    (dy >= 0 && dy <= radius / 2 && dx % std::max<int32_t>(2, radius / 3) == 0); break;
                case static_cast<uint16_t>(IconId::Play): on = dx >= -radius / 2 && dx <= radius / 2 &&
                    std::abs(dy) <= (dx + radius / 2); break;
                case static_cast<uint16_t>(IconId::Application): on = std::abs(std::max(std::abs(dx), std::abs(dy)) - radius) <= 1 ||
                    (dy == -radius / 2 && std::abs(dx) <= radius); break;
                case static_cast<uint16_t>(IconId::Sequence): on = (dx <= -radius / 2 && std::abs(dy) <= radius && dy % std::max<int32_t>(2, radius / 2) == 0) ||
                    (dx > -radius / 3 && dx <= radius && std::abs(dy) <= radius && dy % std::max<int32_t>(2, radius / 2) == 0); break;
                case static_cast<uint16_t>(IconId::Microphone): on = (std::abs(dx) <= radius / 3 && dy >= -radius && dy <= radius / 3) ||
                    (std::abs(dx) <= radius / 2 && std::abs(dy - radius / 3) <= 1) ||
                    (std::abs(dx) <= 1 && dy >= radius / 3 && dy <= radius); break;
                case static_cast<uint16_t>(IconId::Headphones): on = (dy <= 0 && std::abs(dx * dx + dy * dy - radius * radius) <= radius * 2) ||
                    (std::abs(dx) >= radius - 2 && std::abs(dx) <= radius && dy >= 0 && dy <= radius); break;
                case static_cast<uint16_t>(IconId::Music): on = (std::abs(dx - radius / 3) <= 1 && dy >= -radius && dy <= radius / 2) ||
                    (dy == -radius && dx >= -radius / 2 && dx <= radius / 3) ||
                    ((dx + radius / 3) * (dx + radius / 3) + (dy - radius / 2) * (dy - radius / 2) <= 6); break;
                case static_cast<uint16_t>(IconId::Link): on = (std::abs(dx + dy) <= 1 && std::abs(dx) <= radius) ||
                    (std::abs(dx - dy) >= radius && std::abs(dx - dy) <= radius + 2); break;
                case static_cast<uint16_t>(IconId::Power): on = (dy > -radius / 2 && std::abs(dx * dx + dy * dy - radius * radius) <= radius * 2) ||
                    (std::abs(dx) <= 1 && dy >= -radius && dy <= 0); break;
                case static_cast<uint16_t>(IconId::Tools): on = std::abs(dx - dy) <= 1 || std::abs(dx - dy) == 2 ||
                    ((dx + radius / 2) * (dx + radius / 2) + (dy + radius / 2) * (dy + radius / 2) <= 5); break;
                case static_cast<uint16_t>(IconId::Left): on = (std::abs(dy) <= 1 && dx >= -radius && dx <= radius) ||
                    (dx <= -radius / 3 && std::abs(std::abs(dy) + dx + radius) <= 1); break;
                case static_cast<uint16_t>(IconId::Right): on = (std::abs(dy) <= 1 && dx >= -radius && dx <= radius) ||
                    (dx >= radius / 3 && std::abs(std::abs(dy) - dx + radius) <= 1); break;
                case static_cast<uint16_t>(IconId::Up): on = (std::abs(dx) <= 1 && dy >= -radius && dy <= radius) ||
                    (dy <= -radius / 3 && std::abs(std::abs(dx) + dy + radius) <= 1); break;
                case static_cast<uint16_t>(IconId::Down): on = (std::abs(dx) <= 1 && dy >= -radius && dy <= radius) ||
                    (dy >= radius / 3 && std::abs(std::abs(dx) - dy + radius) <= 1); break;
                case static_cast<uint16_t>(IconId::Message): on = std::abs(std::max(std::abs(dx), std::abs(dy)) - radius) <= 1 ||
                    (dy >= radius - 2 && dx <= -radius / 2); break;
                case static_cast<uint16_t>(IconId::Person): on = dx * dx + (dy + radius / 2) * (dy + radius / 2) <= radius * radius / 9 ||
                    (dy >= 0 && dy <= radius && std::abs(dx) <= radius - dy / 2); break;
                case static_cast<uint16_t>(IconId::Alert): on = dy >= -radius && dy <= radius &&
                    std::abs(dx) <= (dy + radius) / 2 && (dy >= radius - 2 || std::abs(dx) >= (dy + radius) / 2 - 1); break;
                case static_cast<uint16_t>(IconId::Success): on = (dx <= 0 && std::abs(dy - dx) <= 1) ||
                    (dx >= 0 && std::abs(dy + dx / 2) <= 1); break;
                case static_cast<uint16_t>(IconId::Grid): on = (std::abs(dx) == radius || std::abs(dy) == radius ||
                    std::abs(dx) <= 1 || std::abs(dy) <= 1); break;
                case static_cast<uint16_t>(IconId::Press): on = std::abs(dx * dx + dy * dy - radius * radius) <= radius * 2 ||
                    dx * dx + dy * dy <= std::max<int32_t>(2, radius / 4) *
                        std::max<int32_t>(2, radius / 4); break;
                default: on = (std::abs(dx) <= 1 || std::abs(dy) <= 1) &&
                    std::max(std::abs(dx), std::abs(dy)) <= radius; break;
            }
            if (on) put_pixel(strip, pixels, x, y, color, resolved.opacity);
        }
    }
}

}  // namespace

Rect DisplayRasterizer::clip_to_screen(const Rect& rect) {
    return intersection(rect, {0, 0, width, height});
}

bool DisplayRasterizer::intersects(const Rect& left, const Rect& right) {
    return intersection(left, right).width > 0;
}

bool DisplayRasterizer::icon_supported(uint16_t icon_id) {
    return is_known_icon(icon_id);
}

void DisplayRasterizer::render_strip(const Scene& scene, const AssetPool* assets,
                                     const Rect& requested, uint16_t* pixels,
                                     std::size_t pixel_count) {
    const Rect strip = clip_to_screen(requested);
    if (!pixels || strip.width <= 0 || strip.height <= 0 ||
        pixel_count < std::size_t(strip.width) * strip.height) return;
    std::fill_n(pixels, std::size_t(strip.width) * strip.height, kBlack);

    std::size_t count = 0;
    for (const auto& node : scene.nodes()) {
        if (!node.used || node.kind == NodeKind::Group) continue;
        ResolvedNode value{};
        if (!resolve_node(scene, node, value) || !value.visible || value.opacity == 0 ||
            !intersects(value.clip, strip)) continue;
        resolved_[count++] = value;
    }
    for (std::size_t i = 1; i < count; ++i) {
        const ResolvedNode value = resolved_[i];
        std::size_t position = i;
        while (position > 0 && resolved_[position - 1].z > value.z) {
            resolved_[position] = resolved_[position - 1];
            --position;
        }
        resolved_[position] = value;
    }

    for (std::size_t index = 0; index < count; ++index) {
        const ResolvedNode& value = resolved_[index];
        const Node& node = *value.node;
        if (node.kind == NodeKind::Text) {
            const uint8_t* text = nullptr; std::size_t length = 0;
            if (scene.text(node, text, length))
                draw_text(&scene, value, strip, pixels, text, length,
                          node.fill ? node.fill : kWhite, node.font_size ? node.font_size : 8,
                          node.alignment, node.font_id >= 2);
            continue;
        }
        if (node.kind == NodeKind::Icon) { draw_icon(value, strip, pixels); continue; }
        const Rect area = intersection(value.clip, strip);
        const Asset* asset = assets && node.kind == NodeKind::Image ? assets->find(node.asset_id) : nullptr;
        const uint8_t* asset_data = asset ? assets->data(*asset) : nullptr;
        for (int32_t y = area.y; y < area.y + area.height; ++y) for (int32_t x = area.x; x < area.x + area.width; ++x) {
            int32_t lx = 0, ly = 0; inverse_rotate(value, x, y, lx, ly);
            uint16_t color = node.fill; bool draw = false;
            switch (node.kind) {
                case NodeKind::Rectangle: {
                    const bool outer = rounded_contains(lx, ly, value.bounds.width, value.bounds.height, node.corner_radius);
                    if (!outer) break;
                    const int32_t stroke = node.stroke_width;
                    const bool inner = stroke == 0 || rounded_contains(lx - stroke, ly - stroke,
                        value.bounds.width - stroke * 2, value.bounds.height - stroke * 2,
                        std::max<int32_t>(0, node.corner_radius - stroke));
                    color = (!inner && node.stroke_width) ? node.stroke : node.fill; draw = true; break;
                }
                case NodeKind::Circle: {
                    const int32_t rx = std::max<int32_t>(1, value.bounds.width / 2);
                    const int32_t ry = std::max<int32_t>(1, value.bounds.height / 2);
                    const int64_t dx = lx - rx, dy = ly - ry;
                    const int64_t metric = dx * dx * ry * ry + dy * dy * rx * rx;
                    const int64_t outer = int64_t(rx) * rx * ry * ry;
                    if (metric > outer) break;
                    draw = true; color = node.fill;
                    if (node.stroke_width) {
                        const int32_t irx = std::max<int32_t>(0, rx - node.stroke_width);
                        const int32_t iry = std::max<int32_t>(0, ry - node.stroke_width);
                        const int64_t inner = int64_t(irx) * irx * iry * iry;
                        if (irx == 0 || iry == 0 || dx * dx * iry * iry + dy * dy * irx * irx > inner) color = node.stroke;
                    }
                    break;
                }
                case NodeKind::Line: draw = line_contains(lx, ly, value.bounds.width, value.bounds.height,
                    std::max<int32_t>(1, node.stroke_width)); color = node.stroke ? node.stroke : node.fill; break;
                case NodeKind::Progress: {
                    draw = rounded_contains(lx, ly, value.bounds.width, value.bounds.height, node.corner_radius);
                    const int32_t filled = int32_t(value.bounds.width) * std::min<uint16_t>(1000, node.progress) / 1000;
                    color = lx < filled ? (node.fill ? node.fill : kWhite) :
                        (node.stroke ? node.stroke : darken(node.fill ? node.fill : kFallback)); break;
                }
                case NodeKind::Image: {
                    if (!asset || !asset_data || asset->width == 0 || asset->height == 0) {
                        draw = ((lx + ly) % 8 == 0 || (lx - ly) % 8 == 0); color = kFallback; break;
                    }
                    if (lx < 0 || ly < 0 || lx >= value.bounds.width || ly >= value.bounds.height) break;
                    const uint32_t sx = uint32_t(lx) * asset->width / value.bounds.width;
                    const uint32_t sy = uint32_t(ly) * asset->height / value.bounds.height;
                    const std::size_t source = (std::size_t(sy) * asset->width + sx) * 2;
                    if (source + 1 < asset->size) { color = uint16_t(asset_data[source]) | uint16_t(asset_data[source + 1] << 8); draw = true; }
                    break;
                }
                default: break;
            }
            if (draw) put_pixel(strip, pixels, x, y, color, value.opacity);
        }
    }
}

void DisplayRasterizer::render_waiting_strip(const Rect& requested, uint16_t* pixels,
                                             std::size_t pixel_count) const {
    const Rect strip = clip_to_screen(requested);
    if (!pixels || pixel_count < std::size_t(strip.width) * strip.height) return;
    std::fill_n(pixels, std::size_t(strip.width) * strip.height, kBlack);
    const uint8_t title[] = "StreamDeck DIY";
    const uint8_t subtitle[] = "Esperando aplicacion...";
    ResolvedNode first{nullptr, {0, 122, width, 32}, {0, 0, width, height}, 0, 255, 0, true};
    ResolvedNode second{nullptr, {0, 166, width, 24}, {0, 0, width, height}, 0, 255, 0, true};
    draw_text(nullptr, first, strip, pixels, title, sizeof(title) - 1, kWhite, 18, 1, true);
    draw_text(nullptr, second, strip, pixels, subtitle, sizeof(subtitle) - 1, 0x8410, 11, 1, false);
}

}  // namespace streamdeck::display
