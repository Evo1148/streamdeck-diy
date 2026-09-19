#include <cassert>

#include "config/config_record.hpp"

int main() {
    using namespace streamdeck;
    using namespace streamdeck::config;

    DeviceConfig config{};
    config.buttons[3].type = actions::ActionType::KeyboardShortcut;
    config.buttons[3].key_code = 0x04;
    config.buttons[3].modifiers =
        static_cast<uint8_t>(actions::Modifier::Control);

    ConfigRecordBytes first{};
    assert(encode_config_record(config, 27, first));
    DecodedConfigRecord decoded{};
    assert(decode_config_record(first.data(), first.size(), decoded));
    assert(decoded.sequence == 27);
    assert(decoded.config.buttons[3].type ==
           actions::ActionType::KeyboardShortcut);
    assert(decoded.config.buttons[3].key_code == 0x04);

    auto corrupt_crc = first;
    corrupt_crc.back() ^= 0x01;
    assert(!decode_config_record(corrupt_crc.data(), corrupt_crc.size(),
                                 decoded));
    auto corrupt_magic = first;
    corrupt_magic[0] ^= 0x01;
    assert(!decode_config_record(corrupt_magic.data(), corrupt_magic.size(),
                                 decoded));
    auto corrupt_version = first;
    corrupt_version[4] = 2;
    assert(!decode_config_record(corrupt_version.data(),
                                 corrupt_version.size(), decoded));

    ConfigRecordBytes second{};
    assert(encode_config_record(config, 28, second));
    assert(select_newest_record(first.data(), first.size(), second.data(),
                                second.size(), decoded) == SlotSelection::B);
    assert(decoded.sequence == 28);

    assert(select_newest_record(first.data(), first.size(), corrupt_crc.data(),
                                corrupt_crc.size(), decoded) ==
           SlotSelection::A);
    assert(decoded.sequence == 27);

    ConfigRecordBytes invalid{};
    invalid.fill(0xFF);
    assert(select_newest_record(invalid.data(), invalid.size(),
                                corrupt_crc.data(), corrupt_crc.size(),
                                decoded) == SlotSelection::None);

    ConfigRecordBytes before_wrap{};
    ConfigRecordBytes after_wrap{};
    assert(encode_config_record(config, 0xFFFFFFFFu, before_wrap));
    assert(encode_config_record(config, 0u, after_wrap));
    assert(sequence_is_newer(0u, 0xFFFFFFFFu));
    assert(select_newest_record(before_wrap.data(), before_wrap.size(),
                                after_wrap.data(), after_wrap.size(), decoded) ==
           SlotSelection::B);
}
