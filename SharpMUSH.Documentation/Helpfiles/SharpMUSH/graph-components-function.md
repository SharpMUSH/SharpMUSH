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
      "id": "examples",
      "heading": "Examples",
      "lookup": "graph_components() examples"
    },
    {
      "id": "every-node",
      "heading": "Every node",
      "lookup": "graph_components() every node"
    }
  ]
}
-->
# graph_components()

`graph_components([<obj>/]<link-attr>, <nodes>[, <delimiter>[, <osep>[, <gsep>[, <arg>...]]]])`

Splits a graph into the groups its links join. `<link-attr>` gets a node as %0 and returns its links (see [graph functions link function]). Each `<arg>` is passed on as in [graph functions extra arguments]. Two nodes share a group when a chain of links joins them, whichever way the links point: if A and B both link to C, the three are one group.

The groups hold `<nodes>` and every node they lead to, so list every node; see [graph_components() every node]. Groups come in the order of their first node, and a group's nodes in the order the walk met them.

- `<delimiter>`: splits `<nodes>` and what the link attribute returns. A space by default.
- `<osep>`: goes between the nodes of a group. A space by default.
- `<gsep>`: goes between groups. `|` by default, and when empty.

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_components(me/FUN`NEEDS,WHIRL SWORDS PARRY DUAL RIPOSTE COOKING)
WHIRL SWORDS PARRY DUAL RIPOSTE|COOKING
```

COOKING needs nothing and nothing needs it, so it is a group of its own: a node with no links either way. `words(<result>,|)` counts the groups when no node name holds a `|`. Errors: any `#-1` the link attribute returns.

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_REACH()]
- [graph_components() examples]
- [graph_components() every node]
:::

## Examples

Which rooms are cut off from the rest: the Square (#42), the Wharf (#44) and the Keep (#47) are joined by exits, and so are the Island (#50) and the Cove (#51), but no exit joins the two groups.

```sharp
> &FUN`EXITS me=iter(lexits(%0),loc(%i0))
> &FUN`NAMES me=iter(%0,name(%i0))
> think map(me/FUN`NAMES,graph_components(me/FUN`EXITS,#42 #44 #47 #50 #51),|,|)
Square Wharf Keep|Island Cove
> think words(graph_components(me/FUN`EXITS,#42 #44 #47 #50 #51),|)
2
```

Every faction and its subfactions are one group: the count is 1 when the whole graph hangs together. A `#-1` from the link attribute also counts as 1, so check for one first, as with `strmatch(<result>,#-1*)`.

## Every node

`lattr()` lists every skill kept as in [graph functions keeping a graph], with a ``COST`` leaf on each:

```sharp
> &SKILL`COOKING`COST me=1
> think graph_components(me/FUN`NEEDS,iter(lattr(%!/SKILL`*),last(%i0,`)),,%b,%r)
COOKING
DUAL PARRY RIPOSTE SWORDS WHIRL
```

A node `lattr()` misses is still found when a listed node links to it.
