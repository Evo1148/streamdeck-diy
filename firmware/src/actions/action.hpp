#pragma once
#include <cstdint>
#include <type_traits>

namespace streamdeck::actions {
enum class ActionType : uint8_t {
    None,
    Keyboard,
    KeyboardShortcut,
    ConsumerControl,
    HostAction,
};

enum class ConsumerAction : uint8_t {
    VolumeUp,
    VolumeDown,
    Mute,
    PlayPause,
};

enum class Modifier : uint8_t {
    Control = 0x01,
    Shift = 0x02,
    Alt = 0x04,
    Gui = 0x08,
};

inline constexpr uint8_t VALID_MODIFIERS = 0x0F;

struct Action {
    ActionType type{ActionType::None};
    uint8_t key_code{};
    uint8_t modifiers{};
    ConsumerAction consumer{ConsumerAction::VolumeUp};
    uint32_t host_action_id{};
};

static_assert(std::is_trivially_copyable_v<Action>);
}
