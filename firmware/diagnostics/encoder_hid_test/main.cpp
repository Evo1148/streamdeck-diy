#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>

#include "core/events.hpp"
#include "input/encoder.hpp"
#include "pico/stdlib.h"
#include "tusb.h"

namespace {
using streamdeck::Event;
using streamdeck::EventQueue;
using streamdeck::EventType;

class KeyboardSender {
public:
    bool enqueue(uint8_t keycode) {
        if (keycode == 0 || count_ == queue_.size()) return false;
        queue_[write_] = keycode;
        write_ = (write_ + 1) % queue_.size();
        ++count_;
        return true;
    }

    void task() {
        if (!tud_mounted() || tud_suspended() || !tud_hid_ready()) return;
        if (release_pending_) {
            if (tud_hid_keyboard_report(0, 0, nullptr)) release_pending_ = false;
            return;
        }
        if (count_ == 0) return;

        uint8_t keys[6]{};
        keys[0] = queue_[read_];
        if (tud_hid_keyboard_report(0, 0, keys)) {
            read_ = (read_ + 1) % queue_.size();
            --count_;
            release_pending_ = true;
        }
    }

private:
    std::array<uint8_t, 32> queue_{};
    std::size_t read_{};
    std::size_t write_{};
    std::size_t count_{};
    bool release_pending_{};
};
}

int main() {
    stdio_init_all();

    streamdeck::input::Encoder encoder;
    EventQueue<> events;
    KeyboardSender keyboard;
    encoder.init();

    tusb_rhport_init_t usb_configuration{};
    usb_configuration.role = TUSB_ROLE_DEVICE;
    usb_configuration.speed = TUSB_SPEED_AUTO;
    (void)tusb_init(0, &usb_configuration);

    while (true) {
        tud_task();
        const uint32_t now = to_ms_since_boot(get_absolute_time());
        if (!encoder.task(now, events)) panic("Encoder test event queue overflow");

        Event event{};
        while (events.pop(event)) {
            uint8_t keycode = 0;
            switch (event.type) {
            case EventType::EncoderLeft:
                std::printf("Giro izquierda\r\n");
                keycode = HID_KEY_I;
                break;
            case EventType::EncoderRight:
                std::printf("Giro derecha\r\n");
                keycode = HID_KEY_D;
                break;
            case EventType::EncoderPressed:
                std::printf("Pulsacion\r\n");
                keycode = HID_KEY_P;
                break;
            case EventType::EncoderReleased:
                std::printf("Soltado\r\n");
                break;
            default:
                break;
            }
            if (keycode != 0 && !keyboard.enqueue(keycode))
                panic("Encoder test HID queue overflow");
        }

        keyboard.task();
        sleep_ms(1);
    }
}

extern "C" uint16_t tud_hid_get_report_cb(
    uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
    uint8_t* buffer, uint16_t requested_length) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)requested_length;
    return 0;
}

extern "C" void tud_hid_set_report_cb(
    uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
    uint8_t const* buffer, uint16_t buffer_size) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)buffer_size;
}
