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
      "id": "extra-arguments",
      "heading": "Extra arguments",
      "lookup": "graph functions extra arguments"
    },
    {
      "id": "keeping-a-graph",
      "heading": "Keeping a graph",
      "lookup": "graph functions keeping a graph"
    },
    {
      "id": "json-graphs",
      "heading": "JSON graphs",
      "lookup": "graph functions json graphs"
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

The graph functions walk a tree or a graph that you keep in attributes, in JSON, or in anything else softcode can read, such as rooms and their exits. You give an attribute that says what a node links to, or a JSON graph; the function walks, keeping the visited set, the order and any loop it finds.

- [GRAPH_REACH()]: every node the starts lead to, in the order you name, shaped as you name.
- [GRAPH_PATH()]: the shortest way from one node to another, by steps or by cost.
- [GRAPH_FOLD()]: a result for each node built from its children's results, such as a total or a drawing.
- [GRAPH_COMPONENTS()]: the separate groups a graph's links join, such as nodes nothing links to.
- [JSON_WALK()]: every node of a JSON tree, under whatever key holds its children.

The same walks over a JSON graph (see [graph functions json graphs]): [JSON_GRAPH_REACH()], [JSON_GRAPH_PATH()], [JSON_GRAPH_FOLD()] and [JSON_GRAPH_COMPONENTS()]. [GRAPH_JSON()] writes the graph a link attribute describes as JSON, and [JSON_TREE_GRAPH()] turns a JSON tree into one.

::: seealso
- [graph functions link function]
- [graph functions link function examples]
- [graph functions extra arguments]
- [graph functions keeping a graph]
- [graph functions json graphs]
- [graph functions limits]
- [ATTRIBUTE TREES]
- [TREE()]
:::

## The link function

Each `graph_` function takes `[<obj>/]<link-attr>` first, as [MAP()] takes its attribute, then the nodes, then what that function needs, then option words, then `<delimiter>` and `<osep>` as list functions do, then any extra arguments (see [graph functions extra arguments]). [JSON_WALK()] takes the attribute that makes each node's result, then the JSON.

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_reach(me/FUN`NEEDS,WHIRL)
DUAL RIPOSTE SWORDS PARRY
```

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

Names with spaces: return the links split by a delimiter other than a space, and pass it as `<delimiter>`.

```sharp
> &FUN`TOWN me=switch(%0,Old Town,North Gate|Market Square,North Gate,Old Town)
> think graph_reach(me/FUN`TOWN,Old Town,bfs starts,,|,%,%b)
Old Town, North Gate, Market Square
```

## Extra arguments

Arguments after a graph function's last one are passed on to the attributes it calls, after their own: %1 onward for `<link-attr>`, %3 onward for a format, %2 onward for a cost or a combine. [JSON_WALK()] passes them as %4 onward.

Skip exits a player can't pass: pass the player, and have the link attribute leave out each exit whose lock %1 fails. Here the Keep Gate, from the Wharf (#44) to the Keep (#47), is locked, so the path goes back through the Square:

```sharp
> &FUN`OPEN me=iter(lexits(%0),if(elock(%i0,%1),loc(%i0)))
> think map(me/FUN`NAME,graph_path(me/FUN`OPEN,#44,#47,starts,,,%b,%#))
Wharf Square Keep
```

Every argument before them must be given, even empty; an empty `<osep>` is empty, so give `%b` for a space.

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

## JSON graphs

A JSON graph is an object with a key for each node, holding a list of the nodes it links to:

```sharp
> &GRAPH`LOOP me={"A":["B"],"B":["C"],"C":["A"]}
> think json_graph_reach(v(GRAPH`LOOP),A,bfs starts)
A B C
```

A link with fields of its own is written as an object instead of a list, its keys the nodes linked to: `{"HARBOR":{"DOCKS":{"cost":1}}}`. Each node appears once, so a loop is a link back to a key already there, handled as in [graph_reach() loops]. Keys compare ignoring case, a link to a node with no key or a `null` one links nowhere, and anything else is `#-1 BAD ARGUMENT FORMAT`. Walking a JSON graph calls no attribute, so it counts nothing toward the invocation limit.

## Limits

Every function call inside the link attribute, a format or a cost counts toward the function invocation limit, as in any [U()]; the walk itself counts nothing and does not recurse, so depth is no limit. A link attribute that is one `get()` walks about 24,000 nodes in one command at the default limit of 25,000.
