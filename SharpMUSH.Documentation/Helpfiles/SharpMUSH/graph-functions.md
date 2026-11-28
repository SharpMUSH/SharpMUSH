<!-- help-article
{
  "corpus": "help",
  "id": "graph-functions",
  "lookup": "graph functions",
  "aliases": [
    "GRAPHS",
    "TREES"
  ],
  "sections": [
    {
      "id": "link-function",
      "heading": "The link function",
      "lookup": "graph functions link function"
    },
    {
      "id": "keeping-a-graph",
      "heading": "Keeping a graph",
      "lookup": "graph functions keeping a graph"
    },
    {
      "id": "limits",
      "heading": "Limits",
      "lookup": "graph functions limits"
    }
  ]
}
-->
# Graph Functions

The graph functions walk a tree or a graph that you keep in attributes, or in anything else softcode can read. You name a function that says what a node links to; the function walks, keeping the visited set, the order and any loop it finds.

- [GRAPH_REACH()]: every node the starts lead to, in the order you name, shaped as you name.
- [GRAPH_PATH()]: the shortest way from one node to another, by steps or by cost.
- [GRAPH_FOLD()]: a result for each node built from its children's results, such as a total or a drawing.
- [GRAPH_COMPONENTS()]: the separate groups a graph's links join, such as nodes nothing links to.
- [JSON_WALK()]: every node of a JSON tree, under whatever key holds its children.

Each takes the link function first, as `map()` takes its attribute, then the nodes, then what that function needs, then option words, then `<delimiter>` and `<output separator>` as list functions do.

::: seealso
- [graph functions link function]
- [graph functions keeping a graph]
- [graph functions limits]
- [ATTRIBUTE TREES]
- [TREE()]
:::

## The link function

The link function is an `<object>/<attribute>` or a `#lambda`. It gets a node as %0 and returns the nodes it links to, as a list split by `<delimiter>` (a space unless you give one). It runs as [U()] would: as the attribute's owner, with the caller's q-registers, so a link function can read who is travelling from `%q<mover>` and leave out a way they may not use.

Nodes are text, compared ignoring case; the output keeps the first spelling seen. A node's links are read once, however many times the walk reaches it.

A result that starts with `#-1` stops the walk, and the graph function returns it, so an error is never read as nodes.

## Keeping a graph

Keep each node as a branch of an attribute tree, with its links in one leaf:

```sharp
&SKILL`WHIRL`NEEDS me=DUAL RIPOSTE
&SKILL`RIPOSTE`NEEDS me=PARRY
&SKILL`PARRY`NEEDS me=SWORDS
&SKILL`DUAL`NEEDS me=SWORDS
&FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
```

Other fields of a node sit beside its links (``SKILL`WHIRL`COST``), and a link with fields of its own is a branch under its node (``WAY`HARBOR`TO`DOCKS`COST``), which `lattr()` lists:

```sharp
&FUN`WAYS me=iter(lattr(%!/WAY`%0`TO`*),last(%i0,`))
```

## Limits

Every function call inside the link function, a format or a cost counts toward the function invocation limit, as in any [U()]; the walk itself counts nothing and does not recurse, so depth is no limit. A link function that is one `get()` walks about 24,000 nodes in one command at the default limit of 25,000.
