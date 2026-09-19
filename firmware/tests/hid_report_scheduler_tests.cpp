#include <cassert>
#include "usb/hid_report_scheduler.hpp"

int main() {
    using namespace streamdeck::usb;
    const auto simulated_burst_ms = [](std::size_t actions,
                                       uint32_t poll_interval_ms) {
        HidReportScheduler timed;
        for (std::size_t index = 0; index < actions; ++index) {
            assert(timed.enqueue_keyboard(0x04, 0,
                                          static_cast<uint32_t>(index + 1)));
        }
        uint32_t elapsed_ms = 0;
        PendingHidReport report{};
        while (timed.next_report(report)) {
            elapsed_ms += poll_interval_ms;
            timed.report_sent();
        }
        return elapsed_ms;
    };
    assert(simulated_burst_ms(3, 1) == 6);
    assert(simulated_burst_ms(10, 1) == 20);
    assert(simulated_burst_ms(20, 1) == 40);

    HidReportScheduler three;
    for (uint32_t trace = 1; trace <= 3; ++trace) {
        assert(three.enqueue_keyboard(0x04, 0, trace));
    }
    assert(three.size() == 3 && three.high_watermark() == 3);
    for (uint32_t trace = 1; trace <= 3; ++trace) {
        PendingHidReport report{};
        assert(three.next_report(report));
        assert(report.action.trace_id == trace &&
               report.phase == HidReportPhase::Press);
        three.report_sent();
        assert(three.next_report(report));
        assert(report.action.trace_id == trace &&
               report.phase == HidReportPhase::Release);
        three.report_sent();
    }
    assert(three.size() == 0);

    HidReportScheduler scheduler;
    for (uint32_t trace = 1; trace <= 20; ++trace) {
        assert(scheduler.enqueue_keyboard(0x04, 0, trace));
    }
    assert(scheduler.size() == 20);
    assert(scheduler.high_watermark() == 20);

    for (uint32_t trace = 1; trace <= 20; ++trace) {
        PendingHidReport report{};
        assert(scheduler.next_report(report));
        assert(report.action.trace_id == trace);
        assert(report.phase == HidReportPhase::Press);
        PendingHidReport retained{};
        assert(scheduler.next_report(retained));
        assert(retained.action.trace_id == trace &&
               retained.phase == HidReportPhase::Press);
        scheduler.report_sent();
        assert(scheduler.next_report(report));
        assert(report.action.trace_id == trace);
        assert(report.phase == HidReportPhase::Release);
        scheduler.report_sent();
    }
    assert(scheduler.size() == 0);

    for (std::size_t index = 0; index < HidReportScheduler::capacity; ++index) {
        assert(scheduler.enqueue_consumer(0xE9, uint32_t(index + 1)));
    }
    assert(!scheduler.enqueue_consumer(0xEA, 99));
    assert(scheduler.dropped() == 1);
    assert(scheduler.high_watermark() == HidReportScheduler::capacity);

    HidReportScheduler mixed;
    assert(mixed.enqueue_consumer(0xE9, 101));
    PendingHidReport report{};
    assert(mixed.next_report(report) &&
           report.action.kind == HidActionKind::Consumer &&
           report.action.consumer == 0xE9 &&
           report.phase == HidReportPhase::Press);
    mixed.report_sent();
    assert(mixed.next_report(report) &&
           report.phase == HidReportPhase::Release);
    mixed.report_sent();
    assert(!mixed.next_report(report));
}
