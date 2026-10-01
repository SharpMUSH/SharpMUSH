<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-output",
  "lookup": "compatibility output",
  "aliases": [],
  "sections": [
    {
      "id": "render-markup",
      "heading": "`render()` markup compatibility",
      "lookup": "compatibility output render markup"
    }
  ]
}
-->
# COMPATIBILITY OUTPUT

`render(<string>, <formats>)` converts a string's markup for something outside the game — a bot, a
web page, an SQL column. The formats are `ansi`, `html`, `noaccents` and `markup`, as in PennMUSH,
and `ansi` requires the `Can_Spoof` power.

## `render()` markup compatibility

**A choice.**

**PennMUSH** uses the `markup` flag to retain whatever the other formats did not handle as internal markup tags.

**SharpMUSH** accepts `markup` without changing the result.

**Why.** SharpMUSH stores markup as layers over text and renders a whole string to one format, rather than leaving unrendered inline tags behind.

```sharp
> think render(ansi(r,a<b>c),html)
<span style="color: #aa0000">a&lt;b&gt;c</span>
> think render(ansi(r,red),markup)
red
```

**Workaround.** Choose the output format needed by the consumer to get a portable string: `html` for a
web page, `ansi` for a terminal capture, neither for plain text.
