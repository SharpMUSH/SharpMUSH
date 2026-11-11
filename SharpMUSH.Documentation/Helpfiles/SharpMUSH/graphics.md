<!-- help-article
{
  "corpus": "help",
  "id": "graphics",
  "lookup": "graphics",
  "aliases": [],
  "sections": [
    {"id": "methods", "heading": "Drawing methods", "lookup": "graphics methods"},
    {"id": "display", "heading": "How pictures are shown", "lookup": "graphics display"},
    {"id": "detection", "heading": "Capability evidence", "lookup": "graphics detection"}
  ]
}
-->
# graphics

`SOCKSET graphics=<value>`<br>
`@SOCKSET [me|<descriptor>]=graphics,<value>`

New telnet connections start with graphics=auto. Before the connect screen SharpMUSH asks about MXP support and, when the client reports a terminal emulator by name (such as kitty, foot or xterm) and sends no MTTS, about terminal graphics. A MUD client would print those questions, so it is asked only when you set SOCKSET graphics=auto or detect. SharpMUSH waits up to `negotiation_wait_ms` milliseconds for replies (a Net game option, default 400). Set it to 0 to skip the wait; changes apply to new connections. Late replies update later output. Use SOCKSET graphics=off to request text instead; manual method choices override detection. Client profiles remain assumptions: a proxy or disabled image setting can prevent rendering.

- [graphics methods]: the values, from auto and detect to kitty, sixel and off
- [graphics display]: UTF-8, screen readers, layout, fetching and moving pictures
- [graphics detection]: terminal replies, profile assumptions and cell dimensions

## Drawing methods

- auto: asks the terminal for its capabilities (on connect, only a terminal emulator is asked) and chooses a drawing method (see [TERMINAL]): kitty first, then iterm2, then sixel, then blocks. Replies take priority over client-profile assumptions. An unknown client can use Sixel or Kitty when it answers the probes; without evidence it gets blocks. Also accepted as 'on' or 'yes'.
- detect: selects auto and explicitly requests another probe. Replies are processed immediately, without Enter or another command. Setting auto again also repeats the probe. A silent client remains unknown; silence is not a rejection.
- kitty: the Kitty graphics protocol (kitty, Ghostty, Rio). The picture is sent once per connection and drawn in its cells as text, so it scrolls and wraps like text.
- iterm2: iTerm2 inline images (iTerm2, WezTerm, Konsole, mintty, Visual Studio Code, Rio, mlterm).
- sixel: sixel graphics (foot, WezTerm, iTerm2, Konsole, Windows Terminal, mintty, Contour, mlterm, xterm started with sixel support).
- blocks: coloured half-block characters, two pixels to a cell. Any UTF-8 terminal with colour shows these.
- off: text art or the image address.

## How pictures are shown

Kitty pictures and half blocks are only sent to a connection receiving UTF-8. A connection identified as a screen reader always receives the picture's description instead. SOCKSET shows the method in use, as "auto (<method>)" for auto, and terminfo() includes it.

While pictures are off, a picture is written as its address, which you can open and which some clients show as a picture of their own. A figure with text art shows its art. A screen reader gets the picture's description instead; SOCKSET alttext=on asks for descriptions, and alttext=off for addresses, either way. The game's own pictures have an address only once mud_url is set.

A picture appears in the space its text art takes, so whatever is laid out around it stays where it is. The first time a picture is shown it may appear as its text art while the game fetches it. A moving picture shows its first frame unless animation is on; see [ANIMATION].

Example:
```sharp
> SOCKSET graphics=auto
Graphics set to 'auto'
```

## Capability evidence

SOCKSET also shows separate evidence for Kitty, iTerm2 and Sixel: supported or unsupported from a terminal reply, an unconfirmed client-profile assumption, or unknown when no reply is recorded. Cell pixels shows the reported geometry when available. These describe capabilities, independently of your graphics override; selecting a method manually does not prove support. iTerm2 image support currently relies on a profile or a manual choice because its image protocol has no equivalent capability acknowledgement.

Use [graphics methods] to select auto or request another probe.

::: seealso
- [@SOCKSET]
- [ANIMATION]
- [TERMINAL]
- [HYPERLINKS]
- [TERMINFO()]
- [FIGURE()]
:::
