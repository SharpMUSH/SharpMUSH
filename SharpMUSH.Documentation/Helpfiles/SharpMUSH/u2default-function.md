<!-- help-article
{
  "corpus": "help",
  "id": "u2default-function",
  "lookup": "u2default()",
  "aliases": [
    "U2LDEFAULT()",
    "u2default",
    "u2ldefault"
  ],
  "sections": []
}
-->
# u2default()

`u2default([<object>/]<attribute>, <default>[, <arg0>[, ... , <arg31>]])`<br>
`u2ldefault([<object>/]<attribute>, <default>[, <arg0>[, ... , <arg31>]])`

u2default() is udefault() with the attribute run as the calling object, as u2() runs it. When the attribute exists, it is evaluated with the `<arg>`s as %0 onward; when it does not, `<default>` is evaluated instead. Only the one that is used is evaluated.

u2ldefault() does the same, and also keeps the q-registers safe while it runs, as uldefault() does.

Examples:
```sharp
> &JOIN #123=%0-%1
> think u2default(#123/JOIN, none, a, b)
a-b
> think u2default(#123/MISSING, none, a, b)
none
```

u2default() and u2ldefault() come from RhostMUSH and take the same arguments.

::: seealso
- [u2local()]
- [UDEFAULT()]
- [ULDEFAULT()]
:::
