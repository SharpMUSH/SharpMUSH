<!-- help-article
{
  "corpus": "help",
  "id": "tone-function",
  "lookup": "tone()",
  "aliases": [],
  "sections": [
    {
      "id": "who-sees",
      "heading": "Who sees what",
      "lookup": "tone() who sees what"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "tone() examples"
    }
  ]
}
-->
# tone()

`tone(<colour>, <text>)`

*<text>* in a theme colour, named for what it is for rather than as a colour, so each reader sees it in their own theme. *<colour>* is one of the colours in [LAYOUT THEMES]:

- For what the text is: `foreground`, `primary`, `secondary`, `tertiary`, `muted`, `subtle` (quieter than muted), `link`, `success`, `warning`, `error` or `info`.
- `strong` - the text colour in bold, for a name or a heading.
- `highlight` - the text on a marked background, for a search hit or something new.
- A hue: `red`, `orange`, `yellow`, `green`, `cyan`, `blue`, `purple` or `pink`. Each theme draws its own red, as a terminal's colour scheme does, so it still reads on that theme.

- [tone() who sees what]: the colour each kind of reader sees
- [tone() examples]: when to use it, an OOC remark and a coloured speech

::: seealso
- [@THEME]
- [LAYOUT THEMES]
- [BADGE()]
- [ANSI()]
:::

## Who sees what

- A telnet reader with an [@THEME] sees that theme's colour, so a player who makes their `muted` colour paler sees every `tone(muted,...)` paler.
- A telnet reader without one sees the game's `layout_theme` colour, or a standard colour when the game has none: `muted` in grey, `info` in cyan, `success` in green, `warning` in yellow, `error` in red, each hue in its standard colour, and `strong` in bold.
- The web portal colours it from the reader's portal theme.

## Examples

Use it for text whose colour says what kind of thing it is, such as an out-of-character remark or a radio call, and [ANSI()] for a colour chosen for its own sake. The scene package draws pose types with it; see `+help scene types`.

```sharp
> think tone(muted,<OOC> Back in five.)
<OOC> Back in five.
```

That line is grey for a reader with no theme, and their theme's `muted` colour for one with an @theme.

```sharp
> think tone(strong,Wren) says, "[tone(purple,Look.)]"
Wren says, "Look."
```

The name is bold, and the speech is in the reader's theme's purple.
