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

New telnet connections start with graphics=auto. Before the connect screen SharpMUSH asks about MXP support. It does not ask about terminal graphics, because a MUD client would print the questions: a connection is sent Kitty, iTerm2 or Sixel pictures when the first terminal type its client reports names a terminal or client SharpMUSH knows to draw them (such as xterm-kitty, foot or MudForge), when you name the terminal with SOCKSET terminal, or after SOCKSET graphics=auto or detect asks the terminal. SOCKSET graphics=auto asks only a terminal SharpMUSH knows, by its name or by SOCKSET terminal; SOCKSET graphics=detect asks any client. A bare xterm, which PuTTY and many clients report, names no terminal. SharpMUSH waits up to `negotiation_wait_ms` milliseconds for replies (a Net game option, default 400). Set it to 0 to skip the wait; changes apply to new connections. Late replies update later output. Use SOCKSET graphics=off to request text instead; manual method choices override detection. Client profiles remain assumptions: a proxy or disabled image setting can prevent rendering.

- [graphics methods]: the values, from auto and detect to kitty, sixel and off
- [graphics display]: UTF-8, screen readers, layout, fetching and moving pictures
- [graphics detection]: terminal replies, profile assumptions and cell dimensions

## Drawing methods

- auto: chooses a drawing method (see [TERMINAL]): kitty first, then iterm2, then sixel, then blocks. Setting it asks a known terminal for its capabilities; replies take priority over client-profile assumptions. A MUD client or an unknown client is not asked, because it would print the questions; without evidence it gets blocks. Also accepted as 'on' or 'yes'.
- detect: selects auto and asks the client what it can draw, whatever the client is. An unknown terminal can use Sixel or Kitty when it answers; a MUD client may print the questions. Replies are processed immediately, without Enter or another command. Setting auto again repeats the probe for a known terminal. A silent client remains unknown; silence is not a rejection.
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
