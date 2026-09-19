#include <string.h>

#include "pico/unique_id.h"
#include "tusb.h"

enum { USB_VID = 0xCAFE, USB_PID = 0x4006, USB_BCD = 0x0200 };

static tusb_desc_device_t const device_descriptor = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = USB_BCD,
    .bDeviceClass = 0,
    .bDeviceSubClass = 0,
    .bDeviceProtocol = 0,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = USB_VID,
    .idProduct = USB_PID,
    .bcdDevice = 0x0100,
    .iManufacturer = 1,
    .iProduct = 2,
    .iSerialNumber = 3,
    .bNumConfigurations = 1,
};

uint8_t const* tud_descriptor_device_cb(void) {
    return (uint8_t const*)&device_descriptor;
}

static uint8_t const report_descriptor[] = {
    TUD_HID_REPORT_DESC_KEYBOARD(),
};

uint8_t const* tud_hid_descriptor_report_cb(uint8_t instance) {
    (void)instance;
    return report_descriptor;
}

enum {
    INTERFACE_HID,
    INTERFACE_COUNT,
    CONFIGURATION_TOTAL_LENGTH = TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN,
    ENDPOINT_KEYBOARD_IN = 0x81,
};

static uint8_t const configuration_descriptor[] = {
    TUD_CONFIG_DESCRIPTOR(1, INTERFACE_COUNT, 0, CONFIGURATION_TOTAL_LENGTH,
                          TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 100),
    TUD_HID_DESCRIPTOR(INTERFACE_HID, 0, HID_ITF_PROTOCOL_KEYBOARD,
                       sizeof(report_descriptor), ENDPOINT_KEYBOARD_IN,
                       CFG_TUD_HID_EP_BUFSIZE, 5),
};

uint8_t const* tud_descriptor_configuration_cb(uint8_t index) {
    (void)index;
    return configuration_descriptor;
}

enum { STRING_LANGUAGE, STRING_MANUFACTURER, STRING_PRODUCT, STRING_SERIAL };
static char const* const strings[] = {
    NULL, "StreamDeck DIY", "StreamDeck Encoder Test", NULL,
};
static uint16_t utf16_descriptor[33];
static char serial_number[PICO_UNIQUE_BOARD_ID_SIZE_BYTES * 2 + 1];

uint16_t const* tud_descriptor_string_cb(uint8_t index, uint16_t language_id) {
    (void)language_id;
    size_t count = 0;
    if (index == STRING_LANGUAGE) {
        utf16_descriptor[1] = 0x0409;
        count = 1;
    } else {
        if (index >= TU_ARRAY_SIZE(strings)) return NULL;
        char const* source = strings[index];
        if (index == STRING_SERIAL) {
            pico_get_unique_board_id_string(serial_number, sizeof(serial_number));
            source = serial_number;
        }
        if (source == NULL) return NULL;
        count = strlen(source);
        if (count > TU_ARRAY_SIZE(utf16_descriptor) - 1)
            count = TU_ARRAY_SIZE(utf16_descriptor) - 1;
        for (size_t i = 0; i < count; ++i)
            utf16_descriptor[i + 1] = (uint8_t)source[i];
    }
    utf16_descriptor[0] =
        (uint16_t)((TUSB_DESC_STRING << 8) | (2 * count + 2));
    return utf16_descriptor;
}
