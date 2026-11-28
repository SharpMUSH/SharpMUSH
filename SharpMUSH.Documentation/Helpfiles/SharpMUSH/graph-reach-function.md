<!-- help-article
{
  "corpus": "help",
  "id": "graph-reach-function",
  "lookup": "graph_reach()",
  "aliases": [
    "graph_reach"
  ],
  "sections": [
    {
      "id": "orders",
      "heading": "Orders",
      "lookup": "graph_reach() orders"
    },
    {
      "id": "shaping-the-output",
      "heading": "Shaping the output",
      "lookup": "graph_reach() shaping the output"
    },
    {
      "id": "loops",
      "heading": "Loops",
      "lookup": "graph_reach() loops"
    }
  ]
}
-->
# graph_reach()

`graph_reach(<links>, <starts>[, <options>[, <depth>[, <delimiter>[, <osep>[, <lsep>[, <format>]]]]]])`

Every node the `<starts>` lead to through `<links>`, a link function (see [graph functions link function]).

- `<options>`: words. One order, `bfs` (the default), `dfs`, `post` or `layers`; see [graph_reach() orders]. `starts` lists the start nodes too; without it, only the nodes a link reaches are listed.
- `<depth>`: how many links from a start, counting the fewest; empty for no limit.
- `<delimiter>`: splits `<starts>` and what the link function returns. A space by default.
- `<osep>`, `<lsep>` and `<format>`: see [graph_reach() shaping the output].

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_reach(me/FUN`NEEDS,WHIRL)
DUAL RIPOSTE SWORDS PARRY
> think graph_reach(me/FUN`NEEDS,WHIRL,bfs,1)
DUAL RIPOSTE
```

The skills are those of [graph functions keeping a graph].

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_PATH()]
- [GRAPH_FOLD()]
:::

## Orders

- `bfs`: nearest first, level by level.
- `dfs`: a node, then everything below it, then its next sibling.
- `post`: everything below a node, then the node. For skills, an order to learn them in.
- `layers`: nodes with nothing below them first, then the nodes above those, and so on.

Within a level, nodes keep the order the link function gave them.

```sharp
> think graph_reach(me/FUN`NEEDS,WHIRL,dfs starts)
WHIRL DUAL SWORDS RIPOSTE PARRY
> think graph_reach(me/FUN`NEEDS,WHIRL,post starts)
SWORDS DUAL PARRY RIPOSTE WHIRL
```

## Shaping the output

`<osep>` goes between nodes, a space by default. With `bfs` and `layers`, `<lsep>` goes between levels instead; empty, it is `<osep>`. `<format>`, an attribute or `#lambda`, runs once for each node and its result is listed in the node's place: %0 is the node, %1 its level (a start is 0; with `layers`, how far below it the walk goes), %2 the node it was reached from (empty for a start).

```sharp
> &FUN`SUBS me=get(%!/FACTION`%0`SUBS)
> think graph_reach(me/FUN`SUBS,CROWN,bfs starts,,,%b,%r)
CROWN
GUARD COURT
NIGHTWATCH
> &FUN`INDENT me=[space(mul(2,%1))]%0
> think graph_reach(me/FUN`SUBS,CROWN,dfs starts,,,%r,,me/FUN`INDENT)
CROWN
  GUARD
    NIGHTWATCH
  COURT
```

For a tree drawn with guide lines, see [GRAPH_FOLD()].

## Loops

`bfs` and `dfs` list each node once and go on past a loop. A start is listed without `starts` only when a link reaches it, so `member(graph_reach(<links>,<node>),<node>)` is more than 0 when `<node>` is on a loop.

`post` and `layers` refuse a loop, since "everything below first" cannot hold around one, and name it:

```sharp
> think graph_reach(me/FUN`QNEEDS,FIRSTSTEPS,post starts)
#-1 CYCLE FIRSTSTEPS VAULT LANTERN FIRSTSTEPS
```

Errors: `#-1 NO SUCH OPTION`, `#-1 CONFLICTING OPTIONS` (two orders), `#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER` (`<depth>`), `#-1 CYCLE <nodes>`, and any `#-1` the link function returns.
