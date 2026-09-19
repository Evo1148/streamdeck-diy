#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

#include "core/events.hpp"

namespace streamdeck::usb {
enum class HidActionKind : uint8_t { Keyboard, Consumer };
enum class HidReportPhase : uint8_t { Press, Release };

struct PendingHidAction {
    HidActionKind kind{};
    uint8_t key{};
    uint8_t modifiers{};
    uint16_t consumer{};
    uint32_t trace_id{};
    uint64_t input_timestamp_us{};
    uint64_t queued_timestamp_us{};
    InputSource source{InputSource::Unknown};
};

struct PendingHidReport {
    PendingHidAction action{};
    HidReportPhase phase{};
};

class HidReportScheduler {
public:
    static constexpr std::size_t capacity = 32;

    bool enqueue_keyboard(
        uint8_t key, uint8_t modifiers, uint32_t trace_id = 0,
        uint64_t input_timestamp_us = 0, uint64_t queued_timestamp_us = 0,
        InputSource source = InputSource::Unknown);
    bool enqueue_consumer(uint16_t control, uint32_t trace_id = 0,
        uint64_t input_timestamp_us = 0, uint64_t queued_timestamp_us = 0,
        InputSource source = InputSource::Unknown);
    bool next_report(PendingHidReport& report) const;
    void report_sent();
    std::size_t size() const { return size_; }
    uint32_t dropped() const { return dropped_; }
    std::size_t high_watermark() const { return high_watermark_; }

private:
    bool push(PendingHidAction action);

    std::array<PendingHidAction, capacity> actions_{};
    std::size_t read_{};
    std::size_t write_{};
    std::size_t size_{};
    uint32_t dropped_{};
    std::size_t high_watermark_{};
    HidReportPhase phase_{HidReportPhase::Press};
};
}
