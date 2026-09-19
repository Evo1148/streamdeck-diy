#pragma once

#include <cstdint>

namespace streamdeck::system {
class IBootloaderRequest {
public:
    virtual ~IBootloaderRequest() = default;
    virtual bool request_bootloader() = 0;
};

class BootloaderController final : public IBootloaderRequest {
public:
    using ResetFunction = void (*)();

    explicit BootloaderController(ResetFunction reset_function)
        : reset_function_(reset_function) {}

    bool request_bootloader() override {
        if (state_ != State::Idle) return false;
        state_ = State::AwaitingResponse;
        return true;
    }

    void response_sent() {
        if (state_ == State::AwaitingResponse) state_ = State::ResetReady;
    }

    void response_failed() {
        if (state_ == State::AwaitingResponse) state_ = State::Idle;
    }

    void task() {
        if (state_ != State::ResetReady || reset_function_ == nullptr) return;
        state_ = State::Resetting;
        reset_function_();
    }

    bool awaiting_response() const {
        return state_ == State::AwaitingResponse;
    }

private:
    enum class State : uint8_t { Idle, AwaitingResponse, ResetReady, Resetting };

    ResetFunction reset_function_;
    State state_{State::Idle};
};

void enter_usb_bootloader();
}
