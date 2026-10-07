<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-identity",
  "lookup": "compatibility identity",
  "aliases": [],
  "sections": [
    {
      "id": "time-precision",
      "heading": "Time precision",
      "lookup": "compatibility identity time precision"
    },
    {
      "id": "objids-and-stamped-dbrefs",
      "heading": "Objids and stamped dbrefs",
      "lookup": "compatibility identity objids and stamped dbrefs"
    },
    {
      "id": "a-destroyed-object-s-dbref-is-not-reused",
      "heading": "A destroyed object's dbref is not reused",
      "lookup": "compatibility identity a destroyed object s dbref is not reused"
    },
    {
      "id": "number-precision",
      "heading": "Number precision",
      "lookup": "compatibility identity number precision"
    },
    {
      "id": "a-result-that-rounds-to-zero-from-below-is-0",
      "heading": "A result that rounds to zero from below is `0`",
      "lookup": "compatibility identity a result that rounds to zero from below is 0"
    },
    {
      "id": "fraction-representation",
      "heading": "Fraction representation",
      "lookup": "compatibility identity fraction representation"
    }
  ]
}
-->
# COMPATIBILITY IDENTITY

SharpMUSH and PennMUSH differ in object identity and numeric representation.
Object timestamps and objids use milliseconds here, automatic dbref allocation
does not reuse purged objects, and some numeric results use a different textual
form. The sections below explain these differences and the portable alternatives.

## Time precision

SharpMUSH stores object creation and modification times to the millisecond. PennMUSH stores whole
seconds. Every function that reports one of those times answers whole seconds by default (see
[compatibility arguments]), so a PennMUSH call gets a PennMUSH answer.

The consequence worth knowing: an objid carries the millisecond value, so
`[num(<obj>)]:[csecs(<obj>)]` does **not** reconstruct one here, where in PennMUSH it does. Ask for
the field the objid actually holds:

```sharp unchecked
> think [num(me)]:[csecs(me,ms)]
#1:1789000000123
```
That is the same two fields as `objid(me)`; whole seconds are not:

```sharp
> think strmatch(objid(me),[num(me)]:[csecs(me,ms)])
1
> think strmatch(objid(me),[num(me)]:[csecs(me)])
0
```

## Objids and stamped dbrefs

SharpMUSH's `objid` is `<dbref>:<creation time in milliseconds>`. Where PennMUSH's `unparse_dbref`
produces a bare dbref in a message, SharpMUSH may produce the stamped form; the two are not
interchangeable as text. Parse an objid with `num()` when you want the dbref alone. The
representation choice is deliberate and tracked separately in #1006 item 13.

## A destroyed object's dbref is not reused

**A choice.**

**PennMUSH** turns a destroyed object into garbage when it is purged and puts its dbref on a free
list; `new_object()` takes the next object from that list (`free_get()`, `src/db.c`,
`src/destroy.c`), so the most recently purged dbref is the next one created. `isdbref()` answers 1
for a garbage dbref, because `parse_objid()` accepts any dbref inside the database (`fun_isdbref`,
`src/fundb.c`).<br>
**SharpMUSH** keeps no garbage objects: a purged object's row is removed. A new object takes the next
dbref after the highest one ever allocated, so a purged object's dbref is not handed out again unless
a wizard or a holder of the `Pick_DBRefs` power names it (`@create <name>=<cost>,<dbref>`), and
`isdbref()` answers 0 for it.<br>
**Why.** A dbref stored in an attribute, a lock or a list keeps pointing at nothing once its object
is gone, instead of silently pointing at whatever was created next.<br>
**Workaround.** Do not rely on a new object taking a destroyed object's dbref, and test whether a
dbref still names an object with `isdbref()`, not by comparing against old dbrefs.

Until the purge, the object is only set `GOING`, and both servers answer `isdbref()` with 1. Here the
second `@nuke` of a `GOING` object purges it at once:

```sharp unchecked
> @create Scratch
Created Scratch (#50:1789000000123).
> @nuke #50
> @nuke #50
> think isdbref(#50)
0
> @create Scratch
Created Scratch (#51:1789000000456).
```

In PennMUSH the same lines answer `1` and create `#50` again.

## Number precision

`float_precision` is the number of decimal places every floating-point result is written with,
trailing zeros dropped, as in PennMUSH. SharpMUSH defaults to 6, the same as PennMUSH, and caps the
setting at 15. `round()` cannot ask for more places than the setting allows.

```sharp
> think pi()
3.141593
> think round(pi(),6)
3.141593
```

## A result that rounds to zero from below is `0`

**A choice.**

**PennMUSH** writes it `-0`, the sign C's `printf` leaves on it.<br>
**SharpMUSH** writes `0`.<br>
**Why.** `-0` and `0` are the same number, but compared as text, by `switch()`, `strmatch()` or a
`$`-command pattern, they are different answers.<br>
**Workaround.** Compare numbers with `eq()`, which is true for `-0` and `0` on both servers.

PennMUSH answers `-0` to the first line:

```sharp
> think fdiv(-1,10000000000000000)
0
> think eq(-0,0)
1
```

## Fraction representation

**A choice.**

**PennMUSH** answers the *simplest* fraction within one part in 10^10: `frac()` walks the
convergents of a continued fraction and stops at the first one inside that tolerance, which its own
help states: "dividing the numerator by the denominator of the results will not always return the
original `<number>`, but something close to it".<br>
**SharpMUSH** answers the **exact** rational of its argument, reduced. Where no simpler fraction is
within PennMUSH's tolerance the two agree, which covers every number of six decimal places or fewer;
where one is, SharpMUSH stays exact and PennMUSH does not.<br>
**Why.** Every number softcode can hand it is a decimal, so an exact rational always exists, and
dividing the result out gives back exactly the number that went in.<br>
**Workaround.** `round()` the number first if you want a simpler fraction than the one it names.

```sharp
> think fraction(pi())
3141593/1000000
> think fraction(0.3333334)
1666667/5000000
> think fraction(-2.75)
-11/4
```

PennMUSH answers `348987/111086` for the first of those, off by 1.8e-11.
