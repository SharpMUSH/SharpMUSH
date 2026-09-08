# Time precision in softcode

SharpMUSH stores every timestamp in **milliseconds**. PennMUSH stores **seconds**. This
guide is how the two are reconciled, and what you type to get each.

## The short version

Every PennMUSH time function returns what PennMUSH returns. Milliseconds are opt-in,
through a `<precision>` argument you add at the end:

```
secs()        →  1778518155
secs(f)       →  1778518155.494
secs(ms)      →  1778518155494
```

Paste PennMUSH softcode in and it keeps working, because nothing you did not change
means something different.

## The precision argument

| Short | Long | You get |
|---|---|---|
| *(omit it)* | | Whole seconds — PennMUSH's answer |
| `s` | `seconds` | Whole seconds |
| `f` | `fractional` | Seconds with a decimal part, trailing zeros trimmed |
| `ms` | `milliseconds` | Whole milliseconds |

Anything else is `#-1 INVALID PRECISION`. It does not quietly fall back to seconds — a
typo that silently returned a plausible number is the whole problem this design exists
to avoid.

It always goes last, after the function's existing arguments:

```
csecs(#7)              →  1778518155
csecs(#7,ms)           →  1778518155494
timestring(90)         →   1m  30s
timestring(90.25,0,ms) →   1m  30.250s
convtime(Mon May 11 16:49:15 2026,UTC,ms)
```

Skipping a slot to reach it is fine — `etime(<secs>,,ms)` leaves the width empty.

## Precision is about output, never input

**Every number you hand a time function is seconds.** There is no argument that makes a
function read milliseconds, and nothing guesses from a value's size.

```
timefmt($Y,1778518155)      →  2026
timefmt($Y,1778518155494)   →  year 58333   ← read as seconds, exactly as written
```

If you stored a millisecond value and want to format it, divide first:

```
timefmt($Y,fdiv(%q<stamp>,1000))
```

Seconds may carry a fraction, and it is honoured to the millisecond:

```
timestring(90.25)     →   1m  30s        ← rendered whole, PennMUSH-style
timestring(90.25,0,f) →   1m  30.25s     ← ask for the fraction to see it
stringsecs(1.5s)      →  1
stringsecs(1.5s,ms)   →  1500
@wait 0.25=think tick
```

Decimal points are decimal points regardless of the server's locale.

## Which functions take a precision argument

Functions that return a **number**:

`secs()` · `csecs()` · `msecs()` · `stringsecs()` · `convtime()` · `convutctime()` ·
`uptime()` · `idlesecs()` / `idle()`

Functions that render a **duration string**, where precision sets how the seconds field
is written:

`timestring()` · `etime()` · `etimefmt()`

Functions that return a **formatted time string** have no numeric unit and take no
precision argument: `time()`, `utctime()`, `ctime()`, `mtime()`, `convsecs()`,
`convutcsecs()`, `starttime()`, `restarttime()`, `timecalc()`, `mailtime()`.

`secscalc()` is the exception. It is variadic — every trailing argument is a modifier —
so there is no slot to put a precision argument in, and it always returns whole seconds.
It still reads a fractional input, including PennMUSH's `YYYY-MM-DD HH:MM:SS.SSS` form.

## objid does not equal num:csecs here

This is the one place SharpMUSH and PennMUSH genuinely differ, so it is worth knowing
before it surprises you.

PennMUSH documents `objid(<object>)` as `[num(<object>)]:[csecs(<object>)]`, because its
objid carries a creation time in seconds. SharpMUSH's objid carries **milliseconds**:

```
objid(#7)              →  #7:1778518155494
csecs(#7)              →  1778518155
[num(#7)]:[csecs(#7)]  →  #7:1778518155      ← does NOT resolve
csecs(#7,ms)           →  1778518155494      ← the field objid actually holds
```

Use `objid()` when you want an objid. If you are pulling the timestamp back out of one,
`after(objid(%0),:)` gives you milliseconds, not seconds — divide by 1000, or just call
`csecs(%0)`.

Ported PennMUSH softcode holding hardcoded `#N:<seconds>` strings will not resolve them.
Rebuild those from `objid()`.
