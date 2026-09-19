#include <cassert>

#include "actions/action_engine.hpp"
#include "config/config_manager.hpp"
#include "config/config_storage.hpp"
#include "controls/control.hpp"
#include "protocol/action_codec.hpp"
#include "protocol/device_info.hpp"
#include "protocol/protocol.hpp"
#include "system/bootloader.hpp"
#include "usb/usb_device.hpp"

namespace {
class TestHostActions final : public streamdeck::actions::IHostActionSink {
public:
    bool trigger(uint32_t action_id) override {
        ++trigger_count;
        last_action_id = action_id;
        return accepts;
    }
    bool accepts{true};
    int trigger_count{};
    uint32_t last_action_id{};
};

class TestStorage final : public streamdeck::config::IConfigStorage {
public:
    bool load(streamdeck::config::DeviceConfig&) const override {
        return false;
    }
    bool save(const streamdeck::config::DeviceConfig& config) override {
        ++save_count;
        last_saved = config;
        return save_succeeds;
    }
    bool save_succeeds{true};
    int save_count{};
    streamdeck::config::DeviceConfig last_saved{};
};

class TestBootloaderRequest final
    : public streamdeck::system::IBootloaderRequest {
public:
    bool request_bootloader() override {
        ++request_count;
        if (!accepts || requested) return false;
        requested = true;
        return true;
    }
    bool accepts{true};
    bool requested{};
    int request_count{};
};

int reset_count = 0;
void fake_reset() { ++reset_count; }

uint16_t read_u16(const uint8_t* data) {
    return static_cast<uint16_t>(data[0]) |
           static_cast<uint16_t>(static_cast<uint16_t>(data[1]) << 8);
}

streamdeck::protocol::Packet request(uint8_t type, uint16_t sequence,
                                     uint16_t payload_length = 0) {
    using namespace streamdeck::protocol;
    Packet packet{};
    packet[0] = static_cast<uint8_t>(PACKET_MAGIC);
    packet[1] = static_cast<uint8_t>(PACKET_MAGIC >> 8);
    packet[2] = PROTOCOL_VERSION;
    packet[3] = type;
    packet[4] = static_cast<uint8_t>(sequence);
    packet[5] = static_cast<uint8_t>(sequence >> 8);
    packet[6] = static_cast<uint8_t>(payload_length);
    packet[7] = static_cast<uint8_t>(payload_length >> 8);
    return packet;
}
}

int main() {
    using namespace streamdeck;
    using namespace streamdeck::protocol;

    config::ConfigManager config;
    TestStorage storage;
    config.initialize(storage);
    actions::ActionEngine action_engine;
    usb::Device usb;
    TestHostActions host_actions;
    Protocol protocol;
    const Services services{&config, &action_engine, &usb, &host_actions};
    Packet response{};

    assert(config.get_binding({controls::ControlType::Button, 0})->type ==
           actions::ActionType::None);
    assert(config.get_binding({controls::ControlType::EncoderPress, 0})->type ==
           actions::ActionType::None);

    auto packet = request(static_cast<uint8_t>(MessageType::GetDeviceInfo),
                          0x1234);
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[3] == static_cast<uint8_t>(MessageType::DeviceInfo));
    assert(read_u16(&response[4]) == 0x1234);
    assert(read_u16(&response[6]) == DEVICE_INFO_PAYLOAD_SIZE);
    assert(response[8] == PROTOCOL_VERSION);
    assert(response[9] == FIRMWARE_VERSION_MAJOR);
    assert(response[10] == FIRMWARE_VERSION_MINOR);
    assert(response[11] == FIRMWARE_VERSION_PATCH);
    assert(response[12] == 12 && response[13] == 1);
    assert(read_u16(&response[14]) == 0x0007);

    actions::Action shortcut{};
    shortcut.type = actions::ActionType::KeyboardShortcut;
    shortcut.key_code = 0x04;
    shortcut.modifiers = static_cast<uint8_t>(actions::Modifier::Control) |
                         static_cast<uint8_t>(actions::Modifier::Shift);
    uint8_t serialized[ACTION_PAYLOAD_SIZE]{};
    encode_action(shortcut, serialized);
    assert(serialized[0] == 2 && serialized[1] == 0x04);
    assert(serialized[2] == 0x03 && serialized[3] == 0);
    actions::Action decoded{};
    assert(decode_action(serialized, sizeof(serialized), decoded));
    assert(decoded.type == shortcut.type && decoded.key_code == shortcut.key_code);
    assert(decoded.modifiers == shortcut.modifiers);

    packet = request(static_cast<uint8_t>(MessageType::SetBinding), 2, 10);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 11;
    encode_action(shortcut, &packet[10]);
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(read_u16(&response[4]) == 2 && read_u16(&response[6]) == 0);

    packet = request(static_cast<uint8_t>(MessageType::GetBinding), 3, 2);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 11;
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[3] == static_cast<uint8_t>(MessageType::BindingInfo));
    assert(read_u16(&response[6]) == 10);
    assert(response[8] == static_cast<uint8_t>(controls::ControlType::Button));
    assert(response[9] == 11);
    assert(response[10] == static_cast<uint8_t>(actions::ActionType::KeyboardShortcut));
    assert(response[11] == 0x04 && response[12] == 0x03);

    packet[9] = 12;
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Nack));
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidControl));

    packet = request(static_cast<uint8_t>(MessageType::SetBinding), 4, 10);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 0;
    shortcut.modifiers = 0x10;
    encode_action(shortcut, &packet[10]);
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidAction));

    const actions::Action previous =
        *config.get_binding({controls::ControlType::Button, 0});
    storage.save_succeeds = false;
    shortcut.modifiers = static_cast<uint8_t>(actions::Modifier::Control);
    packet = request(static_cast<uint8_t>(MessageType::SetBinding), 8, 10);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 0;
    encode_action(shortcut, &packet[10]);
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[8] == static_cast<uint8_t>(NackReason::StorageError));
    const actions::Action* after_failed_save =
        config.get_binding({controls::ControlType::Button, 0});
    assert(after_failed_save->type == previous.type);
    assert(after_failed_save->key_code == previous.key_code);
    storage.save_succeeds = true;

    actions::Action none{};
    packet = request(static_cast<uint8_t>(MessageType::ExecuteActionTest), 5,
                     ACTION_PAYLOAD_SIZE);
    encode_action(none, &packet[8]);
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));

    actions::Action host{};
    host.type = actions::ActionType::HostAction;
    host.host_action_id = 42;
    packet = request(static_cast<uint8_t>(MessageType::ExecuteActionTest), 6,
                     ACTION_PAYLOAD_SIZE);
    encode_action(host, &packet[8]);
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(host_actions.trigger_count == 1 && host_actions.last_action_id == 42);

    TestBootloaderRequest bootloader_request;
    const Services bootloader_services{&config, &action_engine, &usb,
                                      &host_actions, nullptr, 0,
                                      &bootloader_request};
    packet = request(static_cast<uint8_t>(MessageType::EnterBootloader), 7);
    protocol.handle(packet.data(), packet.size(), response, bootloader_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(read_u16(&response[4]) == 7 && bootloader_request.request_count == 1);
    assert(host_actions.trigger_count == 1);
    protocol.handle(packet.data(), packet.size(), response, bootloader_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Nack));
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidState));

    system::BootloaderController bootloader_controller(fake_reset);
    reset_count = 0;
    assert(bootloader_controller.request_bootloader());
    assert(bootloader_controller.awaiting_response());
    bootloader_controller.task();
    assert(reset_count == 0);
    bootloader_controller.response_sent();
    assert(!bootloader_controller.awaiting_response() && reset_count == 0);
    bootloader_controller.task();
    bootloader_controller.task();
    assert(reset_count == 1);

    Packet host_event{};
    protocol.make_host_action_triggered(0x89ABCDEFu, 0x4321, host_event);
    assert(host_event[3] ==
           static_cast<uint8_t>(MessageType::HostActionTriggered));
    assert(read_u16(&host_event[4]) == 0x4321);
    assert(read_u16(&host_event[6]) == 4);
    assert(host_event[8] == 0xEF && host_event[9] == 0xCD &&
           host_event[10] == 0xAB && host_event[11] == 0x89);

    config::ConfigManager transactional_config;
    TestStorage transactional_storage;
    transactional_config.initialize(transactional_storage);
    const Services transactional_services{&transactional_config, nullptr, nullptr,
                                          nullptr};
    actions::Action key_a{};
    key_a.type = actions::ActionType::Keyboard;
    key_a.key_code = 0x04;
    actions::Action key_b = key_a;
    key_b.key_code = 0x05;

    packet = request(static_cast<uint8_t>(MessageType::BeginConfigUpdate), 20);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(transactional_config.update_active());

    packet = request(static_cast<uint8_t>(MessageType::BeginConfigUpdate), 21);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidState));

    packet = request(static_cast<uint8_t>(MessageType::SetBinding), 22, 10);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 0;
    encode_action(key_a, &packet[10]);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    packet[9] = 1;
    encode_action(key_b, &packet[10]);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(transactional_storage.save_count == 0);
    assert(transactional_config.get_binding(
               {controls::ControlType::Button, 0})->type ==
           actions::ActionType::None);

    packet = request(static_cast<uint8_t>(MessageType::CommitConfigUpdate), 23);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(transactional_storage.save_count == 1);
    assert(transactional_config.get_binding(
               {controls::ControlType::Button, 0})->key_code == 0x04);
    assert(transactional_config.get_binding(
               {controls::ControlType::Button, 1})->key_code == 0x05);

    packet = request(static_cast<uint8_t>(MessageType::CommitConfigUpdate), 24);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidState));

    packet = request(static_cast<uint8_t>(MessageType::BeginConfigUpdate), 25);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    packet = request(static_cast<uint8_t>(MessageType::SetBinding), 26, 10);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 0;
    encode_action(key_b, &packet[10]);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    packet = request(static_cast<uint8_t>(MessageType::CancelConfigUpdate), 27);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(transactional_storage.save_count == 1);
    assert(transactional_config.get_binding(
               {controls::ControlType::Button, 0})->key_code == 0x04);

    packet = request(static_cast<uint8_t>(MessageType::CancelConfigUpdate), 28);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidState));

    packet = request(static_cast<uint8_t>(MessageType::BeginConfigUpdate), 29);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    packet = request(static_cast<uint8_t>(MessageType::SetBinding), 30, 10);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 0;
    encode_action(key_b, &packet[10]);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    transactional_storage.save_succeeds = false;
    packet = request(static_cast<uint8_t>(MessageType::CommitConfigUpdate), 31);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[8] == static_cast<uint8_t>(NackReason::StorageError));
    assert(transactional_config.get_binding(
               {controls::ControlType::Button, 0})->key_code == 0x04);
    transactional_storage.save_succeeds = true;
    packet = request(static_cast<uint8_t>(MessageType::CancelConfigUpdate), 32);
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));

    packet = request(static_cast<uint8_t>(MessageType::SetBinding), 33, 10);
    packet[8] = static_cast<uint8_t>(controls::ControlType::Button);
    packet[9] = 0;
    encode_action(key_b, &packet[10]);
    const int saves_before_normal_set = transactional_storage.save_count;
    protocol.handle(packet.data(), packet.size(), response, transactional_services);
    assert(response[3] == static_cast<uint8_t>(MessageType::Ack));
    assert(transactional_storage.save_count == saves_before_normal_set + 1);
    assert(transactional_config.get_binding(
               {controls::ControlType::Button, 0})->key_code == 0x05);

    packet[0] = 0;
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidMagic));

    packet = request(0x55, 7);
    protocol.handle(packet.data(), packet.size(), response, services);
    assert(response[8] == static_cast<uint8_t>(NackReason::UnknownMessageType));

    protocol.handle(packet.data(), packet.size() - 1, response, services);
    assert(response[8] == static_cast<uint8_t>(NackReason::InvalidPacketSize));
}
