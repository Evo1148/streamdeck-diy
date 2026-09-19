#include "input/encoder.hpp"

#include "hardware/gpio.h"
#include "hardware/hardware_map.hpp"

namespace streamdeck::input {
namespace {
void init_input_pullup(uint8_t pin) {
    gpio_init(pin);
    gpio_set_dir(pin, GPIO_IN);
    gpio_pull_up(pin);
}
}

void Encoder::init() {
    init_input_pullup(hardware::encoder_a);
    init_input_pullup(hardware::encoder_b);
    init_input_pullup(hardware::encoder_switch);
    logic_.reset(gpio_get(hardware::encoder_a), gpio_get(hardware::encoder_b));
}

bool Encoder::task(uint32_t now_ms, EventQueue<>& events) {
    bool queued = true;
    const Rotation rotation = logic_.sample_rotation(
        gpio_get(hardware::encoder_a), gpio_get(hardware::encoder_b));
    if (rotation != Rotation::None) {
        const uint32_t trace_id = next_trace_id_++;
        if (next_trace_id_ == 0) next_trace_id_ = 1;
        const Event event{
            rotation == Rotation::Clockwise ? EventType::EncoderRight
                                            : EventType::EncoderLeft,
            0, now_ms, trace_id, static_cast<uint64_t>(now_ms) * 1000u,
            InputSource::Encoder};
        if (events.push(event)) ++queued_;
        else { ++dropped_; queued = false; }
    }

    Event switch_event{};
    if (logic_.sample_switch(!gpio_get(hardware::encoder_switch), now_ms,
                             switch_event)) {
        if (!events.push(switch_event)) {
            if (switch_event.type == EventType::EncoderPressed) ++dropped_;
            queued = false;
        } else if (switch_event.type == EventType::EncoderPressed) {
            ++queued_;
        }
    }
    return queued;
}
}
