<!-- help-article
{
  "corpus": "help",
  "id": "terminfo-function",
  "lookup": "terminfo()",
  "aliases": [],
  "sections": [
    {"id": "capabilities", "heading": "Capabilities", "lookup": "terminfo capabilities"},
    {"id": "visibility", "heading": "Who sees what", "lookup": "terminfo visibility"},
    {"id": "example", "heading": "Example", "lookup": "terminfo example"}
  ]
}
-->
# terminfo()
`terminfo(<player|descriptor>)`

  Returns a list with at least one element - the type of client used by the player, or "unknown" if the client being used doesn't support being asked to identify itself using RFC 1091.

- [terminfo capabilities]: every capability in the list, and the color style
- [terminfo visibility]: what you see about other players, and screen readers
- [terminfo example]: the portal with Screen reader mode on

## Capabilities

  Other elements in the list describe client capabilities, and currently include:<br>
  pueblo           present if the client is in Pueblo mode.<br>
  mxp              present if the client is in MXP mode.<br>
  screenreader     a screen reader reads this connection: MTTS says so, or the player turned on Screen reader mode in the web portal's Terminal settings, on Play.<br>
  telnet           present if the client understands the telnet protocol.<br>
  gmcp             present if GMCP is negotiated via telnet; see help oob()<br>
  ssl              present if the client is using an SSL/TLS connection.<br>
  websocket        present if the client is connected via WebSocket.<br>
  portal           present if the connection is a background portal (system) session.<br>
  prompt_newlines  see [PROMPT_NEWLINES]<br>
  stripaccents     client is receiving 7-bit ascii, no accented characters<br>
  hyperlinks       web links are sent as clickable OSC 8 links; see [HYPERLINKS]<br>
  commandlinks     command links are sent as clickable MSLP links; see [HYPERLINKS]<br>
  kitty, iterm2, sixel or blocks  how pictures are drawn; see [GRAPHICS]<br>
  animation        moving pictures play; see [ANIMATION]

  One of the color styles shown in [COLORSTYLE] will also be included.

## Who sees what

  You must have see_all, or use terminfo() on yourself, to see all information or use a `<descriptor>`. Mortals using terminfo() on another player will always receive "unknown" for the client name, and will not get telnet/gmcp/ssl/prompt_newlines in the output list.

  Anyone may see pueblo, mxp, screenreader, stripaccents and the color style. Softcode can use screenreader to leave out what only draws, such as a logo made of characters. A screen reader on telnet is sent no color, links or pictures, so its color style is plain; the web portal draws them itself, so it keeps its own.

## Example

  Example, in the web portal with Screen reader mode on:
```sharp
think terminfo(me)
SHARPMUSH-PORTAL screenreader websocket commandlinks truecolor
```


::: seealso
- [PUEBLO()]
- [WIDTH()]
- [WIDTH()]
- [SSL()]
- [@SOCKSET]
- [OOB()]
:::
