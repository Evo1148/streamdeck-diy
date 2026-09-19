# Protocol v1 system command

`ENTER_BOOTLOADER` uses message type `0x08` with an empty payload. The Windows
application sends it only from the explicit firmware-update control. Firmware
returns the normal sequence-matched `ACK` before leaving normal USB mode and
reappearing as the RP2040 ROM drive `RPI-RP2`.

The disconnect following a successful `ACK` is expected. It is kept separate
from actions, bindings, HostAction and DisplayLink commands.
