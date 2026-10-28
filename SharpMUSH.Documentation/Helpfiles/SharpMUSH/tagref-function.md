<!-- help-article
{
  "corpus": "help",
  "id": "tagref-function",
  "lookup": "tagref()",
  "aliases": [
    "tagref",
    "istag()",
    "istag",
    "listtags()",
    "listtags",
    "tagmatch()",
    "tagmatch"
  ],
  "sections": []
}
-->
# tagref()

`tagref(<tag>)`<br>
`istag(<string>)`<br>
`listtags([<object>])`<br>
`tagmatch(<pattern>)`

Functions over the tags [@tag] and `@ltag` keep. A tag is looked up as `#<tag>` is: the world's tag of that name, else one of your own.

- `tagref()` returns the dbref `<tag>` names, or `#-1`. A leading `#` is allowed. RhostMUSH calls it `tag()`; here [tag()] writes HTML.
- `istag()` returns 1 when `<string>` is `#` followed by a tag that names an object, else 0.
- `listtags()` returns every tag as `<tag>|<dbref>`. Given `<object>`, it returns the names of the tags that name it.
- `tagmatch()` returns the names of the tags that match the wildcard `<pattern>`.

`listtags()` and `tagmatch()` need the `config.admin` permission. Each covers the world's tags and your own.

```sharp
> think tagref(weather) [istag(#weather)] [listtags(#42)]
#42 1 weather
```

Output: `weather` names `#42`.

::: seealso
- [@tag]
:::
