#include "system/bootloader.hpp"

#include "pico/bootrom.h"

namespace streamdeck::system {
void enter_usb_bootloader() {
    reset_usb_boot(0, 0);
}
}
