#include <cassert>
#include "core/events.hpp"
#include "core/led_self_test.hpp"
#include "hardware/hardware_map.hpp"
#include "usb/usb_device.hpp"

int main() {
    using namespace streamdeck;
    Event event;
    EventQueue<2> queue;
    assert(!queue.pop(event));
    EventQueue<> burst;
    for (uint8_t index = 0; index < 20; ++index) {
        assert(burst.push({EventType::ButtonPressed, index, index, index + 1u}));
    }
    for (uint8_t index = 0; index < 20; ++index) {
        assert(burst.pop(event));
        assert(event.index == index && event.trace_id == index + 1u);
    }
    assert(!burst.pop(event) && burst.dropped() == 0);
    assert(burst.high_watermark() == 20 && burst.size() == 0);
    assert(queue.push({EventType::ButtonPressed, 11, 12}));
    assert(queue.push({EventType::ButtonReleased, 11, 13}));
    assert(!queue.push({EventType::EncoderLeft, 0, 14}));
    assert(queue.dropped() == 1);
    assert(queue.high_watermark() == 2);
    assert(queue.pop(event) && event.type == EventType::ButtonPressed && event.index == 11);
    assert(queue.push({EventType::EncoderRight, 0, 15}));
    assert(queue.pop(event) && event.type == EventType::ButtonReleased);
    assert(queue.pop(event) && event.type == EventType::EncoderRight && event.timestamp_ms == 15);
    assert(!queue.pop(event));
    LedSelfTest test;
    constexpr uint32_t start = UINT32_MAX - 100;
    assert(test.poll(start, event) && event.index == 0);
    assert(!test.poll(start + 699u, event));
    assert(test.poll(start + 700u, event) && event.index == 1);
    assert(test.poll(start + 1400u, event) && event.index == 2);
    assert(test.poll(start + 2100u, event) && event.index == 3);
    assert(test.poll(start + 2800u, event) && event.index == 0);
    usb::Device device;
    device.init(); device.task();
    assert(!device.ready());
    assert(device.send(event) == usb::SendResult::Disabled);
}
