#include <string.h>

#include "pico/unique_id.h"
#include "tusb.h"
#include "usb_descriptors.h"

enum {
    USB_VID = 0xCAFE,
    USB_PID = 0x4004,
    USB_BCD = 0x0200,
};

static tusb_desc_device_t const device_descriptor = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = USB_BCD,
    .bDeviceClass = TUSB_CLASS_MISC,
    .bDeviceSubClass = MISC_SUBCLASS_COMMON,
    .bDeviceProtocol = MISC_PROTOCOL_IAD,
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

static uint8_t const input_report_descriptor[] = {
    TUD_HID_REPORT_DESC_KEYBOARD(HID_REPORT_ID(REPORT_ID_KEYBOARD)),
    TUD_HID_REPORT_DESC_CONSUMER(HID_REPORT_ID(REPORT_ID_CONSUMER_CONTROL)),
};

static uint8_t const control_report_descriptor[] = {
    TUD_HID_REPORT_DESC_GENERIC_INOUT(STREAMDECK_CONTROL_REPORT_SIZE),
};

uint8_t const* tud_hid_descriptor_report_cb(uint8_t instance) {
    if (instance == HID_INSTANCE_INPUT) return input_report_descriptor;
    if (instance == HID_INSTANCE_CONTROL) return control_report_descriptor;
    return NULL;
}

enum {
    INTERFACE_HID_INPUT,
    INTERFACE_HID_CONTROL,
    INTERFACE_CDC_CONTROL,
    INTERFACE_CDC_DATA,
    INTERFACE_COUNT,
};

enum {
    CONFIGURATION_TOTAL_LENGTH = TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN +
                                 TUD_HID_INOUT_DESC_LEN + TUD_CDC_DESC_LEN,
    ENDPOINT_INPUT_IN = 0x81,
    ENDPOINT_CONTROL_OUT = 0x02,
    ENDPOINT_CONTROL_IN = 0x82,
    ENDPOINT_CDC_NOTIFICATION = 0x83,
    ENDPOINT_CDC_OUT = 0x04,
    ENDPOINT_CDC_IN = 0x84,
};

static uint8_t const configuration_descriptor[] = {
    TUD_CONFIG_DESCRIPTOR(
        1, INTERFACE_COUNT, 0, CONFIGURATION_TOTAL_LENGTH,
        TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP, 100),
    TUD_HID_DESCRIPTOR(
        INTERFACE_HID_INPUT, 0, HID_ITF_PROTOCOL_NONE,
        sizeof(input_report_descriptor), ENDPOINT_INPUT_IN,
        CFG_TUD_HID_EP_BUFSIZE, 1),
    TUD_HID_INOUT_DESCRIPTOR(
        INTERFACE_HID_CONTROL, 0, HID_ITF_PROTOCOL_NONE,
        sizeof(control_report_descriptor), ENDPOINT_CONTROL_OUT,
        ENDPOINT_CONTROL_IN, CFG_TUD_HID_EP_BUFSIZE, 1),
    TUD_CDC_DESCRIPTOR(
        INTERFACE_CDC_CONTROL, 0, ENDPOINT_CDC_NOTIFICATION, 8,
        ENDPOINT_CDC_OUT, ENDPOINT_CDC_IN, 64),
};

uint8_t const* tud_descriptor_configuration_cb(uint8_t index) {
    (void)index;
    return configuration_descriptor;
}

enum {
    STRING_LANGUAGE,
    STRING_MANUFACTURER,
    STRING_PRODUCT,
    STRING_SERIAL,
};

static char const* const string_descriptors[] = {
    NULL,
    "StreamDeck DIY",
    "StreamDeck DIY",
    NULL,
};

static uint16_t utf16_descriptor[33];
static char serial_number[PICO_UNIQUE_BOARD_ID_SIZE_BYTES * 2 + 1];

uint16_t const* tud_descriptor_string_cb(uint8_t index, uint16_t language_id) {
    (void)language_id;
    size_t character_count = 0;

    if (index == STRING_LANGUAGE) {
        utf16_descriptor[1] = 0x0409;
        character_count = 1;
    } else {
        if (index >= TU_ARRAY_SIZE(string_descriptors)) return NULL;

        char const* source = string_descriptors[index];
        if (index == STRING_SERIAL) {
            pico_get_unique_board_id_string(serial_number, sizeof(serial_number));
            source = serial_number;
        }
        if (source == NULL) return NULL;

        character_count = strlen(source);
        if (character_count > TU_ARRAY_SIZE(utf16_descriptor) - 1) {
            character_count = TU_ARRAY_SIZE(utf16_descriptor) - 1;
        }
        for (size_t i = 0; i < character_count; ++i) {
            utf16_descriptor[1 + i] = (uint8_t)source[i];
        }
    }

    utf16_descriptor[0] = (uint16_t)((TUSB_DESC_STRING << 8) |
                                     (2 * character_count + 2));
    return utf16_descriptor;
}
