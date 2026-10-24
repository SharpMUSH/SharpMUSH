<!-- help-article
{
  "corpus": "help",
  "id": "terminfo-function",
  "lookup": "terminfo()",
  "aliases": [],
  "sections": [
    {"id": "capabilities", "heading": "Capabilities", "lookup": "terminfo capabilities"},
    {"id": "display", "heading": "Display capabilities", "lookup": "terminfo display"},
    {"id": "visibility", "heading": "Who sees what", "lookup": "terminfo visibility"},
    {"id": "example", "heading": "Example", "lookup": "terminfo example"}
  ]
}
-->
# terminfo()
`terminfo(<player|descriptor>)`

Returns a list with at least one element: the type of client used by the player, or "unknown" if the client being used doesn't support being asked to identify itself using RFC 1091.

- [terminfo capabilities]: the client's protocols and connection
- [terminfo display]: what the client can draw, and the color style
- [terminfo visibility]: what you see about other players, and screen readers
- [terminfo example]: the portal with Screen reader mode on

## Capabilities

The other elements describe what the client can do. These are about its protocols and connection; the rest are in [terminfo display].

| Element | Present when |
| --- | --- |
| `pueblo` | the client is in Pueblo mode |
| `mxp` | the client is in MXP mode |
| `screenreader` | a screen reader reads this connection: MTTS says so, or the player turned on Screen reader mode in the web portal's Terminal settings, on Play |
| `telnet` | the client understands the telnet protocol |
| `gmcp` | GMCP was agreed over telnet; see [oob()] |
| `ssl` | the client is using an SSL/TLS connection |
| `websocket` | the client is connected through a WebSocket |
| `portal` | the connection is the web portal's background session |

## Display capabilities

These elements describe what the client can draw:

| Element | Present when |
| --- | --- |
| `prompt_newlines` | see [PROMPT_NEWLINES] |
| `stripaccents` | the client is sent 7-bit ASCII, with no accented characters |
| `hyperlinks` | web links are sent as clickable OSC 8 links; see [HYPERLINKS] |
| `commandlinks` | command links are sent as clickable MSLP links; see [HYPERLINKS] |
| one of `kitty`, `iterm2`, `sixel` or `blocks` | pictures are drawn that way; see [GRAPHICS] |
| `animation` | moving pictures play; see [ANIMATION] |

One of the color styles shown in [COLORSTYLE] will also be included.

## Who sees what

You must have see_all, or use terminfo() on yourself, to see all information or use a `<descriptor>`. Mortals using terminfo() on another player will always receive "unknown" for the client name, and will not get telnet/gmcp/ssl/prompt_newlines in the output list.

Anyone may see pueblo, mxp, screenreader, stripaccents and the color style. Softcode can use screenreader to leave out what only draws, such as a logo made of characters. A screen reader on telnet is sent no color, links or pictures, so its color style is plain; the web portal draws them itself, so it keeps its own.

## Example

In the web portal with Screen reader mode on:

```sharp
> think terminfo(me)
SHARPMUSH-PORTAL screenreader websocket commandlinks truecolor
```

::: seealso
- [PUEBLO()]
- [WIDTH()]
- [SSL()]
- [@SOCKSET]
- [OOB()]
:::
