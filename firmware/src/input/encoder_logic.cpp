#include "input/encoder_logic.hpp"

#include <array>

namespace streamdeck::input {
namespace {
constexpr std::array<int8_t, 16> TRANSITIONS{
    0,  1, -1, 0,
   -1,  0,  0, 1,
    1,  0,  0, -1,
    0, -1,  1, 0,
};

uint8_t state(bool a, bool b) {
    return static_cast<uint8_t>((static_cast<uint8_t>(a) << 1) |
                                static_cast<uint8_t>(b));
}
}

void EncoderLogic::reset(bool a, bool b) {
    previous_state_ = state(a, b);
    transition_count_ = 0;
    stats_ = {};
}

Rotation EncoderLogic::sample_rotation(bool a, bool b) {
    const uint8_t current = state(a, b);
    if (current == previous_state_) return Rotation::None;
    ++stats_.raw_transitions;

    if ((current ^ previous_state_) == 0x03) {
        ++stats_.invalid_transitions;
        transition_count_ = 0;
    } else {
        const int8_t delta = TRANSITIONS[(previous_state_ << 2) | current];
        if (delta != 0) ++stats_.valid_transitions;
        transition_count_ = static_cast<int8_t>(transition_count_ + delta);
    }
    previous_state_ = current;

    if (transition_count_ >= 4) {
        transition_count_ = 0;
        ++stats_.counterclockwise_detents;
        return Rotation::CounterClockwise;
    }
    if (transition_count_ <= -4) {
        transition_count_ = 0;
        ++stats_.clockwise_detents;
        return Rotation::Clockwise;
    }
    return Rotation::None;
}

bool EncoderLogic::sample_switch(bool pressed, uint32_t now_ms, Event& event) {
    if (pressed != switch_candidate_) {
        switch_candidate_ = pressed;
        switch_candidate_since_ms_ = now_ms;
        return false;
    }
    if (switch_candidate_ == switch_stable_ ||
        now_ms - switch_candidate_since_ms_ < SWITCH_DEBOUNCE_MS) {
        return false;
    }
    switch_stable_ = switch_candidate_;
    event = {switch_stable_ ? EventType::EncoderPressed
                            : EventType::EncoderReleased,
             0, now_ms, 0, static_cast<uint64_t>(now_ms) * 1000u,
             InputSource::Encoder};
    return true;
}
}
