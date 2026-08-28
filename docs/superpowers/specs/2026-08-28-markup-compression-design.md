# Markup compression: format, memory, and wire

**Date:** 2026-08-28
**Status:** approved, ready to implement

## Problem

`MModule.serialize` output is roughly 165x the plain text it carries. This surfaced as
`NatsPayloadTooLargeException` on `@wiki help:general:markdown_guide` — one wiki page
serialised to 1,521,690 bytes. PR #831 raised the NATS server ceiling from 1 MB to 6 MB,
which unblocks that page but leaves only about 4x headroom and does nothing for the two
other places markup is expensive: the database and process memory.

### Measurements

The seeded `Help:Markdown Guide` rendered through `RecursiveMarkdownHelper.RenderMarkdown`
at width 78:

| | |
|---|---|
| markdown source | 2,712 chars |
| rendered plain text | 4,840 chars |
| `MModule.serialize` output | 794,777 bytes (164x the plain text) |
| attribute runs | 2,440 |
| distinct markup values across those runs | **5** |
| adjacent runs with identical markup | **94%** |

Two smaller cases that matter more in aggregate, because every MUSH attribute is a
`MarkupString` and most are plain text:

```
serialize(single("hello"))      :  80 bytes
  {"Text":"hello","Runs":[{"Start":0,"Length":5,"Markups":[],"End":5}],"Length":5}
serialize(red "hello")          : 407 bytes
```

Memory, measured with `GC.GetTotalAllocatedBytes`:

```
MModule.single("hello")  : 936 bytes/instance
bare .NET string "hello" :  32 bytes/instance
```

`MarkupString`'s constructor eagerly allocates six `Lazy<string>` render caches — ToString,
ANSI, HTML, PlainText, Pueblo, MXP — plus six closures. Six `Lazy` + closures measured at
~928 bytes, so that is essentially the entire per-instance cost. Every intermediate
`MarkupString` the parser builds and discards pays for six caches it never reads.

### Root causes

1. **Runs are never coalesced.** `MModule.Optimize` merges adjacent runs with equal markup
   and has **zero call sites** anywhere in the repo. Running it on the guide takes 2,440
   runs to 147 and 794,777 bytes to 37,589, with plain text and ANSI render byte-identical.
2. **`AnsiMarkup` and `HtmlMarkup` have no value equality.** Neither overrides `Equals` or
   `GetHashCode`, so `Optimize`'s merge test is reference equality. It happens to work on
   the wiki path because the renderer shares markup instances, but any path that builds
   equal-but-distinct markup silently fails to merge.
3. **The JSON emits every field at its default** — `"Blink":false`, `"LinkText":""` — plus
   the computed `End` property and a redundant top-level `Length`.
4. **The same markup value is repeated per run** rather than referenced.
5. **Nothing on the wire is compressed.** Caddy does `encode zstd gzip` for plain HTTP, but
   that covers neither WebSocket frames nor NATS.

## Non-goals

- **Back-compatibility with the current on-disk format.** The project is pre-production; the
  format is replaced outright rather than versioned. No migration, no dual reader.
- **Interning markup instances in a process-wide cache.** Once value equality lands,
  coalescing collapses the duplicates interning would have caught, and a global cache brings
  lifetime questions for no measured gain.
- **A separate binary encoding for the bus.** Compact JSON plus gzip reaches ~3.4 KB on the
  guide, measured. A second format is not worth the boundary conversion and the loss of a
  greppable database.

## Design

### 1. Wire format

```json
{"t":"…","p":[null,[{"f":"#ffffff","bo":1}],[{"f":"#569cd6"}]],"r":[12,0,8,1,40,0,6,2]}
```

- **`t`** — the plain text. Omitted when empty.
- **`p`** — palette of distinct markup values. Index 0 is always `null`, meaning no markup.
  Every entry is an *array* of markups, including the common single-markup case: 2 bytes per
  palette entry buys a reader with no union type in it, and the palette is tiny (5 entries
  for the entire guide).
- **`r`** — flat `[length, paletteIndex, length, paletteIndex, …]` forming a complete cover
  of `t`. `Start` and `End` are the running sum; the top-level `Length` is `t.Length`. All
  three fields disappear.

`p` and `r` are both omitted when the string carries no markup, so `single("hello")` becomes
`{"t":"hello"}` — 13 bytes against today's 80.

Palette entry keys, all optional, absent meaning default:

| Key | Meaning |
|---|---|
| `f`, `g` | foreground, background |
| `lt`, `lu`, `lk` | link text, link url, link kind |
| `bl bo cl fa in it ov un st` | blink, bold, clear, faint, inverted, italic, overlined, underlined, strikethrough |

Booleans are emitted as `1` only when true. Colours are `"#rrggbb"` for `AnsiColor.RGB` and
a byte array such as `[1,31]` for `AnsiColor.ANSI`; absent means `NoAnsi`. `HtmlMarkup` is
`{"h":"send","a":"…"}`, where the presence of `h` is the discriminator — no `$type` string
appears anywhere. `NeutralMarkup` is `{"n":1}`.

**Edge case that must survive:** `MModule.MarkupSingle2` deliberately produces a zero-length
run carrying markup when wrapping an empty string (`MarkupStringModule.cs:773`). That encodes
as `{"p":[null,[…]],"r":[0,1]}`; the reader must not treat a length of 0 as a terminator.

### 2. Serializer

New file `SharpMUSH.MarkupString/MarkupStringSerializer.cs`, hand-written over
`Utf8JsonWriter` and `Utf8JsonReader`. `MModule.Serialize` and `MModule.Deserialize` become
one-line delegations.

Hand-written rather than POCOs plus attributes because the format is not a 1:1 map of the
object model — palette construction and the flat run array have no attribute expression — and
because it avoids allocating intermediate DTOs on a path that runs on every attribute read.
It also keeps the format out of `MarkupStringModule.cs`, already 1,029 lines.

`ColorJsonConverter` and the public `SerializationOptions` property are deleted; nothing
outside the module references them.

This is safe to swap wholesale because `Serialize`/`Deserialize` are the only doors into the
format. Nothing parses the shape by hand: the Blazor client
(`SharpMUSH.Client/Services/TerminalFrameRenderer.cs:55`) and the ConnectionServer
(`SharpMUSH.ConnectionServer/Services/MarkupOutputRenderer.cs:57`) both call
`MModule.deserialize`, and the three database providers all call `MModule.serialize`.

### 3. Value equality and constructor coalescing

`AnsiMarkup` and `HtmlMarkup` get `Equals`/`GetHashCode` forwarding to their `Details`
structs, which are `readonly record struct` and already have value equality. `NeutralMarkup`
is a singleton and needs nothing.

The `MarkupString` constructor then folds adjacent runs carrying equal markup lists. It scans
first and allocates a new array only when a merge is available, so the common case costs one
O(n) pass and no allocation. This is the existing `MModule.Optimize` logic relocated to the
one choke point every instance passes through; `Optimize` as a public function is then
deleted.

Safe at construction because `.Runs` is read only inside `MarkupStringModule.cs` and five
test files — no production code outside the module depends on run boundaries. Tests asserting
today's exact run counts are asserting the old behaviour and are updated as part of this
change.

### 4. Render cache

Six `Lazy<string>` fields and their six closures become six plain `string?` fields with null
checks. Nothing is allocated until something renders, which for parser intermediates is
never: 936 bytes drops to roughly 130 for a 5-char `MarkupString`.

**Accepted trade-off:** `Lazy<string>` guaranteed its factory ran exactly once. Plain fields
do not. Two threads racing can both compute a render and one wins. The renders are pure
functions of immutable state, so both threads compute the same string — a benign race, and
the standard pattern for idempotent caches.

### 5. NATS compression

A `CompressingNatsSerializer<T>` decorator around `NatsJsonSerializer<T>`: serialize into a
pooled buffer, and if the result exceeds a threshold (4 KB initially) write gzip instead.

No header flag is needed. JSON always begins with `{` (0x7B); gzip always begins with
0x1F 0x8B. The reader sniffs the first two bytes. This matters because the consumer side
reads `ConsumeAsync<JsonElement>` and cannot inspect per-message headers before deserialising.

Three call sites: both `PublishAsync` calls in `NatsJetStreamMessageBus` and the
`ConsumeAsync` in `NatsJetStreamConsumerService`. Applied to every message type — anything
under the threshold passes through as plain JSON.

## Expected results

| Step | bytes on the guide | vs today |
|---|---|---|
| today | 794,777 | 1x |
| coalescing alone | 37,589 | 21x (measured) |
| + compact format | ~6,600 | ~120x (modelled) |
| + gzip on the bus | ~3,400 | ~230x (measured on the coalesced blob) |

At roughly 6,600 bytes the text itself is 4,840 of them — about 74%. The compact format lands
near the floor for uncompressed JSON, which is why gzip is the lever that buys anything
beyond it.

Memory: `MModule.single("hello")` from 936 bytes to roughly 130.

## Testing

- **Round-trip properties.** serialize → deserialize → assert plain text, ANSI render, HTML
  render and run structure all match, over the existing markup corpus plus the rendered wiki
  guide as the pathological case.
- **Coalescing.** The guide goes 2,440 → 147 runs, and `Render("ansi")` is byte-identical
  before and after. That equivalence is the real safety property.
- **Value equality.** Equal-but-distinct `AnsiMarkup` instances compare equal and merge —
  the regression that currently makes coalescing work only by accident.
- **Zero-length markup run** round-trips.
- **Compression.** A payload over the threshold round-trips through the decorator; one under
  it stays plain JSON; byte-sniffing correctly identifies both.
- **Size assertions** with real numbers, so a regression fails a test rather than leaking
  slowly.
