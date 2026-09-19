#pragma once
#include <cstddef>
#include <cstdint>

#include "core/events.hpp"

namespace streamdeck::usb {
class Device;

using CooperativeServiceCallback = void (*)();
bool control_hid_display_init(bool run_bringup_test, bool render_enabled);
void control_hid_set_cooperative_service(CooperativeServiceCallback callback);
void control_hid_touch_init(bool touch_enabled, bool visual_test_enabled,
                            bool calibration_mode);
void control_hid_init(Device& device);
void control_hid_receive(const uint8_t* report, std::size_t length);
void control_hid_report_complete();
void control_hid_report_failed();
void control_hid_task();
void control_hid_service_touch();
void control_hid_process_event(const Event& event);
void control_hid_trace_matrix(uint8_t stage, uint32_t trace_id,
                              uint8_t index);
void control_hid_trace_event_dequeued(const Event& event);
void control_hid_input_summary(
    uint32_t now_ms, uint32_t encoder_raw_transitions,
    uint32_t encoder_valid_transitions, uint32_t encoder_cw_detents,
    uint32_t encoder_ccw_detents, uint32_t encoder_queued,
    uint32_t encoder_dropped, std::size_t event_queue_high_watermark,
    uint32_t event_queue_dropped, uint64_t matrix_samples,
    uint64_t max_main_loop_gap_us, uint64_t max_input_service_gap_us,
    uint64_t max_matrix_scan_gap_us);
}
