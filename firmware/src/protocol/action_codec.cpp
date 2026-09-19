#include "protocol/action_codec.hpp"

namespace streamdeck::protocol {
namespace {
uint32_t read_u32(const uint8_t* data) {
    return static_cast<uint32_t>(data[0]) |
           (static_cast<uint32_t>(data[1]) << 8) |
           (static_cast<uint32_t>(data[2]) << 16) |
           (static_cast<uint32_t>(data[3]) << 24);
}

void write_u32(uint8_t* data, uint32_t value) {
    data[0] = static_cast<uint8_t>(value);
    data[1] = static_cast<uint8_t>(value >> 8);
    data[2] = static_cast<uint8_t>(value >> 16);
    data[3] = static_cast<uint8_t>(value >> 24);
}
}

bool is_valid_action(const actions::Action& action) {
    using actions::ActionType;
    switch (action.type) {
    case ActionType::None:
        return action.key_code == 0 && action.modifiers == 0 &&
               action.consumer == actions::ConsumerAction::VolumeUp &&
               action.host_action_id == 0;
    case ActionType::Keyboard:
        return action.key_code != 0 && action.modifiers == 0 &&
               action.consumer == actions::ConsumerAction::VolumeUp &&
               action.host_action_id == 0;
    case ActionType::KeyboardShortcut:
        return action.key_code != 0 && action.modifiers != 0 &&
               (action.modifiers & ~actions::VALID_MODIFIERS) == 0 &&
               action.consumer == actions::ConsumerAction::VolumeUp &&
               action.host_action_id == 0;
    case ActionType::ConsumerControl:
        return action.key_code == 0 && action.modifiers == 0 &&
               static_cast<uint8_t>(action.consumer) <=
                   static_cast<uint8_t>(actions::ConsumerAction::PlayPause) &&
               action.host_action_id == 0;
    case ActionType::HostAction:
        return action.key_code == 0 && action.modifiers == 0 &&
               action.consumer == actions::ConsumerAction::VolumeUp;
    }
    return false;
}

bool decode_action(const uint8_t* data, std::size_t length,
                   actions::Action& action) {
    if (data == nullptr || length != ACTION_PAYLOAD_SIZE) return false;
    action.type = static_cast<actions::ActionType>(data[0]);
    action.key_code = data[1];
    action.modifiers = data[2];
    action.consumer = static_cast<actions::ConsumerAction>(data[3]);
    action.host_action_id = read_u32(&data[4]);
    return is_valid_action(action);
}

void encode_action(const actions::Action& action, uint8_t* data) {
    data[0] = static_cast<uint8_t>(action.type);
    data[1] = action.key_code;
    data[2] = action.modifiers;
    data[3] = static_cast<uint8_t>(action.consumer);
    write_u32(&data[4], action.host_action_id);
}
}
