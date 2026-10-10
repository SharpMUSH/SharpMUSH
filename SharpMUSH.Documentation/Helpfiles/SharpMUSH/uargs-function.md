<!-- help-article
{
  "corpus": "help",
  "id": "uargs-function",
  "lookup": "uargs()",
  "aliases": [
    "uargs",
    "named arguments"
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

Named arguments are not q-registers. They belong to the one call, as %0-%9 do: the attribute's own u() calls do not see them, and nothing is left behind when it returns. The attribute gets no %0-%9, and %+ is 0, unless a `<name>` is a number.

Up to 32 name and value pairs can be passed. An empty name returns `#-1 ARGUMENT NAME INVALID`, and an unpaired name returns an error for the odd argument count. Permissions are the same as for ufun().

## uargs examples

Example:
```sharp
&FUN`GREETING me=Hello, %<name>. You have %<count> new [if(eq(%<count>,1),post,posts)].
think uargs(me/FUN`GREETING, name, %n, count, 3)
```
Output: `Hello, Cyclonus. You have 3 new posts.`

::: seealso
- [u()]
- [r()]
- [substitutions]
- [ulocal()]
:::
