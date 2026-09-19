# DisplayLink v1 firmware note

The canonical wire specification is `../../StreamDeckDIY_App/docs/DisplayLink-v1.md` when both repositories are kept as sibling directories.

Firmware implementation lives in `src/display/`. It uses fixed-capacity scenes and resource pools, an atomic active/staging swap, dirty-region updates and a `NullDisplayBackend`. This foundation intentionally contains no ILI9488/ST7796/ST7796S or XPT2046 driver and allocates no framebuffer.

The existing Protocol v1 Vendor HID interface remains 64 bytes. DisplayLink uses outer message types `0x20`, `0x83` and `0x91`, with the eight-byte DisplayLink envelope documented by the Windows project. The null backend reports a 480x320 RGB565 logical surface while `physical_display_present` and `touch_present` remain false.
