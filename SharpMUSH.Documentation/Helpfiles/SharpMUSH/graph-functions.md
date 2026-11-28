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
      "id": "link-function-examples",
      "heading": "Link function examples",
      "lookup": "graph functions link function examples"
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

The graph functions walk a tree or a graph that you keep in attributes, or in anything else softcode can read, such as rooms and their exits. You give an attribute that says what a node links to; the function walks, keeping the visited set, the order and any loop it finds.

- [GRAPH_REACH()]: every node the starts lead to, in the order you name, shaped as you name.
- [GRAPH_PATH()]: the shortest way from one node to another, by steps or by cost.
- [GRAPH_FOLD()]: a result for each node built from its children's results, such as a total or a drawing.
- [GRAPH_COMPONENTS()]: the separate groups a graph's links join, such as nodes nothing links to.
- [JSON_WALK()]: every node of a JSON tree, under whatever key holds its children.

Each `graph_` function takes `[<obj>/]<link-attr>` first, as [MAP()] takes its attribute, then the nodes, then what that function needs, then option words, then `<delimiter>` and `<osep>` as list functions do. [JSON_WALK()] takes the attribute that makes each node's result, then the JSON.

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_reach(me/FUN`NEEDS,WHIRL)
DUAL RIPOSTE SWORDS PARRY
```

::: seealso
- [graph functions link function]
- [graph functions link function examples]
- [graph functions keeping a graph]
- [graph functions limits]
- [ATTRIBUTE TREES]
- [TREE()]
:::

## The link function

`[<obj>/]<link-attr>` is an attribute, or a `#lambda` (see [anonymous attributes]). It gets a node as %0 and returns the nodes it links to, as a list split by `<delimiter>` (a space unless you give one). It runs as [U()] would: as the attribute's owner, with the caller's q-registers.

Nodes are text, compared ignoring case; the output keeps the first spelling seen. A dbref is a node like any other, so rooms, objects and players can be walked. A node's links are read once, however many times the walk reaches it.

A result that starts with `#-1` stops the walk, and the graph function returns it, so an error is never read as nodes.

```sharp
> &FUN`BAD me=#-1 NO MAP HERE
> think graph_reach(me/FUN`BAD,%l)
#-1 NO MAP HERE
```

For more, see [graph functions link function examples].

## Link function examples

Rooms and their exits: a room links to where each of its exits leads.

```sharp
> &FUN`EXITS me=iter(lexits(%0),loc(%i0))
> &FUN`NAME me=name(%0)
> think graph_reach(me/FUN`EXITS,%l,bfs starts,,,%b,,me/FUN`NAME)
Square Keep Wharf
```

Leave out a way someone may not use: read who is travelling from a q-register. Here the Keep Gate, from the Wharf (#44) to the Keep (#47), is locked, so the way goes back through the Square:

```sharp
> &FUN`OPEN me=iter(lexits(%0),if(elock(%i0,%q<mover>),loc(%i0)))
> think [setq(mover,%#)][map(me/FUN`NAME,graph_path(me/FUN`OPEN,#44,#47,starts))]
Wharf Square Keep
```

Names with spaces: return the links split by a delimiter other than a space, and pass it as `<delimiter>`.

```sharp
> &FUN`TOWN me=switch(%0,Old Town,North Gate|Market Square,North Gate,Old Town)
> think graph_reach(me/FUN`TOWN,Old Town,bfs starts,,|,%,%b)
Old Town, North Gate, Market Square
```

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

Every function call inside the link attribute, a format or a cost counts toward the function invocation limit, as in any [U()]; the walk itself counts nothing and does not recurse, so depth is no limit. A link attribute that is one `get()` walks about 24,000 nodes in one command at the default limit of 25,000.
