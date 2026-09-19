#pragma once

#include "actions/action.hpp"
#include "core/events.hpp"

namespace streamdeck::usb { class Device; }

namespace streamdeck::actions {
class IHostActionSink {
public:
    virtual ~IHostActionSink() = default;
    virtual bool trigger(uint32_t action_id) = 0;
};

enum class ExecuteResult {
    Started,
    NoAction,
    Busy,
    Unsupported,
    Invalid,
};

class ActionEngine {
public:
    ExecuteResult execute(const Action& action, usb::Device& device,
                          IHostActionSink* host_actions = nullptr,
                          uint32_t trace_id = 0,
                          uint64_t input_timestamp_us = 0,
                          InputSource source = InputSource::Unknown) const;
};
}
