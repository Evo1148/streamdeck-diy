#pragma once

#include "actions/action.hpp"
#include "config/device_config.hpp"
#include "controls/control.hpp"

namespace streamdeck::config {
class IConfigStorage;

enum class SetBindingResult { Saved, Invalid, StorageError };
enum class ConfigUpdateResult { Completed, InvalidState, InvalidConfig, StorageError };

class ConfigManager {
public:
    void initialize(IConfigStorage& storage);
    bool set_binding(controls::Control control, const actions::Action& action);
    SetBindingResult set_binding_and_save(controls::Control control,
                                          const actions::Action& action);
    const actions::Action* get_binding(controls::Control control) const;
    ConfigUpdateResult begin_update();
    ConfigUpdateResult commit_update();
    ConfigUpdateResult cancel_update();
    bool update_active() const { return update_active_; }

private:
    static actions::Action* binding(DeviceConfig& config,
                                    controls::Control control);
    static const actions::Action* binding(const DeviceConfig& config,
                                          controls::Control control);
    static bool is_valid(const DeviceConfig& config);
    DeviceConfig config_{};
    DeviceConfig staging_{};
    IConfigStorage* storage_{};
    bool update_active_{};
};
}
