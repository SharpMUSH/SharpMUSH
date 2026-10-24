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
      "heading": "Examples",
      "lookup": "uargs examples"
    }
  ]
}
-->
# uargs()

`uargs([<object>/]<attribute>[, <name>, <value>[, ... ]])`

uargs() is ufun() with named arguments. It evaluates `<attribute>` on `<object>` (or on the caller, if no `<object>` is given), passing each `<value>` under its `<name>`. The attribute reads them with `%<name>` or r(`<name>`, args). Names are case-insensitive.

Each value is also passed by position, in the order given: the first is `%0`, the second `%1`, and so on, and `%+` counts them. An empty value still takes its place. When a name is given twice, both values keep their positions and the name reads the last one.

Named arguments are not q-registers. They belong to the one call, as %0-%9 do: the attribute's own u() calls do not see them, and nothing is left behind when it returns.

Up to 32 name and value pairs can be passed. A name that is a number must be the value's own position, counted from 0: `uargs(me/FUN,0,X)` is allowed, `uargs(me/FUN,1,X)` is not. An empty or wrong name returns `#-1 ARGUMENT NAME INVALID`, and a name with no value an error for the odd number of arguments; the attribute is not run. The arguments are evaluated before the call, as for ufun(), and permissions are the same as ufun()'s.

## Examples

```sharp
&FUN`GREETING me=Hello, %<name>. You have %<count> new [if(eq(%<count>,1),post,posts)].
think uargs(me/FUN`GREETING, name, %n, count, 3)
```

Output: `Hello, Cyclonus. You have 3 new posts.`

```sharp
&SHOW me=%0 %1 %<a>
think uargs(me/SHOW, a, 1, a, 2)
```

Output: `1 2 2`

::: seealso
- [named arguments]
- [hasarg()]
- [u()]
- [r()]
- [substitutions]
- [ulocal()]
:::
