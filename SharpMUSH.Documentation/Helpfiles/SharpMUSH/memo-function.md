<!-- help-article
{
  "corpus": "help",
  "id": "memo-function",
  "lookup": "memo()",
  "aliases": [
    "memo"
  ],
  "sections": [
    {
      "id": "kept-answers",
      "heading": "Kept answers",
      "lookup": "memo kept answers"
    },
    {
      "id": "memo-examples",
      "heading": "Memo examples",
      "lookup": "memo examples"
    }
  ]
}
-->
# memo()

`memo([<object>/]<attribute>[, <arg0>[, ... , <arg29>]])`

memo() runs `<attribute>` as [u()] does, and keeps the answer. A later call that is the same gets the kept answer without running the code again. Use it for slow code that is called often with the same arguments, such as working out a character's derived stats for every line of a listing.

The code may not change anything. Every side-effect function it calls, directly or through u(), answers #-1 FUNCTION DISABLED, and memo() then returns `#-1 MEMO REFUSES SIDE EFFECTS` and keeps nothing. Side-effect functions are the ones the `function_side_effects` option turns off, such as set(), pemit() and create(), and the parts of roll() and benchmark() that record or send something.

The code starts with no q-registers, and the ones it sets are gone when it ends. It cannot read or change the caller's %q registers; pass what it needs as arguments.

`<attribute>` may also be a #lambda (see [anonymous attributes]).

::: seealso
- [u()]
- [ulocal()]
:::

## Kept answers

Two calls are the same when they run the same code on the same object, called by the same object, for the same enactor (%#), with the same arguments. Colour in an argument counts.

A kept answer is dropped when:

- an attribute the code read now reads differently, or is gone;
- the code itself is changed;
- `memo_window` seconds have passed (60 by default); or
- the memory set by `memo_max_bytes` is full and it is the least recently used.

Both options are under [@config limits].

Other changes, such as a name, location, flag or lock, are not noticed until the window passes. A function that answers differently each time, such as rand() or secs(), gives the same answer until the answer is dropped. Messages the code sends to the caller, such as "I can't see that here.", are only sent when it runs.

An answer that hit a function limit, or is longer than 65536 characters, is not kept.

## Memo examples

```sharp
> &DOUBLE me=mul(%0,2)
> think memo(DOUBLE,21)
42
```

Output: 42

```sharp
> &BONUS me=10
> &TOTAL me=add(%0,v(BONUS))
> think memo(TOTAL,5)
15
> &BONUS me=20
> think memo(TOTAL,5)
25
```

Output: 25, because TOTAL read BONUS and BONUS changed.

```sharp
> &MARK me=[set(me,SEEN:1)]done
> think memo(MARK)
#-1 MEMO REFUSES SIDE EFFECTS
```

Output: #-1 MEMO REFUSES SIDE EFFECTS
