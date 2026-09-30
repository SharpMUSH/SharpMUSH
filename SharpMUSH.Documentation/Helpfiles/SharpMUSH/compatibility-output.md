<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-output",
  "lookup": "compatibility output",
  "aliases": [],
  "sections": []
}
-->
# COMPATIBILITY OUTPUT

`render(<string>, <formats>)` converts a string's markup for something outside the game — a bot, a
web page, an SQL column. The formats are `ansi`, `html`, `noaccents` and `markup`, as in PennMUSH,
and `ansi` requires the `Can_Spoof` power.

**One difference.** PennMUSH's `markup` flag asks for whatever the other flags did not handle to
survive as internal markup tags. SharpMUSH holds markup as layers over the text rather than as inline
tags, and renders a whole string to one format, so there is nothing for the flag to leave behind: it
is accepted and changes nothing.

```sharp
> think render(ansi(r,a<b>c),html)
<span style="color: #aa0000">a&lt;b&gt;c</span>
> think render(ansi(r,red),markup)
red
```

Rendering to a format the game does not otherwise use is how you get a portable string: `html` for a
web page, `ansi` for a terminal capture, neither for plain text.
