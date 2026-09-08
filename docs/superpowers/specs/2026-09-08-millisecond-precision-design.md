# Millisecond precision without breaking PennMUSH compatibility

## Problem

SharpMUSH stores every object timestamp as Unix **milliseconds** —
`SharpObject.CreationTime`, `SharpObject.ModifiedTime`, and the `:` field of
`DBRef` — while PennMUSH stores **seconds**. `src/parse.c:161` writes an objid as
`#<dbref>:<CreTime>` where `CreTime` is a `time_t`, and `pennfunc.hlp` defines the
objid as `[num(<object>)]:[csecs(<object>)]` with `csecs()` documented as "the number
of seconds since the epoch".

The millisecond representation leaked into the softcode surface. `csecs()` and
`msecs()` return milliseconds, so the canonical Penn idiom for an object's age —
`sub(secs(),csecs(%0))` — is off by three orders of magnitude and every age gate,
decay timer and `timestring()` built on it produces nonsense. `isdaylight()` reads a
Penn-seconds argument through `FromUnixTimeMilliseconds`, so it answers for January
1970 whatever you ask it. `uptime()` returns seconds for its default and milliseconds
for every sub-key. And `idle()` returns `TimeSpan.TotalSeconds`, a fractional number,
where PennMUSH returns a whole one — disagreeing with its own `idlesecs()` alias, which
truncates.

Milliseconds are worth keeping. The problem is that they were exposed by changing what
existing names mean, rather than by adding a way to ask for them.

## The invariant

One rule, three clauses:

1. **Storage is milliseconds.** `CreationTime`, `ModifiedTime` and
   `DBRef.CreationMilliseconds` are unchanged. No persisted objid string is
   rewritten.
2. **Every PennMUSH function's default output is PennMUSH's unit** — integer seconds —
   reached by omitting the precision argument. Softcode pasted in from a Penn game
   behaves identically.
3. **Precision describes output only. Every number a function reads is seconds,
   possibly fractional.** `timefmt($Y, 1778518155494)` reads its argument as seconds —
   a millisecond stamp is a thousand times too large, so it lands outside the
   representable range and returns `#-1 TIME INTEGER OUT OF RANGE`. Nothing inspects a
   value's magnitude to guess its unit.

Clause 3 is what makes clause 2 safe. Because no input path changes meaning, a call
that is textually identical in Penn and in SharpMUSH computes the same thing, and the
only way to reach milliseconds is to ask for them by name.

## The precision argument

Tokens are case-insensitive, and both a short and a long spelling are accepted. The
short form is what the help documents:

| Short | Long | Meaning |
|---|---|---|
| *(omitted)* | | Integer seconds — PennMUSH's unit |
| `s` | `seconds` | Integer seconds |
| `f` | `fractional` | Seconds with a fractional part, trailing zeros trimmed |
| `ms` | `milliseconds` | Integer milliseconds |

Any other token returns `#-1 INVALID PRECISION`. It never falls back to seconds: a
silent fallback would turn a typo into a wrong number rather than an error, which is
the failure this whole design exists to prevent.

The argument always goes **last**, after PennMUSH's existing arguments, so `tz`,
`pad` and `width` keep their slots and every Penn call site keeps working.

On a function returning a **timestamp or duration number**, precision selects the
unit. On a function returning a **rendered duration string** — `timestring()`,
`etime()`, `etimefmt()` — it selects the granularity of the seconds field: `s`
truncates to whole seconds as Penn does, `f` appends a trimmed fraction, `ms` appends
exactly three decimal places.

Parsing and formatting both use `CultureInfo.InvariantCulture`. This is load-bearing,
not hygiene: `TimeFunctions.cs` currently calls bare `double.TryParse`, so on a host
with a comma decimal separator `stringsecs(1.5s)` already parses as 15 seconds.

## The surface

### Gains a precision argument

Every widening below is a superset of PennMUSH's arity.

| Function | Penn arity | SharpMUSH |
|---|---|---|
| `secs()` | 0,0 | `secs([<precision>])` |
| `csecs()` | 1,1 | `csecs(<object>[, <precision>])` |
| `msecs()` | 1,1 | `msecs(<object>[, <precision>])` |
| `stringsecs()` | 1,1 | `stringsecs(<timestring>[, <precision>])` |
| `convtime()` | 1,2 | `convtime(<timestring>[, <tz>][, <precision>])` |
| `convutctime()` | 1,1 | `convutctime(<timestring>[, <precision>])` |
| `uptime()` | 0,1 | `uptime([<type>][, <precision>])` |
| `idlesecs()` | 0,1 | `idlesecs([<player>][, <precision>])` |
| `timestring()` | 1,2 | `timestring(<secs>[, <pad>][, <precision>])` |
| `etime()` | 1,2 | `etime(<secs>[, <width>][, <precision>])` |
| `etimefmt()` | 2,2 | `etimefmt(<fmt>, <secs>[, <precision>])` |

### Accepts fractional seconds on input, arity unchanged

`convsecs()`, `convutcsecs()`, `isdaylight()`, `timefmt()`, and the `<secs>` argument
of `timestring()`, `etime()` and `etimefmt()`. `@wait` already accepts a fractional
delay via `TimeSpan.FromSeconds(double)`, which is the precedent the rest of the
surface is being brought in line with.

### Unchanged

Functions that return a formatted time string carry no numeric unit and gain nothing:
`time()`, `utctime()`, `ctime()`, `mtime()`, `convsecs()`, `convutcsecs()`,
`starttime()`, `restarttime()`, `timecalc()`, `mailtime()`.

### The one exception: `secscalc()`

`secscalc(<timestring>, <modifier>, ...)` is variadic — every trailing argument is a
modifier — so there is no free slot to append a precision argument to, and a precision
token in modifier position cannot be told apart from a modifier. `secscalc()` stays
integer seconds.

This costs less than it appears to. PennMUSH's own `secscalc()` already accepts
`YYYY-MM-DD HH:MM:SS.SSS` input, so the fractional-input clause applies to it
unchanged; only its output is whole. `timecalc()` is unaffected, since it returns a
time string.

## objid and PennMUSH import

`objid()` keeps emitting `#N:<milliseconds>`. `DBRef.TryParse` stays strict
`#\d+(:\d+)?` with the `:` field read as milliseconds. **No seconds-versus-milliseconds
heuristic exists anywhere in the codebase** — a digit-length test would be a guess
about a value's meaning, and would be wrong for any game whose data outlives the
assumption.

The consequence is a real incompatibility and is stated outright in the help for both
`objid()` and `csecs()`: **`[num(%0)]:[csecs(%0)]` does not produce a resolvable objid
in SharpMUSH.** PennMUSH's documented identity `objid == num:csecs` does not hold here;
use `objid()`. Softcode ported from a Penn game that holds hardcoded `#N:<seconds>`
strings will not resolve those after import, even though the objects themselves import
correctly.

`PennMUSHDatabaseParser` reads Penn's `created` and `modified` fields correctly, but
`PennMUSHDatabaseConverter` never carries them across — an imported object is created
with a fresh timestamp, so **every objid changes on import** and any softcode holding
one stops resolving. Preserving them requires an explicit creation time on
`IObjectStore`'s four create methods and on both database providers, which is a
cross-cutting change with its own review surface; it is tracked separately rather than
folded in here. What this change does is document the unit on `PennMUSHObject` —
seconds, as PennMUSH writes it — so the eventual carry-over scales by 1000 rather than
storing seconds into a millisecond field.

## Shared mechanism

A new `SharpMUSH.Library/Time/TimePrecision.cs` holds the rule in one place rather
than in ten `switch` statements:

- `TimePrecision.TryParse(string?, out TimePrecision)` — the token table, including
  the omitted-argument case.
- `Format(long milliseconds, TimePrecision)` — a stored millisecond value out, as an
  epoch value or a duration.
- `FormatSecondsField(long milliseconds, TimePrecision)` — the seconds field of a
  rendered duration.
- `ToWholeSeconds(long milliseconds)` — floor, matching
  `DateTimeOffset.ToUnixTimeSeconds` for pre-epoch instants.
- `TryParseSeconds(string, out long milliseconds)` — the input side. Invariant-culture
  decimal seconds in, milliseconds out. Every consumer's existing
  `long.TryParse(secsStr, ...)` is replaced by a call to this, which is what makes the
  fractional-input clause uniform instead of per-function.

Fractional rendering reuses the invariant `"0.##########"` format already established
by `ArgHelpers.FormatDecimal`, rather than introducing a second number format.

## Bugs fixed in the same pass

Each is inside a file this work already touches.

- `csecs()` and `msecs()` return milliseconds; they return seconds by default. Their
  unreachable `Arguments["1"]` "utc" read is deleted — PennMUSH has no `utc` argument
  on either, the functions declared `MaxArgs = 1` so the branch could never run, and a
  Unix epoch value has no timezone to vary by. Slot 2 becomes precision.
- `mtime()` and `msecs()` return `CreationTime` in their `utc` branch where
  `ModifiedTime` is meant.
- `ctime()` and `mtime()` return a raw number in the `utc` branch; PennMUSH returns a
  time string in both branches.
- `isdaylight()` passes a Penn-seconds argument to `FromUnixTimeMilliseconds`.
- `uptime()` returns seconds for its default type and milliseconds for every named
  type; PennMUSH is seconds throughout. Penn also returns `-1` for `save` and
  `warnings` when unset, where SharpMUSH returns the current time.
- `starttime()` and `restarttime()` return `DateTimeOffset.ToString()`; PennMUSH
  returns `time()` format, `ddd MMM dd HH:mm:ss yyyy`.
- `idle()` returns `TimeSpan.TotalSeconds`, so it renders a fractional second where
  PennMUSH renders a whole one, and disagrees with its own `idlesecs()` alias.
- Culture-sensitive `double.TryParse` and `long.TryParse` throughout
  `TimeFunctions.cs`.

## Testing

Test first, in every case.

**PennMUSH parity.** For each function in the surface table, the no-precision call
returns what the Penn help documents. Where `tools/oracle/` can be driven, observed
Penn output is baked into a deterministic assertion rather than asserted from the help
text.

**Precision.** `secs(ms)` equals `secs()` times 1000 to within a tick; `csecs(#N)`
equals that object's stored milliseconds divided by 1000; every rejected token yields
`#-1 INVALID PRECISION`; `f` output feeds back through the consumers and round-trips.

**Hostile input.** Softcode can call these functions with anything, so nothing may
escape as an exception: a duration term large enough to overflow `decimal`
(`stringsecs(99999999999999999999999999y)`), a seconds value outside the range
`DateTimeOffset` can represent (`timefmt($Y,1778518155494)`), an `etimefmt` field width
too large for `Int32` (`$99999999999s`), and a sub-second negative duration
(`timestring(-0.5)`), whose sign lives in the millisecond remainder because the
whole-second part of anything in `(-1, 0)` is zero.

**Regression.** One test per bug above.

**Culture.** A test pinned to a comma-decimal culture asserting `stringsecs(1.5s)` is
not 15.
