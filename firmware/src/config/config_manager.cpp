#include "config/config_manager.hpp"

#include "config/config_storage.hpp"
#include "protocol/action_codec.hpp"

namespace streamdeck::config {
void ConfigManager::initialize(IConfigStorage& storage) {
    storage_ = &storage;
    DeviceConfig loaded{};
    config_ = storage.load(loaded) ? loaded : DeviceConfig{};
}

bool ConfigManager::set_binding(controls::Control control,
                                const actions::Action& action) {
    if (!protocol::is_valid_action(action)) return false;
    actions::Action* destination = binding(config_, control);
    if (destination == nullptr) return false;
    *destination = action;
    return true;
}

SetBindingResult ConfigManager::set_binding_and_save(
    controls::Control control, const actions::Action& action) {
    if (!protocol::is_valid_action(action)) return SetBindingResult::Invalid;
    DeviceConfig& target = update_active_ ? staging_ : config_;
    actions::Action* destination = binding(target, control);
    if (destination == nullptr) return SetBindingResult::Invalid;
    if (update_active_) {
        *destination = action;
        return SetBindingResult::Saved;
    }
    if (storage_ == nullptr) return SetBindingResult::StorageError;

    const actions::Action previous = *destination;
    *destination = action;
    if (storage_->save(config_)) return SetBindingResult::Saved;
    *destination = previous;
    return SetBindingResult::StorageError;
}

const actions::Action* ConfigManager::get_binding(controls::Control control) const {
    return binding(config_, control);
}

ConfigUpdateResult ConfigManager::begin_update() {
    if (update_active_) return ConfigUpdateResult::InvalidState;
    staging_ = config_;
    update_active_ = true;
    return ConfigUpdateResult::Completed;
}

ConfigUpdateResult ConfigManager::commit_update() {
    if (!update_active_) return ConfigUpdateResult::InvalidState;
    if (!is_valid(staging_)) return ConfigUpdateResult::InvalidConfig;
    if (storage_ == nullptr || !storage_->save(staging_)) {
        return ConfigUpdateResult::StorageError;
    }
    config_ = staging_;
    update_active_ = false;
    return ConfigUpdateResult::Completed;
}

ConfigUpdateResult ConfigManager::cancel_update() {
    if (!update_active_) return ConfigUpdateResult::InvalidState;
    update_active_ = false;
    return ConfigUpdateResult::Completed;
}

bool ConfigManager::is_valid(const DeviceConfig& config) {
    for (const actions::Action& action : config.buttons) {
        if (!protocol::is_valid_action(action)) return false;
    }
    return protocol::is_valid_action(config.encoder.clockwise) &&
           protocol::is_valid_action(config.encoder.counterclockwise) &&
           protocol::is_valid_action(config.encoder.press);
}

const actions::Action* ConfigManager::binding(const DeviceConfig& config,
                                               controls::Control control) {
    if (!controls::is_valid(control)) return nullptr;
    switch (control.type) {
    case controls::ControlType::Button:
        return &config.buttons[control.index];
    case controls::ControlType::EncoderClockwise:
        return &config.encoder.clockwise;
    case controls::ControlType::EncoderCounterClockwise:
        return &config.encoder.counterclockwise;
    case controls::ControlType::EncoderPress:
        return &config.encoder.press;
    }
    return nullptr;
}

actions::Action* ConfigManager::binding(DeviceConfig& config,
                                        controls::Control control) {
    if (!controls::is_valid(control)) return nullptr;
    switch (control.type) {
    case controls::ControlType::Button:
        return &config.buttons[control.index];
    case controls::ControlType::EncoderClockwise:
        return &config.encoder.clockwise;
    case controls::ControlType::EncoderCounterClockwise:
        return &config.encoder.counterclockwise;
    case controls::ControlType::EncoderPress:
        return &config.encoder.press;
    }
    return nullptr;
}
}
