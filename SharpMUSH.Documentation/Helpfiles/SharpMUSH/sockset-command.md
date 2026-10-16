<!-- help-article
{
  "corpus": "help",
  "id": "sockset-command",
  "lookup": "@sockset",
  "aliases": ["SOCKSET"],
  "sections": [
    {"id": "display-options", "heading": "Display options", "lookup": "@sockset display options"},
    {"id": "connection-options", "heading": "Connection options", "lookup": "@sockset connection options"}
  ]
}
-->
# @sockset

`SOCKSET [<option>=<value>]`<br>
`@sockset [<descriptor>][=<option>, <value>[, ..., <optionN>, <valueN>]]`

SOCKSET is a socket command which sets or queries socket-specific options. These options are usually set automatically, or negotiated by the MUSH and your client, but this command lets you override those settings.

With no args, SOCKSET shows the current value of the socket options. With an `<option>=<value>` pair, it attempts to set the given option.

@sockset is a similar in-game command, but can specify which descriptor to change options for, and can set multiple options at once. Only Wizards can change the options for other players' descriptors. `<descriptor>` defaults to your least-idle descriptor, when used by a player; for non-players, it has no default.

Note that changing 'telnet' or 'pueblo' may stop your client from parsing or displaying output correctly; only use if you know what you're doing!

- [@sockset display options]: colour, links, pictures, terminal, character set and screen size
- [@sockset connection options]: prefixes, Pueblo, telnet, prompts and quota

## Display options

- colorstyle: See [COLORSTYLE]
- hyperlinks, commandlinks: See [HYPERLINKS]
- graphics: See [GRAPHICS]
- animation: See [ANIMATION]
- terminal: See [TERMINAL]
- charset: See [CHARSET]
- screenreader: on, off or auto. Same as SCREENREADER; see [screenreader client]
- width: Set your width(), same as SCREENWIDTH
- height: Set your height(), same as SCREENHEIGHT
- terminaltype: Your terminal type, used by terminfo()
- stripaccents: Send this connection plain ASCII, each other character replaced as [CHARSET] describes. Like the NOACCENTS flag, but connection-specific.

## Connection options

- outputprefix: Same as OUTPUTPREFIX
- outputsuffix: Same as OUTPUTSUFFIX
- pueblo: Sets Pueblo-related options. If value has md5=...", then it will set the pueblo checksum. If empty, Pueblo mode is turned off.
- telnet: Yes or no, to enable/disable telnet negotiation
- prompt_newlines: Set whether a newline is shown after prompts from @prompt, same as PROMPT_NEWLINES
- noquota: Input command quota is set to max every refresh. Can only be set by a logged-in Wizard.

::: seealso
- [socket commands]
- [TERMINFO()]
- [pueblo]
- [COLORSTYLE]
- [HYPERLINKS]
- [GRAPHICS]
- [TERMINAL]
- [CHARSET]
- [@prompt]
:::
