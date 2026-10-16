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
      "id": "a-trailing-supported-on-oob",
      "heading": "A trailing `supported` on `oob()`",
      "lookup": "compatibility arguments a trailing supported on oob"
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
    },
    {
      "id": "tinymux-s-forms-of-shared-functions",
      "heading": "TinyMUX's forms of shared functions",
      "lookup": "compatibility arguments tinymux s forms of shared functions"
    }
  ]
}
-->
# Compatibility Arguments

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

## A trailing `supported` on `oob()`

**A choice.**

**PennMUSH** sends the package to every connection that speaks GMCP.<br>
**SharpMUSH** does the same, unless a fourth argument `supported` asks it to skip a telnet client
that listed GMCP modules in Core.Supports without this package's. A client that listed nothing, and
a WebSocket, still get it.<br>
**Why.** A client that says which modules it wants should not be sent the rest, but most clients
never say, and Mudlet uses some packages it does not list.<br>
**Workaround.** Omit it.

```sharp unchecked
> think oob(me,Room.Info,json(object,num,json(number,1234)),supported)
1
```
(The count is how many of your connections took it.)

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
deprecation warning: `strlen(a%tb,0)` warns, then answers 5.<br>
**SharpMUSH** takes an optional second argument. A false value counts control characters
(`%t`, `%r`) as zero, as `displaywidth()` does, so `strlen(a%tb,0)` is 2. Omitted or true, each
control character counts as one, as in PennMUSH. (#1256)<br>
**Why.** A tab or newline takes no columns, and padding a table needs that width.<br>
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
none. `[chr(28450)][chr(23383)]` is two wide CJK characters (the word "kanji"), for which PennMUSH would
answer `6`:

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

## TinyMUX's forms of shared functions

**A choice.**

**PennMUSH** refuses these calls: `switch(x,default)` and `add(5)` are argument-count errors, as
are `and()`, `or()`, `xor()`, `cand()`, `cor()` and `mul()` with one argument. `itext()` and
`lnum()` with no argument are errors. `wrap()` takes at most four arguments, and its third must be
a number. A set function's fourth argument is its sort type and its fifth the output separator,
so `setunion(B a c,b, ,|,a)` joins the result with `a`: `Baaabac`.<br>
**SharpMUSH** answers them as TinyMUX does; [tinymux forms] says how it reads each one.<br>
**Why.** TinyMUX code that uses these forms runs. All but the set functions' order are errors in
PennMUSH, so no working PennMUSH code changes; a set function changes only when its fourth argument
names no sort type and its fifth names one, which PennMUSH would use as the separator.<br>
**Workaround.** Give PennMUSH's arguments for code that has to run on both: a default with a
pattern before it, two numbers, a level for `itext()`, a number as `wrap()`'s third argument, and
a sort type before the output separator.

```sharp
> think [switch(x,default)]|[add(5)]|[and(1)]|[lnum()]|[iter(a b,itext())]
default|5|1||a b
> think wrap(aaa bbb ccc,5,right)
  aaa
  bbb
  ccc
> think setunion(B a c,b, ,|,a)
B|a|b|c
```
