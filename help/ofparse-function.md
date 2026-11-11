<!-- help-article
{
  "corpus": "help",
  "id": "ofparse-function",
  "lookup": "ofparse()",
  "aliases": [
    "ofparse"
  ],
  "sections": []
}
-->
# ofparse()

`ofparse(<type>[, <value>, ... ], <default>)`<br>
`ofparse(<type>[, <value>, ... ], <separator>)`

Evaluates each `<value>` in turn and returns the ones `<type>` picks.

| Type | Returns | Last argument |
| --- | --- | --- |
| 1 | the first true value | `<default>` |
| 2 | every true value | `<separator>` |
| 3 | the first false value | `<default>` |
| 4 | every false value | `<separator>` |
| 5 | the first value that is not empty | `<default>` |
| 6 | every value that is not empty | `<separator>` |
| 7 | nothing, when a value is empty | `<default>` |
| 8 | every empty value | `<separator>` |

The odd types stop at the first value that counts, and return `<default>` when none does. The even types join what they keep with the first character of `<separator>`. A false value is empty, blank, `0`, or starts with `#-`, so `0.0` is true. Types 1, 2, 5 and 6 work as [firstof()], [allof()], [strfirstof()] and [strallof()]. ofparse() comes from RhostMUSH.

```sharp
> think ofparse(1,0,#-1,first,second,none)
first
```

Output: the first true value.

::: seealso
- [firstof()]
- [allof()]
:::
