# RC1 LED / Caps Lock Trace

This document records static findings from `C1HK_verA012.rfb` and live observations documented in `RC1manual.md`. Addresses are firmware virtual addresses based at `0x08010000`. Treat behavior interpretations as hypotheses unless labeled confirmed.

## Current summary

- **Goal:** Trace the RC1 status LED path and add Caps Lock-driven LED behavior only if a verified route or safe unused bit is proven.
- **Current state:** The earlier "LED router" (`0x0801E7E6`, global `0x200094B8`) is reclassified as the USB IRQ handler and `0x20008FDC` as an I2C handle (see Correction). The real RGB status-LED engine is at `app+0x8758` with a volatile API (`0x08019D0E` etc.), and the R3 command dispatcher `0x0801B300` has a candidate host LED command `0xA1` (sub-commands 0-6). Not yet verified on hardware. Entries below that describe a "router"/"sub-object status word" are superseded.
- **Blockers:** No verified Caps Lock-specific producer or safe unused bit, limited REA xref/analyze support on the archive target, and no known firmware build/flash workflow.
- **Files:** `realforce-apc-mouse/asm/C1HK_verA012.thumb.full.asm`, `realforce-apc-mouse/docs/LED_TRACE.md`, `realforce-apc-mouse/docs/RC1manual.md`, `realforce-apc-mouse/docs/PROTOCOL.md`, `realforce-apc-mouse/docs/profile.C10K.xml`, `realforce-apc-mouse/docs/C1HK0000.prf`, `realforce-apc-mouse/src/Program.cs`, `realforce-apc-mouse/src/Protocol/KeyStatusReader.cs`.
- **Next step:** Hardware-test command `0xA1` (solid color sub-command 0 with a short duration, then sub-command 2 for a breathing-style effect) and check whether the firmware's own indicator updates overwrite it. If it works, drive mouse-mode glow/breathing and a host-side Caps Lock blink from the companion app; no firmware patch needed.

## Correction (supersedes the "LED router" claims below)

- **`0x0801E7E6` is a USB device-peripheral IRQ handler, not an LED router (strongly inferred from register layout).** `(*0x200094B8)` is the peripheral register base (HAL-style `Instance`), so `[sub+0x40]` is `USB_CNTR` and `[sub+0x44]` is `USB_ISTR`; the "status bits" are the standard ISTR flags: `0x8000` CTR (-> endpoint handler `0x0801EE38`, low nibble = EP_ID, `0x10` = DIR), `0x4000` PMAOVR, `0x2000` ERR, `0x1000` WKUP, `0x0800` SUSP, `0x0400` RESET, `0x0200` SOF, `0x0100` ESOF, `0x0080` LPM request. The `+0x40` bits `0x04`/`0x08` are CNTR LPMODE/FSUSP; `+0x54` is LPMCSR; the handle's `+0x2a8` is the lock byte and `+0x2e0` the LPM-active flag. The "producer" of those bits is the USB hardware, which is why no software store sets them. The `0x2B8` endpoint-handler and `0x08026254` SET_REPORT path are consistent with this USB stack.
- **`0x20008FDC` and the `+0x40`..`+0x44` sequencer family are an I2C master HAL handle (inferred).** Handle layout matches a HAL I2C handle (`+0x24` buffer ptr, `+0x28`/`+0x2a` XferSize/XferCount, `+0x40` Lock, `+0x41` State `0x20` ready / `0x21` busy, `+0x42` Mode, `+0x44` ErrorCode with `0x20` = TIMEOUT). Register accesses use `ISR` (`+0x18`) and `ICR` (`+0x1c`, `0x200` = ARLO clear). `0x0801E04C`/`0x0801E200`/`0x0801E0EA` are wait-on-flag/timeout helpers, not LED builders. So the earlier "APC flash candidate" reading of `0x0801DAF8` is withdrawn; it is I2C transfer setup.
- **The real status-LED engine is an RGB object at `app+0x8758`** (a field of the main keyboard object; callers pass `obj+0x8700+0x58`). `0x080195FC` is its software-PWM tick (called from `0x08016398`): it divides the current color bytes `[+3]`,`[+4]`,`[+5]` by 4 and drives GPIOC pins `0x800`, `0x400`, `0x004` over a 63-step PWM counter at `[+2]`. `0x08019740` is its effect state machine (called from `0x08016400`): 8-way jump on state `[+0x16]`, timer `[+0x18]`. Pin-to-color order is not yet confirmed.
- **LED API (all volatile, RAM-only; no ROM write):** `0x08019D0E(led, c1, c2, c3, dur)` = solid color now (state 0; targets `[+0x13..0x15]`, copied to `[+3..5]`, duration `[+0x1c]`); `0x08019D70` = same args but state 1 (transition to target over `dur`); `0x08019DDA` = state `n+2` repeat/pulse variant with period `[+0x1a]`; `0x08019E54`/`0x08019EC4` = secondary layer at `+0x1e..0x28` (period/phase fields, likely the breathing modulation); `0x08019F34`/`0x08019F94` = tertiary layer at `+0x2a..0x34`. Firmware's own indicator patterns call `0x08019D0E` from `0x08016994`/`0x080169AC`/`0x080169EA` (periodic color swaps), `0x08017C42`, `0x08017D16`, `0x08017D30`, `0x080187C0`, `0x080188BE`/`0x080188E6`/`0x08018900`.
- **Host-driven LED command found in the R3 command dispatcher `0x0801B300`** (request buffer `app+0x87C8`, response `app+0x8808`, response signature `0x5555`; firmware `in[k]` = HID report byte `k+1`, so `in[2]` = command at report offset 3). It switches on command `in[2]`: `0x01`, `0x02`, and `0xA0..0xE0` via a table at `0x0801B370` (stored entries are consistently 3 bytes below the real case starts; treat the case address as entry+3). By that mapping command **`0xA1`** reaches the LED case at `0x0801B4EA` (command `0xA0` stores `in[5]` to `app+0x74`; `0xC0`/`0xC1`/`0xC2` land on `0x0801AA74`/`0x0801AB3A`/`0x0801AA9E`, matching the known general/ROM-read/ROM-write order, which supports the mapping). Case `0xA1` layout (report offsets): `[6]` sub-command 0-6, `[7]` RGB channel mask (bit0 -> arg c3, bit1 -> c2, bit2 -> c1), `[8]` level (`level<<6 - 1` => `0x3f/0x7f/0xbf/0xff`, 0 = off), `[15]:[16]` big-endian duration/period; it also sets `app+0x8759 = 1`. Sub-commands: 0/1 -> `0x08019D0E` (solid; reached from `0x0801B5C0` and `0x0801B5DA`), 2 -> `0x08019E54` (secondary layer with `dur`, `dur/2`, `dur` -- breathing-like), 3 -> `0x0801A024`, 4 -> `0x08019F34`, 5 -> `0x0801A058`, 6 -> `0x08019CC8`. A sibling case at `0x0801B654` (command `0xA2`) scales three RGB bytes by `x*255/100` (`/100` via reciprocal). **Not yet verified on hardware**; the vendor `RFSWLib.dll` `KeyboardEntryR3` does not appear to use `0xA1` (its key-effect writer uses ROM blocks `0x62`/`0x72`), so this may be a factory/debug command.
- Consequence for the goal: if `0xA1` works, mouse-mode glow and breathing can be driven from the host over the existing HID path **without patching firmware**. Caps Lock detection can then be done host-side (`GetKeyState(VK_CAPITAL)`), which removes the need to find a firmware Caps Lock producer. Still unverified: command `0xA1` effect, how it interacts with the firmware's own periodic indicator updates (`0x08016994` etc.), and RGB pin ordering.

## Confirmed

- The RC1 status indicator is driven by firmware, not by profile blocks 50/51 or per-key illumination blocks 96-133. Attempts to change those saved blocks did not visibly affect the indicator.
- Firmware contains a three-channel software-PWM worker at `0x08019600`; it drives GPIOC bits `0x800`, `0x400`, and `0x004`.
- The LED state object has fields around offsets `0x40`-`0x50`. The initializer at `0x0801D9C2` initializes the object, sets mode byte `0x41` to `0x24`, and seeds the transition state. The transition/update paths include `0x0801DB14`, `0x0801E3C0`, and `0x0801E4BA`.
- The LED router object is the **channel** global at `0x200094B8`. The thin wrapper `0x0801BE68` loads this pointer and calls `0x0801E7E6`.
- **Nested object model (verified):** a channel's first member (`channel[0]`) is a pointer to a **sub-object**. The router `0x0801E7E6` reads its status from `(*0x200094B8) + 0x44` — it does `ldr r3,[root]; ldr r3,[r3]` first, then `0x08024AEA` reads `[sub + 0x44]`. The router's `+0x40`/`+0x44` branches all operate on this sub-object, not on the channel base.
- Channels share one struct layout: `+0x00` = sub-object pointer, `+0x24` = current record pointer, `+0x28`/`+0x2a` = counters, `+0x40` = busy-lock byte, `+0x41` = mode byte (`0x20` idle / `0x21` running / `0x24` init), `+0x42` = sub-mode/latch, `+0x44` = the channel's own counter (distinct from the sub-object's `+0x44` status word). At least two channel globals exist: `0x200094B8` (router) and `0x20008FDC` (setting-change builder).
- The `0x8000` flag path calls the shared animation routine `0x0801EE38`. That routine uses the low nibble of the status word to select a table record; it reads timing values from a table based on the record index and updates animation state. A later helper (`0x0801F5AA`) only sign-checks the same word before falling into the shared `0x0801EE42` path. This confirms a table-driven effect path, but does not identify every status bit's user-visible meaning.
- In the flag router, `0x0400` is cleared before calls to `0x0802725A` and `0x0801EA34`; `0x4000` and `0x2000` are cleared and return; `0x1000` clears bits `0x04` and `0x08` in the separate word at `+0x40`, calls `0x080272D0`, and clears `0x1000`; `0x0800` sets `0x04` and `0x08` in `+0x40`, clears `0x0800`, and calls `0x08027298`; `0x0200` is cleared before calling `0x0802723E`; `0x0100` is cleared. These are control-flow facts only; the bits are not yet mapped to named LED events or colors.
- The setup-packet dispatcher at `0x08025884` checks `bmRequestType & 0x60`. Request type class (`0x20`) dispatches by `bRequest`; request number 9 reaches `0x08026254`. This is consistent with HID `SET_REPORT`, but the handler does not validate the report type/recipient or identify the report as the Caps Lock bitmap.
- `0x08026254` requires `wLength == 1`, chooses a staging pointer based on internal mode `0x29C`, and arms transfer helper `0x08026514`. That helper calls `0x080276EC`, which forwards the byte through `0x0801EC2A` into a generic per-usage record consumed by `0x0802310A`. This traced path does **not** directly write the LED router status word at `+0x44`.
- `object + 0x2B8` is not a static LED vtable. It is a per-endpoint handler object pointer: `0x08025100` initializes it to 0 (and clears `+0x2C4`/`+0x2D0`, sets mode `+0x29C = 1`), and `0x08025148` registers a caller-supplied handler pointer into `+0x2B8`. Dispatch helpers (`0x080251D4`, `0x08025218`, `0x08025250`) load `+0x2B8` and indirect-call its function pointers at offsets `0`/`+4`. These are generic USB endpoint callbacks; none of the inspected ones write the LED object `0x200094B8`.
- `0x080217DA` is **not** an LED writer. It is the USB transfer-descriptor enqueue primitive, operating on a different object than the LED global `0x200094B8`. It sets `obj+0x40` = data pointer, `obj+0x44` = transfer length (the `0x46`/`0x3e` copies are the same length), `obj+0x38` = source pointer, and uses `obj+0x5c` as a busy lock and `obj+0x5d` as a 4→5 state byte. The four callers (`0x08011284`, `0x080112DE`, `0x0801135C`, `0x08011394`) are command-transport senders inside `0x08011240` / `0x08011314`; their sibling wrappers pass command codes `0xB1`/`0xB2`/`0xB3`/`0xB4`/`0xC0`/`0xC1`/`0xC3` (the R3 memory-map command set). The `+0x44` written here is a transfer length, not the LED status word; this function is unrelated to the indicator.
- `0x08017178` calls `0x0801DAF8` from a setting-change flow after splitting a 16-bit value into bytes. Crucially, the channel pointer it passes is the literal at `0x08017184` = `0x20008FDC` — a **different channel** than the router's `0x200094B8`. The builder primes that channel (sets `+0x41 = 0x21`, `+0x40` lock, clears the channel's own `+0x44` counter, stores the record at `+0x24`, counts at `+0x28`/`+0x2a`) and calls `0x0801E04C` / `0x0801E3C0`. Because it writes a different channel's counter (not the router sub-object's `+0x44` status), it is not confirmed to drive the router's indicator; it is a candidate for a setting-change effect (possibly the manual's purple APC flash) on its own channel.
- `0x0801E04C` is not APC-only. It is called from multiple state builders (`0x0801DB14`, `0x0801DC6C`, `0x0801DD92`, `0x0801DE44`, `0x0801DEA2`, `0x0801DFE6`) and appears to be a shared timed gate that primes the LED state machine.
- `0x0801E3C0` is also shared across several builders (`0x0801DBEE`, `0x0801DC04`, `0x0801DC9C`, `0x0801DCBE`, `0x0801DE12`, `0x0801DE34`, `0x0801DED2`, `0x0801DEF4`, `0x0801DF84`). It looks like the per-step sequencer that consumes the shared LED state rather than the top-level trigger.
- `0x08024B22` and `0x08024B4C` are tiny helpers that set/clear bit `0x10` in the `+0x40` word. They are only called from `0x0801EE38`, so that bit looks like an internal animation latch for the shared effect path, not a separate Caps Lock producer.
- `0x0801E200` is a shared timed-transition helper. It reads the sub-object's flags (`(*channel)->0x18` bits `0x10`/`0x8000`, `(*channel)->0x04` bit `0x4000`), and its wrapper `0x0801E0F8` ORs `0x20` into the **channel's own** `+0x44` counter while forcing `+0x41 = 0x20` and clearing `+0x42`/`+0x40`. This is generic per-channel sequencer state, not a producer of the router sub-object's status bits (`0x8000`/`0x0400`/...), and not a Caps Lock-specific sink.
- The `0x2B8` slot bodies checked so far (`0x08025D7C`, `0x08026078`, `0x08026100`) are generic report/state wrappers. They branch into table-driven callbacks and `0x0806432`/`0x0805250`/`0x08065C8`-style helpers, but none of the inspected paths write `+0x44` directly.
- The class-request/report plumbing around `0x08026254` and the `0x08026728` / `0x0802677C` helpers stays in generic usage-state handling. `0x080276EC` forwards the byte to `0x0801EC2A`, which assembles a record and calls `0x0802310A`. That function (`0x0802310A`) is a generic keyboard-state processor: it reads from usage-state records, populates timing fields, and does not write the LED router at `+0x44`. The HID output-report path does not trigger LED changes.
- The profile/XML mapping and RC1 manual are separate evidence sources. The manual documents user-visible pairing, battery, Eco, and APC indicator patterns; the profile describes saved configuration only.
- `0x0801E7E6` also handles the router sub-object's `0x80`, `0x200`, and `0x100` bits: `0x80` clears the bit, sets `+0x40` bits `0x04/0x08`, flips `+0x2e0`, and calls `0x0802009A`; `0x200` clears and calls `0x0802723E`; `0x100` just clears. The low nibble is already live in `0x0801EE38`, so no safe router bit is proven yet.
- `0x0801EE38` / `0x0801F14C` / `0x0801F5C2` are one shared effect engine. The low nibble of `(*0x200094B8)+0x44` picks a 16-slot record, and the top-level `0x10` bit is also live and drops into the alternate `0x0801EF14` path. `0x08024B22` / `0x08024B4C` only flip the `+0x40` latch. Inside the selected record, the live branches I traced key off record-local `0x40`, `0x80`, `0x100`, `0x800`, and `0x4000`; the later `0x0801F7D4` tail also toggles local `0x10`/`0x20` bits in its pack word. All of that still hands off to `0x0802310A`, `0x0801F7D4`, or the record-pack helpers, and none of it writes the router status word.
- `0x0801F7D4` is the paired helper on the same record structure. The code traced so far only walks per-record timers/buffers and feeds `0x08024B76` / `0x08024BFA`; those helpers just pack/copy record-local bytes into `+0x400` / `+0x402`-based buffers, including the local `0x10`/`0x20` pack bits, and still do not touch `(*0x200094B8)+0x44`.
- `0x080174CA` sits inside a long `0x08017144` command stream (`0x45`, `0x46`, `0x23`, `0x38`, `0x39`, etc.), so the lone `0x39` hit is command data, not a Caps Lock compare.
- `0x08024AEA` is getter-only for `[sub+0x44]`; it just returns the word the router already sees. That keeps the producer search on indirect stores or copies into the sub-object, not a hidden accessor.
- `0x0802188E` is another object-level `+0x44` store. `0x08021F68` is an init/reset helper that writes `+0x3d`..`+0x47` to `1`; the `+0x44`/`+0x45`/`+0x46` byte stores there are seed bytes, not router status writes. `0x080224F2` and `0x0802258A` are zeroing helpers. None are LED producers.
- **`+0x44` is a shared struct offset across object families (verified).** The word-stores that OR `0x20` into `+0x44` (`0x0801E0A4`, `0x0801E142`, `0x0801E394`, plus the `0x0801E0F8`/`0x0801E200` pair) are all the **per-channel sequencer family**: each reads `[p+0x44]`, ORs `0x20`, writes it back, then forces `+0x41 = 0x20`, `+0x42 = 0`, `+0x40 = 0`, with surrounding ring-buffer logic (pointer subtraction, length compare, `(*p)->0x18` bit-2 "full" test at `0x0801E114`-`0x0801E16C`). They operate on the **channel passed in as a parameter** (same family as `0x0801DAF8 → 0x20008FDC`), not on the router sub-object `(*0x200094B8)`. This resolves the apparent paradox: the router's `+0x44` *clears* and these `+0x44` *sets* are on **different objects that happen to share offset `0x44`**.
- **Full `+0x44` store census (36 sites) complete.** Every literal `[rX, #0x44]` store resolves to one of: (a) the router `0x0801E7E6` clearing the sub status word (`strh`); (b) a per-channel sequencer writing its *own* counter (`0x0801DAD6`, `0x0801DB66`, `0x0801DD62`, `0x0801DDB4`, `0x0801E0A4`, `0x0801E142`, `0x0801E1C8`, `0x0801E394`); (c) the USB transfer-descriptor length (`0x0802188E`, and `0x080217DA`); (d) init/zero helpers (`0x08021F68`, `0x080224F2`, `0x0802258A`); or (e) `[r7, #0x44]` stack locals. **None of them OR-sets the router sub-object's high status bits (`0x8000`/`0x0400`/`0x0200`/`0x0100`).**
- `0x08026F08` / `0x08026F0C` are also **not code** — they land in the literal-pool/data gap between `0x08026EE0` and the next real function at `0x08026F18`. Like `0x08027932` / `0x0802796A`, they are mis-decoded data and not producers.
- `0x08027932` (`str r5,[r4,#0x44]`) and `0x0802796A` (`str r2,[r6,#0x44]`) are **not code** — they sit inside the USB string-descriptor block (`"REALFORCE"`, `"RC1"`, `"Topre HID Config Keyboard"`) and were mis-decoded as instructions. Ruled out as producers.

## Live Observations

The manual-derived behaviors in `RC1manual.md` include slow blue pairing blink, slot-count blue flashes, USB-green/Bluetooth-blue connection indications, battery-level colors, green/red Eco indications, purple APC-change flashes, and white save confirmation. Live observations have also seen blue Bluetooth selection/connection behavior, purple APC-change flashes, and green Eco cycling.

These observations establish that the firmware can render multiple indicator patterns. They do not by themselves map a particular `+0x44` flag to a color or identify Caps Lock as a producer.

## Caps Lock Path: Unresolved

The HID report descriptor includes standard keyboard LED output usages `0x01`-`0x05`. The firmware has a class-request handler at `0x08026254`, but tracing `0x08026254 → 0x080276EC → 0x0801EC2A → 0x0802310A` shows that this path only populates generic keyboard-state records. It does not write the LED router at `+0x44`. 

No direct `0x39` (Caps Lock HID usage) comparison was found in any traced routine. The only explicit `0x39` hit so far is a `movs r0, #0x39` inside a multi-usage table setup at `0x080174CA`, where helper `0x08017144` is fed standard keyboard usage IDs. That looks like keymap data, not an LED branch. No HID-output-report-to-LED-router bridge has been located. Either Caps Lock LED feedback is not implemented in firmware, or it uses a completely different internal event flow not reached via the standard HID output-report path. The Caps Lock producer location remains unknown.

- `0x08025284` selects among the standard-request, class-request, and third request-family handlers by `self->2AA & 0x1F`.
- `0x08025424` uses `byte & 0x7F` and then calls slot `0x18` when `mode == 3`.
- `0x08025588` uses `byte | 0x80` and then calls slot `0x14` when `mode == 3`.

```c
// 0x08025284 / 0x0802532E / 0x08025424 / 0x08025588 (sketch)
switch (self->field_2AA & 0x1F) {
case 0: return std_req(self, req);     // 0x08025884
case 1: return class_req_a(self, req);  // 0x08025968
case 2: return class_req_b(self, req);  // 0x08025A40
}

if (mode == 3 && table->slot18)
    table->slot18(self, req_byte & 0x7F);
if (mode == 3 && table->slot14)
    table->slot14(self, req_byte | 0x80);
```

## Next Evidence

1. Trace the consumer of the record emitted by `0x0802310A` and determine whether it updates keyboard LED output state or only key processing.
2. Confirm the per-endpoint handler registered at `object + 0x2B8` (set by `0x08025148`) and whether any registered handler ever writes the LED global `0x200094B8`. So far none do.
3. Trace the producers that write the LED/global `+0x44` counters: the setting-change builder `0x0801DAF8`/`0x0801DB14` (reached from `0x08017178`) and the timed transition helper `0x0801E200` (reached from `0x0801E0FE` and `0x0801E18C`). Identify the event each represents; if one is Caps Lock, only then consider a patch. (`0x080217DA` is excluded: it writes a USB transfer descriptor, not the LED object.)
4. The shared animation path uses `+0x40` bit `0x10` as a latch, and the low nibble of `+0x44` is also live. The router bits now mapped still do not expose Caps Lock, and the only `0x39` hit is command data, so keep tracing until a real `+0x44` producer or a safe unused bit is proved.
5. **Pivot (no literal `#0x44` producer exists).** Because the full 36-site census shows no store that OR-sets the router sub-object's high status bits via a literal `[rX, #0x44]`, the producer must write the sub word through one of: (a) a base register already pointing at `sub+0x40`/`sub+0x44`, so the store offset is `#0`/`#4` (search `str`/`strh` with small offsets where the base is derived from `(*0x200094B8)`); (b) a `strh`/`strb` to the status word's individual bytes; or (c) a struct-copy/memcpy into the sub-object. The last false lead (`0x08026F08` / `0x08026F0C`) was literal data, not code. Until one of these is found, the Caps Lock producer remains unlocated.
6. Only after the Caps Lock output bit is linked to a verified animation request should a firmware-side patch be considered. The current companion app does not patch firmware or change Caps Lock LED behavior, and no firmware build/flash workflow is known — so even a located producer does not yet yield a shippable Caps Lock LED feature.

## Pseudo-C first pass

```c
// 0x08026078
int handle_usage_state(Obj *self, Setup *req) {
    if (req->wIndex != 0 || req->wLength != 0 || req->wValue > 0x7f)
        return reject(self, req);

    uint8_t usage = req->wValue & 0x7f;
    if (self->mode == 3)
        return reject(self, req);

    self->last_usage = usage;
    route_usage(self, usage);   // 0x08027680
    commit_usage(self);         // 0x080265C8
    self->mode = (usage != 0) ? 2 : 1;
    return 0;
}

// 0x08026254
int arm_report_stage(Obj *self, Setup *req) {
    if (req->wLength != 1)
        return reject(self, req);

    uint32_t *buf;
    if (self->mode == 3)
        buf = &self->stage4;
    else if (self->mode == 1 || self->mode == 2)
        buf = &self->stage8;
    else
        return reject(self, req);

    *buf = 0;
    return arm_transfer(self, buf, 1);  // 0x08026514
}

// 0x0801E7E6 (partial)
void led_router(Obj *root) {
    uint16_t status = get_status(root); // 0x08024AEA

    if (status & 0x8000) { animation_preset(root); return; }
    if (status & 0x0400) { clear_status_bit(root, 0x0400); set_alert(root, 0); return; }
    if (status & 0x1000) { root->flags40 &= ~0x000C; disable_path(root); return; }
    if (status & 0x0800) { root->flags40 |=  0x000C; enable_path(root);  return; }
    if (status & 0x0200) { transport_alert(root); return; }
    if (status & 0x0100) { clear_status_bit(root, 0x0100); return; }
}
```
