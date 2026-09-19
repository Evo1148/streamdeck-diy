#include "touch/xpt2046.hpp"

#include "hardware/gpio.h"
#include "hardware/hardware_map.hpp"
#include "hardware/spi.h"
#include "pico/time.h"

namespace streamdeck::touch {
namespace {
static_assert(hardware::spi_sck == 10);
static_assert(hardware::spi_mosi == 11);
static_assert(hardware::spi_miso == 12);
static_assert(hardware::touch_cs == 9);
}

void Xpt2046::init(uint32_t display_spi_frequency_hz) {
    display_spi_frequency_hz_ = display_spi_frequency_hz;
    gpio_init(hardware::touch_cs);
    gpio_set_dir(hardware::touch_cs, GPIO_OUT);
    gpio_put(hardware::touch_cs, true);

    gpio_init(hardware::touch_irq);
    gpio_set_dir(hardware::touch_irq, GPIO_IN);
    gpio_pull_up(hardware::touch_irq);

    gpio_set_function(hardware::spi_sck, GPIO_FUNC_SPI);
    gpio_set_function(hardware::spi_mosi, GPIO_FUNC_SPI);

    gpio_disable_pulls(hardware::spi_miso);
    gpio_set_function(hardware::spi_miso, GPIO_FUNC_SPI);
    gpio_set_input_enabled(hardware::spi_miso, true);
    initialized_ = true;
}

bool Xpt2046::irq_level_high() const {
    return !initialized_ || gpio_get(hardware::touch_irq);
}

bool Xpt2046::chip_select_high() const {
    return !initialized_ || gpio_get(hardware::touch_cs);
}

bool Xpt2046::contact_active() const {
    return initialized_ && irq_active(irq_level_high());
}

void Xpt2046::set_touch_cs_phase(TouchCsPhase phase) {
    gpio_put(hardware::touch_cs, touch_cs_level_high(phase));
    busy_wait_us_32(2);
}

RawChannelRead Xpt2046::read_channel(uint8_t command) {
    RawChannelRead result{};
    result.command = command;
    set_touch_cs_phase(TouchCsPhase::Idle);
    gpio_put(hardware::tft_cs, true);
    busy_wait_us_32(2);
    result.chip_select.touch_before_high = gpio_get(hardware::touch_cs);
    result.chip_select.tft_high = gpio_get(hardware::tft_cs);
    set_touch_cs_phase(TouchCsPhase::Begin);
    result.chip_select.touch_active_low = !gpio_get(hardware::touch_cs);

    const uint8_t transmit[3]{command, 0, 0};
    uint8_t receive[3]{};
    set_touch_cs_phase(TouchCsPhase::Transfer);
    result.chip_select.touch_transfer_low = !gpio_get(hardware::touch_cs);
    spi_write_read_blocking(spi1, transmit, receive, sizeof(transmit));

    set_touch_cs_phase(TouchCsPhase::End);
    result.chip_select.touch_after_high = gpio_get(hardware::touch_cs);
    result.rx0 = receive[0];
    result.rx1 = receive[1];
    result.rx2 = receive[2];
    result.no_response = xpt2046_no_response(result.rx0, result.rx1,
                                              result.rx2);
    result.value = unpack_xpt2046_12bit(result.rx1, result.rx2);
    return result;
}

bool Xpt2046::read(RawTouchRead& result) {
    if (!contact_active()) return false;

    gpio_put(hardware::tft_cs, true);
    set_touch_cs_phase(TouchCsPhase::Idle);
    spi_set_format(spi1, 8, SPI_CPOL_0, SPI_CPHA_0, SPI_MSB_FIRST);
    spi_set_baudrate(spi1, spi_frequency_hz);

    result.x = read_channel(read_x_command);
    result.y = read_channel(read_y_command);

    spi_set_baudrate(spi1, display_spi_frequency_hz_);
    spi_set_format(spi1, 8, SPI_CPOL_0, SPI_CPHA_0, SPI_MSB_FIRST);
    return true;
}

void Xpt2046::configure_bitbang(bool miso_pull_up) {
    gpio_put(hardware::tft_cs, true);
    set_touch_cs_phase(TouchCsPhase::Idle);

    gpio_init(hardware::spi_sck);
    gpio_set_dir(hardware::spi_sck, GPIO_OUT);
    gpio_put(hardware::spi_sck, false);

    gpio_init(hardware::spi_mosi);
    gpio_set_dir(hardware::spi_mosi, GPIO_OUT);
    gpio_put(hardware::spi_mosi, false);

    gpio_init(hardware::spi_miso);
    gpio_set_dir(hardware::spi_miso, GPIO_IN);
    if (miso_pull_up) {
        gpio_pull_up(hardware::spi_miso);
    } else {
        gpio_disable_pulls(hardware::spi_miso);
    }
}

void Xpt2046::restore_hardware_spi() {
    set_touch_cs_phase(TouchCsPhase::Idle);
    gpio_put(hardware::tft_cs, true);
    gpio_put(hardware::spi_sck, false);
    gpio_put(hardware::spi_mosi, false);
    gpio_disable_pulls(hardware::spi_miso);
    gpio_set_function(hardware::spi_sck, GPIO_FUNC_SPI);
    gpio_set_function(hardware::spi_mosi, GPIO_FUNC_SPI);
    gpio_set_function(hardware::spi_miso, GPIO_FUNC_SPI);
    gpio_set_input_enabled(hardware::spi_miso, true);
    spi_set_format(spi1, 8, SPI_CPOL_0, SPI_CPHA_0, SPI_MSB_FIRST);
    spi_set_baudrate(spi1, display_spi_frequency_hz_);
}

void Xpt2046::bitbang_write_bit(bool value, uint8_t& pulses) {
    gpio_put(hardware::spi_mosi, value);
    busy_wait_us_32(bitbang_half_period_us);
    gpio_put(hardware::spi_sck, true);
    busy_wait_us_32(bitbang_half_period_us);
    gpio_put(hardware::spi_sck, false);
    ++pulses;
}

bool Xpt2046::bitbang_read_bit(uint8_t& pulses) {
    gpio_put(hardware::spi_mosi, false);
    busy_wait_us_32(bitbang_half_period_us);
    gpio_put(hardware::spi_sck, true);
    const bool value = gpio_get(hardware::spi_miso);
    busy_wait_us_32(bitbang_half_period_us);
    gpio_put(hardware::spi_sck, false);
    ++pulses;
    return value;
}

BitBangChannelRead Xpt2046::read_channel_bitbang(uint8_t command) {
    BitBangChannelRead result{};
    result.command = command;

    set_touch_cs_phase(TouchCsPhase::Idle);
    gpio_put(hardware::tft_cs, true);
    gpio_put(hardware::spi_sck, false);
    busy_wait_us_32(2);
    result.chip_select.touch_before_high = gpio_get(hardware::touch_cs);
    result.chip_select.tft_high = gpio_get(hardware::tft_cs);

    set_touch_cs_phase(TouchCsPhase::Begin);
    result.chip_select.touch_active_low = !gpio_get(hardware::touch_cs);

    for (int bit = 7; bit >= 0; --bit) {
        bitbang_write_bit((command & (1u << bit)) != 0, result.clock_pulses);
    }

    set_touch_cs_phase(TouchCsPhase::Transfer);
    result.chip_select.touch_transfer_low = !gpio_get(hardware::touch_cs);
    for (int bit = 7; bit >= 0; --bit) {
        if (bitbang_read_bit(result.clock_pulses)) {
            result.rx1 |= static_cast<uint8_t>(1u << bit);
        }
    }
    for (int bit = 7; bit >= 0; --bit) {
        if (bitbang_read_bit(result.clock_pulses)) {
            result.rx2 |= static_cast<uint8_t>(1u << bit);
        }
    }

    set_touch_cs_phase(TouchCsPhase::End);
    result.chip_select.touch_after_high = gpio_get(hardware::touch_cs);
    return result;
}

bool Xpt2046::read_bitbang(bool miso_pull_up, BitBangTouchRead& result) {
    if (!contact_active()) return false;

    configure_bitbang(miso_pull_up);
    result.miso_pull_up = miso_pull_up;
    result.x = read_channel_bitbang(read_x_command);
    result.y = read_channel_bitbang(read_y_command);
    restore_hardware_spi();
    return true;
}
}
