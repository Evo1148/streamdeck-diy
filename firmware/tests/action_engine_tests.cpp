#include <cassert>

#include "actions/action_engine.hpp"
#include "input/system_navigation.hpp"
#include "usb/usb_device.hpp"

namespace {
class TestHostActions final : public streamdeck::actions::IHostActionSink {
public:
    bool trigger(uint32_t action_id) override {
        ++count;
        received_id = action_id;
        return true;
    }
    int count{};
    uint32_t received_id{};
};
}

int main() {
    using namespace streamdeck;
    actions::Action action{};
    action.type = actions::ActionType::HostAction;
    action.host_action_id = 0xF1234567u;
    actions::ActionEngine engine;
    usb::Device device;
    TestHostActions host_actions;

    assert(engine.execute(action, device, &host_actions) ==
           actions::ExecuteResult::Started);
    assert(host_actions.count == 1);
    assert(host_actions.received_id == 0xF1234567u);
    assert(engine.execute(action, device) == actions::ExecuteResult::Unsupported);

    input::SystemNavigation navigation{};
    assert(!input::system_navigation_from_button(8, navigation));
    assert(input::system_navigation_from_button(9, navigation) &&
           navigation == input::SystemNavigation::Previous &&
           input::system_navigation_region(navigation) == 109);
    assert(input::system_navigation_from_button(10, navigation) &&
           navigation == input::SystemNavigation::Home);
    assert(input::system_navigation_from_button(11, navigation) &&
           navigation == input::SystemNavigation::Next &&
           input::system_navigation_region(navigation) == 111);
    assert(!input::system_navigation_from_button(12, navigation));
}
