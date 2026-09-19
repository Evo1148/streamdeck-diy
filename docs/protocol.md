# Vendor HID Protocol

> This document describes the current protocol concept. The source implementation will remain the authoritative reference once the firmware is imported into this repository.

## Transport

- USB Vendor HID
- Fixed report size: **64 bytes**
- Device magic: `0x4453`
- Explicit ACK / NACK responses

## Current message set

| Message | Purpose |
| --- | --- |
| `GET_DEVICE_INFO` | Read device/protocol information |
| `SET_BINDING` | Write a binding |
| `GET_BINDING` | Request a binding |
| `BINDING_INFO` | Return binding information |
| `EXECUTE_ACTION_TEST` | Execute an action for testing |
| ACK / NACK | Confirm or reject an operation |

## Action model

The current firmware supports these action families:

- None
- Keyboard
- KeyboardShortcut
- ConsumerControl
- HostAction

The device currently exposes bindings for the 12 keys plus encoder-related inputs.

## Persistence

Bindings are stored in flash using an A/B scheme with CRC32 validation.

The persistence layer is designed to allow recovery from an invalid or interrupted write rather than treating one flash slot as the only source of truth.

## Stability

Protocol changes should be versioned deliberately. The goal is to keep the Windows application and firmware independently updateable whenever possible.
