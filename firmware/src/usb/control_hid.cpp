#include "usb/control_hid.hpp"

#include <algorithm>
#include <array>
#include <cstdarg>
#include <cstdio>

#include "actions/action_engine.hpp"
#include "config/config_manager.hpp"
#include "config/config_storage.hpp"
#include "controls/control.hpp"
#include "display/display_runtime.hpp"
#include "display/st7796_display.hpp"
#include "input/matrix_debouncer.hpp"
#include "input/system_navigation.hpp"
#include "pico/stdlib.h"
#include "pico/rand.h"
#include "pico/unique_id.h"
#include "protocol/protocol.hpp"
#include "system/bootloader.hpp"
#include "touch/touch_calibration.hpp"
#include "touch/touch_math.hpp"
#include "touch/xpt2046.hpp"
#include "tusb.h"
#include "usb/usb_device.hpp"
#include "usb/usb_descriptors.h"

namespace streamdeck::usb {
namespace {
class HostActionQueue final : public actions::IHostActionSink {
public:
    bool trigger(uint32_t action_id) override {
        if (size_ == action_ids_.size()) return false;
        action_ids_[write_] = action_id;
        write_ = (write_ + 1) % action_ids_.size();
        ++size_;
        return true;
    }

    bool peek(uint32_t& action_id) const {
        if (size_ == 0) return false;
        action_id = action_ids_[read_];
        return true;
    }

    void pop() {
        if (size_ == 0) return;
        read_ = (read_ + 1) % action_ids_.size();
        --size_;
    }

private:
    std::array<uint32_t, 8> action_ids_{};
    std::size_t read_{};
    std::size_t write_{};
    std::size_t size_{};
};

class DisplayEventQueue {
public:
    struct Entry {
        protocol::Packet packet{};
        uint32_t trace_id{};
        uint32_t control{};
        uint16_t region{};
        bool send_failure_logged{};
    };

    bool push(const protocol::Packet& packet, uint32_t trace_id,
              uint16_t region, uint32_t control) {
        if (size_ == entries_.size()) return false;
        entries_[write_] = {packet, trace_id, control, region, false};
        write_ = (write_ + 1) % entries_.size();
        ++size_;
        return true;
    }

    Entry* front() {
        return size_ == 0 ? nullptr : &entries_[read_];
    }

    void pop() {
        if (size_ == 0) return;
        read_ = (read_ + 1) % entries_.size();
        --size_;
    }

private:
    std::array<Entry, 8> entries_{};
    std::size_t read_{};
    std::size_t write_{};
    std::size_t size_{};
};

class DiagnosticLogger {
public:
    bool connected() const { return tud_cdc_connected(); }

    void reset() {
        read_ = 0;
        write_ = 0;
        size_ = 0;
        offset_ = 0;
    }

    bool enqueue(const char* format, ...) {
        if (size_ == messages_.size()) return false;
        va_list arguments;
        va_start(arguments, format);
        const int result = std::vsnprintf(messages_[write_].data(),
                                          messages_[write_].size(), format,
                                          arguments);
        va_end(arguments);
        if (result < 0) return false;
        lengths_[write_] = static_cast<uint16_t>(
            std::min<std::size_t>(static_cast<std::size_t>(result),
                                  messages_[write_].size() - 1));
        write_ = (write_ + 1) % messages_.size();
        ++size_;
        return true;
    }

    void task() {
        if (!connected() || size_ == 0) return;
        const uint32_t available = tud_cdc_write_available();
        if (available == 0) return;
        const std::size_t remaining = lengths_[read_] - offset_;
        const uint32_t requested = static_cast<uint32_t>(
            std::min<std::size_t>(remaining, available));
        const uint32_t written = tud_cdc_write(
            messages_[read_].data() + offset_, requested);
        offset_ += written;
        tud_cdc_write_flush();
        if (offset_ == lengths_[read_]) {
            read_ = (read_ + 1) % messages_.size();
            --size_;
            offset_ = 0;
        }
    }

private:
    std::array<std::array<char, 384>, 24> messages_{};
    std::array<uint16_t, 24> lengths_{};
    std::size_t read_{};
    std::size_t write_{};
    std::size_t size_{};
    std::size_t offset_{};
};

protocol::Protocol protocol;
config::ConfigManager config_manager;
config::ConfigStorage config_storage;
actions::ActionEngine action_engine;
Device* usb_device = nullptr;
protocol::Packet pending_response{};
bool response_pending = false;
protocol::Packet pending_request{};
std::size_t pending_request_length = 0;
bool request_pending = false;
HostActionQueue host_actions;
uint16_t next_host_sequence = 1;
DisplayEventQueue display_events;
uint16_t next_display_sequence = 1;
display::St7796DisplayBackend display_backend;
display::DisplayRuntime display_runtime(display_backend);
system::BootloaderController bootloader_controller(system::enter_usb_bootloader);
bool bootloader_response_in_flight = false;
touch::Xpt2046 touch_controller;
touch::IrqDebouncer touch_irq;
touch::TapDetector tap_detector;
DiagnosticLogger diagnostic_log;
bool touch_enabled = false;
bool touch_visual_test_enabled = false;
bool touch_calibration_mode = false;
bool diagnostic_was_connected = false;
bool calibration_screen_started = false;
bool calibration_point_captured = false;
bool calibration_complete = false;
uint8_t calibration_point_index = 0;
std::array<touch::RawPoint, 5> calibration_points{};
touch::CalibrationParameters touch_calibration{};
bool test_cross_visible = false;
display::Rect test_cross_region{};
uint32_t test_cross_expires_ms = 0;
uint32_t next_tracking_sample_ms = 0;
constexpr uint32_t tracking_sample_interval_ms = 40;
constexpr bool INPUT_TRACE_VERBOSE = false;
constexpr bool touch_event_log_enabled = INPUT_TRACE_VERBOSE;
constexpr uint32_t input_summary_interval_ms = 5000;
uint32_t last_input_summary_ms = 0;
uint32_t last_summary_events_received = 0;
uint32_t last_summary_encoder_raw = 0;
uint32_t last_summary_tft_renders = 0;
struct TouchPerformance {
    uint64_t poll_calls{};
    uint32_t irq_assertions{};
    uint32_t spi_reads{};
    uint32_t valid_samples{};
    uint32_t presses{};
    uint32_t releases{};
    uint32_t taps{};
    uint64_t last_service_us{};
    uint64_t max_service_gap_us{};
};
TouchPerformance touch_performance;
bool touch_irq_was_high = true;
uint64_t last_summary_touch_polls = 0;
constexpr std::array<const char*, 5> calibration_point_names{
    "TL", "TR", "BR", "BL", "C"};

const char* execute_result_name(actions::ExecuteResult result) {
    switch (result) {
    case actions::ExecuteResult::Started: return "Started";
    case actions::ExecuteResult::NoAction: return "NoAction";
    case actions::ExecuteResult::Busy: return "Busy";
    case actions::ExecuteResult::Unsupported: return "Unsupported";
    case actions::ExecuteResult::Invalid: return "Invalid";
    }
    return "Unknown";
}

bool collect_touch_sample(touch::RawPoint& result) {
    std::array<touch::RawPoint, touch::touch_sample_count> samples{};
    std::size_t count = 0;
    for (std::size_t index = 0; index < touch::touch_sample_count; ++index) {
        touch::RawTouchRead raw{};
        ++touch_performance.spi_reads;
        if (!touch_controller.read(raw)) break;
        if (!raw.x.no_response && !raw.y.no_response) {
            samples[count++] = {raw.x.value, raw.y.value};
            ++touch_performance.valid_samples;
        }
        busy_wait_us_32(150);
    }
    return touch::median_touch_sample(samples.data(), count, result);
}

void draw_calibration_target(uint8_t index, uint16_t color) {
    const touch::ScreenPoint target = touch::calibration_targets[index];
    display_backend.draw_crosshair(target.x, target.y, color);
}

void restore_test_cross() {
    if (!test_cross_visible) return;
    if (display_runtime.active_scene().generation != 0) {
        display_backend.present(display_runtime.active_scene(),
                                display_runtime.assets(), false,
                                &test_cross_region, 1);
    } else {
        display_backend.restore_waiting_region(test_cross_region);
    }
    test_cross_visible = false;
}

void begin_calibration_screen() {
    display_backend.fill(display::st7796::black);
    calibration_point_index = 0;
    calibration_point_captured = false;
    calibration_complete = false;
    touch_calibration = {};
    draw_calibration_target(0, display::st7796::white);
    calibration_screen_started = true;
}

void finish_calibration() {
    if (!touch::calculate_calibration(calibration_points,
                                      touch_calibration)) {
        if (diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue("CAL INVALID\r\n");
        }
        begin_calibration_screen();
        return;
    }

    if (diagnostic_log.connected()) {
        (void)diagnostic_log.enqueue(
            "CAL RESULT\r\n"
            "swap_xy=%s\r\n"
            "invert_x=%s\r\n"
            "invert_y=%s\r\n"
            "x_min=%u\r\n"
            "x_max=%u\r\n"
            "y_min=%u\r\n"
            "y_max=%u\r\n",
            touch_calibration.TOUCH_SWAP_XY ? "true" : "false",
            touch_calibration.TOUCH_INVERT_X ? "true" : "false",
            touch_calibration.TOUCH_INVERT_Y ? "true" : "false",
            unsigned(touch_calibration.TOUCH_X_MIN),
            unsigned(touch_calibration.TOUCH_X_MAX),
            unsigned(touch_calibration.TOUCH_Y_MIN),
            unsigned(touch_calibration.TOUCH_Y_MAX));
    }

    if (display_runtime.active_scene().generation != 0) {
        display_backend.present(display_runtime.active_scene(),
                                display_runtime.assets(), true, nullptr, 0);
    } else {
        display_backend.show_waiting_screen();
    }
    calibration_complete = true;
}

void show_test_cross(touch::ScreenPoint mapped, uint32_t now) {
    if (!touch_visual_test_enabled) return;
    restore_test_cross();
    display_backend.draw_crosshair(mapped.x, mapped.y, display::st7796::green);
    constexpr int16_t radius = 11;
    const int16_t left = std::max<int16_t>(0, int16_t(mapped.x) - radius);
    const int16_t top = std::max<int16_t>(0, int16_t(mapped.y) - radius);
    const int16_t right = std::min<int16_t>(
        display::st7796::logical_width - 1, int16_t(mapped.x) + radius);
    const int16_t bottom = std::min<int16_t>(
        display::st7796::logical_height - 1, int16_t(mapped.y) + radius);
    test_cross_region = {left, top, int16_t(right - left + 1),
                         int16_t(bottom - top + 1)};
    test_cross_visible = true;
    test_cross_expires_ms = now + 1000;
}

void handle_touch_down(uint32_t now) {
    touch::RawPoint raw{};
    if (!collect_touch_sample(raw)) {
        if (diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue("CAL INVALID\r\n");
        }
        return;
    }

    if (!calibration_complete) {
        calibration_points[calibration_point_index] = raw;
        calibration_point_captured = true;
        draw_calibration_target(calibration_point_index,
                                display::st7796::green);
        if (diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue("CAL point=%s raw_x=%u raw_y=%u\r\n",
                calibration_point_names[calibration_point_index],
                unsigned(raw.x), unsigned(raw.y));
        }
        return;
    }

    const touch::ScreenPoint mapped = touch::map_touch(raw, touch_calibration);
    tap_detector.begin(mapped, now);
    next_tracking_sample_ms = now + tracking_sample_interval_ms;
    show_test_cross(mapped, now);
    if (touch_visual_test_enabled && diagnostic_log.connected()) {
        (void)diagnostic_log.enqueue(
            "TOUCH raw_x=%u raw_y=%u x=%u y=%u\r\n",
            unsigned(raw.x), unsigned(raw.y), unsigned(mapped.x),
            unsigned(mapped.y));
    }
}

void track_touch(uint32_t now) {
    if (!tap_detector.active() || !touch_controller.contact_active() ||
        static_cast<int32_t>(now - next_tracking_sample_ms) < 0) {
        return;
    }
    next_tracking_sample_ms = now + tracking_sample_interval_ms;
    touch::RawPoint raw{};
    if (collect_touch_sample(raw)) {
        tap_detector.update(touch::map_touch(raw, touch_calibration));
    }
}

void queue_touch_event(touch::ScreenPoint point, uint32_t now) {
    display::TouchHit hit{};
    if (!display_runtime.hit_test(static_cast<int16_t>(point.x),
                                  static_cast<int16_t>(point.y),
                                  display::TapGesture, hit)) {
        if (touch_event_log_enabled && diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue("TOUCH MISS x=%u y=%u\r\n",
                                         unsigned(point.x), unsigned(point.y));
        }
        return;
    }

    std::array<uint8_t, protocol::MAX_PAYLOAD_SIZE> payload{};
    std::size_t payload_length = 0;
    protocol::Packet packet{};
    uint32_t trace_id = 0;
    if (!display_runtime.inject_touch(hit.region_id, display::TapGesture,
                                      static_cast<int16_t>(point.x),
                                      static_cast<int16_t>(point.y), now,
                                      trace_id, payload.data(), payload_length)) {
        if (touch_event_log_enabled && diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue(
                "TOUCH EVENT DROP trace=unknown reason=no_active_scene\r\n");
        }
        return;
    }
    if (touch_event_log_enabled && diagnostic_log.connected()) {
        (void)diagnostic_log.enqueue(
            "TOUCH HIT trace=%lu region=%u control=%lu\r\n",
            static_cast<unsigned long>(trace_id), unsigned(hit.region_id),
            static_cast<unsigned long>(hit.parameter));
    }
    if (!protocol.make_display_link_event(payload.data(), payload_length,
                                          next_display_sequence, packet)) {
        if (touch_event_log_enabled && diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue(
                "TOUCH EVENT DROP trace=%lu reason=encode_failed\r\n",
                static_cast<unsigned long>(trace_id));
        }
        return;
    }
    if (!display_events.push(packet, trace_id, hit.region_id, hit.parameter)) {
        if (touch_event_log_enabled && diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue(
                "TOUCH EVENT DROP trace=%lu reason=queue_full capacity=8\r\n",
                static_cast<unsigned long>(trace_id));
        }
        return;
    }
    if (++next_display_sequence == 0) next_display_sequence = 1;
    if (touch_event_log_enabled && diagnostic_log.connected()) {
        (void)diagnostic_log.enqueue(
            "TOUCH EVENT QUEUED trace=%lu region=%u control=%lu\r\n",
            static_cast<unsigned long>(trace_id), unsigned(hit.region_id),
            static_cast<unsigned long>(hit.parameter));
    }
}

bool queue_system_navigation_event(input::SystemNavigation navigation,
                                   uint32_t now) {
    const uint16_t region = input::system_navigation_region(navigation);
    const uint32_t parameter = static_cast<uint8_t>(navigation);
    std::array<uint8_t, protocol::MAX_PAYLOAD_SIZE> payload{};
    std::size_t payload_length = 0;
    protocol::Packet packet{};
    uint32_t trace_id = 0;
    if (!display_runtime.inject_touch(region, display::TapGesture, 0, 0, now,
                                      trace_id, payload.data(), payload_length) ||
        !protocol.make_display_link_event(payload.data(), payload_length,
                                          next_display_sequence, packet) ||
        !display_events.push(packet, trace_id, region, parameter)) {
        return false;
    }
    if (++next_display_sequence == 0) next_display_sequence = 1;
    return true;
}

void handle_touch_up(uint32_t now) {
    if (!calibration_complete) {
        if (!calibration_point_captured) return;
        draw_calibration_target(calibration_point_index,
                                display::st7796::black);
        calibration_point_captured = false;
        ++calibration_point_index;
        if (calibration_point_index < calibration_points.size()) {
            draw_calibration_target(calibration_point_index,
                                    display::st7796::white);
        } else {
            finish_calibration();
        }
        return;
    }

    touch::ScreenPoint point{};
    if (tap_detector.end(now, point)) {
        ++touch_performance.taps;
        queue_touch_event(point, now);
    }
}

void service_touch(uint32_t now) {
    if (!touch_enabled) return;
    const uint64_t now_us = time_us_64();
    ++touch_performance.poll_calls;
    if (touch_performance.last_service_us != 0) {
        touch_performance.max_service_gap_us = std::max(
            touch_performance.max_service_gap_us,
            now_us - touch_performance.last_service_us);
    }
    touch_performance.last_service_us = now_us;
    const bool irq_high = touch_controller.irq_level_high();
    if (touch_irq_was_high && !irq_high) ++touch_performance.irq_assertions;
    touch_irq_was_high = irq_high;
    bool active = false;
    if (touch_irq.update(irq_high, now, active)) {
        if (active) {
            ++touch_performance.presses;
            handle_touch_down(now);
        } else {
            ++touch_performance.releases;
            handle_touch_up(now);
        }
    }
    track_touch(now);
}
}

bool control_hid_display_init(bool run_bringup_test, bool render_enabled) {
    display_backend.set_render_enabled(render_enabled);
    if (!display_backend.init()) return false;
    if (run_bringup_test) display_backend.run_bringup_test();
    display_backend.show_waiting_screen();
    return true;
}

void control_hid_set_cooperative_service(CooperativeServiceCallback callback) {
    display_backend.set_service_callback(callback);
}

void control_hid_touch_init(bool enabled, bool visual_test_enabled,
                            bool calibration_mode) {
    touch_enabled = enabled;
    touch_visual_test_enabled = visual_test_enabled;
    touch_calibration_mode = calibration_mode;
    diagnostic_was_connected = false;
    diagnostic_log.reset();
    calibration_screen_started = false;
    calibration_complete = !calibration_mode;
    calibration_point_captured = false;
    test_cross_visible = false;
    touch_calibration = calibration_mode
                            ? touch::CalibrationParameters{}
                            : touch::provisional_touch_calibration;
    tap_detector.cancel();
    touch_performance = {};
    if (enabled) {
        touch_controller.init(display::st7796::spi_frequency_hz);
        touch_irq.reset(touch_controller.irq_level_high(),
                        to_ms_since_boot(get_absolute_time()));
        touch_irq_was_high = touch_controller.irq_level_high();
    }
}

void control_hid_init(Device& device) {
    usb_device = &device;
    device.set_trace_callback(+[](Device::TraceStage stage, uint32_t trace_id) {
        if (!INPUT_TRACE_VERBOSE || !diagnostic_log.connected()) return;
        const char* name = "UNKNOWN";
        switch (stage) {
        case Device::TraceStage::KeyboardPressQueued:
            name = "HID KEY PRESS QUEUED"; break;
        case Device::TraceStage::KeyboardReleaseQueued:
            name = "HID KEY RELEASE QUEUED"; break;
        case Device::TraceStage::KeyboardPressSent:
            name = "HID KEY PRESS SENT"; break;
        case Device::TraceStage::KeyboardReleaseSent:
            name = "HID KEY RELEASE SENT"; break;
        case Device::TraceStage::ConsumerPressQueued:
            name = "HID CONSUMER PRESS QUEUED"; break;
        case Device::TraceStage::ConsumerReleaseQueued:
            name = "HID CONSUMER RELEASE QUEUED"; break;
        case Device::TraceStage::ConsumerPressSent:
            name = "HID CONSUMER PRESS SENT"; break;
        case Device::TraceStage::ConsumerReleaseSent:
            name = "HID CONSUMER RELEASE SENT"; break;
        }
        (void)diagnostic_log.enqueue("%s trace=%lu\r\n", name,
                                     static_cast<unsigned long>(trace_id));
    });
    config_manager.initialize(config_storage);
    pico_unique_board_id_t board_id{};
    pico_get_unique_board_id(&board_id);
    uint32_t session = get_rand_32() ^ 0xD15A1A1u;
    for (uint8_t byte : board_id.id) session = (session * 16777619u) ^ byte;
    display_runtime.set_boot_session(session);
}

void control_hid_receive(const uint8_t* report, std::size_t length) {
    if (response_pending || request_pending || report == nullptr ||
        length > pending_request.size()) return;
    std::copy_n(report, length, pending_request.begin());
    pending_request_length = length;
    request_pending = true;
}

void control_hid_report_complete() {
    if (!bootloader_response_in_flight) return;
    bootloader_response_in_flight = false;
    bootloader_controller.response_sent();
}

void control_hid_report_failed() {
    if (!bootloader_response_in_flight) return;
    bootloader_response_in_flight = false;
    bootloader_controller.response_failed();
}

static void process_control_request(uint32_t now) {
    if (!request_pending || response_pending) return;
    request_pending = false;
    protocol.handle(pending_request.data(), pending_request_length,
                    pending_response,
                    {&config_manager, &action_engine, usb_device,
                     &host_actions, &display_runtime, now,
                     &bootloader_controller});
    response_pending = true;
}

static void service_control_output() {
    if (!tud_hid_n_ready(HID_INSTANCE_CONTROL)) return;
    if (response_pending) {
        if (tud_hid_n_report(HID_INSTANCE_CONTROL, 0, pending_response.data(),
                             pending_response.size())) {
            if (bootloader_controller.awaiting_response()) {
                bootloader_response_in_flight = true;
            }
            response_pending = false;
        }
        return;
    }

    if (DisplayEventQueue::Entry* event = display_events.front()) {
        if (tud_hid_n_report(HID_INSTANCE_CONTROL, 0, event->packet.data(),
                             event->packet.size())) {
            if (touch_event_log_enabled && diagnostic_log.connected()) {
                (void)diagnostic_log.enqueue(
                    "TOUCH EVENT SENT trace=%lu region=%u control=%lu\r\n",
                    static_cast<unsigned long>(event->trace_id),
                    unsigned(event->region),
                    static_cast<unsigned long>(event->control));
            }
            display_events.pop();
        } else if (!event->send_failure_logged) {
            event->send_failure_logged = true;
            if (touch_event_log_enabled && diagnostic_log.connected()) {
                (void)diagnostic_log.enqueue(
                    "TOUCH EVENT SEND_FAIL trace=%lu reason=hid_busy retained=true\r\n",
                    static_cast<unsigned long>(event->trace_id));
            }
        }
        return;
    }

    uint32_t action_id = 0;
    if (host_actions.peek(action_id)) {
        protocol::Packet packet{};
        protocol.make_host_action_triggered(action_id, next_host_sequence,
                                            packet);
        if (tud_hid_n_report(HID_INSTANCE_CONTROL, 0, packet.data(),
                             packet.size())) {
            host_actions.pop();
            if (++next_host_sequence == 0) next_host_sequence = 1;
        }
    }
}

void control_hid_task() {
    const uint32_t now = to_ms_since_boot(get_absolute_time());
    process_control_request(now);
    service_control_output();
    display_runtime.task(now);
    if (touch_enabled) {
        if (touch_calibration_mode && !calibration_screen_started) {
            begin_calibration_screen();
        }
        const bool connected = diagnostic_log.connected();
        if (connected && !diagnostic_was_connected) {
            diagnostic_log.reset();
        } else if (!connected && diagnostic_was_connected) {
            diagnostic_log.reset();
        }
        diagnostic_was_connected = connected;

        service_touch(now);
        if (test_cross_visible &&
            static_cast<int32_t>(now - test_cross_expires_ms) >= 0) {
            restore_test_cross();
        }
        diagnostic_log.task();
    }
    service_control_output();
    bootloader_controller.task();
}

void control_hid_service_touch() {
    const uint32_t now = to_ms_since_boot(get_absolute_time());
    process_control_request(now);
    service_touch(now);
    service_control_output();
    bootloader_controller.task();
}

void control_hid_process_event(const Event& event) {
    controls::Control control{};
    switch (event.type) {
    case EventType::ButtonPressed: {
        input::SystemNavigation navigation{};
        if (input::system_navigation_from_button(event.index, navigation)) {
            (void)queue_system_navigation_event(navigation, event.timestamp_ms);
            return;
        }
        control = {controls::ControlType::Button, event.index};
        break;
    }
    case EventType::EncoderRight:
        control = {controls::ControlType::EncoderClockwise, 0};
        break;
    case EventType::EncoderLeft:
        control = {controls::ControlType::EncoderCounterClockwise, 0};
        break;
    case EventType::EncoderPressed:
        control = {controls::ControlType::EncoderPress, 0};
        break;
    case EventType::LedTestStep:
    case EventType::ButtonReleased:
    case EventType::EncoderReleased:
        return;
    }
    if (INPUT_TRACE_VERBOSE && event.trace_id != 0 && diagnostic_log.connected()) {
        (void)diagnostic_log.enqueue("ACTION BEGIN trace=%lu\r\n",
                                     static_cast<unsigned long>(event.trace_id));
    }
    if (usb_device == nullptr) {
        if (event.trace_id != 0 && diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue(
                "DROP trace=%lu stage=action reason=device_unavailable\r\n",
                static_cast<unsigned long>(event.trace_id));
        }
        return;
    }
    const actions::Action* action = config_manager.get_binding(control);
    if (action == nullptr) {
        if (event.trace_id != 0 && diagnostic_log.connected()) {
            (void)diagnostic_log.enqueue(
                "DROP trace=%lu stage=action reason=binding_missing\r\n",
                static_cast<unsigned long>(event.trace_id));
        }
        return;
    }
    const actions::ExecuteResult result = action_engine.execute(
        *action, *usb_device, &host_actions, event.trace_id,
        event.input_timestamp_us, event.source);
    if (event.trace_id != 0 && diagnostic_log.connected() &&
        (INPUT_TRACE_VERBOSE || result == actions::ExecuteResult::Busy)) {
        if (INPUT_TRACE_VERBOSE) {
            (void)diagnostic_log.enqueue("ACTION END trace=%lu result=%s\r\n",
                                         static_cast<unsigned long>(event.trace_id),
                                         execute_result_name(result));
        }
        if (result == actions::ExecuteResult::Busy) {
            (void)diagnostic_log.enqueue(
                "DROP trace=%lu stage=action reason=output_queue_full_or_disconnected\r\n",
                static_cast<unsigned long>(event.trace_id));
        }
    }
}

void control_hid_trace_matrix(uint8_t stage, uint32_t trace_id,
                              uint8_t index) {
    if (!diagnostic_log.connected() || trace_id == 0) return;
    switch (static_cast<input::MatrixTraceStage>(stage)) {
    case input::MatrixTraceStage::RawDown:
        if (!INPUT_TRACE_VERBOSE) break;
        (void)diagnostic_log.enqueue(
            "MATRIX RAW DOWN trace=%lu control=%u\r\n",
            static_cast<unsigned long>(trace_id), unsigned(index));
        break;
    case input::MatrixTraceStage::DebouncedPress:
        if (!INPUT_TRACE_VERBOSE) break;
        (void)diagnostic_log.enqueue(
            "MATRIX DEBOUNCED PRESS trace=%lu control=%u\r\n",
            static_cast<unsigned long>(trace_id), unsigned(index));
        break;
    case input::MatrixTraceStage::EventQueued:
        if (!INPUT_TRACE_VERBOSE) break;
        (void)diagnostic_log.enqueue("EVENT QUEUED trace=%lu control=%u\r\n",
            static_cast<unsigned long>(trace_id), unsigned(index));
        break;
    case input::MatrixTraceStage::EventDropped:
        (void)diagnostic_log.enqueue(
            "DROP trace=%lu stage=event_queue reason=full control=%u\r\n",
            static_cast<unsigned long>(trace_id), unsigned(index));
        break;
    }
}

void control_hid_trace_event_dequeued(const Event& event) {
    if (INPUT_TRACE_VERBOSE && event.type == EventType::ButtonPressed && event.trace_id != 0 &&
        diagnostic_log.connected()) {
        (void)diagnostic_log.enqueue("EVENT DEQUEUED trace=%lu control=%u\r\n",
            static_cast<unsigned long>(event.trace_id), unsigned(event.index));
    }
}

void control_hid_input_summary(
    uint32_t now_ms, uint32_t encoder_raw_transitions,
    uint32_t encoder_valid_transitions, uint32_t encoder_cw_detents,
    uint32_t encoder_ccw_detents, uint32_t encoder_queued,
    uint32_t encoder_dropped, std::size_t event_queue_high_watermark,
    uint32_t event_queue_dropped, uint64_t matrix_samples,
    uint64_t max_main_loop_gap_us, uint64_t max_input_service_gap_us,
    uint64_t max_matrix_scan_gap_us) {
    if (usb_device == nullptr || !diagnostic_log.connected() ||
        now_ms - last_input_summary_ms < input_summary_interval_ms) return;
    const Device::Metrics& metrics = usb_device->metrics();
    const display::St7796Performance& tft = display_backend.performance();
    if (metrics.events_received == last_summary_events_received &&
        encoder_raw_transitions == last_summary_encoder_raw &&
        tft.renders == last_summary_tft_renders &&
        touch_performance.poll_calls == last_summary_touch_polls &&
        metrics.events_dropped == 0 && encoder_dropped == 0 &&
        event_queue_dropped == 0 && metrics.send_failures == 0) return;
    last_input_summary_ms = now_ms;
    last_summary_events_received = metrics.events_received;
    last_summary_encoder_raw = encoder_raw_transitions;
    last_summary_tft_renders = tft.renders;
    last_summary_touch_polls = touch_performance.poll_calls;
    const uint64_t average = metrics.hid_presses == 0 ? 0 :
        metrics.total_input_to_press_us / metrics.hid_presses;
    (void)diagnostic_log.enqueue(
        "INPUT SUMMARY events_received=%lu executed=%lu dropped=%lu queue_high_watermark=%u avg_input_to_press_us=%llu max_input_to_press_us=%llu event_queue_high_watermark=%u event_queue_dropped=%lu send_failures=%lu\r\n",
        static_cast<unsigned long>(metrics.events_received),
        static_cast<unsigned long>(metrics.events_executed),
        static_cast<unsigned long>(metrics.events_dropped),
        unsigned(usb_device->queue_high_watermark()),
        static_cast<unsigned long long>(average),
        static_cast<unsigned long long>(metrics.max_input_to_press_us),
        unsigned(event_queue_high_watermark),
        static_cast<unsigned long>(event_queue_dropped),
        static_cast<unsigned long>(metrics.send_failures));
    (void)diagnostic_log.enqueue(
        "ENC TEST raw_transitions=%lu valid_transitions=%lu cw_detents=%lu ccw_detents=%lu queued=%lu completed=%lu dropped=%lu queue_high_watermark=%u\r\n",
        static_cast<unsigned long>(encoder_raw_transitions),
        static_cast<unsigned long>(encoder_valid_transitions),
        static_cast<unsigned long>(encoder_cw_detents),
        static_cast<unsigned long>(encoder_ccw_detents),
        static_cast<unsigned long>(encoder_queued),
        static_cast<unsigned long>(metrics.encoder_completed),
        static_cast<unsigned long>(encoder_dropped + metrics.encoder_dropped),
        unsigned(usb_device->queue_high_watermark()));
    const uint64_t average_render = tft.renders == 0 ? 0 :
        tft.total_render_us / tft.renders;
    (void)diagnostic_log.enqueue(
        "TFT PERF enabled=%u renders=%lu max_render_us=%llu avg_render_us=%llu max_spi_block_us=%llu bytes=%llu regions=%lu full_redraws=%lu partial_redraws=%lu skipped=%lu cs_violations=%lu\r\n",
        unsigned(display_backend.render_enabled()),
        static_cast<unsigned long>(tft.renders),
        static_cast<unsigned long long>(tft.max_render_us),
        static_cast<unsigned long long>(average_render),
        static_cast<unsigned long long>(tft.max_spi_block_us),
        static_cast<unsigned long long>(tft.pixel_bytes),
        static_cast<unsigned long>(tft.regions),
        static_cast<unsigned long>(tft.full_redraws),
        static_cast<unsigned long>(tft.partial_redraws),
        static_cast<unsigned long>(tft.skipped_renders),
        static_cast<unsigned long>(tft.chip_select_violations));
    (void)diagnostic_log.enqueue(
        "INPUT/TFT matrix_samples=%llu encoder_transitions=%lu input_events=%lu hid_actions=%lu tft_active_us=%llu max_main_loop_gap_us=%llu max_input_service_gap_us=%llu max_matrix_scan_gap_us=%llu\r\n",
        static_cast<unsigned long long>(matrix_samples),
        static_cast<unsigned long>(encoder_raw_transitions),
        static_cast<unsigned long>(metrics.events_received),
        static_cast<unsigned long>(metrics.events_executed),
        static_cast<unsigned long long>(tft.total_render_us),
        static_cast<unsigned long long>(max_main_loop_gap_us),
        static_cast<unsigned long long>(max_input_service_gap_us),
        static_cast<unsigned long long>(max_matrix_scan_gap_us));
    (void)diagnostic_log.enqueue(
        "TOUCH PERF polls=%llu irq_assertions=%lu spi_reads=%lu valid_samples=%lu press=%lu release=%lu taps=%lu max_touch_service_gap_us=%llu\r\n",
        static_cast<unsigned long long>(touch_performance.poll_calls),
        static_cast<unsigned long>(touch_performance.irq_assertions),
        static_cast<unsigned long>(touch_performance.spi_reads),
        static_cast<unsigned long>(touch_performance.valid_samples),
        static_cast<unsigned long>(touch_performance.presses),
        static_cast<unsigned long>(touch_performance.releases),
        static_cast<unsigned long>(touch_performance.taps),
        static_cast<unsigned long long>(touch_performance.max_service_gap_us));
}
}
