#pragma once

#include <cstdint>

#include "touch/touch_math.hpp"

namespace streamdeck::touch {
enum class TouchCsPhase : uint8_t { Idle, Begin, Transfer, End };

constexpr bool touch_cs_level_high(TouchCsPhase phase) {
    return phase == TouchCsPhase::Idle || phase == TouchCsPhase::End;
}

struct ChipSelectDiagnostic {
    bool touch_before_high{};
    bool tft_high{};
    bool touch_active_low{};
    bool touch_transfer_low{};
    bool touch_after_high{};

    constexpr bool valid() const {
        return touch_before_high && tft_high && touch_active_low &&
               touch_transfer_low && touch_after_high;
    }
};

struct RawChannelRead {
    uint8_t command{};
    uint8_t rx0{};
    uint8_t rx1{};
    uint8_t rx2{};
    uint16_t value{};
    bool no_response{};
    ChipSelectDiagnostic chip_select{};
};

struct RawTouchRead {
    RawChannelRead x{};
    RawChannelRead y{};
};

struct BitBangChannelRead {
    uint8_t command{};
    uint8_t rx1{};
    uint8_t rx2{};
    uint8_t clock_pulses{};
    ChipSelectDiagnostic chip_select{};
};

struct BitBangTouchRead {
    BitBangChannelRead x{};
    BitBangChannelRead y{};
    bool miso_pull_up{};
};

struct MisoPinDiagnostic {
    bool pull_up_high{};
    bool pull_down_high{};
};

class Xpt2046 {
public:
    void init(uint32_t display_spi_frequency_hz);
    bool contact_active() const;
    bool irq_level_high() const;
    bool chip_select_high() const;
    bool read(RawTouchRead& result);
    bool read_bitbang(bool miso_pull_up, BitBangTouchRead& result);
    MisoPinDiagnostic miso_diagnostic() const { return miso_diagnostic_; }

    static constexpr uint32_t spi_frequency_hz = 1'000'000;
    static constexpr uint8_t read_x_command = 0xD0;
    static constexpr uint8_t read_y_command = 0x90;
    static constexpr uint32_t bitbang_half_period_us = 8;
    static constexpr uint8_t bitbang_clock_pulses = 24;

private:
    void set_touch_cs_phase(TouchCsPhase phase);
    RawChannelRead read_channel(uint8_t command);
    BitBangChannelRead read_channel_bitbang(uint8_t command);
    void configure_bitbang(bool miso_pull_up);
    void restore_hardware_spi();
    void bitbang_write_bit(bool value, uint8_t& pulses);
    bool bitbang_read_bit(uint8_t& pulses);
    uint32_t display_spi_frequency_hz_{};
    MisoPinDiagnostic miso_diagnostic_{};
    bool initialized_{};
};
}
