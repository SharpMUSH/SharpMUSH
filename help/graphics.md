<!-- help-article
{
  "corpus": "help",
  "id": "graphics",
  "lookup": "graphics",
  "aliases": [],
  "sections": [
    {"id": "methods", "heading": "Drawing methods", "lookup": "graphics methods"},
    {"id": "display", "heading": "How pictures are shown", "lookup": "graphics display"}
  ]
}
-->
# graphics

`SOCKSET graphics=<value>`<br>
`@SOCKSET [me|<descriptor>]=graphics,<value>`

Pictures in the game (such as those placed with figure()) are shown as their text art until you turn pictures on. They are off by default because only you can see whether your terminal shows them: a terminal reached through tmux, screen or an ssh hop, or one with images turned off in its settings, may not, even when SharpMUSH knows it can. The graphics setting says how your terminal draws a picture.

- [graphics methods]: the values, from auto and detect to kitty, sixel and off
- [graphics display]: UTF-8, screen readers, layout, fetching and moving pictures

## Drawing methods

- auto: the best way your terminal has. SharpMUSH picks from what your terminal can do (see [TERMINAL]): kitty first, then iterm2, then sixel, then blocks. A terminal it does not know gets blocks. Also accepted as 'on'.
- detect: like auto, and also asks your terminal what it can draw and what it is. The answer arrives with the next line you send, so send any command afterwards. Use this when your terminal does not say which it is.
- kitty: the Kitty graphics protocol (kitty, Ghostty, Rio). The picture is sent once per connection and drawn in its cells as text, so it scrolls and wraps like text.
- iterm2: iTerm2 inline images (iTerm2, WezTerm, Konsole, mintty, Visual Studio Code, Rio, mlterm).
- sixel: sixel graphics (foot, WezTerm, iTerm2, Konsole, Windows Terminal, mintty, Contour, mlterm, xterm started with sixel support).
- blocks: coloured half-block characters, two pixels to a cell. Any UTF-8 terminal with colour shows these.
- off: the text art (the default).

## How pictures are shown

Kitty pictures and half blocks are only sent to a connection receiving UTF-8. A connection identified as a screen reader always receives the picture's description instead. SOCKSET shows the method in use, as "auto (<method>)" for auto, and terminfo() includes it.

A picture appears in the space its text art takes, so whatever is laid out around it stays where it is. The first time a picture is shown it may appear as its text art while the game fetches it. A moving picture shows its first frame unless animation is on; see [ANIMATION].

Example:
```sharp
> SOCKSET graphics=auto
Graphics set to 'auto'
```

::: seealso
- [@SOCKSET]
- [ANIMATION]
- [TERMINAL]
- [HYPERLINKS]
- [TERMINFO()]
- [FIGURE()]
:::
