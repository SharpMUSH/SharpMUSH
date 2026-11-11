<!-- help-article
{
  "corpus": "help",
  "id": "parenmatch-function",
  "lookup": "parenmatch()",
  "aliases": [
    "parenmatch"
  ],
  "sections": []
}
-->
# parenmatch()

`parenmatch([<object>/]<attribute>[, <type>[, <key>[, <spacing>]]])`

Returns an attribute's text with its brackets, parentheses and braces coloured as [parenstr()] colours them, the first mismatch in bold red. `<object>` is `me` when left out, and the attribute is read as [get()] reads it.

- A `<type>` of 1 colours only the mismatch.
- `<key>` is accepted for RhostMUSH code and changes nothing.
- `<spacing>` adds spaces up to the mismatch: `s` after `,` and `;` and around `=`, `p` inside parentheses, `b` inside brackets and `B` inside braces.

parenmatch() comes from RhostMUSH.

```sharp
> &check me=[add(1,2))]
> think parenmatch(check,,,bps)
[ add( 1, 2 ) )]
```

Output: the code spaced out; the second `)` is red.

::: seealso
- [parenstr()]
- [syntaxdiagnostics()]
:::
