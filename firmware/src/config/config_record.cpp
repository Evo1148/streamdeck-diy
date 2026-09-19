#include "config/config_record.hpp"

#include "protocol/action_codec.hpp"

namespace streamdeck::config {
namespace {
constexpr std::size_t OFFSET_MAGIC = 0;
constexpr std::size_t OFFSET_STORAGE_VERSION = 4;
constexpr std::size_t OFFSET_CONFIG_VERSION = 6;
constexpr std::size_t OFFSET_SEQUENCE = 8;
constexpr std::size_t OFFSET_PAYLOAD_SIZE = 12;
constexpr std::size_t OFFSET_CRC32 = 16;
constexpr std::size_t OFFSET_PAYLOAD = CONFIG_RECORD_HEADER_SIZE;

uint16_t read_u16(const uint8_t* data) {
    return static_cast<uint16_t>(data[0]) |
           static_cast<uint16_t>(static_cast<uint16_t>(data[1]) << 8);
}

uint32_t read_u32(const uint8_t* data) {
    return static_cast<uint32_t>(data[0]) |
           (static_cast<uint32_t>(data[1]) << 8) |
           (static_cast<uint32_t>(data[2]) << 16) |
           (static_cast<uint32_t>(data[3]) << 24);
}

void write_u16(uint8_t* data, uint16_t value) {
    data[0] = static_cast<uint8_t>(value);
    data[1] = static_cast<uint8_t>(value >> 8);
}

void write_u32(uint8_t* data, uint32_t value) {
    data[0] = static_cast<uint8_t>(value);
    data[1] = static_cast<uint8_t>(value >> 8);
    data[2] = static_cast<uint8_t>(value >> 16);
    data[3] = static_cast<uint8_t>(value >> 24);
}

uint32_t update_crc32(uint32_t crc, const uint8_t* data, std::size_t size) {
    for (std::size_t i = 0; i < size; ++i) {
        crc ^= data[i];
        for (uint8_t bit = 0; bit < 8; ++bit) {
            crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1u)));
        }
    }
    return crc;
}

uint32_t record_crc32(const uint8_t* data) {
    uint32_t crc = update_crc32(0xFFFFFFFFu, data, OFFSET_CRC32);
    crc = update_crc32(crc, data + OFFSET_PAYLOAD, CONFIG_PAYLOAD_SIZE);
    return ~crc;
}

bool valid_config(const DeviceConfig& config) {
    if (config.config_version != CONFIG_VERSION || config.active_profile != 0 ||
        config.active_page != 0) {
        return false;
    }
    for (const auto& action : config.buttons) {
        if (!protocol::is_valid_action(action)) return false;
    }
    return protocol::is_valid_action(config.encoder.clockwise) &&
           protocol::is_valid_action(config.encoder.counterclockwise) &&
           protocol::is_valid_action(config.encoder.press);
}

void encode_config_payload(const DeviceConfig& config, uint8_t* payload) {
    write_u16(payload, config.config_version);
    payload[2] = config.active_profile;
    payload[3] = config.active_page;
    std::size_t offset = 4;
    for (const auto& action : config.buttons) {
        protocol::encode_action(action, payload + offset);
        offset += protocol::ACTION_PAYLOAD_SIZE;
    }
    protocol::encode_action(config.encoder.clockwise, payload + offset);
    offset += protocol::ACTION_PAYLOAD_SIZE;
    protocol::encode_action(config.encoder.counterclockwise, payload + offset);
    offset += protocol::ACTION_PAYLOAD_SIZE;
    protocol::encode_action(config.encoder.press, payload + offset);
}

bool decode_config_payload(const uint8_t* payload, DeviceConfig& config) {
    config = {};
    config.config_version = read_u16(payload);
    config.active_profile = payload[2];
    config.active_page = payload[3];
    std::size_t offset = 4;
    for (auto& action : config.buttons) {
        if (!protocol::decode_action(payload + offset,
                                     protocol::ACTION_PAYLOAD_SIZE, action)) {
            return false;
        }
        offset += protocol::ACTION_PAYLOAD_SIZE;
    }
    if (!protocol::decode_action(payload + offset,
                                 protocol::ACTION_PAYLOAD_SIZE,
                                 config.encoder.clockwise)) {
        return false;
    }
    offset += protocol::ACTION_PAYLOAD_SIZE;
    if (!protocol::decode_action(payload + offset,
                                 protocol::ACTION_PAYLOAD_SIZE,
                                 config.encoder.counterclockwise)) {
        return false;
    }
    offset += protocol::ACTION_PAYLOAD_SIZE;
    return protocol::decode_action(payload + offset,
                                   protocol::ACTION_PAYLOAD_SIZE,
                                   config.encoder.press) &&
           valid_config(config);
}
}

bool encode_config_record(const DeviceConfig& config, uint32_t sequence,
                          ConfigRecordBytes& record) {
    if (!valid_config(config)) return false;
    record.fill(0);
    write_u32(record.data() + OFFSET_MAGIC, CONFIG_RECORD_MAGIC);
    write_u16(record.data() + OFFSET_STORAGE_VERSION, STORAGE_VERSION);
    write_u16(record.data() + OFFSET_CONFIG_VERSION, CONFIG_VERSION);
    write_u32(record.data() + OFFSET_SEQUENCE, sequence);
    write_u32(record.data() + OFFSET_PAYLOAD_SIZE, CONFIG_PAYLOAD_SIZE);
    encode_config_payload(config, record.data() + OFFSET_PAYLOAD);
    write_u32(record.data() + OFFSET_CRC32, record_crc32(record.data()));
    return true;
}

bool decode_config_record(const uint8_t* data, std::size_t size,
                          DecodedConfigRecord& record) {
    if (data == nullptr || size < CONFIG_RECORD_SIZE ||
        read_u32(data + OFFSET_MAGIC) != CONFIG_RECORD_MAGIC ||
        read_u16(data + OFFSET_STORAGE_VERSION) != STORAGE_VERSION ||
        read_u16(data + OFFSET_CONFIG_VERSION) != CONFIG_VERSION ||
        read_u32(data + OFFSET_PAYLOAD_SIZE) != CONFIG_PAYLOAD_SIZE ||
        read_u32(data + OFFSET_CRC32) != record_crc32(data)) {
        return false;
    }
    DeviceConfig config{};
    if (!decode_config_payload(data + OFFSET_PAYLOAD, config)) return false;
    record.config = config;
    record.sequence = read_u32(data + OFFSET_SEQUENCE);
    return true;
}

bool sequence_is_newer(uint32_t candidate, uint32_t reference) {
    const uint32_t distance = candidate - reference;
    return distance != 0 && distance < 0x80000000u;
}

SlotSelection select_newest_record(const uint8_t* slot_a,
                                   std::size_t slot_a_size,
                                   const uint8_t* slot_b,
                                   std::size_t slot_b_size,
                                   DecodedConfigRecord& record) {
    DecodedConfigRecord a{};
    DecodedConfigRecord b{};
    const bool a_valid = decode_config_record(slot_a, slot_a_size, a);
    const bool b_valid = decode_config_record(slot_b, slot_b_size, b);
    if (!a_valid && !b_valid) return SlotSelection::None;
    if (!b_valid || (a_valid && !sequence_is_newer(b.sequence, a.sequence))) {
        record = a;
        return SlotSelection::A;
    }
    record = b;
    return SlotSelection::B;
}
}
