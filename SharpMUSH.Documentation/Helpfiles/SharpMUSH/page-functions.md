<!-- help-article
{
  "corpus": "help",
  "id": "page-functions",
  "lookup": "page functions",
  "aliases": [],
  "sections": [
    {
      "id": "pagerecall",
      "heading": "pagerecall()",
      "lookup": "pagerecall()",
      "aliases": []
    },
    {
      "id": "pageconversations",
      "heading": "pageconversations()",
      "lookup": "pageconversations()",
      "aliases": []
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "page functions examples"
    }
  ]
}
-->
# Page functions

These functions read your own page log, the same reads as `page/recall` and `page/conversations` (see [page recall]). They are SharpMUSH extensions: PennMUSH keeps no page log. They only read: they send nothing and trigger nothing. While the `page_log` @config option is off, both return `#-1 PAGE LOGGING IS OFF`.

## pagerecall()

`pagerecall(<player list>[, <lines>[, <delimiter>]])`

- **pagerecall()**: Your last *\<lines\>* logged pages with the players in *\<player list\>*, oldest first, each as `page/recall` shows it but without a timestamp, joined by *\<delimiter\>* (`%r` by default). The players are matched as `page/recall` matches them (see [page recall]): as `page` matches recipients, then among the people in your own conversations by full objid or by the name your log gave them, so every list [pageconversations()] returns recalls its conversation. A list of several is the group conversation with exactly those players. An empty *\<player list\>* is your latest pages across all your conversations. *\<lines\>* is 10 unless given, and at most 500; 0 returns as many as that. It returns nothing when there are no pages, `#-1 NO SUCH PLAYER` or `#-2 I DON'T KNOW WHICH ONE YOU MEAN` for a name that matches no one, or more than one connected player or logged partner, and `#-1 ARGUMENT MUST BE INTEGER` for a *\<lines\>* that is not a whole number of 0 or more.

## pageconversations()

`pageconversations([<delimiter>])`

- **pageconversations()**: Your logged page conversations, the most recent first (at most 100), separated by *\<delimiter\>* (`|` by default). Each conversation is the objids of the others in it, separated by spaces, so it can be passed straight to [pagerecall()]. Objids, not names, so a conversation still recalls after someone in it is renamed, and never matches a newer player who took a recycled dbref. A conversation with yourself is your own objid. It returns nothing when you have no logged conversations.

## Examples

```sharp
> think pagerecall(Airwolf,2)
You paged Airwolf with 'hi there!'
Airwolf pages: hello!
> think pageconversations()
#12:1790888816538|#12:1790888816538 #44:1790888816839
> think pagerecall(first(pageconversations(),|),1)
Airwolf pages: hello!
```

::: seealso
- [page]
- [page log]
- [crecall()]
:::
