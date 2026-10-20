<!-- help-article
{
  "corpus": "help",
  "id": "help-search",
  "lookup": "help search",
  "aliases": [
    "help/search",
    "helpfile"
  ],
  "sections": [
    {
      "id": "patterns",
      "heading": "Name patterns and text search",
      "lookup": "help search patterns"
    },
    {
      "id": "content-search",
      "heading": "Content search",
      "lookup": "help search content search",
      "aliases": [
        "help query"
      ]
    }
  ],
  "redirects": {
    "helpfile2": "help search content search"
  }
}
-->
# Help Search

`help <textname>`<br>
`help <namepattern>`<br>
`help/search <text>`<br>

These commands and switches also work with other things using our helpfile setup: news, ahelp, and any others added.

help `<textname>` is how to see a help file.
```sharp
  > help helpfile
  > help @search
```

- [help search patterns]: finding entries by a name pattern, or by text they contain

help/search lists the entries whose text contains a string, see [help search content search] for more.


::: seealso
- [TEXTFILE()]: `textfile()` reads an entry, `textentries()` lists matching topic names, and `textsearch()` searches entry content.
:::

## Name patterns and text search

help `<namepattern>` is a good way to read long helpfilenames or find a pattern you're looking for. If `<namepattern>` matches multiple entries, it will show you their names.
```sharp
  > help @tri*2
  > help sort*()
```

If you want to find helpfiles that _contain_ a pattern, use "help/search". For example:
```sharp
  > help/search @trigger
  > help/search @switch
```

## Content search

Lists the names of help entries whose text contains `<text>`, as `Matches: <names>`, or `No matches.` when none does. The match is a plain substring match that ignores case: wildcards, quote marks and words like AND or OR are matched as literal text.

Examples:
```sharp
  > help/search cosine
  > help/search treated as
```
