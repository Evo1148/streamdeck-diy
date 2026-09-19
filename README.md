<p align="center">
  <strong>🇬🇧 English</strong> · <a href="./README.es.md">🇪🇸 Español</a>
</p>

<h1 align="center">DIY Stream Deck</h1>

<p align="center">
  A custom RP2040 macro pad / Stream Deck built from scratch:
  electronics, firmware, USB protocol, Windows software and a 3D-printed enclosure.
</p>

<p align="center">
  <strong>12 mechanical keys · rotary encoder · 480×320 TFT · touch · USB HID · WinUI app</strong>
</p>

---

## Overview

This project started as a simple DIY macro pad and evolved into a complete hardware/software system.

The device is built around a **Waveshare RP2040-Zero** and combines a 4×3 mechanical key matrix, rotary encoder, 480×320 TFT, XPT2046 touch controller, persistent on-device configuration and a Windows application that communicates with the device over a custom Vendor HID protocol.

> **Status:** active development. The firmware and Windows application source are now public. The physical prototype, matrix, encoder, USB HID stack, persistent bindings, display pipeline and Windows configuration workflow are functional. The enclosure/CAD is still being iterated before publication.

## Highlights

- **12 mechanical keys** in a 4×3 matrix.
- **Rotary encoder** with configurable clockwise, counter-clockwise and push actions.
- **480×320 TFT** with ST7796 backend.
- **XPT2046 touch** support.
- Standard **USB HID Keyboard** and **Consumer Control**.
- Custom **Vendor HID** protocol with fixed 64-byte reports.
- Transactional configuration updates and persistent A/B flash records with CRC32.
- **DisplayLink** synchronization path between desktop software and firmware.
- **Windows configuration application** built with C# / .NET / WinUI.
- Profiles, host actions, automation, system stats and display editing.
- Firmware and desktop-side automated tests.

## Source

- 🧠 [RP2040 firmware](./firmware/)
- 🪟 [Windows application](./software/)
- 🔌 [Protocol documentation](./docs/protocol.md)
- 🧩 [Architecture](./docs/architecture.md)
- ⚡ [Hardware / GPIO map](./docs/hardware.md)

## System architecture

```text
┌──────────────────────────┐
│ Windows App              │
│ C# / .NET / WinUI        │
│                          │
│ Profiles · Host Actions  │
│ Display · Automation     │
└────────────┬─────────────┘
             │ Vendor HID / DisplayLink
             │ 64-byte reports
┌────────────▼─────────────┐
│ RP2040-Zero              │
│                          │
│ USB HID                  │
│ Config + flash storage   │
│ Input + display runtime  │
└────┬────────┬────────┬───┘
     │        │        │
     │        │        └────► TFT 480×320 + touch
     │        └─────────────► Rotary encoder
     └──────────────────────► 4×3 key matrix
```

## Hardware

| Component | Role |
| --- | --- |
| Waveshare RP2040-Zero | Main microcontroller |
| 12× MX-compatible switches | 4×3 key matrix |
| 1N4148 diodes | Matrix isolation |
| Rotary encoder | Configurable input / navigation |
| ST7796 480×320 TFT | Device display |
| XPT2046 | Touch controller |
| Perfboard | Prototype electronics |
| 3D-printed enclosure | Mechanical assembly |

The current GPIO mapping has been cross-checked against the firmware source and is documented in [docs/hardware.md](./docs/hardware.md).

## USB protocol

The device exposes normal HID keyboard/media functionality plus a dedicated Vendor HID interface.

Current protocol features include:

- device information;
- read/write bindings;
- action testing;
- transactional configuration updates;
- bootloader entry;
- asynchronous host-action events;
- DisplayLink commands, responses and events;
- ACK / NACK error handling.

See [docs/protocol.md](./docs/protocol.md) and the implementation under [firmware/src/protocol](./firmware/src/protocol/) and [software/StreamDeckDIY.Protocol](./software/StreamDeckDIY.Protocol/).

## Software stack

| Layer | Technologies |
| --- | --- |
| Firmware | C/C++ · Pico SDK 2.3.1 · TinyUSB |
| USB | HID Keyboard · Consumer Control · Vendor HID |
| Display / touch | ST7796 · XPT2046 · DisplayLink |
| Desktop app | C# · .NET 10 · WinUI · Windows App SDK |
| Transport | HidSharp |
| Hardware telemetry | LibreHardwareMonitor |
| Mechanical design | CAD · FDM 3D printing |
| Version control | Git · GitHub · Forgejo |

## Repository layout

```text
streamdeck-diy/
├── firmware/        # RP2040 source, diagnostics and tests
├── software/        # Windows app, protocol, transport and tests
├── docs/            # Architecture, hardware and protocol docs
├── assets/          # Public project media
├── tools/           # Repository/import tooling
├── .gitignore
├── .gitattributes
├── README.md
└── README.es.md
```

## Building

### Firmware

Requirements:

- Raspberry Pi Pico SDK **2.3.1**
- CMake
- ARM toolchain compatible with the Pico SDK
- board target: `waveshare_rp2040_zero`

From the repository root:

```bash
cmake -S firmware -B firmware/build
cmake --build firmware/build
```

The main output is `StreamDeck_Firmware.uf2`. The CMake project also contains matrix-only and encoder-only diagnostic firmware targets.

### Windows application

Requirements:

- Windows
- .NET **10 SDK**
- x64
- Windows App SDK dependencies restored through NuGet

```powershell
dotnet restore .\software\StreamDeckDIY.sln
dotnet build .\software\StreamDeckDIY.sln -c Debug -p:Platform=x64
```

The desktop-side test project can be run with:

```powershell
dotnet run --project .\software\StreamDeckDIY.Protocol.Tests\StreamDeckDIY.Protocol.Tests.csproj
```

## Current state

| Area | Status |
| --- | --- |
| 4×3 key matrix | ✅ Working |
| Rotary encoder + push | ✅ Working |
| USB Keyboard HID | ✅ Working |
| Consumer Control HID | ✅ Working |
| Vendor HID transport | ✅ Working |
| Persistent configuration | ✅ Implemented |
| Windows configuration app | ✅ Functional |
| 480×320 display pipeline | ✅ Functional |
| Touch support | ✅ Implemented |
| Firmware / desktop tests | ✅ Included |
| Public firmware source | ✅ Published |
| Public Windows app source | ✅ Published |
| Final enclosure / CAD | 🚧 Iterating |
| Assembly guide | ⏳ Planned |

## Development principles

- Keep firmware behavior explicit and testable.
- Treat the USB protocol as a stable interface between independently evolving components.
- Keep important device behavior available without requiring the desktop application to remain open.
- Design electronics and enclosure around real assembly and servicing constraints.
- Preserve regression tests as features evolve.
- Document failures and revisions instead of hiding them.

## License

A license has not been selected yet. Until one is added, the repository remains under default copyright rules.

---

Built in Spain 🇪🇸 as a personal engineering project.

*This is an independent DIY project and is not affiliated with or endorsed by Elgato or Corsair.*
