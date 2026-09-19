# Hardware

## Current prototype

The current device is built around an **RP2040-Zero** and a hand-wired prototype.

Main components:

- RP2040-Zero
- 12× MX-compatible mechanical switches
- 1N4148 diodes
- rotary encoder with push button
- 480×320 TFT display
- perfboard
- custom 3D-printed enclosure

## Key matrix

The 12 switches are wired as a **4×3 matrix**.

Each switch uses a diode so that multiple simultaneous key presses can be scanned reliably without unintended matrix paths.

## Encoder

The rotary encoder provides:

- clockwise rotation;
- counter-clockwise rotation;
- push-button input.

These inputs are treated as configurable device actions rather than being permanently tied to one behavior.

## Display

The project targets a **480×320 logical landscape resolution**.

Display configuration is intended to remain centralized so the desktop preview and device renderer use the same assumptions instead of duplicating hard-coded dimensions.

## Enclosure

The enclosure is being designed as a serviceable 3D-printed assembly.

Current mechanical goals include:

- access to the electronics;
- a clean USB-C path;
- removable/interchangeable stands;
- usable stand angles;
- no structural dependence on the USB connector;
- practical tolerances for real FDM printing.

## Wiring

The exact pin map will be published here after it is cross-checked against the firmware source during the public source import. This avoids documenting a stale prototype revision as the final wiring reference.
