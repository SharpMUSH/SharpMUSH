<!-- help-article
{
  "corpus": "help",
  "id": "textfile-function",
  "lookup": "textfile()",
  "aliases": ["textentries()", "textsearch()", "dynhelp()"],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "textfile examples"}
  ]
}
-->
# textfile()
`textfile(<type>, <entry>)`<br>
`textentries(<type>, <pattern>[, <osep>])`<br>
`textsearch(<type>, <pattern>[, <osep>])`

  textfile() returns the text of entries from cached text files (such as "help", "news", "events", etc.) All whitespace and newlines are included, so you may want to edit %r's and squish the result if you plan to use the text as a list of words rather than a display.

  textentries() returns the topic names in `<type>` matching `<pattern>`, separated by `<osep>` (a space by default). `<pattern>` is a wildcard pattern matched against the topic name; use `*` for every topic, or textsearch() where you want matching by content instead.

  Both textfile() and textentries() return #-1 NO SUCH FILE for a `<type>` that is not a text-file category, and #-1 PERMISSION DENIED for an administrator-only one (ahelp) to anyone who is not a wizard or royalty.

  textsearch() returns the names of all topics whose contents matches the given `<pattern>`, the same as "help/search `<pattern>`", with topic names separated by `<osep>`.

- [textfile examples]: listing, searching and reading help topics

## Examples

  Example:
```sharp
say textentries(help, ?who())
You say, "CWHO() LWHO() MWHO() NWHO() XWHO() ZWHO()"
```

    > say textsearch(help, pronouns, |)<br>
    You say, "1.6.0P0|GENDER|SEX"

    > say textfile(help, ln\(\))<br>
    You say, "  ln(`<number>`)

      Returns the natural log of `<number>`.


::: seealso
- [LOG()]
    "
:::

::: seealso
- [WILDCARDS]
:::
