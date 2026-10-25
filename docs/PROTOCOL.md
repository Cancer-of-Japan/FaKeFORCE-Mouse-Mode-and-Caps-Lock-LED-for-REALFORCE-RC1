# Topre REALFORCE "R3-generation" configuration protocol

This document describes the USB HID vendor protocol used by Topre REALFORCE
keyboards with APC (Actuation Point Changeable) switches to configure
actuation points, key maps, LEDs, and — the part this project cares about —
to read the **live analog press depth** of each key.

These are protocol *facts* (framing, command numbers, memory-block offsets,
struct layout): numeric constants and wire-format layout, not copyrighted
expression. They were derived through clean, interoperability-purpose
analysis of the devices' public USB interface (observing device identifiers,
and independently re-deriving the memory-map semantics), and are
corroborated against an independent open-source implementation,
[`st0nie/toprelinux`](https://github.com/st0nie/toprelinux) (Python, MIT),
which documents the same command set. No vendor source code, IL, or string
tables are reproduced here or in this project's source — everything in this
document is restated in our own words as protocol facts, equivalent to a
hardware datasheet.

Applies to: REALFORCE "R3-generation" keyboards (model series marketed as
R3, R3S, X1, C1, T1, R4, GX1/GX1P) that use APC switches. Vendor USB ID is
`0x0853` (Topre Corporation). The configuration channel is exposed as a
separate vendor-defined HID top-level collection — on Windows it shows up
as USB interface `MI_02` alongside the keyboard's boot/consumer-control
interfaces (and, on combo keyboard+pointing-device units, a mouse
interface).

## Transport framing

Every request/response is a fixed 65-byte HID report:

```
offset  size  name        meaning
0       1     report id   0x00 for the wired/USB session, 0x12 for the
                           Bluetooth-wrapped session
1       1     sig low     0xAA on requests ("SIG_SEND"), 0x55 on responses
                           ("SIG_RCV")
2       1     sig high    same byte as offset 1, repeated
3       1     cmd         command code (see below)
4       1     flag/status request: fragmentation flag; response: status
                           code (0 = ok)
5       1     len         payload length in bytes (0-59)
6..64   59    data        command payload / response payload
```

The two signature bytes are always equal: `0xAA 0xAA` on the host->device
direction, `0x55 0x55` on the device->host direction. A non-zero status
byte in a response means the command failed.

## Command set (R3-generation / memory-mapped protocol)

The R3-generation protocol treats most of the keyboard's configuration as a
flat array of 32-byte blocks, split into a ROM region (persisted to flash)
and a RAM region (live/volatile state):

| Command | Value | Meaning |
|---|---|---|
| `CMDC_GENERAL_REQUEST` | `0xC0` | Fire a numbered sub-request (reset, commit RAM->ROM, reload a section, reset key counters, ...) |
| `CMDC_ROM_READ` | `0xC1` | Read one 32-byte ROM block. Payload: `[block]` |
| `CMDC_ROM_WRITE` | `0xC2` | Write one 32-byte ROM block. Payload: `[block] + 32 bytes` |
| `CMDC_RAM_READ` | `0xC3` | Read one RAM block (up to 32 bytes). Payload: `[block]` |
| `CMDC_RAM_WRITE` | `0xC4` | Write one 32-byte RAM block. Payload: `[block] + 32 bytes` |

ROM has 192 blocks (`R3_ROM_BLOCK_COUNT`), RAM has 161 (`R3_RAM_BLOCK_COUNT`),
each block is 32 bytes (`R3_MEM_BLOCK_LENGTH`).

General-request sub-codes (payload byte 0 of a `0xC0` command) of note:

| Sub-request | Value |
|---|---|
| Device reset | `0x00` |
| Commit RAM -> ROM (flash write) | `0x01` |
| Revert RAM from ROM | `0x02` |
| Reload APC scene 1 / 2 | `0x03` / `0x04` |
| Reload keymap surface A / B | `0x05` / `0x06` |
| Reset per-key / total press counters | `0x07` / `0x08` |
| Reload LED settings | `0x09` |
| Reload APC mode | `0x0A` |
| Reload APC scene 3 / 4 (3rd-gen APC models) | `0x18` / `0x19` |
| Reload keymap surface C / D (3rd-gen APC models) | `0x1A` / `0x1B` |

**Committing RAM to ROM costs a flash write cycle on the keyboard's controller.**
Treat `REQUEST_RAM_TO_ROM` as a rare, deliberate operation. The mouse-mode and
inspection commands only read live key-status RAM. The experimental
`led-test` / `led-restore` commands are separate: they directly write ROM
blocks 50-51 and reload LED settings, but do not issue `REQUEST_RAM_TO_ROM`.
Those direct ROM writes still modify persisted LED settings and are not
temporary RAM changes.

## ROM block map (persisted settings)

| Blocks | Content |
|---|---|
| 4 | APC custom: continuous/dynamic-switch flag (byte per key) |
| 5 | APC custom: actuation point (word per key) |
| 6 | APC custom: sensitivity / dynamic distance (byte pair per key) |
| 45 | Shortcut key table |
| 46 / 47 | Dynamic APC distance 1 / 2 |
| 48 | APC mode |
| 49 | Keymap surface selector |
| 50 | LED brightness |
| 51 | LED color |
| 52 | Eco mode |
| 53 | Spacer setting |
| 54 | Eco mode custom |
| 56-59 | Per-key APC value, scene/separate curve 1 (one byte per key, 128 keys) |
| 60-63 | Per-key APC value, scene/separate curve 2 |
| 64-71 / 72-79 | Keymap surface A, normal / Fn layer (big-endian HID usage word per key) |
| 80-87 / 88-95 | Keymap surface B, normal / Fn layer |
| 96-133 | Illumination base/light, per-key effect & action tables |
| 136 / 137 | Dynamic APC distance 3 / 4 (3rd-gen APC models) |
| 138-141 / 142-145 | Per-key APC, scene/separate curve 3 / 4 (3rd-gen APC models) |
| 146-153 / 154-161 | Keymap surface C, normal / Fn layer (3rd-gen APC models) |
| 162-169 / 170-177 | Keymap surface D, normal / Fn layer (3rd-gen APC models) |
| 191 | Boot options |

A keyboard's capability bitmap (RAM block 157, see below) tells you which
of the APC scenes / keymap surfaces actually exist on a given model.

## RAM block map (live/volatile state)

| Blocks | Content |
|---|---|
| 0-127 | **Per-key live status**, one block per key (see below) — this is the mechanism this project uses |
| 157 | Supported-function bitmap |
| 158 | Active pairing slot |
| 159 | Battery level |
| 160 | Last-connected slot |

### Supported-function bitmap (RAM block 157, byte 0)

| Bit | Meaning |
|---|---|
| `0x01` | Eco-mode customization |
| `0x02` | Shortcut keys |
| `0x04` | APC user-custom curves |
| `0x08` | Kill-switch (key priority) editing |
| `0x10` | 3rd-generation APC (per-key actuation point in finer steps, plus scenes 3/4) |
| `0x20` | 3rd-generation APC "dynamic mode" (continuous rapid-trigger-like re-actuation) |
| `0x40` | Hold-swap |
| `0x80` | 16-slot shortcut keys |

## Per-key live status block — the analog depth read

**This is the key discovery this project relies on.** Reading RAM block
`N` for `0 <= N < 128` (`R3_RAM_BLOCK_KEY_COUNT`) returns a 24-byte record
describing key `N`'s current analog state:

```
offset  size  field
0       1     status flags
1       1     lock flag
2       2     RawData   (big-endian u16) - current analog sample
4       2     Minimum   (big-endian u16) - observed/calibrated low bound
6       2     Maximum   (big-endian u16) - observed/calibrated high bound
8       16    Thresh[8] (big-endian u16 x 8) - per-level actuation thresholds
```

All multi-byte fields are **big-endian** (`(hi << 8) | lo`), matching the
byte order used elsewhere in this protocol (e.g. keymap HID usage words).

In other words: block index == key index (0-127, same numbering used by the
per-key APC byte array and the per-key keymap word array), and reading it
at any time — independent of whether a "configuration session" is open and
independent of whatever the key is currently mapped to do — returns a live
analog reading plus the keyboard's own running calibration bounds for that
key. `RawData` moves continuously as the key is pressed and released;
`Minimum`/`Maximum` bound the range the keyboard has observed for that key
since power-on (or since calibration was last reset), and `Thresh[]` holds
the (up to 8) actuation-point thresholds the keyboard compares `RawData`
against to decide when to fire a HID keystroke, matching the 8-level
actuation granularity exposed by "3rd-generation APC" models.

The mouse-emulation path turns this analog read into a normalized depth ratio

```
depth = clamp((RawData - Minimum) / (Maximum - Minimum), 0, 1)
```

and drives mouse-cursor velocity from it. That runtime path does not use or
alter ROM configuration. The separate experimental LED test commands do write
ROM blocks 50-51 as described above.

## Corroboration

The command numbers, block numbers, and framing above were cross-checked
against `st0nie/toprelinux`'s `protocol.py`/`r3.py`, an independent
clean-room Python implementation of the same keyboards' USB HID protocol.
Every constant we found matches theirs. The one capability documented here
that their README does not mention is the per-key live analog-depth RAM
read (`RAM 0-127`) — we located it by observing argument counts, struct
field layout, and block-selection arithmetic exposed by the device's own
configuration channel. Treat the exact field widths/offsets in that struct
as a strong hypothesis pending on-device confirmation; `src/KeyStatusReader.cs`
logs the raw bytes so you can verify behavior against your own hardware
before relying on it.

## Firmware LED / Status Indicator

Static analysis of `C1HK_verA012.rfb` shows a firmware-driven status indicator:

- A three-channel software PWM worker at `0x08019600` drives GPIOC bits
  `0x800`, `0x400`, and `0x004`.
- The LED state object has fields around `+0x40` through `+0x50`. The
  initializer at `0x0801D9C2` sets mode byte `+0x41` to `0x24` and initializes
  transition state. Related helpers include `0x0801DB14`, `0x0801E3C0`, and
  `0x0801E4BA`.
- In live tests, writing profile blocks 50/51 or per-key illumination blocks
  96-133 did not visibly affect the RC1 status indicator.
- The firmware report descriptor contains standard keyboard LED output usages
  `0x01`-`0x05`. This alone does not establish that Caps Lock drives the status
  indicator.

## Setup Request Path

The dispatcher at `0x08025884` checks `bmRequestType & 0x60`. For the class
request path (`0x20`), it dispatches by `bRequest`; request number 9 reaches
`0x08026254`. This is structurally consistent with HID `SET_REPORT`, but the
report type, recipient, and meaning of its byte have not been confirmed.

The earlier `0x08026254` lead now looks like generic USB state handling, not
the LED sink. The better class/report lead is the `0x08025284` dispatcher:
`self->2AA & 0x1F` selects `0x08025884`, `0x08025968`, or `0x08025A40`.
Inside that cluster, `0x08025424` and `0x08025588` are the strongest report-
stage candidates because they gate on `mode == 3`, flip bit 7 of the request
byte, and invoke `0x2B8` slots `0x18` and `0x14`.

This means the most useful next step is to identify the concrete implementation
behind those slots and check whether it writes the LED status word at `+0x44`.

`0x080251D4` is a separate wrapper. It calls `0x08027400`, then dispatches via
a function pointer loaded from the object at `+0x2B8` (entry at `+4`, table
index 1). Its role in indicator handling remains unknown.

The LED router object is the RAM **channel** global at `0x200094B8` (loaded by
`0x0801BE68`, which calls `0x0801E7E6`). The router reads status from the
sub-object it points to: `(*0x200094B8) + 0x44`, not from the channel base. A
channel's first member (`channel[0]`) is a pointer to that sub-object; channels
share a struct with `+0x41` mode (`0x20`/`0x21`/`0x24`), `+0x40` lock, `+0x44`
counter, `+0x24` record, `+0x28`/`+0x2a` counts.

`0x080217DA` is **not** an LED writer. It is the USB transfer-descriptor enqueue
primitive and operates on a different object: `obj+0x40` is the data pointer,
`obj+0x44` is the transfer length, `obj+0x5c`/`+0x5d` are a busy lock and state
byte. Its callers (`0x08011284`, `0x080112DE`, `0x0801135C`, `0x08011394`) are
command-transport senders in `0x08011240`/`0x08011314`, whose sibling wrappers
pass the R3 command codes `0xB1`/`0xB2`/`0xB3`/`0xB4`/`0xC0`/`0xC1`/`0xC3`. The
`+0x44` here is a transfer length, unrelated to the LED status word.

The `0x0801DAF8` / `0x0801DB14` builder reached from `0x08017178` operates on a
**different channel** (`0x20008FDC`, the literal at `0x08017184`), not the
router's `0x200094B8`. It primes that channel and clears the channel's own
`+0x44` counter; it does not write the router sub-object's `+0x44` status word.
It may drive a setting-change effect on its own channel (possibly the manual's
APC purple flash), but it is not confirmed to be the router's indicator
producer.

Those two callees look shared, not APC-specific: `0x0801E04C` shows up in several
indicator builders as a timed gate, and `0x0801E3C0` shows up as the common
sequencer for those builders.

The `0x2B8` slot bodies checked so far are not the LED sink. They are report
dispatch/state wrappers that fan out to table-driven callbacks and helper
functions, with no direct `+0x44` write seen in the inspected code.

The class-request/output-report side still looks generic too. `0x08026254` and
the `0x08026728` / `0x0802677C` helpers only stage usage state and forward it to
`0x080276EC`; they do not yet connect the Caps Lock output byte to `+0x44`.

One concrete control bit is now known: the shared animation routine `0x0801EE38`
uses the `+0x40` word's bit `0x10` as an internal latch via `0x08024B22` /
`0x08024B4C`. That is still separate from the unresolved Caps Lock mapping.

## Status Router

`0x08024AEA` returns the 16-bit status word at object offset `+0x44`.
`0x0801E7E6` tests its flags in priority order. The observed branch actions are
listed below; meanings and colors are not assigned by this trace.

| Status bit | Observed control flow |
| --- | --- |
| `0x8000` | Calls `0x0801EE38`, a shared table-driven animation routine. |
| `0x4000`, `0x2000` | Clear the bit and return. |
| `0x1000` | Clears bits `0x04` and `0x08` in the separate `+0x40` word, calls `0x080272D0`, then clears the status bit. |
| `0x0800` | Sets bits `0x04` and `0x08` in `+0x40`, clears the status bit, then calls `0x08027298`. |
| `0x0400` | Clears the bit, then calls `0x0802725A` and `0x0801EA34`. |
| `0x0200` | Clears the bit, then calls `0x0802723E`. |
| `0x0100` | Clears the bit. |

`0x0801EE38` uses the low nibble of `+0x44` to select table data and updates
animation timing/state fields. No producer-to-status-bit trace currently
connects Caps Lock or the manual's battery alert to these branches. No firmware
patch has been made.
