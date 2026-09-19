#pragma once
#include <array>
#include <cstddef>
#include <cstdint>

namespace streamdeck {
enum class EventType : uint8_t {
    LedTestStep, ButtonPressed, ButtonReleased,
    EncoderLeft, EncoderRight, EncoderPressed, EncoderReleased
};
enum class InputSource : uint8_t { Unknown, Matrix, Encoder, Protocol };
struct Event {
    EventType type{};
    uint8_t index{}; // Key 0..11 or LED test step 0..3.
    uint32_t timestamp_ms{};
    uint32_t trace_id{};
    uint64_t input_timestamp_us{};
    InputSource source{InputSource::Unknown};
};

// Single producer/consumer in the main loop only. No IRQ/multicore access.
template<std::size_t Capacity = 32>
class EventQueue {
    static_assert(Capacity > 0);
public:
    bool push(Event event) {
        if (size_ == Capacity) { ++dropped_; return false; }
        data_[write_] = event;
        write_ = (write_ + 1) % Capacity;
        ++size_;
        if (size_ > high_watermark_) high_watermark_ = size_;
        return true;
    }
    bool pop(Event& event) {
        if (size_ == 0) return false;
        event = data_[read_];
        read_ = (read_ + 1) % Capacity;
        --size_;
        return true;
    }
    uint32_t dropped() const { return dropped_; }
    std::size_t size() const { return size_; }
    std::size_t high_watermark() const { return high_watermark_; }
private:
    std::array<Event, Capacity> data_{};
    std::size_t read_{}, write_{}, size_{};
    uint32_t dropped_{};
    std::size_t high_watermark_{};
};
}
