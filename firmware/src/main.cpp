#include "pico/stdlib.h"
#include <algorithm>
#include "core/events.hpp"
#include "core/led_self_test.hpp"
#include "hardware/status_led.hpp"
#include "input/encoder.hpp"
#include "input/matrix_scanner.hpp"
#include "usb/control_hid.hpp"
#include "usb/usb_device.hpp"

namespace {
constexpr bool HID_SELF_TEST_ENABLED = false;
constexpr uint32_t HID_SELF_TEST_DELAY_MS = 5000;
constexpr bool TFT_BRINGUP_TEST_ENABLED = false;
constexpr bool TOUCH_CALIBRATION_MODE = false;
constexpr bool TOUCH_VISUAL_TEST_ENABLED = false;
constexpr bool TFT_RENDER_ENABLED = true;  // Set false for the renderer A/B test.

struct RuntimeContext {
    streamdeck::usb::Device* usb{};
    streamdeck::input::Encoder* encoder{};
    streamdeck::input::MatrixScanner* matrix{};
    streamdeck::EventQueue<>* events{};
    streamdeck::hardware::StatusLed* led{};
    uint64_t last_input_service_us{};
    uint64_t last_matrix_scan_us{};
    uint64_t last_main_service_us{};
    uint64_t matrix_samples{};
    uint64_t max_input_service_gap_us{};
    uint64_t max_matrix_scan_gap_us{};
    uint64_t max_main_loop_gap_us{};
};
RuntimeContext runtime;

void mark_main_service(uint64_t now_us) {
    if (runtime.last_main_service_us != 0) {
        runtime.max_main_loop_gap_us = std::max(
            runtime.max_main_loop_gap_us,
            now_us - runtime.last_main_service_us);
    }
    runtime.last_main_service_us = now_us;
}

void drain_events() {
    streamdeck::Event event{};
    while (runtime.events->pop(event)) {
        streamdeck::usb::control_hid_trace_event_dequeued(event);
        if (event.type == streamdeck::EventType::LedTestStep) {
            runtime.led->show_test_step(event.index);
        } else {
            streamdeck::usb::control_hid_process_event(event);
        }
    }
}

void sample_inputs() {
    const uint64_t now_us = time_us_64();
    if (runtime.last_input_service_us != 0) {
        runtime.max_input_service_gap_us = std::max(
            runtime.max_input_service_gap_us,
            now_us - runtime.last_input_service_us);
    }
    runtime.last_input_service_us = now_us;
    const uint32_t now_ms = static_cast<uint32_t>(now_us / 1000u);
    (void)runtime.encoder->task(now_ms, *runtime.events);
    if (runtime.last_matrix_scan_us != 0) {
        runtime.max_matrix_scan_gap_us = std::max(
            runtime.max_matrix_scan_gap_us,
            now_us - runtime.last_matrix_scan_us);
    }
    runtime.last_matrix_scan_us = now_us;
    ++runtime.matrix_samples;
    (void)runtime.matrix->task(now_ms, *runtime.events);
    drain_events();
}

void cooperative_service() {
    mark_main_service(time_us_64());
    runtime.usb->service_transport();
    sample_inputs();
    streamdeck::usb::control_hid_service_touch();
}
}

int main() {
    streamdeck::hardware::StatusLed led;
    streamdeck::EventQueue<> events;
    streamdeck::LedSelfTest self_test;
    streamdeck::input::Encoder encoder;
    streamdeck::input::MatrixScanner matrix;
    streamdeck::usb::Device usb;
    runtime = {&usb, &encoder, &matrix, &events, &led};
    led.init();
    encoder.init();
    matrix.init();
    matrix.set_trace_callback(+[](streamdeck::input::MatrixTraceStage stage,
                                  uint32_t trace_id, uint8_t index) {
        streamdeck::usb::control_hid_trace_matrix(
            static_cast<uint8_t>(stage), trace_id, index);
    });
    usb.init();
    streamdeck::usb::control_hid_set_cooperative_service(cooperative_service);
    streamdeck::usb::control_hid_touch_init(true, TOUCH_VISUAL_TEST_ENABLED,
                                            TOUCH_CALIBRATION_MODE);
    (void)streamdeck::usb::control_hid_display_init(
        TFT_BRINGUP_TEST_ENABLED, TFT_RENDER_ENABLED);
    bool hid_self_test_queued = false;
    runtime.last_main_service_us = time_us_64();

    while (true) {
        const uint64_t loop_us = time_us_64();
        mark_main_service(loop_us);
        usb.task();
        const uint32_t now = to_ms_since_boot(get_absolute_time());
        sample_inputs();
        streamdeck::Event event;
        if (self_test.poll(now, event)) {
            (void)events.push(event);
        }
        if (HID_SELF_TEST_ENABLED && !hid_self_test_queued &&
            now >= HID_SELF_TEST_DELAY_MS) {
            hid_self_test_queued = usb.send_key(streamdeck::usb::keycode::A);
        }
        drain_events();
        const streamdeck::input::Encoder::Stats encoder_stats = encoder.stats();
        streamdeck::usb::control_hid_input_summary(
            now, encoder_stats.transitions.raw_transitions,
            encoder_stats.transitions.valid_transitions,
            encoder_stats.transitions.clockwise_detents,
            encoder_stats.transitions.counterclockwise_detents,
            encoder_stats.queued, encoder_stats.dropped,
            events.high_watermark(), events.dropped(), runtime.matrix_samples,
            runtime.max_main_loop_gap_us, runtime.max_input_service_gap_us,
            runtime.max_matrix_scan_gap_us);
        tight_loop_contents();
    }
}
