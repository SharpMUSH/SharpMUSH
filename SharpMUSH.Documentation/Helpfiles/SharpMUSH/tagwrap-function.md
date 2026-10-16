<!-- help-article
{
  "corpus": "help",
  "id": "tagwrap-function",
  "lookup": "tagwrap()",
  "aliases": [],
  "sections": [
    {"id": "example", "heading": "Example", "lookup": "tagwrap() example"},
    {"id": "allowed-tags", "heading": "Allowed tags", "lookup": "tagwrap() allowed tags"}
  ]
}
-->
# tagwrap()

`tagwrap(<name>[, <parameters>], <string>)`

This function outputs *<string>*, wrapped in the *<name>* HTML tag with the specified *<parameters>*.

The tag is markup on *<string>*, not text in it. A Pueblo client, an MXP client and the web portal receive it as a tag; every other client receives *<string>* alone. strlen() and the other string functions see only *<string>*.

A particularly important use of this function is `tagwrap(pre, <string>)`. Because Pueblo works like an html browser, spaces and tabs are compressed to a single space. If you have code (a +who function, for example) that relies on exact spacing, surround its output with a tagwrap(pre,...) so that Pueblo will render it as "preformatted" text.

- [tagwrap() example]: a link, and tags that differ between clients
- [tagwrap() allowed tags]: the tags and parameters allowed without Send_OOB

## Example

```sharp
tagwrap(a,href="https://sharpmush.com",SharpMUSH Downloads)
```

Will output (in HTML):
```html
<a href="https://sharpmush.com">SharpMUSH Downloads</a>
```

The tag is written as given, so it has to mean something in the reader's client. Most tags are spelled the same in Pueblo and MXP, but command links are not: use [CMDLINK()] for one.

## Allowed tags

Without Send_OOB, *<name>* must be one of PennMUSH's allowed tags (A, B, I, U, FONT, PRE, IMG, TABLE and the like; anything else is `#-1 PERMISSION DENIED`), and the *<parameters>* are kept only if every one is on SharpMUSH's list of presentational attributes (href, src, color, face, size, align, width, height, title, xch_hint, class, and a few more), with any href or src naming an http, https, mailto, ftp or tel address. If any parameter fails, all of them are dropped. This is stricter than PennMUSH because the web portal renders the tag in a browser. A *<name>* that is not letters and digits is `#-1 INVALID TAG NAME` for everyone.

::: seealso
- [CMDLINK()]
- [TAG()]
- [ENDTAG()]
- [HTML()]
:::
