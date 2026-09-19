#pragma once

#include <cstdint>

#include "core/events.hpp"

namespace streamdeck::input {
enum class Rotation { None, Clockwise, CounterClockwise };
struct EncoderTransitionStats {
    uint32_t raw_transitions{};
    uint32_t valid_transitions{};
    uint32_t invalid_transitions{};
    uint32_t clockwise_detents{};
    uint32_t counterclockwise_detents{};
};

class EncoderLogic {
public:
    static constexpr uint32_t SWITCH_DEBOUNCE_MS = 15;

    void reset(bool a, bool b);
    Rotation sample_rotation(bool a, bool b);
    bool sample_switch(bool pressed, uint32_t now_ms, Event& event);
    const EncoderTransitionStats& stats() const { return stats_; }

private:
    uint8_t previous_state_{};
    int8_t transition_count_{};
    bool switch_stable_{};
    bool switch_candidate_{};
    uint32_t switch_candidate_since_ms_{};
    EncoderTransitionStats stats_{};
};
}
