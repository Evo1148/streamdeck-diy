#include "input/matrix_scanner.hpp"

#include "hardware/gpio.h"
#include "hardware/hardware_map.hpp"
#include "pico/time.h"

namespace streamdeck::input {
void MatrixScanner::init() {
    for (const uint8_t row : hardware::rows) {
        gpio_init(row);
        gpio_set_dir(row, GPIO_IN);
        gpio_pull_up(row);
    }
    for (const uint8_t column : hardware::columns) {
        gpio_init(column);
        gpio_put(column, false);
        gpio_set_dir(column, GPIO_IN);
        gpio_disable_pulls(column);
    }
}

bool MatrixScanner::task(uint32_t now_ms, EventQueue<>& events) {
    const uint8_t column_index = next_column_;
    const uint8_t column_pin = hardware::columns[column_index];

    // Hardware V0.1: the diode stripe faces the column. Only the scanned
    // column drives LOW; every other column remains high impedance.
    gpio_put(column_pin, false);
    gpio_set_dir(column_pin, GPIO_OUT);
    busy_wait_us_32(5);

    bool queued = true;
    for (std::size_t row = 0; row < hardware::rows.size(); ++row) {
        const bool pressed = !gpio_get(hardware::rows[row]);
        const std::size_t index =
            row * hardware::columns.size() + column_index;
        Event event{};
        MatrixTrace trace{};
        const bool event_ready =
            debouncer_.sample(index, pressed, now_ms, event, &trace);
        if (trace.trace_id != 0 && trace_callback_ != nullptr) {
            trace_callback_(trace.stage, trace.trace_id, trace.index);
        }
        if (event_ready) {
            if (events.push(event)) {
                if (event.type == EventType::ButtonPressed &&
                    trace_callback_ != nullptr) {
                    trace_callback_(MatrixTraceStage::EventQueued,
                                    event.trace_id, event.index);
                }
            } else {
                queued = false;
                if (event.type == EventType::ButtonPressed &&
                    trace_callback_ != nullptr) {
                    trace_callback_(MatrixTraceStage::EventDropped,
                                    event.trace_id, event.index);
                }
            }
        }
    }

    gpio_set_dir(column_pin, GPIO_IN);
    gpio_disable_pulls(column_pin);
    next_column_ =
        static_cast<uint8_t>((next_column_ + 1) % hardware::columns.size());
    return queued;
}
}
