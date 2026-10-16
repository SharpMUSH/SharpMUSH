<!-- help-article
{
  "corpus": "help",
  "id": "uargs-function",
  "lookup": "uargs()",
  "aliases": [
    "uargs"
  ],
  "sections": [
    {
      "id": "uargs-examples",
      "heading": "uargs examples",
      "lookup": "uargs examples"
    }
  ]
}
-->
# uargs()

`uargs([<object>/]<attribute>[, <name>, <value>[, ... ]])`

uargs() is ufun() with named arguments. It evaluates `<attribute>` on `<object>` (or on the caller, if no `<object>` is given), passing each `<value>` under its `<name>`. The attribute reads them with %`\<name\>` or r(`<name>`, args). Names are case-insensitive.

Named arguments are not q-registers. They belong to the one call, as %0-%9 do: the attribute's own u() calls do not see them, and nothing is left behind when it returns. Values also occupy %0, %1, and subsequent numbered slots in supplied order. An empty value occupies a slot; %+ counts positions, not alias keys. Repeated names keep their ordered slots while the last name wins. Numeric names contain ASCII digits and are normalized without leading zeroes; they must equal their own zero-based position (`00,A` is valid, `1,A` is refused).

Up to 32 name and value pairs can be passed. An empty name returns `#-1 ARGUMENT NAME INVALID`, and an unpaired name returns an error for the odd argument count. Permissions are the same as for ufun(). Input expressions retain normal eager evaluation; a rejected binding does not execute the attribute.

Migration: older named calls left numbered slots empty. Code relying on that absence should use [hasarg()] or explicit named keys.

## uargs examples

Example:
```sharp
&FUN`GREETING me=Hello, %<name>. You have %<count> new [if(eq(%<count>,1),post,posts)].
think uargs(me/FUN`GREETING, name, %n, count, 3)
```
Output: `Hello, Cyclonus. You have 3 new posts.`

::: seealso
- [named arguments]
- [u()]
- [r()]
- [substitutions]
- [ulocal()]
:::
