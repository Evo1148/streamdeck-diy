#pragma once
#include <cstddef>
#include <cstdint>

#include "actions/action.hpp"

namespace streamdeck::protocol {
inline constexpr std::size_t ACTION_PAYLOAD_SIZE = 8;

bool is_valid_action(const actions::Action& action);
bool decode_action(const uint8_t* data, std::size_t length,
                   actions::Action& action);
void encode_action(const actions::Action& action, uint8_t* data);
}
