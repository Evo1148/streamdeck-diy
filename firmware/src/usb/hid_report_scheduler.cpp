#include "usb/hid_report_scheduler.hpp"

namespace streamdeck::usb {
bool HidReportScheduler::push(PendingHidAction action) {
    if (size_ == actions_.size()) {
        ++dropped_;
        return false;
    }
    actions_[write_] = action;
    write_ = (write_ + 1) % actions_.size();
    ++size_;
    if (size_ > high_watermark_) high_watermark_ = size_;
    return true;
}

bool HidReportScheduler::enqueue_keyboard(
    uint8_t key, uint8_t modifiers, uint32_t trace_id,
    uint64_t input_timestamp_us, uint64_t queued_timestamp_us,
    InputSource source) {
    if (key == 0) return false;
    return push({HidActionKind::Keyboard, key, modifiers, 0, trace_id,
                 input_timestamp_us, queued_timestamp_us, source});
}

bool HidReportScheduler::enqueue_consumer(
    uint16_t control, uint32_t trace_id, uint64_t input_timestamp_us,
    uint64_t queued_timestamp_us, InputSource source) {
    return push({HidActionKind::Consumer, 0, 0, control, trace_id,
                 input_timestamp_us, queued_timestamp_us, source});
}

bool HidReportScheduler::next_report(PendingHidReport& report) const {
    if (size_ == 0) return false;
    report = {actions_[read_], phase_};
    return true;
}

void HidReportScheduler::report_sent() {
    if (size_ == 0) return;
    if (phase_ == HidReportPhase::Press) {
        phase_ = HidReportPhase::Release;
        return;
    }
    read_ = (read_ + 1) % actions_.size();
    --size_;
    phase_ = HidReportPhase::Press;
}
}
