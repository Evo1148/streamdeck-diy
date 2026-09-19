#include "touch/touch_math.hpp"

namespace streamdeck::touch {
namespace {
uint32_t squared_distance(ScreenPoint first, ScreenPoint second) {
    const int32_t dx = static_cast<int32_t>(second.x) - first.x;
    const int32_t dy = static_cast<int32_t>(second.y) - first.y;
    return static_cast<uint32_t>(dx * dx + dy * dy);
}
}
void IrqDebouncer::reset(bool gpio_level_high, uint32_t now_ms) {
    stable_level_high_ = gpio_level_high;
    candidate_level_high_ = gpio_level_high;
    candidate_since_ms_ = now_ms;
}

bool IrqDebouncer::update(bool gpio_level_high, uint32_t now_ms,
                          bool& active) {
    if (gpio_level_high != candidate_level_high_) {
        candidate_level_high_ = gpio_level_high;
        candidate_since_ms_ = now_ms;
        return false;
    }
    if (candidate_level_high_ == stable_level_high_ ||
        now_ms - candidate_since_ms_ < debounce_ms) {
        return false;
    }
    stable_level_high_ = candidate_level_high_;
    active = irq_active(stable_level_high_);
    return true;
}

void TapDetector::begin(ScreenPoint point, uint32_t timestamp_ms) {
    start_ = point;
    last_ = point;
    started_ms_ = timestamp_ms;
    moved_too_far_ = false;
    active_ = true;
}

void TapDetector::update(ScreenPoint point) {
    if (!active_) return;
    last_ = point;
    constexpr uint32_t limit =
        maximum_movement_pixels * maximum_movement_pixels;
    if (squared_distance(start_, point) > limit) moved_too_far_ = true;
}

bool TapDetector::end(uint32_t timestamp_ms, ScreenPoint& point) {
    if (!active_) return false;
    active_ = false;
    point = last_;
    return !moved_too_far_ &&
           timestamp_ms - started_ms_ <= maximum_duration_ms;
}

void TapDetector::cancel() {
    active_ = false;
    moved_too_far_ = false;
}
}
