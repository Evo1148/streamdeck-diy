#include <cassert>

#include "input/encoder_logic.hpp"

namespace {
using streamdeck::input::EncoderLogic;
using streamdeck::input::Rotation;

Rotation step(EncoderLogic& logic, uint8_t state) {
    return logic.sample_rotation((state & 2) != 0, (state & 1) != 0);
}
}

int main() {
    using namespace streamdeck;

    EncoderLogic clockwise;
    clockwise.reset(true, true);
    assert(step(clockwise, 1) == Rotation::None);
    assert(step(clockwise, 0) == Rotation::None);
    assert(step(clockwise, 2) == Rotation::None);
    assert(step(clockwise, 3) == Rotation::Clockwise);
    assert(step(clockwise, 3) == Rotation::None);

    EncoderLogic counterclockwise;
    counterclockwise.reset(true, true);
    assert(step(counterclockwise, 2) == Rotation::None);
    assert(step(counterclockwise, 0) == Rotation::None);
    assert(step(counterclockwise, 1) == Rotation::None);
    assert(step(counterclockwise, 3) == Rotation::CounterClockwise);

    EncoderLogic bounce;
    bounce.reset(true, true);
    assert(step(bounce, 2) == Rotation::None);
    assert(step(bounce, 3) == Rotation::None);
    assert(step(bounce, 2) == Rotation::None);
    assert(step(bounce, 0) == Rotation::None);
    assert(step(bounce, 2) == Rotation::None);
    assert(step(bounce, 0) == Rotation::None);
    assert(step(bounce, 1) == Rotation::None);
    assert(step(bounce, 3) == Rotation::CounterClockwise);

    EncoderLogic incomplete;
    incomplete.reset(true, true);
    assert(step(incomplete, 2) == Rotation::None);
    assert(step(incomplete, 0) == Rotation::None);

    EncoderLogic repeated;
    repeated.reset(true, true);
    int events = 0;
    for (int detent = 0; detent < 3; ++detent) {
        for (const uint8_t value : {1, 0, 2, 3}) {
            if (step(repeated, value) == Rotation::Clockwise) ++events;
        }
    }
    assert(events == 3);
    assert(repeated.stats().raw_transitions == 12);
    assert(repeated.stats().valid_transitions == 12);
    assert(repeated.stats().clockwise_detents == 3);

    EncoderLogic rapid_ccw;
    rapid_ccw.reset(true, true);
    events = 0;
    for (int detent = 0; detent < 20; ++detent) {
        for (const uint8_t value : {2, 0, 1, 3}) {
            if (step(rapid_ccw, value) == Rotation::CounterClockwise) ++events;
        }
    }
    assert(events == 20);
    assert(rapid_ccw.stats().counterclockwise_detents == 20);
    assert(rapid_ccw.stats().valid_transitions == 80);

    EncoderLogic invalid;
    invalid.reset(true, true);
    assert(step(invalid, 0) == Rotation::None); // impossible two-bit jump
    assert(step(invalid, 3) == Rotation::None); // impossible two-bit jump
    assert(invalid.stats().raw_transitions == 2);
    assert(invalid.stats().valid_transitions == 0);
    assert(invalid.stats().invalid_transitions == 2);

    EncoderLogic push;
    push.reset(true, true);
    Event event{};
    assert(!push.sample_switch(false, 0, event));
    assert(!push.sample_switch(true, 10, event));
    assert(!push.sample_switch(false, 13, event));
    assert(!push.sample_switch(true, 16, event));
    assert(!push.sample_switch(true, 30, event));
    assert(push.sample_switch(true, 31, event));
    assert(event.type == EventType::EncoderPressed && event.index == 0);
    assert(!push.sample_switch(true, 100, event));
    assert(!push.sample_switch(false, 101, event));
    assert(!push.sample_switch(false, 115, event));
    assert(push.sample_switch(false, 116, event));
    assert(event.type == EventType::EncoderReleased && event.index == 0);
    assert(!push.sample_switch(false, 150, event));
}
