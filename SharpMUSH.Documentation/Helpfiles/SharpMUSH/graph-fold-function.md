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
      "id": "examples",
      "heading": "Examples",
      "lookup": "graph_fold() examples"
    },
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

`graph_fold([<obj>/]<link-attr>, <root>, [<obj>/]<format-attr>[, [<obj>/]<combine-attr>[, <options>[, <delimiter>[, <osep>[, <arg>...]]]]])`

graph_fold() builds a result for `<root>` from the results of its children, whose results are built from their own children's, and so on down. `<link-attr>` is called with a node as %0 and returns its children (see [graph functions link function]). Each `<arg>` is passed on as described in [graph functions extra arguments].

`<format-attr>` makes each node's result. It gets the node as %0, and its children's results as %1, joined by `<osep>` (a space by default). graph_fold() returns the root's result.

- `<combine-attr>`: a second value passed up beside the result; see [graph_fold() a value beside the result].
- `<options>`: `up` (the default) or `down`; see [graph_fold() down].
- `<delimiter>`: separates what the link attribute returns. A space by default.

```sharp
> &FUN`SUBS me=get(%!/FACTION`%0`SUBS)
> &FUN`COUNT me=add(words(get(%!/FACTION`%0`MEMBERS)),ladd(%1,|))
> think graph_fold(me/FUN`SUBS,CROWN,me/FUN`COUNT,,,,|)
7
```

The CROWN has 1 member, the GUARD 2, the NIGHTWATCH below the GUARD 3, and the COURT 1. A loop is refused with `#-1 CYCLE` and the loop's nodes, joined by `<osep>`.

::: seealso
- [GRAPH FUNCTIONS]
- [graph_fold() examples]
- [graph_fold() a value beside the result]
- [TREE()]
- [LNODE()]
:::

## Examples

A tree drawing: [LNODE()] gives each node its line, with its children's lines below it, and [TREE()] draws the guide lines.

```sharp
> &FUN`BRANCH me=lnode(%0,%1,|)
> think tree({{"width":40}},graph_fold(me/FUN`SUBS,CROWN,me/FUN`BRANCH,,,,|))
CROWN
|- GUARD
|  `- NIGHTWATCH
`- COURT
```

The total cost of a skill and everything below it, with the costs in [graph functions keeping a graph]:

```sharp
> &FUN`TOTAL me=add(get(%!/SKILL`%0`COST),ladd(%1))
> think graph_fold(me/FUN`NEEDS,WHIRL,me/FUN`TOTAL)
14
```

SWORDS is below both DUAL and PARRY. It is folded once, but its result goes to both, so the total counts it twice. To add up each node once, use `ladd()` over [GRAPH_REACH()], which gives 13.

## A value beside the result

`[<obj>/]<combine-attr>` passes a second value up beside the result. It runs on each node before `<format-attr>`, with the node as %0 and its children's values as %1, joined by `<osep>`. `<format-attr>` then gets the node's value as %2. This way one walk can draw a tree with totals in it:

```sharp
> &FUN`DRAW me=lnode(%0 %(%2%),%1,|)
> think tree({{"width":40}},graph_fold(me/FUN`SUBS,CROWN,me/FUN`DRAW,me/FUN`COUNT,,,|))
CROWN (7)
|- GUARD (5)
|  `- NIGHTWATCH (3)
`- COURT (1)
```

Each node is visited once. Working out the total inside `<format-attr>` with a second fold would walk each node's subtree again for every node above it.

## Down

With `down`, parents come first. %1 holds the results of all of a node's parents, and a node waits until every parent has its result. graph_fold() returns every node's result in that order, joined by `<osep>`. The root's result is left out unless `<options>` also has `starts`.

```sharp
> &FUN`RANK me=if(%1,inc(%1),1)
> &FUN`SHOWRANK me=%0=%2
> think graph_fold(me/FUN`SUBS,CROWN,me/FUN`SHOWRANK,me/FUN`RANK,down starts)
CROWN=1 GUARD=2 COURT=2 NIGHTWATCH=3
```

Each faction's rank is one more than its parent's.
