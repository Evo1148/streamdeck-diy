<p align="center">
  <strong>🇬🇧 English</strong> · <a href="./README.es.md">🇪🇸 Español</a>
</p>

<h1 align="center">DIY Stream Deck</h1>

<p align="center">
  A custom macro pad / Stream Deck built from scratch around the RP2040:
  electronics, USB HID firmware, Windows configuration software and a 3D-printed enclosure.
</p>

<p align="center">
  <strong>12 mechanical keys · rotary encoder · 480×320 display · USB HID · Windows app · custom enclosure</strong>
</p>

---

## Overview

This project started as a simple DIY macro pad and grew into a complete hardware/software system.

The goal is to build a device that is genuinely useful on a daily desktop setup while also serving as a hands-on project for embedded development, USB protocols, desktop software, electronics and mechanical design.

The device is built around an **RP2040-Zero** and combines a 4×3 mechanical key matrix, a rotary encoder, a 480×320 TFT display and a custom USB HID protocol used by a Windows configuration application.

> **Project status:** active development. The physical prototype, key matrix, encoder, USB HID communication and Windows configuration workflow are functional. This public repository is currently being prepared for the full source and CAD import.

## Highlights

- **12 mechanical keys** arranged as a 4×3 matrix.
- **Rotary encoder** with push button.
- **480×320 TFT display** for a richer on-device interface.
- **USB HID keyboard** support.
- **Consumer Control HID** for multimedia actions.
- **Custom Vendor HID protocol** for configuration and host communication.
- **Persistent bindings** stored on-device.
- **Windows configuration application** built with .NET / WinUI.
- **Custom 3D-printed enclosure** with interchangeable stand angles.
- Designed as one integrated project: hardware, firmware, software and mechanical design evolve together.

## System architecture

```text
┌──────────────────────┐
│   Windows App        │
│   C# / WinUI         │
└──────────┬───────────┘
           │ Vendor HID
           │ 64-byte reports
┌──────────▼───────────┐
│     RP2040-Zero      │
│                     │
│  USB HID Firmware   │
│  Binding storage    │
│  Input processing   │
└───┬────────┬────────┘
    │        │
    │        └──────────────► 480×320 TFT
    │
    ├───────────────────────► Rotary encoder
    │
    └───────────────────────► 4×3 key matrix
```

More detail is available in [docs/architecture.md](./docs/architecture.md).

## USB protocol

The firmware exposes standard HID functionality for keyboard/media actions plus a **Vendor HID** interface for configuration.

The current protocol uses fixed **64-byte reports** and includes operations for:

- device information;
- setting a binding;
- reading a binding;
- reporting binding information;
- testing an action;
- ACK / NACK responses.

See [docs/protocol.md](./docs/protocol.md).

## Hardware

The current prototype is based on:

| Component | Role |
| --- | --- |
| RP2040-Zero | Main microcontroller |
| 12× MX-compatible switches | 4×3 key matrix |
| 1N4148 diodes | Matrix isolation |
| Rotary encoder | Navigation / configurable input |
| 480×320 TFT | Device display |
| Perfboard | Prototype electronics |
| 3D-printed enclosure | Mechanical assembly |

Hardware notes are collected in [docs/hardware.md](./docs/hardware.md).

## Software stack

| Layer | Technologies |
| --- | --- |
| Firmware | C/C++ · Pico SDK · TinyUSB |
| USB | HID Keyboard · Consumer Control · Vendor HID |
| Desktop app | C# · .NET · WinUI |
| Transport | HidSharp |
| Mechanical design | Parametric CAD / 3D printing |
| Version control | Git · GitHub / Forgejo |

## Repository layout

The repository is being prepared around this structure:

```text
streamdeck-diy/
├── firmware/        # RP2040 firmware
├── software/        # Windows configuration application
├── hardware/        # Wiring and electronics documentation
├── cad/             # Enclosure and stand designs
├── docs/            # Architecture and protocol documentation
├── assets/          # Images, screenshots and media
├── .gitignore
├── README.md
└── README.es.md
```

The source folders will be populated as the current development tree is cleaned and imported.

## Current state

| Area | Status |
| --- | --- |
| 4×3 key matrix | ✅ Working |
| Rotary encoder + push | ✅ Working |
| USB keyboard HID | ✅ Working |
| Consumer Control HID | ✅ Working |
| Vendor HID transport | ✅ Working |
| Persistent bindings | ✅ Implemented |
| Windows configuration app | ✅ Functional |
| 480×320 display pipeline | ✅ Functional |
| Final enclosure | 🚧 Iterating |
| Public source import | 🚧 In progress |
| Build guide | ⏳ Planned |

## Goals

The project is intentionally more than a button box. The long-term goal is a compact, polished device with:

- reliable daily-use firmware;
- configurable actions without reflashing;
- a useful on-device display;
- reproducible hardware;
- a clean Windows configuration experience;
- an enclosure that can actually be printed, assembled and serviced.

## Development philosophy

A few principles guide the project:

- keep firmware behavior explicit and testable;
- avoid coupling device functionality to one desktop app;
- treat the USB protocol as a stable interface;
- make hardware and enclosure decisions around real assembly constraints;
- document failures and revisions instead of hiding them.

## License

A license has not been selected yet. Until one is added, the repository remains under the default copyright rules.

---

Built in Spain 🇪🇸 as a personal engineering project.

*This is an independent DIY project and is not affiliated with or endorsed by Elgato or Corsair.*
