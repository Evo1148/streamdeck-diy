# Hardware

## Current prototype

The current device is built around an **RP2040-Zero** and a hand-wired prototype.

Main components:

- RP2040-Zero
- 12× MX-compatible mechanical switches
- 1N4148 diodes
- rotary encoder with push button
- 480×320 TFT display
- XPT2046 touch controller
- perfboard
- custom 3D-printed enclosure

## Verified GPIO map

The public wiring table below has been cross-checked against the current firmware source (`src/hardware/hardware_map.hpp`).

| Function | GPIO |
| --- | ---: |
| Touch IRQ | GP0 |
| TFT backlight control | GP1 |
| Matrix rows 1–4 | GP2, GP3, GP4, GP5 |
| Matrix columns 1–3 | GP6, GP7, GP8 |
| Touch CS | GP9 |
| SPI1 SCK | GP10 |
| SPI1 MOSI | GP11 |
| SPI1 MISO | GP12 |
| TFT CS | GP13 |
| TFT DC | GP14 |
| TFT RESET | GP15 |
| RP2040-Zero RGB LED | GP16 |
| Encoder A | GP26 |
| Encoder B | GP27 |
| Encoder push | GP28 |
| Reserved / free | GP29 |

These are **GPIO numbers**, not physical header positions.

The TFT and touch controller share the SPI bus. The firmware contains a compile-time check that rejects duplicate or invalid GPIO assignments.

## Key matrix

The 12 switches are wired as a **4×3 matrix**.

Each switch uses a diode so multiple simultaneous key presses can be scanned reliably without unintended matrix paths.

## Encoder

The rotary encoder provides three logical controls:

- clockwise rotation;
- counter-clockwise rotation;
- push-button input.

They are exposed through the same configurable action model used by the key matrix.

## Display and touch

The project targets a **480×320 landscape logical resolution**.

Current firmware includes an ST7796 display backend and XPT2046 touch support. Display/touch configuration is kept separate from higher-level UI state so the desktop preview and device renderer can share the same logical assumptions.

## Enclosure

The enclosure is being designed as a serviceable 3D-printed assembly.

Current mechanical goals include:

- access to the electronics;
- a clean USB-C path;
- removable/interchangeable stands;
- usable stand angles;
- no structural dependence on the USB connector;
- practical tolerances for real FDM printing.

The CAD files will be published separately from slicer/build artifacts so printer-specific metadata does not become part of the hardware reference by accident.
