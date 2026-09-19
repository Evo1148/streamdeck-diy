#include "touch/touch_calibration.hpp"

#include <algorithm>
#include <array>
#include <cstdint>

namespace streamdeck::touch {
namespace {
constexpr uint16_t raw_upper_limit = 4080;
constexpr uint16_t outlier_distance = 350;
constexpr uint16_t minimum_axis_span = 500;

uint16_t absolute_difference(uint16_t first, uint16_t second) {
    return first > second ? static_cast<uint16_t>(first - second)
                          : static_cast<uint16_t>(second - first);
}

uint16_t average(uint16_t first, uint16_t second) {
    return static_cast<uint16_t>((static_cast<uint32_t>(first) + second) / 2);
}

uint16_t median(std::array<uint16_t, touch_sample_count> values,
                std::size_t count) {
    for (std::size_t index = 1; index < count; ++index) {
        const uint16_t value = values[index];
        std::size_t position = index;
        while (position > 0 && values[position - 1] > value) {
            values[position] = values[position - 1];
            --position;
        }
        values[position] = value;
    }
    return values[count / 2];
}

uint16_t source_x(const RawPoint& point, bool swap_xy) {
    return swap_xy ? point.y : point.x;
}

uint16_t source_y(const RawPoint& point, bool swap_xy) {
    return swap_xy ? point.x : point.y;
}

uint16_t extrapolate_min(uint16_t low, uint16_t high,
                         uint16_t screen_extent) {
    const uint32_t usable_pixels =
        screen_extent - 1u - 2u * touch_target_margin;
    const uint32_t extension =
        (static_cast<uint32_t>(high - low) * touch_target_margin +
         usable_pixels / 2u) /
        usable_pixels;
    return extension >= low ? 1 : static_cast<uint16_t>(low - extension);
}

uint16_t extrapolate_max(uint16_t low, uint16_t high,
                         uint16_t screen_extent) {
    const uint32_t usable_pixels =
        screen_extent - 1u - 2u * touch_target_margin;
    const uint32_t extension =
        (static_cast<uint32_t>(high - low) * touch_target_margin +
         usable_pixels / 2u) /
        usable_pixels;
    return static_cast<uint16_t>(
        std::min<uint32_t>(raw_upper_limit - 1u, high + extension));
}

uint16_t map_axis(uint16_t raw, uint16_t minimum, uint16_t maximum,
                  uint16_t extent, bool invert) {
    if (maximum <= minimum || extent == 0) return 0;
    const uint16_t clamped = std::clamp(raw, minimum, maximum);
    uint32_t mapped =
        (static_cast<uint32_t>(clamped - minimum) * (extent - 1u) +
         (maximum - minimum) / 2u) /
        (maximum - minimum);
    if (invert) mapped = extent - 1u - mapped;
    return static_cast<uint16_t>(mapped);
}
}  // namespace

bool valid_raw_value(uint16_t value) {
    return value != 0 && value < raw_upper_limit;
}

bool median_touch_sample(const RawPoint* samples, std::size_t count,
                         RawPoint& result) {
    if (samples == nullptr || count == 0) return false;
    count = std::min(count, touch_sample_count);

    std::array<uint16_t, touch_sample_count> x_values{};
    std::array<uint16_t, touch_sample_count> y_values{};
    std::array<RawPoint, touch_sample_count> valid_samples{};
    std::size_t valid_count = 0;
    for (std::size_t index = 0; index < count; ++index) {
        if (!valid_raw_value(samples[index].x) ||
            !valid_raw_value(samples[index].y)) {
            continue;
        }
        valid_samples[valid_count] = samples[index];
        x_values[valid_count] = samples[index].x;
        y_values[valid_count] = samples[index].y;
        ++valid_count;
    }
    if (valid_count < touch_minimum_valid_samples) return false;

    const uint16_t first_x_median = median(x_values, valid_count);
    const uint16_t first_y_median = median(y_values, valid_count);
    std::size_t filtered_count = 0;
    for (std::size_t index = 0; index < valid_count; ++index) {
        if (absolute_difference(valid_samples[index].x, first_x_median) >
                outlier_distance ||
            absolute_difference(valid_samples[index].y, first_y_median) >
                outlier_distance) {
            continue;
        }
        x_values[filtered_count] = valid_samples[index].x;
        y_values[filtered_count] = valid_samples[index].y;
        ++filtered_count;
    }
    if (filtered_count < touch_minimum_valid_samples) return false;

    result = {median(x_values, filtered_count),
              median(y_values, filtered_count)};
    return true;
}

ScreenPoint map_touch(const RawPoint& raw,
                      const CalibrationParameters& calibration) {
    return {
        map_axis(source_x(raw, calibration.TOUCH_SWAP_XY),
                 calibration.TOUCH_X_MIN, calibration.TOUCH_X_MAX,
                 touch_screen_width, calibration.TOUCH_INVERT_X),
        map_axis(source_y(raw, calibration.TOUCH_SWAP_XY),
                 calibration.TOUCH_Y_MIN, calibration.TOUCH_Y_MAX,
                 touch_screen_height, calibration.TOUCH_INVERT_Y),
    };
}

bool calculate_calibration(const std::array<RawPoint, 5>& points,
                           CalibrationParameters& result) {
    result = {};
    for (const RawPoint& point : points) {
        if (!valid_raw_value(point.x) || !valid_raw_value(point.y)) {
            return false;
        }
    }
    for (std::size_t first = 0; first < 4; ++first) {
        for (std::size_t second = first + 1; second < 4; ++second) {
            if (absolute_difference(points[first].x, points[second].x) < 60 &&
                absolute_difference(points[first].y, points[second].y) < 60) {
                return false;
            }
        }
    }

    const uint16_t x_left = average(points[0].x, points[3].x);
    const uint16_t x_right = average(points[1].x, points[2].x);
    const uint16_t x_top = average(points[0].x, points[1].x);
    const uint16_t x_bottom = average(points[3].x, points[2].x);
    const uint16_t y_left = average(points[0].y, points[3].y);
    const uint16_t y_right = average(points[1].y, points[2].y);
    const uint16_t y_top = average(points[0].y, points[1].y);
    const uint16_t y_bottom = average(points[3].y, points[2].y);

    const uint32_t normal_score = absolute_difference(x_left, x_right) +
                                  absolute_difference(y_top, y_bottom);
    const uint32_t swapped_score = absolute_difference(y_left, y_right) +
                                   absolute_difference(x_top, x_bottom);
    if (normal_score == swapped_score) return false;
    const uint32_t stronger_score = std::max(normal_score, swapped_score);
    const uint32_t weaker_score = std::min(normal_score, swapped_score);
    if (stronger_score < 2u * minimum_axis_span ||
        stronger_score * 4u < weaker_score * 5u) {
        return false;
    }
    result.TOUCH_SWAP_XY = swapped_score > normal_score;

    const uint16_t left = average(source_x(points[0], result.TOUCH_SWAP_XY),
                                  source_x(points[3], result.TOUCH_SWAP_XY));
    const uint16_t right = average(source_x(points[1], result.TOUCH_SWAP_XY),
                                   source_x(points[2], result.TOUCH_SWAP_XY));
    const uint16_t top = average(source_y(points[0], result.TOUCH_SWAP_XY),
                                 source_y(points[1], result.TOUCH_SWAP_XY));
    const uint16_t bottom = average(source_y(points[3], result.TOUCH_SWAP_XY),
                                    source_y(points[2], result.TOUCH_SWAP_XY));
    if (absolute_difference(left, right) < minimum_axis_span ||
        absolute_difference(top, bottom) < minimum_axis_span) {
        return false;
    }

    result.TOUCH_INVERT_X = right < left;
    result.TOUCH_INVERT_Y = bottom < top;
    const uint16_t x_low = std::min(left, right);
    const uint16_t x_high = std::max(left, right);
    const uint16_t y_low = std::min(top, bottom);
    const uint16_t y_high = std::max(top, bottom);
    result.TOUCH_X_MIN = extrapolate_min(x_low, x_high, touch_screen_width);
    result.TOUCH_X_MAX = extrapolate_max(x_low, x_high, touch_screen_width);
    result.TOUCH_Y_MIN = extrapolate_min(y_low, y_high, touch_screen_height);
    result.TOUCH_Y_MAX = extrapolate_max(y_low, y_high, touch_screen_height);
    if (result.TOUCH_X_MAX - result.TOUCH_X_MIN < minimum_axis_span ||
        result.TOUCH_Y_MAX - result.TOUCH_Y_MIN < minimum_axis_span) {
        result = {};
        return false;
    }

    result.valid = true;
    const ScreenPoint top_left = map_touch(points[0], result);
    const ScreenPoint top_right = map_touch(points[1], result);
    const ScreenPoint bottom_right = map_touch(points[2], result);
    const ScreenPoint bottom_left = map_touch(points[3], result);
    const ScreenPoint center = map_touch(points[4], result);
    if (top_left.x >= touch_screen_width / 3 ||
        top_left.y >= touch_screen_height / 3 ||
        top_right.x <= touch_screen_width * 2 / 3 ||
        top_right.y >= touch_screen_height / 3 ||
        bottom_right.x <= touch_screen_width * 2 / 3 ||
        bottom_right.y <= touch_screen_height * 2 / 3 ||
        bottom_left.x >= touch_screen_width / 3 ||
        bottom_left.y <= touch_screen_height * 2 / 3 ||
        center.x < touch_screen_width / 4 ||
        center.x > touch_screen_width * 3 / 4 ||
        center.y < touch_screen_height / 4 ||
        center.y > touch_screen_height * 3 / 4) {
        result = {};
        return false;
    }
    return true;
}
}  // namespace streamdeck::touch
