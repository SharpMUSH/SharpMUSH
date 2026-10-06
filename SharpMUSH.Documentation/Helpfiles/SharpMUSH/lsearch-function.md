<!-- help-article
{
  "corpus": "help",
  "id": "lsearch-function",
  "lookup": "lsearch()",
  "aliases": [
    "NLSEARCH()",
    "SEARCH()",
    "NSEARCH()",
    "LSEARCHR()",
    "CHILDREN()",
    "NCHILDREN()",
    "lsearch"
  ],
  "sections": [
    {
      "id": "evaluation-classes",
      "heading": "Evaluation classes",
      "lookup": "lsearch evaluation classes"
    },
    {
      "id": "search-examples",
      "heading": "Search examples",
      "lookup": "lsearch search examples"
    }
  ],
  "redirects": {
    "LSEARCH2": "lsearch evaluation classes",
    "SEARCH2": "lsearch evaluation classes",
    "LSEARCH3": "lsearch search examples",
    "SEARCH3": "lsearch search examples"
  }
}
-->
# lsearch()

`lsearch(<player>[, ... , <classN>, <restrictionN>])`<br>
`nlsearch(<player>[, ... , <classN>, <restrictionN>])`<br>
`lsearchr(<player>[, ... , <classN>, <restrictionN>])`<br>
`children(<object>)`<br>
`nchildren(<object>)`

This function is similar to the @search command, except it returns just a list of dbref numbers. The function must have at least three arguments. You can specify "all" or `<player>` for the `<player>` field; for mortals, only objects they can examine are included. If you do not want to restrict something, use "none" for `<class>` and `<restriction>`.

The possible `<class>`es and `<restriction>`s are the same as those accepted by @search. lsearch() can accept multiple class/restriction pairs, and applies them in a boolean "AND" fashion, returning only dbrefs that fulfill all restrictions. See [@search] for information about them.

children() is exactly the same as lsearch(`[me|all]`, parent, `<object>`), using "all" for See_All/Search players and "me" for others.

nlsearch(...) and nchildren(...) return the count of results that would be returned by lsearch() or children() with the same args.

## Evaluation classes

If `<class>` is one of the eval classes (EVAL, EEXITS, EROOMS, ETHINGS or EPLAYERS), note that any brackets, percent signs, or other special characters should be escaped, as the code in `<restriction>` will be evaluated twice - once as an argument to lsearch(), and then again for each object looked at in the search. Before the per-object evaluation, the string "##" is replaced with the object dbref.

lsearch() is free unless it includes either an eval-class search or an elock search that contains an eval or indirect lock. Otherwise, it costs find_cost pennies to perform the lsearch.

lsearchr() is like an lsearch() run through revwords(). Results are returned from highest dbref to lowest. search() is an alias for lsearch().


::: seealso
- [@search]
- [@find]
- [LPARENT()]
- [LSTATS()]
:::

## Search examples

lsearch() Examples:

lsearch(all, flags, Wc)                  <-- lists all connected wizards.<br>
lsearch(me, type, room)                  <-- lists all rooms owned by me.<br>
lsearch(me, type, room, flag, W)         <-- lists Wizard rooms owned by me.<br>
lsearch(me, type, room, 100, 200)        <-- same, but only w/db# 100-200<br>
lsearch(all, eplayer, \[eq(money(##),100)\]) <-- lists all players with 100 coins.<br>
lsearch(all, type, player, elock, (FLAG^WIZARD|FLAG^ROYALTY)&!FLAG^IC) ^-- list all wiz and roy players that are not IC.<br>
lsearch(all, type, player, elock, sex:m*) <- lists all players with an @sex beginning with 'm'<br>
lsearch(me, elock, !desc:*)              <-- lists all objects you own that don't have an @desc set
