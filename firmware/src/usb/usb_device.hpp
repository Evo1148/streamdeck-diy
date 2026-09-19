#pragma once
#include <cstdint>
#include "core/events.hpp"
#include "usb/hid_report_scheduler.hpp"

namespace streamdeck::usb {
enum class SendResult { Disabled };

using KeyCode = uint8_t;
namespace keycode {
inline constexpr KeyCode A = 0x04;
}

enum class KeyModifier : uint8_t {
    None = 0,
    LeftControl = 1u << 0,
    LeftShift = 1u << 1,
    LeftAlt = 1u << 2,
    LeftGui = 1u << 3,
    RightControl = 1u << 4,
    RightShift = 1u << 5,
    RightAlt = 1u << 6,
    RightGui = 1u << 7,
};

constexpr KeyModifier operator|(KeyModifier left, KeyModifier right) {
    return static_cast<KeyModifier>(static_cast<uint8_t>(left) |
                                    static_cast<uint8_t>(right));
}

enum class ConsumerControl : uint16_t {
    PlayPause = 0x00CD,
    Mute = 0x00E2,
    VolumeUp = 0x00E9,
    VolumeDown = 0x00EA,
};

class Device {
public:
    struct Metrics {
        uint32_t events_received{};
        uint32_t events_executed{};
        uint32_t events_dropped{};
        uint32_t encoder_completed{};
        uint32_t encoder_dropped{};
        uint32_t send_failures{};
        uint32_t hid_presses{};
        uint64_t last_input_timestamp_us{};
        uint64_t last_action_queued_timestamp_us{};
        uint64_t last_hid_press_timestamp_us{};
        uint64_t last_hid_release_timestamp_us{};
        uint64_t total_input_to_press_us{};
        uint64_t max_input_to_press_us{};
    };
    enum class TraceStage : uint8_t {
        KeyboardPressQueued,
        KeyboardReleaseQueued,
        KeyboardPressSent,
        KeyboardReleaseSent,
        ConsumerPressQueued,
        ConsumerReleaseQueued,
        ConsumerPressSent,
        ConsumerReleaseSent,
    };
    using TraceCallback = void (*)(TraceStage stage, uint32_t trace_id);

    void init();
    void task();
    void service_transport();
    bool ready() const;
    void set_trace_callback(TraceCallback callback) { trace_callback_ = callback; }

    bool send_key(KeyCode key, KeyModifier modifiers = KeyModifier::None,
                  uint32_t trace_id = 0, uint64_t input_timestamp_us = 0,
                  InputSource source = InputSource::Unknown);
    bool send_consumer(ConsumerControl control, uint32_t trace_id = 0,
                       uint64_t input_timestamp_us = 0,
                       InputSource source = InputSource::Unknown);
    void report_complete();
    void report_failed();
    const Metrics& metrics() const { return metrics_; }
    std::size_t queue_high_watermark() const { return reports_.high_watermark(); }

    // Event-to-action mapping will be added with the input modules.
    SendResult send(const Event& event);

private:
    void process_pending_report();
    void trace(TraceStage stage, uint32_t trace_id) const;

    HidReportScheduler reports_{};
    TraceCallback trace_callback_{};
    PendingHidReport in_flight_report_{};
    bool report_in_flight_{};
    Metrics metrics_{};
};
}
