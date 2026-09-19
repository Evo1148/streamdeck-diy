#include "usb/usb_device.hpp"
#if defined(PICO_ON_DEVICE) && PICO_ON_DEVICE
#include "tusb.h"
#include "usb/control_hid.hpp"
#include "usb/usb_descriptors.h"
#endif

namespace streamdeck::usb {
#if defined(PICO_ON_DEVICE) && PICO_ON_DEVICE
namespace { Device* active_device = nullptr; }
void Device::init() {
    active_device = this;
    control_hid_init(*this);
    tusb_rhport_init_t configuration{};
    configuration.role = TUSB_ROLE_DEVICE;
    configuration.speed = TUSB_SPEED_AUTO;
    (void)tusb_init(0, &configuration);
}
void Device::task() {
    service_transport();
    control_hid_task();
}
void Device::service_transport() {
    tud_task();
    process_pending_report();
}
bool Device::ready() const { return tud_mounted() && !tud_suspended() && tud_hid_ready(); }

bool Device::send_key(KeyCode key, KeyModifier modifiers, uint32_t trace_id,
                      uint64_t input_timestamp_us, InputSource source) {
    ++metrics_.events_received;
    const uint64_t queued_us = time_us_64();
    if (!tud_mounted() || tud_suspended() ||
        !reports_.enqueue_keyboard(key, static_cast<uint8_t>(modifiers),
                                   trace_id, input_timestamp_us, queued_us,
                                   source)) {
        ++metrics_.events_dropped;
        if (source == InputSource::Encoder) ++metrics_.encoder_dropped;
        return false;
    }
    metrics_.last_input_timestamp_us = input_timestamp_us;
    metrics_.last_action_queued_timestamp_us = queued_us;
    trace(TraceStage::KeyboardPressQueued, trace_id);
    trace(TraceStage::KeyboardReleaseQueued, trace_id);
    return true;
}

bool Device::send_consumer(ConsumerControl control, uint32_t trace_id,
                           uint64_t input_timestamp_us, InputSource source) {
    ++metrics_.events_received;
    const uint64_t queued_us = time_us_64();
    if (!tud_mounted() || tud_suspended() ||
        !reports_.enqueue_consumer(static_cast<uint16_t>(control), trace_id,
                                   input_timestamp_us, queued_us, source)) {
        ++metrics_.events_dropped;
        if (source == InputSource::Encoder) ++metrics_.encoder_dropped;
        return false;
    }
    metrics_.last_input_timestamp_us = input_timestamp_us;
    metrics_.last_action_queued_timestamp_us = queued_us;
    trace(TraceStage::ConsumerPressQueued, trace_id);
    trace(TraceStage::ConsumerReleaseQueued, trace_id);
    return true;
}

void Device::process_pending_report() {
    PendingHidReport report{};
    if (report_in_flight_ || !reports_.next_report(report) || !ready()) return;
    bool sent = false;
    if (report.action.kind == HidActionKind::Keyboard) {
        if (report.phase == HidReportPhase::Press) {
        uint8_t keys[6]{};
            keys[0] = report.action.key;
            sent = tud_hid_keyboard_report(
                REPORT_ID_KEYBOARD, report.action.modifiers, keys);
        } else {
            sent = tud_hid_keyboard_report(REPORT_ID_KEYBOARD, 0, nullptr);
        }
    } else {
        if (report.phase == HidReportPhase::Press) {
            const uint16_t usage = report.action.consumer;
            sent = tud_hid_report(
                REPORT_ID_CONSUMER_CONTROL, &usage, sizeof(usage));
        } else {
            constexpr uint16_t released = 0;
            sent = tud_hid_report(
                REPORT_ID_CONSUMER_CONTROL, &released, sizeof(released));
        }
    }
    if (!sent) { ++metrics_.send_failures; return; }
    in_flight_report_ = report;
    report_in_flight_ = true;
    const bool keyboard = report.action.kind == HidActionKind::Keyboard;
    const bool press = report.phase == HidReportPhase::Press;
    const uint64_t sent_us = time_us_64();
    if (press) {
        ++metrics_.hid_presses;
        metrics_.last_hid_press_timestamp_us = sent_us;
        const uint64_t latency = report.action.input_timestamp_us == 0
            ? 0 : sent_us - report.action.input_timestamp_us;
        metrics_.total_input_to_press_us += latency;
        if (latency > metrics_.max_input_to_press_us) {
            metrics_.max_input_to_press_us = latency;
        }
    }
    trace(keyboard
              ? (press ? TraceStage::KeyboardPressSent
                       : TraceStage::KeyboardReleaseSent)
              : (press ? TraceStage::ConsumerPressSent
                       : TraceStage::ConsumerReleaseSent),
          report.action.trace_id);
}

void Device::report_complete() {
    if (!report_in_flight_) return;
    const PendingHidReport completed = in_flight_report_;
    report_in_flight_ = false;
    reports_.report_sent();
    if (completed.phase == HidReportPhase::Release) {
        ++metrics_.events_executed;
        metrics_.last_hid_release_timestamp_us = time_us_64();
        if (completed.action.source == InputSource::Encoder) {
            ++metrics_.encoder_completed;
        }
    }
    process_pending_report();
}

void Device::report_failed() {
    if (!report_in_flight_) return;
    report_in_flight_ = false;
    ++metrics_.send_failures;
}

void Device::trace(TraceStage stage, uint32_t trace_id) const {
    if (trace_callback_ != nullptr && trace_id != 0) {
        trace_callback_(stage, trace_id);
    }
}

SendResult Device::send(const Event&) { return SendResult::Disabled; }
#else
void Device::init() {}
void Device::task() {}
void Device::service_transport() {}
bool Device::ready() const { return false; }
bool Device::send_key(KeyCode, KeyModifier, uint32_t, uint64_t, InputSource) { return false; }
bool Device::send_consumer(ConsumerControl, uint32_t, uint64_t, InputSource) { return false; }
void Device::report_complete() {}
void Device::report_failed() {}
void Device::process_pending_report() {}
void Device::trace(TraceStage, uint32_t) const {}
SendResult Device::send(const Event&) { return SendResult::Disabled; }
#endif
}

#if defined(PICO_ON_DEVICE) && PICO_ON_DEVICE
extern "C" void tud_hid_report_complete_cb(
    uint8_t instance, uint8_t const*, uint16_t) {
    if (instance == HID_INSTANCE_INPUT && streamdeck::usb::active_device != nullptr) {
        streamdeck::usb::active_device->report_complete();
    } else if (instance == HID_INSTANCE_CONTROL) {
        streamdeck::usb::control_hid_report_complete();
    }
}

extern "C" void tud_hid_report_failed_cb(
    uint8_t instance, hid_report_type_t, uint8_t const*, uint16_t) {
    if (instance == HID_INSTANCE_INPUT && streamdeck::usb::active_device != nullptr) {
        streamdeck::usb::active_device->report_failed();
    } else if (instance == HID_INSTANCE_CONTROL) {
        streamdeck::usb::control_hid_report_failed();
    }
}
#endif

#if defined(PICO_ON_DEVICE) && PICO_ON_DEVICE
extern "C" uint16_t tud_hid_get_report_cb(
    uint8_t instance,
    uint8_t report_id,
    hid_report_type_t report_type,
    uint8_t* buffer,
    uint16_t requested_length) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)requested_length;
    return 0;
}

extern "C" void tud_hid_set_report_cb(
    uint8_t instance,
    uint8_t report_id,
    hid_report_type_t report_type,
    uint8_t const* buffer,
    uint16_t buffer_size) {
    (void)report_id;
    if (instance == HID_INSTANCE_CONTROL &&
        report_type == HID_REPORT_TYPE_OUTPUT) {
        streamdeck::usb::control_hid_receive(buffer, buffer_size);
    }
}
#endif
