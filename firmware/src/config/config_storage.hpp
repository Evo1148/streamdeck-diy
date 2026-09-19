#pragma once

#include "config/device_config.hpp"

namespace streamdeck::config {
class IConfigStorage {
public:
    virtual ~IConfigStorage() = default;
    virtual bool load(DeviceConfig& config) const = 0;
    virtual bool save(const DeviceConfig& config) = 0;
};

class ConfigStorage final : public IConfigStorage {
public:
    bool load(DeviceConfig& config) const override;
    bool save(const DeviceConfig& config) override;
};
}
