#pragma once

#ifndef CFG_TUSB_MCU
#error CFG_TUSB_MCU must be defined by the Pico SDK
#endif

#ifndef CFG_TUSB_OS
#define CFG_TUSB_OS OPT_OS_NONE
#endif
#define CFG_TUSB_DEBUG 0

#define CFG_TUD_ENABLED 1
#define CFG_TUD_ENDPOINT0_SIZE 64

#define CFG_TUD_HID 2
#define CFG_TUD_CDC 1
#define CFG_TUD_MSC 0
#define CFG_TUD_MIDI 0
#define CFG_TUD_VENDOR 0

// The private control interface transfers one complete Protocol v1 packet.
#define CFG_TUD_HID_EP_BUFSIZE 64
#define CFG_TUD_CDC_TX_BUFSIZE 256
#define CFG_TUD_CDC_RX_BUFSIZE 64
