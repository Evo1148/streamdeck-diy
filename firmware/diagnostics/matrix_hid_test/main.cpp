#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>

#include "core/events.hpp"
#include "hardware/gpio.h"
#include "hardware/hardware_map.hpp"
#include "input/matrix_debouncer.hpp"
#include "pico/stdlib.h"
#include "tusb.h"

namespace {
using streamdeck::Event;
using streamdeck::EventQueue;
using streamdeck::EventType;

constexpr std::array<uint8_t, 12> KEYCODES{
    HID_KEY_1, HID_KEY_2, HID_KEY_3,
    HID_KEY_4, HID_KEY_5, HID_KEY_6,
    HID_KEY_7, HID_KEY_8, HID_KEY_9,
    HID_KEY_0, HID_KEY_MINUS, HID_KEY_EQUAL,
};
static_assert(KEYCODES.size() == streamdeck::hardware::key_count);

class MatrixScanner {
public:
    void init() {
        for (const uint8_t row : streamdeck::hardware::rows) {
            gpio_init(row);
            gpio_set_dir(row, GPIO_IN);
            gpio_pull_up(row);
        }
        for (const uint8_t column : streamdeck::hardware::columns) {
            gpio_init(column);
            gpio_put(column, false);
            gpio_set_dir(column, GPIO_IN);
            gpio_disable_pulls(column);
        }
    }

    bool scan(uint32_t now_ms, EventQueue<>& events) {
        bool queued = true;
        for (std::size_t column = 0;
             column < streamdeck::hardware::columns.size(); ++column) {
            const uint8_t column_pin = streamdeck::hardware::columns[column];
            gpio_put(column_pin, false);
            gpio_set_dir(column_pin, GPIO_OUT);
            busy_wait_us_32(5);
            for (std::size_t row = 0; row < streamdeck::hardware::rows.size(); ++row) {
                const bool pressed = !gpio_get(streamdeck::hardware::rows[row]);
                const std::size_t index =
                    row * streamdeck::hardware::columns.size() + column;
                Event event{};
                if (debouncer_.sample(index, pressed, now_ms, event) &&
                    !events.push(event)) queued = false;
            }
            gpio_set_dir(column_pin, GPIO_IN);
            gpio_disable_pulls(column_pin);
        }
        return queued;
    }

private:
    streamdeck::input::MatrixDebouncer debouncer_{};
};

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
    std::array<uint8_t, 12> queue_{};
    std::size_t read_{};
    std::size_t write_{};
    std::size_t count_{};
    bool release_pending_{};
};
}

int main() {
    stdio_init_all();
    MatrixScanner matrix;
    KeyboardSender keyboard;
    EventQueue<> events;
    matrix.init();

    tusb_rhport_init_t usb_configuration{};
    usb_configuration.role = TUSB_ROLE_DEVICE;
    usb_configuration.speed = TUSB_SPEED_AUTO;
    (void)tusb_init(0, &usb_configuration);

    while (true) {
        tud_task();
        const uint32_t now = to_ms_since_boot(get_absolute_time());
        if (!matrix.scan(now, events)) panic("Matrix test event queue overflow");
        Event event{};
        while (events.pop(event)) {
            if (event.type == EventType::ButtonPressed &&
                event.index < KEYCODES.size()) {
                std::printf("Pulsado: SW%u\r\n", unsigned(event.index) + 1);
                if (!keyboard.enqueue(KEYCODES[event.index]))
                    panic("Matrix test HID queue overflow");
            }
        }
        keyboard.task();
        sleep_ms(1);
    }
}

extern "C" uint16_t tud_hid_get_report_cb(
    uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
    uint8_t* buffer, uint16_t requested_length) {
    (void)instance; (void)report_id; (void)report_type;
    (void)buffer; (void)requested_length;
    return 0;
}

extern "C" void tud_hid_set_report_cb(
    uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
    uint8_t const* buffer, uint16_t buffer_size) {
    (void)instance; (void)report_id; (void)report_type;
    (void)buffer; (void)buffer_size;
}
