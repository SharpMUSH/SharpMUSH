<!-- help-article
{
  "corpus": "help",
  "id": "colordepth-function",
  "lookup": "colordepth()",
  "aliases": [
    "colordepth"
  ],
  "sections": []
}
-->
# colordepth()

`colordepth(<player|descriptor>)`

Returns the colour depth a connection is sent:

| Depth | Colour |
| --- | --- |
| 0 | none, or highlight only |
| 4 | the sixteen ANSI colours |
| 8 | the 256 xterm colours |
| 24 | full colour |

It reads the same colour style as [terminfo()], which takes the client and the player's ANSI, COLOR, XTERM256 and TRUECOLOR flags into account. A player with more than one connection answers for the least idle one. Only the player on the connection and objects with See_All may ask.

colordepth() comes from TinyMUX.

```sharp
> think colordepth(me)
8
```

Output: a client that draws 256 colours.

::: seealso
- [terminfo()]
- [width()]
- [ansi()]
:::
