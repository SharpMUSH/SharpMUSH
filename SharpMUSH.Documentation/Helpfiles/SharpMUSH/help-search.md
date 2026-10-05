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
# help search

`help <textname>`<br>
`help <namepattern>`<br>
`help/search <pattern>`<br>
`help/query[/brief] <sqlite3query>`<br>

These commands and switches also work with other things using our helpfile setup: news, ahelp, and any others added.

help `<textname>` is how to see a help file.
```sharp
  > help helpfile
  > help @search
```

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

help/query is a more advanced and complex way to search our helpfiles, see [help search content search] for more.

To add more categories and commands (like 'news'), read "game/txt/README"


::: seealso
- [TEXTFILE()] — `textfile()` reads an entry, `textentries()` lists matching topic names, and `textsearch()` searches entry content.
:::

## Content search

Displays help entries that match `<pattern>` using an advanced full text search. If the /brief switch is given, only shows the names of matching entries. Without it, also shows a fragment of the entry with matches underlined.

The pattern is one or more phrases, where each phrase is one or more words (enclosed with quote marks if there's more than one word, or searching for a word with characters like @ or +). A * at the end treats the phrase as a prefix. Phrases can be combined with AND (The default), OR and NOT. The function NEAR(phrase phrase...) matches entries where two or more phrases are near each other. An optional second argument controls how close the matching is. All these operators must appear in upper case. For full details, see https://www.sqlite.org/fts5.html#full_text_query_syntax


Examples:
```sharp
  help/query cosine OR tangent
  help/query "treated as"
  help/query NEAR(player wizard, 20)
  help/query "@attrib"*
```
