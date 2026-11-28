<!-- help-article
{
  "corpus": "help",
  "id": "graph-fold-function",
  "lookup": "graph_fold()",
  "aliases": [
    "graph_fold"
  ],
  "sections": [
    {
      "id": "a-value-beside-the-result",
      "heading": "A value beside the result",
      "lookup": "graph_fold() a value beside the result"
    },
    {
      "id": "down",
      "heading": "Down",
      "lookup": "graph_fold() down"
    }
  ]
}
-->
# graph_fold()

`graph_fold(<links>, <root>, <format>[, <combine>[, <options>[, <delimiter>[, <osep>]]]])`

A result for `<root>` built from its children's results, which are built from theirs, through `<links>`, a link function (see [graph functions link function]). `<format>`, an attribute or `#lambda`, makes each node's result: %0 is the node and %1 its children's results, joined by `<osep>` (a space by default). The output is the root's result.

A node under two parents is folded once, and its result goes to both; a total over such a graph counts it under each. To add up everything a node leads to once each, use `ladd()` over [GRAPH_REACH()].

```sharp
> &FUN`SUBS me=get(%!/FACTION`%0`SUBS)
> &FUN`COUNT me=add(words(get(%!/FACTION`%0`MEMBERS)),ladd(%1,|))
> think graph_fold(me/FUN`SUBS,CROWN,me/FUN`COUNT,,,,|)
7
> think graph_fold(me/FUN`SUBS,GUARD,me/FUN`COUNT,,,,|)
5
```

Here the Crown has 1 member, the Guard 2, the Night Watch below the Guard 3 and the Court 1.

`<options>` is `up` (the default) or `down`; see [graph_fold() down]. A loop is refused: `#-1 CYCLE <nodes>`, joined by `<osep>`.

::: seealso
- [GRAPH FUNCTIONS]
- [graph_fold() a value beside the result]
- [TREE()]
- [LNODE()]
:::

## A value beside the result

`<combine>`, an attribute or `#lambda`, carries a second value up beside the result. It runs on each node before `<format>`, with %0 the node and %1 its children's values joined by `<osep>`; `<format>` gets the node's value as %2. So one walk gives a drawing with totals in it:

```sharp
> &FUN`DRAW me=lnode(%0 %(%2%),%1,|)
> think tree({{"width":40}},graph_fold(me/FUN`SUBS,CROWN,me/FUN`DRAW,me/FUN`COUNT,,,|))
CROWN (7)
|- GUARD (5)
|  `- NIGHTWATCH (3)
`- COURT (1)
```

Each node is visited once. Working the total out inside `<format>` instead, with a fold of its own, walks each node's subtree again for every node above it.

## Down

With `down`, parents come first: %1 is the results of all of a node's parents, and a node waits until every parent has its result. The output is every node's result in that order, joined by `<osep>`, without the root's unless `<options>` also has `starts`.

```sharp
> &FUN`RANK me=if(%1,inc(%1),1)
> think graph_fold(me/FUN`SUBS,CROWN,#lambda/\%0=\%2,me/FUN`RANK,down starts)
CROWN=1 GUARD=2 COURT=2 NIGHTWATCH=3
```
