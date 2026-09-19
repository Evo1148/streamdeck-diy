#include <cassert>
#include <array>

#include "touch/touch_calibration.hpp"
#include "touch/touch_math.hpp"
#include "touch/xpt2046.hpp"

int main() {
    using namespace streamdeck::touch;

    assert(unpack_xpt2046_12bit(0x00, 0x00) == 0);
    assert(unpack_xpt2046_12bit(0x7A, 0xA8) == 0x0F55);
    assert(unpack_xpt2046_12bit(0xFF, 0xFF) == 0x0FFF);
    assert(xpt2046_no_response(0x00, 0x00, 0x00));
    assert(!xpt2046_no_response(0x00, 0x01, 0x00));
    static_assert(Xpt2046::spi_frequency_hz == 1'000'000);
    static_assert(Xpt2046::read_x_command == 0xD0);
    static_assert(Xpt2046::read_y_command == 0x90);
    assert(irq_active(false));
    assert(!irq_active(true));

    const std::array<RawPoint, touch_sample_count> samples{{
        {0, 1200}, {1000, 2000}, {1010, 1990}, {4095, 4095},
        {995, 2010}, {3000, 100}, {1005, 2005},
    }};
    RawPoint median_sample{};
    assert(median_touch_sample(samples.data(), samples.size(), median_sample));
    assert(median_sample.x == 1005);
    assert(median_sample.y == 2005);
    const std::array<RawPoint, 3> invalid_samples{{
        {0, 1000}, {1000, 4095}, {0, 0},
    }};
    assert(!median_touch_sample(invalid_samples.data(),
                                invalid_samples.size(), median_sample));

    CalibrationParameters mapping{1000, 3000, 500, 2500,
                                  false, false, false, true};
    ScreenPoint point = map_touch({1000, 500}, mapping);
    assert(point.x == 0 && point.y == 0);
    point = map_touch({3000, 2500}, mapping);
    assert(point.x == 479 && point.y == 319);
    point = map_touch({0, 4095}, mapping);
    assert(point.x == 0 && point.y == 319);

    mapping.TOUCH_INVERT_X = true;
    mapping.TOUCH_INVERT_Y = true;
    point = map_touch({1000, 500}, mapping);
    assert(point.x == 479 && point.y == 319);

    mapping.TOUCH_SWAP_XY = true;
    mapping.TOUCH_INVERT_X = false;
    mapping.TOUCH_INVERT_Y = false;
    point = map_touch({500, 1000}, mapping);
    assert(point.x == 0 && point.y == 0);
    mapping.TOUCH_INVERT_X = true;
    mapping.TOUCH_INVERT_Y = true;
    point = map_touch({500, 1000}, mapping);
    assert(point.x == 479 && point.y == 319);

    static_assert(TOUCH_SWAP_XY);
    static_assert(TOUCH_INVERT_X);
    static_assert(TOUCH_INVERT_Y);
    static_assert(TOUCH_X_MIN == 299 && TOUCH_X_MAX == 3967);
    static_assert(TOUCH_Y_MIN == 142 && TOUCH_Y_MAX == 3809);
    point = map_touch({TOUCH_Y_MAX, TOUCH_X_MAX},
                      provisional_touch_calibration);
    assert(point.x == 0 && point.y == 0);
    point = map_touch({TOUCH_Y_MIN, TOUCH_X_MIN},
                      provisional_touch_calibration);
    assert(point.x == 479 && point.y == 319);
    point = map_touch({4095, 4095}, provisional_touch_calibration);
    assert(point.x == 0 && point.y == 0);
    point = map_touch({0, 0}, provisional_touch_calibration);
    assert(point.x == 479 && point.y == 319);

    const std::array<RawPoint, 5> normal_points{{
        {500, 600}, {3500, 600}, {3500, 3400}, {500, 3400},
        {2000, 2000},
    }};
    CalibrationParameters detected{};
    assert(calculate_calibration(normal_points, detected));
    assert(!detected.TOUCH_SWAP_XY);
    assert(!detected.TOUCH_INVERT_X);
    assert(!detected.TOUCH_INVERT_Y);
    point = map_touch(normal_points[4], detected);
    assert(point.x >= 238 && point.x <= 241);
    assert(point.y >= 158 && point.y <= 161);

    const std::array<RawPoint, 5> swapped_inverted_points{{
        {3500, 500}, {3500, 3500}, {500, 3500}, {500, 500},
        {2000, 2000},
    }};
    assert(calculate_calibration(swapped_inverted_points, detected));
    assert(detected.TOUCH_SWAP_XY);
    assert(!detected.TOUCH_INVERT_X);
    assert(detected.TOUCH_INVERT_Y);

    const std::array<RawPoint, 5> insufficient_range{{
        {1000, 1000}, {1100, 1000}, {1100, 1100}, {1000, 1100},
        {1050, 1050},
    }};
    assert(!calculate_calibration(insufficient_range, detected));

    IrqDebouncer irq;
    irq.reset(true, 0);
    bool active = false;
    assert(!irq.update(false, 1, active));
    assert(!irq.update(false, 15, active));
    assert(irq.update(false, 16, active) && active);
    assert(!irq.update(true, 20, active));
    assert(irq.update(true, 35, active) && !active);

    TapDetector tap;
    ScreenPoint tap_point{};
    tap.begin({100, 100}, 1000);
    tap.update({115, 100});
    assert(tap.end(1500, tap_point));
    assert(tap_point.x == 115 && tap_point.y == 100);
    assert(!tap.end(1500, tap_point));

    tap.begin({100, 100}, 2000);
    tap.update({116, 100});
    assert(!tap.end(2100, tap_point));

    tap.begin({100, 100}, 3000);
    assert(!tap.end(3501, tap_point));

    tap.begin({100, 100}, 4000);
    tap.cancel();
    assert(!tap.active() && !tap.end(4001, tap_point));
}
