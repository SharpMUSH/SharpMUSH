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
    },
    {
      "id": "decompose-nesting",
      "heading": "`decompose()` writes colour inside colour nested",
      "lookup": "compatibility output decompose nesting"
    },
    {
      "id": "decompose-tags",
      "heading": "`decompose()` writes tags and links as the calls that make them",
      "lookup": "compatibility output decompose tags"
    }
  ]
}
-->
# Compatibility Output

`render(<string>, <formats>)` converts a string's markup for something outside the game: a bot, a
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

## `decompose()` writes colour inside colour nested

**A choice.**

**PennMUSH** closes the open `ansi()` call and opens a flat one each time the colour changes, so colour
inside colour comes back side by side, each call carrying the combined codes.

**SharpMUSH** writes it the way it was made, one call inside the other. Escapes, the space rule, `%r`/`%t`
and the code letters are PennMUSH's.

**Why.** PennMUSH's markup is a flat stream of open and close codes, so flat is all it can write.
SharpMUSH keeps markup as layers, and the nested form evaluates to the same string.

```sharp
> think decompose(ansi(r,a[ansi(g,b)]c))
[ansi(r,a[ansi(g,b)]c)]
```

PennMUSH: `[ansi(r,a)][ansi(g,b)][ansi(r,c)]`. Example: parity case `dc.nesting`.

**Workaround.** Code that evaluates `decompose()`'s result, which is what it is for, gets the same string
on both. Only code that compares the text of two `decompose()` results needs the colours not to nest.

## `decompose()` writes tags and links as the calls that make them

**A choice.**

**PennMUSH** writes a tag as `[tag(<name>)]` before the text and `[endtag(<name>)]` after it.

**SharpMUSH** writes `[tagwrap(<name>[,<attributes>],<text>)]`, a command link as
`[cmdlink(<text>,<command>[,<hint>])]`, and an address link as `[tagwrap(a,href="<address>",<text>)]`.

**Why.** A tag here is a layer over the text it covers, so it has no half-open form: `tag()` and `endtag()`
answer that `tagwrap()` is the way, and `decompose()` writes the calls that do rebuild the string.

```sharp
> think decompose(tagwrap(b,bold))
[tagwrap(b,bold)]
```

PennMUSH: `[tag(b)]bold[endtag(b)]`. Example: parity case `dc.markup`.

**Workaround.** Evaluate `decompose()`'s result on the game that wrote it. Code that moves decomposed
tags between the two servers has to rewrite one form into the other.
