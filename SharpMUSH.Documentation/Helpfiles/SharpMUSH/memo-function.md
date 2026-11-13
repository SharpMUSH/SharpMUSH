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
      "id": "refused-code",
      "heading": "Refused code",
      "lookup": "memo refused code"
    },
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

The code may not change anything, and its answer may not vary from call to call; see [memo refused code].

The code sees only its arguments: it starts with no q-registers and the ones it sets are gone when it ends, and it cannot see or stop the caller's iter() or switch. Pass what it needs as arguments.

`<attribute>` may also be a #lambda (see [anonymous attributes]).

::: seealso
- [u()]
- [ulocal()]
:::

## Refused code

- If the code names a side-effect function anywhere, even in a branch it would not take, memo() returns `#-1 MEMO REFUSES SIDE EFFECTS`. Side-effect functions are the ones the `function_side_effects` option turns off, such as set(), pemit() and create(), and also stopevent(). A function's reading form, such as name() with one argument, is allowed.
- If the code names a function whose answer varies, such as rand(), shuffle(), roll(), secs(), time(), idle(), conn() or pid(), or timefmt() without a time, memo() returns `#-1 MEMO REFUSES NONDETERMINISTIC FUNCTIONS`. So does a substitution for the running command or the queue, such as %c, %u, %j or %?.

Code reached through u() is checked as it runs: there, such a function answers #-1 FUNCTION DISABLED and memo() returns the same errors. Nothing is kept.

## Kept answers

Two calls are the same when they run the same code on the same object, called by the same object, for the same enactor (%#), with the same arguments. Colour in an argument counts.

A kept answer is dropped when:

- an attribute the code read now reads differently, or is gone, or a list of attributes it read (lattr(), grep() and the like) has changed;
- the code itself is changed;
- `memo_window` seconds have passed (an hour by default); or
- `memo_max_entries` answers (10000 by default) or `memo_max_bytes` of memory (16 MiB by default) are kept and it is the least recently used.

These options are under [@config limits].

Other changes, such as a name, location, flag, lock or @function, are not noticed until the window passes. An answer that read an attribute some other way, such as through indexed() or an alias lookup, is not kept. Messages the code sends to the caller, such as "I can't see that here.", are only sent when it runs.

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

```sharp
> &PICK me=first(shuffle(%0))
> think memo(PICK,a b c)
#-1 MEMO REFUSES NONDETERMINISTIC FUNCTIONS
```

Output: #-1 MEMO REFUSES NONDETERMINISTIC FUNCTIONS
