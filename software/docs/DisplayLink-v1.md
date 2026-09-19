# DisplayLink v1.0

DisplayLink transports a generic display graph over the existing 64-byte Vendor HID reports. It does not replace Protocol v1. The outer Protocol v1 header remains eight bytes (`SD`, protocol version, message type, sequence and payload length), leaving 56 payload bytes.

## Outer message types

| Direction | Type | Meaning |
|---|---:|---|
| Host to device | `0x20` | DisplayLink command |
| Device to host | `0x83` | DisplayLink response |
| Device to host | `0x91` | DisplayLink asynchronous event |

The DisplayLink payload starts with an eight-byte envelope:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 1 | major (`1`) |
| 1 | 1 | minor (`0`) |
| 2 | 1 | opcode |
| 3 | 1 | flags |
| 4 | 4 | generation, little endian |
| 8 | 0..48 | opcode body |

Flag bit 0 requests an acknowledgement. Response bit 7 means the first body byte is a `DisplayLinkError`.

## Capabilities and status

`GET_INFO (0x01)` returns protocol version, logical resolution, RGB565 pixel format, backend, physical-display and touch presence, node and animation masks, font range, fixed pool limits and a boot-session identifier. The v1 firmware reports 480x320, 128 nodes, 32 touch regions, 32 animations, eight keyframes per animation, 4096 string bytes, a 65536-byte asset pool and a 32768-byte single-asset limit. Backend `0` is the null backend.

`GET_STATUS (0x02)` returns active and staging generations, mode, host-heartbeat state, resource counts, asset usage, active transfers, last protocol error, backend state and the active graph CRC32.

## Scene synchronization

A full replacement is atomic:

1. `BEGIN_SYNC (0x03)` creates a staging graph for the supplied non-zero generation and mode.
2. Definition commands modify only staging.
3. `COMMIT_SYNC (0x04)` swaps staging into active and performs a full present.
4. `CANCEL_SYNC (0x05)` discards staging after any interrupted or rejected sync.

Dynamic changes use `BEGIN_UPDATE (0x06)`, `PATCH_NODE`, string fragments and deletes, followed by `COMMIT_UPDATE (0x07)`. These commands require the current active generation. The firmware tracks dirty rectangles and asks the backend for a partial present. Profile or preset changes use a new generation; clocks, stats, progress and other changing values keep the current generation.

## Generic graph

`DEFINE_NODE (0x10)` defines one of: Group, Rectangle, Circle, Text, Icon, Image, Progress or Line. Every node has a stable 16-bit ID, parent ID, signed local bounds, z-order, visibility, opacity and type-specific scalar fields. Parent references must already exist, IDs are unique, bounds must be positive and the hierarchy is capped at eight levels.

`PATCH_NODE (0x11)` contains a node ID followed by typed TLV fields. `DELETE_NODE (0x12)` removes a leaf node. `SET_STRING_FRAGMENT (0x13)` supplies total UTF-8 byte length, offset and a fragment; the host never splits a UTF-8 code point. Strings are bounded by the fixed scene string pool.

The firmware contains no widget-specific node type. Dashboard presets, the advanced `DisplayScene` model and the snake fixture all compile into the same generic graph.

## Touch and animation

Touch regions are separate from visual nodes. `DEFINE_TOUCH_REGION (0x20)` includes stable ID, bounds, priority, gesture mask, action kind/parameter and mode (`Capture`, `PassThrough`, `Ignore`). `TOUCH_EVENT (0x80)` reports event ID, region, gesture, coordinates and timestamp. The null backend exposes the model and event codec; it does not claim physical touch support.

Animations are generic property tracks. `DEFINE_ANIMATION (0x30)` targets X, Y, opacity, scale X/Y or rotation. `DEFINE_KEYFRAME (0x31)` adds up to eight time/value/easing entries. Play, stop and delete use `0x32`, `0x33` and `0x34`. Repeat, loop, delay and the Linear, EaseIn, EaseOut, EaseInOut and Step easings are represented in the model. The snake fixture is a Group plus Circle nodes with an X animation and a pass-through touch region.

## Assets

Assets use `ASSET_BEGIN (0x40)`, one-way `ASSET_CHUNK (0x41)`, `ASSET_COMMIT (0x42)` and `ASSET_RELEASE (0x43)`. A newer begin supersedes an incomplete transfer, allowing changed artwork to cancel stale work. Chunks contain transfer ID, absolute byte offset and up to 42 RGB565 bytes. Begin declares asset ID, dimensions, total bytes and CRC32. Commit publishes the asset only after exact-length and CRC validation; a failed replacement leaves the prior asset valid. Release compacts and reclaims the fixed pool. The host yields between chunks so HID reads, configuration traffic and heartbeat work continue.

## Session and compatibility

The host sends `HEARTBEAT (0x51)` every two seconds; firmware marks it offline after five seconds. `CLOCK_SYNC (0x50)` transports UTC epoch, timezone offset and 24-hour preference. A changed boot-session ID invalidates host assumptions and forces a full graph sync. If an older firmware NACKs `0x20`, the app marks DisplayLink unavailable and keeps all existing controls, profiles, HostActions and configuration features usable.

## Golden vectors

The Windows and firmware tests share these byte-level expectations:

- Protocol v1 `GET_INFO`, sequence `0x1234`: outer bytes begin `53 44 01 20 34 12 08 00`; DisplayLink payload begins `01 00 01 01 00 00 00 00`.
- Rectangle node ID 2, parent 1, bounds `(10,20,30,40)`, z 5, fill `0x1234`, radius 7: body begins `02 00 01 00 01 01 05 00 0A 00 14 00 1E 00 28 00 34 12` and has 34 bytes.

All multibyte integers are little endian. Unknown versions and opcodes return typed errors rather than mutating the active graph.
