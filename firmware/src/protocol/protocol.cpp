#include "protocol/protocol.hpp"

#include "actions/action_engine.hpp"
#include "config/config_manager.hpp"
#include "controls/control.hpp"
#include "display/display_runtime.hpp"
#include "protocol/action_codec.hpp"
#include "protocol/device_info.hpp"
#include "system/bootloader.hpp"
#include "usb/usb_device.hpp"

namespace streamdeck::protocol {
namespace {
constexpr std::size_t OFFSET_MAGIC = 0;
constexpr std::size_t OFFSET_VERSION = 2;
constexpr std::size_t OFFSET_MESSAGE_TYPE = 3;
constexpr std::size_t OFFSET_SEQUENCE = 4;
constexpr std::size_t OFFSET_PAYLOAD_LENGTH = 6;
constexpr std::size_t CONTROL_PAYLOAD_SIZE = 2;
constexpr std::size_t BINDING_PAYLOAD_SIZE =
    CONTROL_PAYLOAD_SIZE + ACTION_PAYLOAD_SIZE;

uint16_t read_u16(const uint8_t* data) {
    return static_cast<uint16_t>(data[0]) |
           static_cast<uint16_t>(static_cast<uint16_t>(data[1]) << 8);
}

void write_u16(uint8_t* data, uint16_t value) {
    data[0] = static_cast<uint8_t>(value);
    data[1] = static_cast<uint8_t>(value >> 8);
}

void write_u32(uint8_t* data, uint32_t value) {
    data[0] = static_cast<uint8_t>(value);
    data[1] = static_cast<uint8_t>(value >> 8);
    data[2] = static_cast<uint8_t>(value >> 16);
    data[3] = static_cast<uint8_t>(value >> 24);
}

void begin_packet(Packet& packet, MessageType type, uint16_t sequence,
                  uint16_t payload_length) {
    packet.fill(0);
    write_u16(&packet[OFFSET_MAGIC], PACKET_MAGIC);
    packet[OFFSET_VERSION] = PROTOCOL_VERSION;
    packet[OFFSET_MESSAGE_TYPE] = static_cast<uint8_t>(type);
    write_u16(&packet[OFFSET_SEQUENCE], sequence);
    write_u16(&packet[OFFSET_PAYLOAD_LENGTH], payload_length);
}

void make_ack(Packet& response, uint16_t sequence) {
    begin_packet(response, MessageType::Ack, sequence, 0);
}

void make_nack(Packet& response, uint16_t sequence, NackReason reason,
               uint8_t rejected_type) {
    begin_packet(response, MessageType::Nack, sequence, 2);
    response[HEADER_SIZE] = static_cast<uint8_t>(reason);
    response[HEADER_SIZE + 1] = rejected_type;
}

void make_device_info(Packet& response, uint16_t sequence) {
    constexpr DeviceInfo info{};
    begin_packet(response, MessageType::DeviceInfo, sequence,
                 DEVICE_INFO_PAYLOAD_SIZE);
    uint8_t* payload = &response[HEADER_SIZE];
    payload[0] = info.protocol_version;
    payload[1] = info.firmware_major;
    payload[2] = info.firmware_minor;
    payload[3] = info.firmware_patch;
    payload[4] = info.button_count;
    payload[5] = info.encoder_count;
    write_u16(&payload[6], static_cast<uint16_t>(info.capabilities));
}

controls::Control decode_control(const uint8_t* payload) {
    return {static_cast<controls::ControlType>(payload[0]), payload[1]};
}

void make_binding_info(Packet& response, uint16_t sequence,
                       controls::Control control,
                       const actions::Action& action) {
    begin_packet(response, MessageType::BindingInfo, sequence,
                 BINDING_PAYLOAD_SIZE);
    uint8_t* payload = &response[HEADER_SIZE];
    payload[0] = static_cast<uint8_t>(control.type);
    payload[1] = control.index;
    encode_action(action, &payload[CONTROL_PAYLOAD_SIZE]);
}
}

void Protocol::handle(const uint8_t* request, std::size_t length,
                      Packet& response, Services services) const {
    if (request == nullptr || length != PACKET_SIZE) {
        make_nack(response, 0, NackReason::InvalidPacketSize, 0);
        return;
    }

    const uint16_t sequence = read_u16(&request[OFFSET_SEQUENCE]);
    const uint8_t raw_type = request[OFFSET_MESSAGE_TYPE];
    if (read_u16(&request[OFFSET_MAGIC]) != PACKET_MAGIC) {
        make_nack(response, sequence, NackReason::InvalidMagic, raw_type);
        return;
    }
    if (request[OFFSET_VERSION] != PROTOCOL_VERSION) {
        make_nack(response, sequence, NackReason::UnsupportedVersion, raw_type);
        return;
    }

    const uint16_t payload_length = read_u16(&request[OFFSET_PAYLOAD_LENGTH]);
    if (payload_length > MAX_PAYLOAD_SIZE) {
        make_nack(response, sequence, NackReason::InvalidPayloadLength, raw_type);
        return;
    }

    const uint8_t* payload = &request[HEADER_SIZE];
    switch (static_cast<MessageType>(raw_type)) {
    case MessageType::GetDeviceInfo:
        if (payload_length != 0) {
            make_nack(response, sequence, NackReason::InvalidPayloadLength,
                      raw_type);
        } else {
            make_device_info(response, sequence);
        }
        return;

    case MessageType::SetBinding: {
        if (payload_length != BINDING_PAYLOAD_SIZE) {
            make_nack(response, sequence, NackReason::InvalidPayloadLength,
                      raw_type);
            return;
        }
        if (services.config == nullptr) {
            make_nack(response, sequence, NackReason::UnsupportedMessage,
                      raw_type);
            return;
        }
        const controls::Control control = decode_control(payload);
        if (!controls::is_valid(control)) {
            make_nack(response, sequence, NackReason::InvalidControl, raw_type);
            return;
        }
        actions::Action action{};
        if (!decode_action(&payload[CONTROL_PAYLOAD_SIZE], ACTION_PAYLOAD_SIZE,
                           action)) {
            make_nack(response, sequence, NackReason::InvalidAction, raw_type);
            return;
        }
        const config::SetBindingResult set_result =
            services.config->set_binding_and_save(control, action);
        if (set_result == config::SetBindingResult::Invalid) {
            make_nack(response, sequence, NackReason::InvalidAction, raw_type);
            return;
        }
        if (set_result == config::SetBindingResult::StorageError) {
            make_nack(response, sequence, NackReason::StorageError, raw_type);
            return;
        }
        make_ack(response, sequence);
        return;
    }

    case MessageType::GetBinding: {
        if (payload_length != CONTROL_PAYLOAD_SIZE) {
            make_nack(response, sequence, NackReason::InvalidPayloadLength,
                      raw_type);
            return;
        }
        if (services.config == nullptr) {
            make_nack(response, sequence, NackReason::UnsupportedMessage,
                      raw_type);
            return;
        }
        const controls::Control control = decode_control(payload);
        const actions::Action* action = services.config->get_binding(control);
        if (action == nullptr) {
            make_nack(response, sequence, NackReason::InvalidControl, raw_type);
            return;
        }
        make_binding_info(response, sequence, control, *action);
        return;
    }

    case MessageType::ExecuteActionTest: {
        if (payload_length != ACTION_PAYLOAD_SIZE) {
            make_nack(response, sequence, NackReason::InvalidPayloadLength,
                      raw_type);
            return;
        }
        if (services.actions == nullptr || services.usb == nullptr) {
            make_nack(response, sequence, NackReason::UnsupportedMessage,
                      raw_type);
            return;
        }
        actions::Action action{};
        if (!decode_action(payload, ACTION_PAYLOAD_SIZE, action)) {
            make_nack(response, sequence, NackReason::InvalidAction, raw_type);
            return;
        }
        switch (services.actions->execute(
            action, *services.usb, services.host_actions, sequence,
            static_cast<uint64_t>(services.now_ms) * 1000u,
            InputSource::Protocol)) {
        case actions::ExecuteResult::Started:
        case actions::ExecuteResult::NoAction:
            make_ack(response, sequence);
            return;
        case actions::ExecuteResult::Busy:
            make_nack(response, sequence, NackReason::ActionBusy, raw_type);
            return;
        case actions::ExecuteResult::Unsupported:
            make_nack(response, sequence, NackReason::UnsupportedAction,
                      raw_type);
            return;
        case actions::ExecuteResult::Invalid:
            make_nack(response, sequence, NackReason::InvalidAction, raw_type);
            return;
        }
        return;
    }

    case MessageType::BeginConfigUpdate:
    case MessageType::CommitConfigUpdate:
    case MessageType::CancelConfigUpdate: {
        if (payload_length != 0) {
            make_nack(response, sequence, NackReason::InvalidPayloadLength,
                      raw_type);
            return;
        }
        if (services.config == nullptr) {
            make_nack(response, sequence, NackReason::UnsupportedMessage,
                      raw_type);
            return;
        }
        config::ConfigUpdateResult result{};
        if (static_cast<MessageType>(raw_type) == MessageType::BeginConfigUpdate) {
            result = services.config->begin_update();
        } else if (static_cast<MessageType>(raw_type) == MessageType::CommitConfigUpdate) {
            result = services.config->commit_update();
        } else {
            result = services.config->cancel_update();
        }
        switch (result) {
        case config::ConfigUpdateResult::Completed:
            make_ack(response, sequence);
            return;
        case config::ConfigUpdateResult::InvalidState:
            make_nack(response, sequence, NackReason::InvalidState, raw_type);
            return;
        case config::ConfigUpdateResult::InvalidConfig:
            make_nack(response, sequence, NackReason::InvalidAction, raw_type);
            return;
        case config::ConfigUpdateResult::StorageError:
            make_nack(response, sequence, NackReason::StorageError, raw_type);
            return;
        }
        return;
    }

    case MessageType::EnterBootloader:
        if (payload_length != 0) {
            make_nack(response, sequence, NackReason::InvalidPayloadLength,
                      raw_type);
        } else if (services.bootloader == nullptr) {
            make_nack(response, sequence, NackReason::UnsupportedMessage,
                      raw_type);
        } else if (!services.bootloader->request_bootloader()) {
            make_nack(response, sequence, NackReason::InvalidState, raw_type);
        } else {
            make_ack(response, sequence);
        }
        return;

    case MessageType::DisplayLinkCommand: {
        if (services.display == nullptr) {
            make_nack(response, sequence, NackReason::UnsupportedMessage, raw_type);
            return;
        }
        std::array<uint8_t, MAX_PAYLOAD_SIZE> display_response{};
        std::size_t display_length = 0;
        services.display->handle(payload, payload_length, display_response.data(),
                                 display_length, services.now_ms);
        if (display_length > MAX_PAYLOAD_SIZE) {
            make_nack(response, sequence, NackReason::InvalidPayloadLength, raw_type);
            return;
        }
        begin_packet(response, MessageType::DisplayLinkResponse, sequence,
                     static_cast<uint16_t>(display_length));
        for (std::size_t i = 0; i < display_length; ++i)
            response[HEADER_SIZE + i] = display_response[i];
        return;
    }

    case MessageType::Ack:
    case MessageType::Nack:
    case MessageType::DeviceInfo:
    case MessageType::BindingInfo:
    case MessageType::HostActionTriggered:
    case MessageType::DisplayLinkResponse:
    case MessageType::DisplayLinkEvent:
        make_nack(response, sequence, NackReason::UnsupportedMessage, raw_type);
        return;
    default:
        make_nack(response, sequence, NackReason::UnknownMessageType, raw_type);
        return;
    }
}

void Protocol::make_host_action_triggered(uint32_t action_id,
                                          uint16_t sequence,
                                          Packet& packet) const {
    begin_packet(packet, MessageType::HostActionTriggered, sequence, 4);
    write_u32(&packet[HEADER_SIZE], action_id);
}

bool Protocol::make_display_link_event(const uint8_t* payload,
                                       std::size_t payload_length,
                                       uint16_t sequence,
                                       Packet& packet) const {
    if (payload == nullptr || payload_length > MAX_PAYLOAD_SIZE) return false;
    begin_packet(packet, MessageType::DisplayLinkEvent, sequence,
                 static_cast<uint16_t>(payload_length));
    for (std::size_t index = 0; index < payload_length; ++index) {
        packet[HEADER_SIZE + index] = payload[index];
    }
    return true;
}
}
