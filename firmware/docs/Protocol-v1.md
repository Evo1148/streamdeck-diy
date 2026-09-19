# Protocol v1 system command

`ENTER_BOOTLOADER` uses message type `0x08` with an empty payload. It is an
explicit host-to-device administrative request and returns the normal `ACK` or
`NACK` response with the request sequence.

After queuing the `ACK`, firmware waits for TinyUSB to confirm completion of
that Vendor HID input report. The main-loop service then calls
`reset_usb_boot(0, 0)`. The command is not an action or binding and cannot be
triggered by buttons, encoder, touch, Consumer Control or HostAction.
