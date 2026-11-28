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

The graph functions walk a tree or a graph: a set of nodes joined by links. The links can be kept in attributes, in JSON, or in anything softcode can read, such as rooms and their exits. The functions remember which nodes they have visited, so no node is walked twice and a loop cannot run forever.

### Graphs in attributes

- [GRAPH_REACH()]: every node the start nodes lead to, in the order you choose.
- [GRAPH_PATH()]: the shortest path from one node to another, by steps or by cost.
- [GRAPH_FOLD()]: a result for each node built from its children's results, such as a total or a drawing.
- [GRAPH_COMPONENTS()]: the groups of nodes that links join, such as nodes nothing links to.
- [GRAPH_JSON()]: the graph a link attribute describes, written as a JSON graph.

### Graphs in JSON

- [JSON_GRAPH_REACH()], [JSON_GRAPH_PATH()], [JSON_GRAPH_FOLD()] and [JSON_GRAPH_COMPONENTS()]: the same walks over a JSON graph.
- [JSON_TREE_GRAPH()]: turns a JSON tree into a JSON graph that the functions above can walk.
- [JSON_WALK()]: calls an attribute on every node of a JSON tree.

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

Each `graph_` function takes `[<obj>/]<link-attr>` first, as [MAP()] takes its attribute. Then come the nodes, the function's own arguments, then option words, then `<delimiter>` and `<osep>` as in other list functions, then any extra arguments (see [graph functions extra arguments]). [JSON_WALK()] instead takes the attribute that makes each node's result, then the JSON.

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_reach(me/FUN`NEEDS,WHIRL)
DUAL RIPOSTE SWORDS PARRY
```

The link attribute can be an attribute or a `#lambda` (see [anonymous attributes]). It is called with a node as %0 and returns the nodes it links to, separated by `<delimiter>` (a space unless you give one). It runs as [U()] does: as the attribute's owner, with the caller's q-registers.

Nodes are text, compared without regard to case. The output uses the first spelling seen. A dbref is a node like any other, so rooms, objects and players can be walked. Each node's links are read once, however many times the walk reaches it.

If the link attribute returns something that starts with `#-1`, the walk stops and the function returns it, so an error is never read as a list of nodes.

```sharp
> &FUN`BAD me=#-1 NO MAP HERE
> think graph_reach(me/FUN`BAD,%l)
#-1 NO MAP HERE
```

## Link function examples

Rooms and their exits: a room links to the room each of its exits leads to.

```sharp
> &FUN`EXITS me=iter(lexits(%0),loc(%i0))
> &FUN`NAME me=name(%0)
> think graph_reach(me/FUN`EXITS,%l,bfs starts,,,%b,,me/FUN`NAME)
Square Keep Wharf
```

Names with spaces: return the links separated by something other than a space, and pass that as `<delimiter>`.

```sharp
> &FUN`TOWN me=switch(%0,Old Town,North Gate|Market Square,North Gate,Old Town)
> think graph_reach(me/FUN`TOWN,Old Town,bfs starts,,|,%,%b)
Old Town, North Gate, Market Square
```

## Extra arguments

Arguments after a graph function's last named argument are passed on to the attributes it calls, after their own arguments: as %1 onward to `<link-attr>`, as %3 onward to a format attribute, and as %2 onward to a cost or combine attribute. [JSON_WALK()] passes them as %4 onward.

For example, to skip exits a player can't pass, pass the player, and have the link attribute leave out each exit whose lock %1 fails. Here the Keep Gate, which leads from the Wharf (#44) to the Keep (#47), is locked, so the path goes back through the Square:

```sharp
> &FUN`OPEN me=iter(lexits(%0),if(elock(%i0,%1),loc(%i0)))
> think map(me/FUN`NAME,graph_path(me/FUN`OPEN,#44,#47,starts,,,%b,%#))
Wharf Square Keep
```

Every argument before the extra ones must be given, even if it is empty. An empty `<osep>` puts nothing between nodes, so give `%b` for a space.

## Keeping a graph

Keep each node as a branch of an attribute tree, with its links in one leaf:

```sharp
&SKILL`WHIRL`NEEDS me=DUAL RIPOSTE
&SKILL`RIPOSTE`NEEDS me=PARRY
&SKILL`PARRY`NEEDS me=SWORDS
&SKILL`DUAL`NEEDS me=SWORDS
&FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
```

Other data about a node sits beside its links. The fold examples add up ``SKILL`<skill>`COST``, which is 5 for WHIRL, 3 for RIPOSTE, 2 for PARRY and DUAL, and 1 for SWORDS. A link with data of its own is a branch under its node, such as ``WAY`HARBOR`TO`DOCKS`COST``, and `lattr()` lists those links:

```sharp
&FUN`WAYS me=iter(lattr(%!/WAY`%0`TO`*),last(%i0,`))
```

The faction examples keep each faction's subfactions in ``FACTION`<name>`SUBS`` (GUARD and COURT under CROWN, NIGHTWATCH under GUARD) and its members in ``FACTION`<name>`MEMBERS``.

## JSON graphs

A JSON graph is an object with one key per node. Each key holds a list of the nodes that node links to:

```sharp
> &GRAPH`LOOP me={"A":["B"],"B":["C"],"C":["A"]}
> think json_graph_reach(v(GRAPH`LOOP),A,bfs starts)
A B C
```

To give a link data of its own, write the node's links as an object instead of a list, with a key for each node it links to: `{"HARBOR":{"DOCKS":{"cost":1}}}`.

Each node is a key only once, so a loop is a link back to a key that is already there; see [graph_reach() loops]. Keys are compared without regard to case. A node with no key, or with `null` as its value, has no links. Any other value gives `#-1 BAD ARGUMENT FORMAT`.

Reading a JSON graph's links calls no attribute, so only a format, combine or cost attribute counts toward the function invocation limit.

## Limits

Each function called inside a link, format or cost attribute counts toward the function invocation limit, as in any [U()]. The walk itself counts nothing and does not recurse, so the depth of a graph has no limit. At the default limit of 25,000, a link attribute that is a single `get()` can walk about 24,000 nodes in one command.
