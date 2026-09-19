#pragma once

#include <cstdint>

#include "core/events.hpp"
#include "input/encoder_logic.hpp"

namespace streamdeck::input {
class Encoder {
public:
    struct Stats {
        EncoderTransitionStats transitions{};
        uint32_t queued{};
        uint32_t dropped{};
    };
    void init();
    bool task(uint32_t now_ms, EventQueue<>& events);
    Stats stats() const { return {logic_.stats(), queued_, dropped_}; }

private:
    EncoderLogic logic_{};
    uint32_t next_trace_id_{1};
    uint32_t queued_{};
    uint32_t dropped_{};
};
}
