#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

#include "config/device_config.hpp"

namespace streamdeck::config {
inline constexpr uint32_t CONFIG_RECORD_MAGIC = 0x43464453; // "SDFC"
inline constexpr uint16_t STORAGE_VERSION = 1;
inline constexpr std::size_t CONFIG_PAYLOAD_SIZE = 124;
inline constexpr std::size_t CONFIG_RECORD_HEADER_SIZE = 20;
inline constexpr std::size_t CONFIG_RECORD_SIZE =
    CONFIG_RECORD_HEADER_SIZE + CONFIG_PAYLOAD_SIZE;

using ConfigRecordBytes = std::array<uint8_t, CONFIG_RECORD_SIZE>;

struct DecodedConfigRecord {
    DeviceConfig config{};
    uint32_t sequence{};
};

enum class SlotSelection : uint8_t { None, A, B };

bool encode_config_record(const DeviceConfig& config, uint32_t sequence,
                          ConfigRecordBytes& record);
bool decode_config_record(const uint8_t* data, std::size_t size,
                          DecodedConfigRecord& record);
bool sequence_is_newer(uint32_t candidate, uint32_t reference);
SlotSelection select_newest_record(const uint8_t* slot_a,
                                   std::size_t slot_a_size,
                                   const uint8_t* slot_b,
                                   std::size_t slot_b_size,
                                   DecodedConfigRecord& record);
}
