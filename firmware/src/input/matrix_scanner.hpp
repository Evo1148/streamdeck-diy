#pragma once

#include <cstdint>

#include "core/events.hpp"
#include "input/matrix_debouncer.hpp"

namespace streamdeck::input {
class MatrixScanner {
public:
    using TraceCallback = void (*)(MatrixTraceStage stage, uint32_t trace_id,
                                   uint8_t index);
    void init();
    bool task(uint32_t now_ms, EventQueue<>& events);
    void set_trace_callback(TraceCallback callback) { trace_callback_ = callback; }

private:
    MatrixDebouncer debouncer_{};
    uint8_t next_column_{};
    TraceCallback trace_callback_{};
};
}
