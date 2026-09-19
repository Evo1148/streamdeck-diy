#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace streamdeck::touch {
inline constexpr uint16_t touch_screen_width = 480;
inline constexpr uint16_t touch_screen_height = 320;
inline constexpr uint16_t touch_target_margin = 24;
inline constexpr std::size_t touch_sample_count = 7;
inline constexpr std::size_t touch_minimum_valid_samples = 4;

struct RawPoint {
    uint16_t x{};
    uint16_t y{};
};

struct ScreenPoint {
    uint16_t x{};
    uint16_t y{};
};

struct CalibrationParameters {
    uint16_t TOUCH_X_MIN{};
    uint16_t TOUCH_X_MAX{};
    uint16_t TOUCH_Y_MIN{};
    uint16_t TOUCH_Y_MAX{};
    bool TOUCH_SWAP_XY{};
    bool TOUCH_INVERT_X{};
    bool TOUCH_INVERT_Y{};
    bool valid{};
};

inline constexpr bool TOUCH_SWAP_XY = true;
inline constexpr bool TOUCH_INVERT_X = true;
inline constexpr bool TOUCH_INVERT_Y = true;
inline constexpr uint16_t TOUCH_X_MIN = 299;
inline constexpr uint16_t TOUCH_X_MAX = 3967;
inline constexpr uint16_t TOUCH_Y_MIN = 142;
inline constexpr uint16_t TOUCH_Y_MAX = 3809;
inline constexpr CalibrationParameters provisional_touch_calibration{
    TOUCH_X_MIN, TOUCH_X_MAX, TOUCH_Y_MIN, TOUCH_Y_MAX,
    TOUCH_SWAP_XY, TOUCH_INVERT_X, TOUCH_INVERT_Y, true};

inline constexpr std::array<ScreenPoint, 5> calibration_targets{{
    {touch_target_margin, touch_target_margin},
    {touch_screen_width - 1 - touch_target_margin, touch_target_margin},
    {touch_screen_width - 1 - touch_target_margin,
     touch_screen_height - 1 - touch_target_margin},
    {touch_target_margin, touch_screen_height - 1 - touch_target_margin},
    {touch_screen_width / 2, touch_screen_height / 2},
}};

bool valid_raw_value(uint16_t value);
bool median_touch_sample(const RawPoint* samples, std::size_t count,
                         RawPoint& result);
bool calculate_calibration(const std::array<RawPoint, 5>& points,
                           CalibrationParameters& result);
ScreenPoint map_touch(const RawPoint& raw,
                      const CalibrationParameters& calibration);
}  // namespace streamdeck::touch
