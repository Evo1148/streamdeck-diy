#include "input/matrix_debouncer.hpp"

namespace streamdeck::input {
bool MatrixDebouncer::sample(std::size_t index, bool pressed, uint32_t now_ms,
                             Event& event, MatrixTrace* trace) {
    if (index >= keys_.size()) return false;
    if (trace != nullptr) *trace = {};
    KeyState& key = keys_[index];
    if (pressed != key.candidate) {
        key.candidate = pressed;
        key.candidate_since_ms = now_ms;
        if (pressed) {
            key.trace_id = next_trace_id_++;
            key.input_timestamp_us = static_cast<uint64_t>(now_ms) * 1000u;
            if (next_trace_id_ == 0) next_trace_id_ = 1;
            if (trace != nullptr) {
                *trace = {MatrixTraceStage::RawDown, key.trace_id,
                          static_cast<uint8_t>(index)};
            }
        }
        return false;
    }
    if (key.candidate == key.stable ||
        now_ms - key.candidate_since_ms < DEBOUNCE_MS) {
        return false;
    }
    key.stable = key.candidate;
    event = {key.stable ? EventType::ButtonPressed : EventType::ButtonReleased,
             static_cast<uint8_t>(index), now_ms, key.trace_id,
             key.input_timestamp_us, InputSource::Matrix};
    if (key.stable && trace != nullptr) {
        *trace = {MatrixTraceStage::DebouncedPress, key.trace_id,
                  static_cast<uint8_t>(index)};
    }
    return true;
}
}
