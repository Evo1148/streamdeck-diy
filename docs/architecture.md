# Architecture

The project is split into four main layers that can evolve independently while sharing stable interfaces.

## 1. Hardware

The physical device contains the RP2040-Zero, a 4×3 mechanical key matrix, a rotary encoder and a 480×320 TFT display.

The hardware layer is responsible only for electrical input/output. Higher-level behavior belongs in firmware.

## 2. Firmware

The firmware runs on the RP2040 and is built with the Pico SDK and TinyUSB.

Its main responsibilities are:

- scanning the key matrix;
- reading the encoder and encoder button;
- producing standard USB HID keyboard reports;
- producing Consumer Control HID reports;
- exposing a Vendor HID interface for configuration;
- storing device bindings persistently;
- driving the display and translating device state into UI state.

Bindings are represented in firmware rather than being hard-coded to physical buttons.

## 3. USB protocol

The configuration channel uses fixed 64-byte Vendor HID reports.

The protocol is deliberately separate from standard keyboard/media reports so that the device can remain a normal HID peripheral while still exposing richer configuration functionality.

See [protocol.md](./protocol.md).

## 4. Windows application

The desktop application is built with C# / .NET / WinUI and communicates with the device through HID.

Its responsibilities include:

- detecting the device;
- reading device information;
- reading current bindings;
- editing actions;
- testing actions;
- saving bindings back to the RP2040;
- presenting display-related configuration.

## Design principle

The desktop application configures the device, but the RP2040 remains authoritative for device-side execution.

This keeps core button behavior available even when the configuration application is not running.
