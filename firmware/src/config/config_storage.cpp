#include "config/config_storage.hpp"

#include <algorithm>
#include <array>

#include "config/config_record.hpp"

#if defined(PICO_ON_DEVICE) && PICO_ON_DEVICE
#include "hardware/flash.h"
#include "hardware/regs/addressmap.h"
#include "pico/flash.h"
#include "pico/platform.h"
#endif

namespace streamdeck::config {
#if defined(PICO_ON_DEVICE) && PICO_ON_DEVICE
namespace {
constexpr uint32_t PHYSICAL_FLASH_SIZE = 2u * 1024u * 1024u;
constexpr uint32_t STORAGE_SIZE = 8u * 1024u;
constexpr uint32_t SLOT_SIZE = 4u * 1024u;
constexpr uint32_t SLOT_A_OFFSET = PHYSICAL_FLASH_SIZE - STORAGE_SIZE;
constexpr uint32_t SLOT_B_OFFSET = SLOT_A_OFFSET + SLOT_SIZE;
constexpr uint32_t FLASH_OPERATION_TIMEOUT_MS = 1000;

static_assert(SLOT_SIZE == FLASH_SECTOR_SIZE);
static_assert(CONFIG_RECORD_SIZE <= FLASH_PAGE_SIZE);
static_assert((SLOT_A_OFFSET % FLASH_SECTOR_SIZE) == 0);
static_assert((SLOT_B_OFFSET % FLASH_SECTOR_SIZE) == 0);

const uint8_t* flash_data(uint32_t offset) {
    return reinterpret_cast<const uint8_t*>(XIP_BASE + offset);
}

struct FlashWrite {
    uint32_t offset;
    const uint8_t* page;
};

void __no_inline_not_in_flash_func(write_slot)(void* parameter) {
    const auto& write = *static_cast<const FlashWrite*>(parameter);
    flash_range_erase(write.offset, FLASH_SECTOR_SIZE);
    flash_range_program(write.offset, write.page, FLASH_PAGE_SIZE);
}
}

bool ConfigStorage::load(DeviceConfig& config) const {
    DecodedConfigRecord selected{};
    if (select_newest_record(flash_data(SLOT_A_OFFSET), SLOT_SIZE,
                             flash_data(SLOT_B_OFFSET), SLOT_SIZE,
                             selected) == SlotSelection::None) {
        return false;
    }
    config = selected.config;
    return true;
}

bool ConfigStorage::save(const DeviceConfig& config) {
    DecodedConfigRecord selected{};
    const SlotSelection current = select_newest_record(
        flash_data(SLOT_A_OFFSET), SLOT_SIZE, flash_data(SLOT_B_OFFSET),
        SLOT_SIZE, selected);
    const uint32_t target_offset =
        current == SlotSelection::A ? SLOT_B_OFFSET : SLOT_A_OFFSET;
    const uint32_t next_sequence =
        current == SlotSelection::None ? 1u : selected.sequence + 1u;

    ConfigRecordBytes record{};
    if (!encode_config_record(config, next_sequence, record)) return false;
    std::array<uint8_t, FLASH_PAGE_SIZE> page{};
    page.fill(0xFF);
    std::copy(record.begin(), record.end(), page.begin());
    FlashWrite operation{target_offset, page.data()};
    if (flash_safe_execute(write_slot, &operation,
                           FLASH_OPERATION_TIMEOUT_MS) != PICO_OK) {
        return false;
    }

    DecodedConfigRecord verified{};
    return decode_config_record(flash_data(target_offset), SLOT_SIZE,
                                verified) &&
           verified.sequence == next_sequence &&
           std::equal(record.begin(), record.end(),
                      flash_data(target_offset));
}
#else
bool ConfigStorage::load(DeviceConfig&) const { return false; }
bool ConfigStorage::save(const DeviceConfig&) { return false; }
#endif
}
