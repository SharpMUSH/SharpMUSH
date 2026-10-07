<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-arguments",
  "lookup": "compatibility arguments",
  "aliases": [],
  "sections": [
    {
      "id": "a-trailing-precision-on-the-time-functions",
      "heading": "A trailing `<precision>` on the time functions",
      "lookup": "compatibility arguments a trailing precision on the time functions"
    },
    {
      "id": "a-trailing-osep-on-the-vector-functions",
      "heading": "A trailing `<osep>` on the vector functions",
      "lookup": "compatibility arguments a trailing osep on the vector functions"
    },
    {
      "id": "prepared-statement-parameters-on-sql-and-mapsql",
      "heading": "Prepared-statement parameters on `sql()` and `mapsql()`",
      "lookup": "compatibility arguments prepared statement parameters on sql and mapsql"
    },
    {
      "id": "a-trailing-count-controls-on-strlen",
      "heading": "A trailing `<count controls>` on `strlen()`",
      "lookup": "compatibility arguments a trailing count controls on strlen"
    },
    {
      "id": "argument-less-forms-pennmush-does-not-offer",
      "heading": "Argument-less forms PennMUSH does not offer",
      "lookup": "compatibility arguments argument less forms pennmush does not offer"
    },
    {
      "id": "ansi-and-lit",
      "heading": "`ansi()` and `lit()`",
      "lookup": "compatibility arguments ansi and lit"
    }
  ]
}
-->
# COMPATIBILITY ARGUMENTS

Functions that accept a different number of arguments here. Every one of these is **additive**: the
call you would write for PennMUSH keeps its PennMUSH meaning, and the extra argument is optional.

`SharpMUSH.Tests/PennMUSH/function-arity.tsv` holds PennMUSH's own declared arity for every
function, and a test compares it against the registered arity on every run, so this list cannot
silently grow. See `docs/guides/registry-parity-and-coverage.md`.

## A trailing `<precision>` on the time functions

**A choice.**

**Affects** `convtime() convutctime() csecs() etime() etimefmt() idle() msecs() secs() stringsecs()
timestring() uptime()`.<br>
**PennMUSH** answers whole seconds.<br>
**SharpMUSH** answers whole seconds too, unless you ask for another unit in one more trailing
argument: `s` (the default), `ms`, `f` for fractional.<br>
**Why.** SharpMUSH stores creation and modification times to the millisecond, so `csecs()` truncating
to seconds would make `[num(%0)]:[csecs(%0)]` fail to reconstruct an objid. Rather than change what a
PennMUSH call returns, the finer value is a separate request.<br>
**Workaround.** Omit the argument for PennMUSH behaviour.

```sharp unchecked
> think secs()
1789000000
> think secs(ms)
1789000000123
```
(The numbers are whatever the clock says; what matters is that the second is the first with three
more digits.)

## A trailing `<osep>` on the vector functions

**A choice.**

**Affects** `vadd() vsub() vmul() vdot() vmax() vmin() vcross()`.<br>
**PennMUSH** joins the answer with the same `<delimiter>` it split the inputs on.<br>
**SharpMUSH** takes a fourth argument that overrides it, defaulting to `<delimiter>`.<br>
**Why.** Every other list function in the game spells the same idea that way.<br>
**Workaround.** Omit it.

```sharp
> think vadd(1|2|3,4|5|6,|)
5|7|9
> think vadd(1|2|3,4|5|6,|,%b)
5 7 9
```

## Prepared-statement parameters on `sql()` and `mapsql()`

**A choice.**

**PennMUSH** takes at most four arguments and rejects a fifth.<br>
**SharpMUSH** binds every argument past the fourth as a prepared-statement parameter, substituted for
a `?` in the query.<br>
**Why.** The alternative is softcode concatenating values into query text.<br>
**Workaround.** None needed; a four-argument call behaves as PennMUSH's does.

```sharp unchecked
> think sql(lit(SELECT name FROM people WHERE id = ?),%b,%b,%b,7)
```

## A trailing `<count controls>` on `strlen()`

**A choice.**

**PennMUSH** takes one argument. A second one is joined to the first, commas and all, with a
warning that this is deprecated: `strlen(a%tb,0)` warns, then answers 5.<br>
**SharpMUSH** takes an optional second argument. A false value counts control characters such as
`%t` and `%r` as zero, as `displaywidth()` does, so `strlen(a%tb,0)` is 2. Omitted or true, each
control character counts as one, as in PennMUSH. (#1256)<br>
**Why.** A tab or a newline takes up no columns, and code that pads a table needs that width.<br>
**Workaround.** Omit the argument for PennMUSH behaviour. A PennMUSH-era call that relied on an
unescaped comma being kept needs it escaped: `strlen(a\,0)`.

```sharp
> think strlen(a%tb)
3
> think strlen(a%tb,0)
2
> think strlen(a\,0)
3
```

For Latin-1 text the two servers count alike. Beyond it, PennMUSH counts the bytes of the UTF-8
encoding and SharpMUSH counts display columns: a wide CJK character is two and a combining mark is
none. `[chr(28450)][chr(23383)]` is two wide CJK characters (the word "kanji"); PennMUSH would answer
`6` for those two characters:

```sharp
> think strlen([chr(28450)][chr(23383)])
4
```

## Argument-less forms PennMUSH does not offer

**A choice.**

**PennMUSH** answers `stext()` with `#-1 ARGUMENT MUST BE INTEGER`, and declares `lwhoid()` with at
most one argument, so `lwhoid(me,online)` is an argument-count error.<br>
**SharpMUSH** treats `stext()` as `stext(0)`, the innermost switch, and lets `lwhoid()` take the
`<status>` argument `lwho()` takes.<br>
**Why.** An omitted level means the innermost one everywhere else. PennMUSH serves `lwho()` and
`lwhoid()` with one C function and only declares them differently.<br>
**Workaround.** Write `stext(0)`, and give `lwhoid()` at most one argument, for code that has to run
on both.

```sharp
> think switch(a,a,stext())
a
> think switch(a,a,stext(0))
a
```

## `ansi()` and `lit()`

PennMUSH spells "the last argument swallows the rest, commas and all" as a negative maximum in its
function table. SharpMUSH reaches the same result with a literal flag on the function instead, so the
declared maximum is not a comparable number. Behaviour matches; only the declaration differs.
