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
      "id": "examples",
      "heading": "Examples",
      "lookup": "graph_reach() examples"
    },
    {
      "id": "every-node",
      "heading": "Every node",
      "lookup": "graph_reach() every node"
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

`graph_reach([<obj>/]<link-attr>, <starts>[, <options>[, <depth>[, <delimiter>[, <osep>[, <lsep>[, [<obj>/]<format-attr>]]]]]])`

Every node the `<starts>` lead to through `<link-attr>`, which gets a node as %0 and returns its links (see [graph functions link function]).

- `<options>`: words. One order, `bfs` (the default), `dfs`, `post` or `layers`; see [graph_reach() orders]. `starts` lists the start nodes too; without it, only the nodes a link reaches are listed.
- `<depth>`: how many links from a start, counting the fewest; empty for no limit.
- `<delimiter>`: splits `<starts>` and what `<link-attr>` returns. A space by default.
- `<osep>`, `<lsep>` and `<format-attr>`: see [graph_reach() shaping the output].

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_reach(me/FUN`NEEDS,WHIRL)
DUAL RIPOSTE SWORDS PARRY
> think graph_reach(me/FUN`NEEDS,WHIRL,bfs starts)
WHIRL DUAL RIPOSTE SWORDS PARRY
> think graph_reach(me/FUN`NEEDS,WHIRL,bfs,1)
DUAL RIPOSTE
```

The skills are those of [graph functions keeping a graph]. For more, see [graph_reach() examples].

::: seealso
- [GRAPH FUNCTIONS]
- [graph_reach() examples]
- [graph_reach() every node]
- [GRAPH_PATH()]
- [GRAPH_FOLD()]
- [GRAPH_COMPONENTS()]
:::

## Orders

- `bfs`: nearest first, level by level.
- `dfs`: a node, then everything below it, then its next sibling.
- `post`: everything below a node, then the node. For skills, an order to learn them in.
- `layers`: nodes with nothing below them first, then the nodes above those, and so on.

Within a level, nodes keep the order the link attribute gave them.

```sharp
> think graph_reach(me/FUN`NEEDS,WHIRL,dfs starts)
WHIRL DUAL SWORDS RIPOSTE PARRY
> think graph_reach(me/FUN`NEEDS,WHIRL,post starts)
SWORDS DUAL PARRY RIPOSTE WHIRL
> think graph_reach(me/FUN`NEEDS,WHIRL,layers starts,,,%b,%r)
SWORDS
DUAL PARRY
RIPOSTE
WHIRL
```

## Shaping the output

`<osep>` goes between nodes, a space by default. With `bfs` and `layers`, `<lsep>` goes between levels instead; empty, it is `<osep>`.

`[<obj>/]<format-attr>` runs once for each node and its result is listed in the node's place: %0 is the node, %1 its level (a start is 0; with `layers`, how far below it the walk goes), %2 the node it was reached from (empty for a start).

```sharp
> &FUN`SUBS me=get(%!/FACTION`%0`SUBS)
> think graph_reach(me/FUN`SUBS,CROWN,bfs starts,,,%b,%r)
CROWN
GUARD COURT
NIGHTWATCH
> &FUN`LEVEL me=%0:%1
> think graph_reach(me/FUN`SUBS,CROWN,bfs starts,,,%b,,me/FUN`LEVEL)
CROWN:0 GUARD:1 COURT:1 NIGHTWATCH:2
> &FUN`INDENT me=[space(mul(2,%1))]%0
> think graph_reach(me/FUN`SUBS,CROWN,dfs starts,,,%r,,me/FUN`INDENT)
CROWN
  GUARD
    NIGHTWATCH
  COURT
```

For a tree drawn with guide lines, see [GRAPH_FOLD()].

## Examples

Rooms one exit away, then every room that can be reached, by name:

```sharp
> &FUN`EXITS me=iter(lexits(%0),loc(%i0))
> &FUN`NAME me=name(%0)
> think graph_reach(me/FUN`EXITS,%l,bfs,1,,%b,,me/FUN`NAME)
Keep Wharf
> think graph_reach(me/FUN`EXITS,%l,bfs starts,,,%b,,me/FUN`NAME)
Square Keep Wharf
```

Where each faction was reached from, with %2:

```sharp
> &FUN`ROUTE me=%0<%2
> think graph_reach(me/FUN`SUBS,CROWN,bfs starts,,,%b,,me/FUN`ROUTE)
CROWN< GUARD<CROWN COURT<CROWN NIGHTWATCH<GUARD
```

What learning Whirlwind costs in all, each skill counted once:

```sharp
> &FUN`SKILLCOST me=get(%!/SKILL`%0`COST)
> think ladd(map(me/FUN`SKILLCOST,graph_reach(me/FUN`NEEDS,WHIRL,bfs starts)))
13
```

## Every node

To list every node of a graph, ones with no links included, pass all of them as starts, with `starts`. Each node is listed once:

```sharp
> &SKILL`COOKING`COST me=1
> think graph_reach(me/FUN`NEEDS,iter(lattr(%!/SKILL`*),last(%i0,`)),bfs starts)
COOKING DUAL PARRY RIPOSTE SWORDS WHIRL
```

To group the nodes by which links join them, see [GRAPH_COMPONENTS()].

## Loops

`bfs` and `dfs` list each node once and go on past a loop. A start is listed without `starts` only when a link reaches it, so `member(graph_reach(<link-attr>,<node>),<node>)` is more than 0 when `<node>` is on a loop.

`post` and `layers` refuse a loop, since "everything below first" cannot hold around one, and name it, its nodes joined by `<osep>`:

```sharp
> &FUN`TOWN me=switch(%0,Old Town,North Gate|Market Square,North Gate,Old Town)
> think graph_reach(me/FUN`TOWN,Old Town,post starts,,|,/)
#-1 CYCLE Old Town/North Gate/Old Town
```

Errors: `#-1 NO SUCH OPTION`, `#-1 CONFLICTING OPTIONS` (two orders), `#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER` (`<depth>`), `#-1 CYCLE <nodes>`, and any `#-1` the link attribute returns.
