<!-- help-article
{
  "corpus": "help",
  "id": "parenstr-function",
  "lookup": "parenstr()",
  "aliases": [
    "parenstr"
  ],
  "sections": []
}
-->
# parenstr()

`parenstr(<string>)`<br>
`parenstr(<part>[, ... , <partN>])`

Returns `<string>`, unevaluated, with each bracket, parenthesis and brace coloured by how deep it is. The first closer without its opener, or else the last opener left open, is bold red, and the text after it is not coloured. A character after `%` or `\` is not counted. Commas are part of `<string>`. When `<string>` starts with `!`, only the mismatch is coloured.

The parser still reads the argument to find where it ends, so a `)` that closes nothing outside brackets ends it early. Check stored code with [parenmatch()]. parenstr() comes from RhostMUSH.

```sharp
> think parenstr([add(1,2))])
[add(1,2))]
```

Output: the brackets coloured; the second `)` is red.

::: seealso
- [parenmatch()]
- [syntaxdiagnostics()]
:::
