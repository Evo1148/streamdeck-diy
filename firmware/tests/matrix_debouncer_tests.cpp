#include <cassert>

#include "input/matrix_debouncer.hpp"

int main() {
    using namespace streamdeck;
    using namespace streamdeck::input;

    MatrixDebouncer debounce;
    Event event{};

    assert(!debounce.sample(0, false, 0, event));
    assert(!debounce.sample(0, true, 1, event));
    assert(!debounce.sample(0, false, 4, event));
    assert(!debounce.sample(0, true, 7, event));
    assert(!debounce.sample(0, false, 10, event));
    assert(!debounce.sample(0, false, 30, event));

    assert(!debounce.sample(0, true, 40, event));
    assert(!debounce.sample(0, true, 54, event));
    assert(debounce.sample(0, true, 55, event));
    assert(event.type == EventType::ButtonPressed && event.index == 0);
    assert(!debounce.sample(0, true, 100, event));

    assert(!debounce.sample(0, false, 101, event));
    assert(!debounce.sample(0, false, 115, event));
    assert(debounce.sample(0, false, 116, event));
    assert(event.type == EventType::ButtonReleased && event.index == 0);
    assert(!debounce.sample(0, false, 150, event));

    MatrixDebouncer rearm;
    MatrixTrace trace{};
    assert(!rearm.sample(1, true, 200, event, &trace));
    assert(trace.stage == MatrixTraceStage::RawDown && trace.trace_id == 1);
    assert(rearm.sample(1, true, 215, event, &trace));
    assert(trace.stage == MatrixTraceStage::DebouncedPress &&
           event.trace_id == 1);
    assert(!rearm.sample(1, false, 220, event, &trace));
    assert(rearm.sample(1, false, 235, event, &trace));
    assert(!rearm.sample(1, true, 240, event, &trace));
    assert(trace.stage == MatrixTraceStage::RawDown && trace.trace_id == 2);
    assert(rearm.sample(1, true, 255, event, &trace));
    assert(event.type == EventType::ButtonPressed && event.trace_id == 2);

    MatrixDebouncer all_keys;
    for (uint8_t index = 0; index < 12; ++index) {
        assert(!all_keys.sample(index, true, 200, event));
    }
    for (uint8_t index = 0; index < 12; ++index) {
        assert(all_keys.sample(index, true, 215, event));
        assert(event.type == EventType::ButtonPressed);
        assert(event.index == index);
    }
    assert(!all_keys.sample(12, true, 300, event));
}
