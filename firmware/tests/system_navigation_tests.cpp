#include <cassert>

#include "input/system_navigation.hpp"

int main() {
    using namespace streamdeck::input;
    SystemNavigation navigation{};
    assert(!system_navigation_from_button(8, navigation));
    assert(system_navigation_from_button(9, navigation));
    assert(navigation == SystemNavigation::Previous);
    assert(system_navigation_region(navigation) == 109);
    assert(system_navigation_from_button(10, navigation));
    assert(navigation == SystemNavigation::Home);
    assert(system_navigation_region(navigation) == 110);
    assert(system_navigation_from_button(11, navigation));
    assert(navigation == SystemNavigation::Next);
    assert(system_navigation_region(navigation) == 111);
    assert(!system_navigation_from_button(12, navigation));
}
