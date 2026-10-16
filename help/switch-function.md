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
      "id": "tokens",
      "heading": "The #$ token",
      "lookup": "switch tokens"
    },
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
`caseall(<str>, <expr1>, <list1>[, ... , <exprN>, <listN>][, <default>])`<br>
`switch(<str>, <default>)`<br>
`switchall(<str>, <default>)`<br>
`case(<str>, <default>)`<br>
`caseall(<str>, <default>)`

These functions match `<string>` against the `<expr>`essions, returning the corresponding `<list>`. If nothing is matched, the `<default>` is returned. As in TinyMUX, a `<default>` may come alone: `switch(x,default)` is `default`. switch() and case() return the `<str>` for the first matching `<expr>`, while switchall() and caseall() return the corresponding `<list>` for all `<expr>`s which match.

switch() and switchall() use wildcard and lt/gt `<expr>`s, as described in [SWITCH WILDCARDS]. case() and caseall() do a case-sensitive exact match, like member() or comp(). In this case, $0-$9 will be set to the text that the nth wildcard character met.

- [switch tokens]: what #$ stands for, in nested switches and beside case()

::: seealso
- [RESWITCH()]
- [STEXT()]
- [STEXT()]
- [IF()]
- [COND()]
- [FIRSTOF()]
:::

## The #$ token

In the `<list>` that is evaluated, or the `<default>`, "#$" stands for `<str>`. The value is never evaluated as code, so #$ is safe on user input. If you nest switch()es, #$ refers to the outermost one whose `<list>` it is written in. For the current switch(), use %$0 or stext(0).

"#$" counts only where it is written in the switch() itself. In an `<expr>`, in the code of an attribute a `<list>` calls such as u(), outside any switch, and inside lit() or after \, it is plain text.

case() and caseall() set "#$" to `<str>` in the `<list>` or `<default>` they evaluate, as if() and ifelse() set it to their condition. When a switch() runs around or inside one of them, #$ names the switch() instead, as it does in PennMUSH, where case() leaves #$ as text. See [compatibility parser in case and if].

With the `innermost_tokens` option on, #$ names the innermost switch, case(), if() or ifelse() whose `<list>`, `<default>` or branch is running, as TinyMUX's does, and it also works in the code of an attribute that `<list>` calls. See [compatibility config innermost loop tokens].

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
