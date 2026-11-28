<!-- help-article
{
  "corpus": "help",
  "id": "graph-components-function",
  "lookup": "graph_components()",
  "aliases": [
    "graph_components"
  ],
  "sections": [
    {
      "id": "every-node",
      "heading": "Every node",
      "lookup": "graph_components() every node"
    }
  ]
}
-->
# graph_components()

`graph_components(<links>, <nodes>[, <delimiter>[, <osep>[, <gsep>]]])`

Splits a graph into the groups its links join, through `<links>`, a link function (see [graph functions link function]). Two nodes share a group when a chain of links joins them, whichever way the links point: if A and B both link to C, the three are one group.

The groups hold `<nodes>` and every node they lead to, so list every node; see [graph_components() every node]. Groups come in the order of their first node, and a group's nodes in the order the walk met them.

- `<delimiter>`: splits `<nodes>` and what the link function returns. A space by default.
- `<osep>`: goes between the nodes of a group. A space by default.
- `<gsep>`: goes between groups. `|` by default, and when empty.

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_components(me/FUN`NEEDS,WHIRL SWORDS PARRY DUAL RIPOSTE COOKING)
WHIRL SWORDS PARRY DUAL RIPOSTE|COOKING
```

COOKING needs nothing and nothing needs it, so it is a group of its own: a node with no links either way. `words(<result>,|)` counts the groups. A loop is a group like any other. Errors: any `#-1` the link function returns.

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_REACH()]
- [graph_components() every node]
:::

## Every node

With the skills kept as in [graph functions keeping a graph], `lattr()` lists the nodes. SWORDS has no attribute of its own, so `lattr()` leaves it out, but WHIRL's group reaches it through links:

```sharp
> &SKILL`COOKING`COST me=1
> think graph_components(me/FUN`NEEDS,iter(lattr(%!/SKILL`*),last(%i0,`)),,%b,%r)
COOKING
DUAL PARRY RIPOSTE WHIRL SWORDS
```
