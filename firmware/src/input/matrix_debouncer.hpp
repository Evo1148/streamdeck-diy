#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

#include "core/events.hpp"
#include "hardware/hardware_map.hpp"

namespace streamdeck::input {
enum class MatrixTraceStage : uint8_t { RawDown, DebouncedPress, EventQueued, EventDropped };
struct MatrixTrace {
    MatrixTraceStage stage{};
    uint32_t trace_id{};
    uint8_t index{};
};

class MatrixDebouncer {
public:
    static constexpr uint32_t DEBOUNCE_MS = 15;

    bool sample(std::size_t index, bool pressed, uint32_t now_ms,
                Event& event, MatrixTrace* trace = nullptr);

private:
    struct KeyState {
        bool stable{};
        bool candidate{};
        uint32_t candidate_since_ms{};
        uint32_t trace_id{};
        uint64_t input_timestamp_us{};
    };

    std::array<KeyState, hardware::key_count> keys_{};
    uint32_t next_trace_id_{1};
};
}
