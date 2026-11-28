<!-- help-article
{
  "corpus": "help",
  "id": "graph-path-function",
  "lookup": "graph_path()",
  "aliases": [
    "graph_path"
  ],
  "sections": [
    {
      "id": "costs",
      "heading": "Costs",
      "lookup": "graph_path() costs"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "graph_path() examples"
    }
  ]
}
-->
# graph_path()

`graph_path([<obj>/]<link-attr>, <from>, <to>[, <options>[, [<obj>/]<cost-attr>[, <delimiter>[, <osep>[, <arg>...]]]]])`

graph_path() returns the nodes on the shortest path from `<from>` to `<to>`. `<link-attr>` is called with a node as %0 and returns its links (see [graph functions link function]). Each `<arg>` is passed on as described in [graph functions extra arguments]. `<from>` is left out unless `<options>` is `starts`. `<to>` may be a list, and the path then leads to the nearest of them.

Without `<cost-attr>`, the shortest path is the one with the fewest links. When two paths are equally short, the one the link attribute listed first wins. To find the lowest total cost instead, see [graph_path() costs]. `<delimiter>` separates `<to>` and what the link attribute returns, and `<osep>` goes between the nodes of the path. Both are a space by default.

```sharp
> &FUN`WAYS me=iter(lattr(%!/WAY`%0`TO`*),last(%i0,`))
> think graph_path(me/FUN`WAYS,HARBOR,FORT)
DOCKS FORT
> think graph_path(me/FUN`WAYS,HARBOR,FORT,starts)
HARBOR DOCKS FORT
> think graph_path(me/FUN`WAYS,HARBOR,MARKET FORT)
MARKET
```

The roads are set up in [graph_path() costs]. For rooms, see [graph_path() examples]. Errors: `#-1 NO PATH`, `#-1 NO SUCH OPTION`, and any `#-1` the link or cost attribute returns.

::: seealso
- [GRAPH FUNCTIONS]
- [graph_path() costs]
- [graph_path() examples]
- [GRAPH_REACH()]
:::

## Costs

`[<obj>/]<cost-attr>` returns the cost of the link from %0 to %1, a number of 0 or more. The path with the lowest total cost wins.

```sharp
> &WAY`HARBOR`TO`DOCKS`COST me=1
> &WAY`HARBOR`TO`MARKET`COST me=4
> &WAY`DOCKS`TO`MARKET`COST me=1
> &WAY`DOCKS`TO`FORT`COST me=5
> &WAY`MARKET`TO`FORT`COST me=1
> &FUN`COST me=get(%!/WAY`%0`TO`%1`COST)
> think graph_path(me/FUN`WAYS,HARBOR,FORT,starts,me/FUN`COST)
HARBOR DOCKS MARKET FORT
```

By steps, the Docks lead straight to the Fort, but that link costs 5. Going through the Market costs 3 in total.

Errors: `#-1 ARGUMENT MUST BE NUMBER` and `#-1 COST MUST NOT BE NEGATIVE`.

## Examples

The path from here to the Keep (#47), and to the Island (#50), which no exit leads to:

```sharp
> &FUN`EXITS me=iter(lexits(%0),loc(%i0))
> &FUN`NAME me=name(%0)
> think map(me/FUN`NAME,graph_path(me/FUN`EXITS,%l,#47,starts))
Square Keep
> think graph_path(me/FUN`EXITS,%l,#50)
#-1 NO PATH
```

To skip exits the player can't pass, pass the player as an extra argument, which the link attribute reads as %1 (see [graph functions extra arguments]). The Keep Gate, which leads from the Wharf (#44) to the Keep, is locked:

```sharp
> &FUN`OPEN me=iter(lexits(%0),if(elock(%i0,%1),loc(%i0)))
> think map(me/FUN`NAME,graph_path(me/FUN`EXITS,#44,#47,starts))
Wharf Keep
> think map(me/FUN`NAME,graph_path(me/FUN`OPEN,#44,#47,starts,,,%b,%#))
Wharf Square Keep
```

`first(graph_path(...))` is the first step of the path, for code that points a player the right way.
