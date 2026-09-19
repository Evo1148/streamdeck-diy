# Vendor HID Protocol

> This document summarizes the current protocol implemented by the firmware and Windows application. Source code remains the authoritative reference.

## Transport

- USB Vendor HID
- VID: `0xCAFE`
- PID: `0x4004`
- Usage page: `0xFF00`
- Usage ID: `0x0001`
- Fixed report size: **64 bytes**
- Packet magic: `0x4453`
- Explicit ACK / NACK responses

The device also exposes standard HID functionality for keyboard and Consumer Control actions.

## Current message set

| Message | Purpose |
| --- | --- |
| `GET_DEVICE_INFO` | Read device/protocol information |
| `SET_BINDING` | Write a control binding |
| `GET_BINDING` | Request a control binding |
| `EXECUTE_ACTION_TEST` | Execute an action for testing |
| `BEGIN_CONFIG_UPDATE` | Start a transactional configuration update |
| `COMMIT_CONFIG_UPDATE` | Commit the pending configuration |
| `CANCEL_CONFIG_UPDATE` | Roll back the pending configuration |
| `ENTER_BOOTLOADER` | Request USB bootloader entry |
| `DISPLAY_LINK_COMMAND` | Send a DisplayLink command |
| `ACK` / `NACK` | Confirm or reject an operation |
| `DEVICE_INFO` | Device information response |
| `BINDING_INFO` | Binding information response |
| `HOST_ACTION_TRIGGERED` | Asynchronous device-to-host host-action event |
| `DISPLAY_LINK_RESPONSE` | DisplayLink command response |
| `DISPLAY_LINK_EVENT` | Asynchronous DisplayLink event |

## Action model

The current firmware/application model supports these action families:

- None
- Keyboard
- KeyboardShortcut
- ConsumerControl
- HostAction

Bindings cover the 12 keys plus encoder clockwise, counter-clockwise and push inputs.

## Configuration persistence

Device configuration is persisted in flash using an **A/B record scheme with CRC32 validation**.

Configuration updates can be grouped transactionally using begin / commit / cancel messages. This avoids partially applying a multi-binding profile update.

## DisplayLink

DisplayLink is the protocol path used for higher-level display synchronization between the Windows application and firmware.

The repository also keeps a dedicated DisplayLink protocol document as the implementation evolves.

## Stability

Protocol changes should be versioned deliberately. The goal is to keep firmware and desktop software independently updateable whenever possible.
