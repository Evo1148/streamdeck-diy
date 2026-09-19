#include "actions/action_engine.hpp"

#include "protocol/action_codec.hpp"
#include "usb/usb_device.hpp"

namespace streamdeck::actions {
ExecuteResult ActionEngine::execute(const Action& action,
                                    usb::Device& device,
                                    IHostActionSink* host_actions,
                                    uint32_t trace_id,
                                    uint64_t input_timestamp_us,
                                    InputSource source) const {
    if (!protocol::is_valid_action(action)) return ExecuteResult::Invalid;

    switch (action.type) {
    case ActionType::None:
        return ExecuteResult::NoAction;
    case ActionType::Keyboard:
        return device.send_key(action.key_code, usb::KeyModifier::None, trace_id, input_timestamp_us, source) ? ExecuteResult::Started
                                                : ExecuteResult::Busy;
    case ActionType::KeyboardShortcut:
        return device.send_key(
                   action.key_code,
                   static_cast<usb::KeyModifier>(action.modifiers), trace_id,
                   input_timestamp_us, source)
                   ? ExecuteResult::Started
                   : ExecuteResult::Busy;
    case ActionType::ConsumerControl: {
        usb::ConsumerControl control{};
        switch (action.consumer) {
        case ConsumerAction::VolumeUp:
            control = usb::ConsumerControl::VolumeUp;
            break;
        case ConsumerAction::VolumeDown:
            control = usb::ConsumerControl::VolumeDown;
            break;
        case ConsumerAction::Mute:
            control = usb::ConsumerControl::Mute;
            break;
        case ConsumerAction::PlayPause:
            control = usb::ConsumerControl::PlayPause;
            break;
        }
        return device.send_consumer(control, trace_id, input_timestamp_us, source) ? ExecuteResult::Started
                                             : ExecuteResult::Busy;
    }
    case ActionType::HostAction:
        if (host_actions == nullptr) return ExecuteResult::Unsupported;
        return host_actions->trigger(action.host_action_id)
                   ? ExecuteResult::Started
                   : ExecuteResult::Busy;
    }
    return ExecuteResult::Invalid;
}
}
