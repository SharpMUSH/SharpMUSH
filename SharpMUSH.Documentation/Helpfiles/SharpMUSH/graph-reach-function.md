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

`graph_reach([<obj>/]<link-attr>, <starts>[, <options>[, <depth>[, <delimiter>[, <osep>[, <lsep>[, [<obj>/]<format-attr>[, <arg>...]]]]]]])`

graph_reach() returns every node that the nodes in `<starts>` lead to. `<link-attr>` is called with a node as %0 and returns its links (see [graph functions link function]). Each `<arg>` is passed on as described in [graph functions extra arguments].

- `<options>`: option words. One order: `bfs` (the default), `dfs`, `post` or `layers`; see [graph_reach() orders]. Add `starts` to list the start nodes too; otherwise only nodes a link reaches are listed.
- `<depth>`: the most links to follow from a start, counted along the shortest route. Leave it empty for no limit.
- `<delimiter>`: separates `<starts>` and what `<link-attr>` returns. A space by default.
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

The skills are set up in [graph functions keeping a graph].

::: seealso
- [GRAPH FUNCTIONS]
- [graph_reach() examples]
- [graph_reach() every node]
- [graph_reach() loops]
- [GRAPH_PATH()]
- [GRAPH_FOLD()]
- [GRAPH_COMPONENTS()]
:::

## Orders

- `bfs` (breadth first): the nearest nodes first, one level at a time.
- `dfs` (depth first): a node, then everything below it, then its next sibling.
- `post`: everything below a node, then the node. For skills, this is an order to learn them in.
- `layers`: nodes with nothing below them first, then the nodes above those, and so on.

Within a level, nodes keep the order the link attribute returned them in.

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

`<osep>` goes between nodes, a space by default. With `bfs` and `layers`, `<lsep>` goes between levels instead. If `<lsep>` is empty, `<osep>` is used.

`[<obj>/]<format-attr>` is called once for each node, and its result is listed in place of the node. It gets:
- %0: the node.
- %1: its level. A start is level 0. With `layers`, it is how many levels lie below the node.
- %2: the node it was reached from, or nothing for a start.

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

To draw a tree with guide lines, see [GRAPH_FOLD()].

## Examples

The rooms one exit away, then every room that can be reached, by name:

```sharp
> &FUN`EXITS me=iter(lexits(%0),loc(%i0))
> &FUN`NAME me=name(%0)
> think graph_reach(me/FUN`EXITS,%l,bfs,1,,%b,,me/FUN`NAME)
Keep Wharf
> think graph_reach(me/FUN`EXITS,%l,bfs starts,,,%b,,me/FUN`NAME)
Square Keep Wharf
```

Where each faction was reached from, using %2:

```sharp
> &FUN`ROUTE me=%0<%2
> think graph_reach(me/FUN`SUBS,CROWN,bfs starts,,,%b,,me/FUN`ROUTE)
CROWN< GUARD<CROWN COURT<CROWN NIGHTWATCH<GUARD
```

The total cost of learning WHIRL, counting each skill once:

```sharp
> &FUN`SKILLCOST me=get(%!/SKILL`%0`COST)
> think ladd(map(me/FUN`SKILLCOST,graph_reach(me/FUN`NEEDS,WHIRL,bfs starts)))
13
```

## Every node

To list every node of a graph, including nodes with no links, pass all of them as starts and add `starts`. Each node is listed once:

```sharp
> &SKILL`COOKING`COST me=1
> think graph_reach(me/FUN`NEEDS,iter(lattr(%!/SKILL`*),last(%i0,`)),bfs starts)
COOKING DUAL PARRY RIPOSTE SWORDS WHIRL
```

To group the nodes by the links that join them, see [GRAPH_COMPONENTS()].

## Loops

`bfs` and `dfs` list each node once and carry on past a loop. Without `starts`, a start node is listed only when a link leads back to it. So `member(graph_reach(<link-attr>,<node>),<node>)` is more than 0 when `<node>` is on a loop.

`post` and `layers` refuse a loop, because a node on a loop is below itself and can't come after everything below it. They return `#-1 CYCLE` and the loop's nodes, joined by `<osep>`:

```sharp
> &FUN`TOWN me=switch(%0,Old Town,North Gate|Market Square,North Gate,Old Town)
> think graph_reach(me/FUN`TOWN,Old Town,post starts,,|,/)
#-1 CYCLE Old Town/North Gate/Old Town
```

Errors: `#-1 NO SUCH OPTION`, `#-1 CONFLICTING OPTIONS` (two orders), `#-1 ARGUMENT MUST BE NON-NEGATIVE INTEGER` (`<depth>`), `#-1 CYCLE <nodes>`, and any `#-1` the link attribute returns.
