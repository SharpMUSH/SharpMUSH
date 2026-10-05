<!-- help-article
{
  "corpus": "help",
  "id": "switch-function",
  "lookup": "switch()",
  "aliases": [
    "SWITCHALL()",
    "CASE()",
    "CASEALL()",
    "switch"
  ],
  "sections": [
    {
      "id": "pattern-selection-examples",
      "heading": "Pattern selection examples",
      "lookup": "switch pattern selection examples"
    }
  ],
  "redirects": {
    "SWITCH2": "switch pattern selection examples"
  }
}
-->
# switch()

`switch(<str>, <expr1>, <list1>[, ... , <exprN>, <listN>][, <default>])`<br>
`switchall(<str>, <expr1>, <list1>[, ... , <exprN>, <listN>][, <default>])`<br>
`case(<str>, <expr1>, <list1>[, ... , <exprN>, <listN>][, <default>])`<br>
`caseall(<str>, <expr1>, <list1>[, ... , <exprN>, <listN>][, <default>])`

These functions match `<string>` against the `<expr>`essions, returning the corresponding `<list>`. If nothing is matched, the `<default>` is returned. switch() and case() return the `<str>` for the first matching `<expr>`, while switchall() and caseall() return the corresponding `<list>` for all `<expr>`s which match.

switch() and switchall() use wildcard and lt/gt `<expr>`s, as described in [SWITCH WILDCARDS]. case() and caseall() do a case-sensitive exact match, like member() or comp(). In this case, $0-$9 will be set to the text that the nth wildcard character met.

If the string "#$" appears in the `<list>` to be evaluated, it will be replaced with the evaluated value of `<str>` /before/ evaluation of `<list>`. This is not done in case() and caseall(), for TinyMUSH 3 compatibility. Note that this replacement happens before evaluation, which makes it unsafe when `<str>` contains user input, and makes it unsuitable for use in nested switch()es. It is strongly recommended you use the %$`\<n\>` substitution or stext() function instead, which solves these problems.


::: seealso
- [RESWITCH()]
- [STEXT()]
- [STEXT()]
- [IF()]
- [COND()]
- [FIRSTOF()]
:::

## Pattern selection examples

Examples:
```sharp
say switch(test, *a*, foo, *b*, bar, *t*, neat, baz)
You say, "neat"
```

```sharp
> say switchall(ack, *a*, foo, *b*, bar, *c*, neat, baz)
You say, "fooneat"
```

```sharp
> say switch(moof, *a*, foo, *b*, bar, *t*, neat, baz)
You say, "baz"
```

```sharp
> say switch(moof, *a*, foo, *b*, bar, *t*, neat, #$)
You say, "moof"
```

```sharp
> say case(moof, *f, foo, moof, bar, baz)
You say, "bar"
```

```sharp
> say switch(foo bazaar,f?o b*r,$0-$1)
You say, "o-azaa"
```
