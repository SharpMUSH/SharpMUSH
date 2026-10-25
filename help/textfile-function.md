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

textfile() returns the text of an entry in a text file category, such as `help` or `news`, as it is written: its Markdown, headings and blank lines included. Use edit() and squish() on the result if you want it as a list of words rather than for display.

textentries() returns the topic names in `<type>` matching `<pattern>`, separated by `<osep>` (a space by default). `<pattern>` is a wildcard pattern matched against the topic name; use `*` for every topic.

textsearch() returns the names of the topics whose text contains `<pattern>`, ignoring case, separated by `<osep>`. `*` and `?` in `<pattern>` are wildcards.

textfile(), textentries() and textsearch() return `#-1 NO SUCH FILE` for a `<type>` that is not a text file category, and `#-1 PERMISSION DENIED` for an administrator-only one (ahelp) to anyone who is not a wizard or royalty.

- [textfile examples]: listing and reading help topics

## Examples

A pattern's parentheses are escaped, so they reach textentries() rather than being read as a function call:

```sharp
> think textentries(help, ?who\(\))
cwho() lwho() mwho() nmwho() xwho() zwho()
```

```sharp
> think textfile(help, pi\(\))
# pi()
`pi()`

  Returns the value of "pi" (3.14159265358979323846264338327, rounded to the game's float_precision setting).
```

::: seealso
- [WILDCARDS]
- [help]
:::
