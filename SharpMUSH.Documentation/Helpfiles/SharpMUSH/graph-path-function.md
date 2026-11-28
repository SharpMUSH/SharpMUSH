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

`graph_path([<obj>/]<link-attr>, <from>, <to>[, <options>[, [<obj>/]<cost-attr>[, <delimiter>[, <osep>]]]])`

The nodes on the shortest way from `<from>` to `<to>` through `<link-attr>`, which gets a node as %0 and returns its links (see [graph functions link function]). `<from>` is left out unless `<options>` is `starts`. `<to>` may be a list: the way leads to the nearest of them.

Without `<cost-attr>`, the shortest way has the fewest links; of two as short, the one the link attribute listed first wins. See [graph_path() costs] for the lowest total instead. `<delimiter>` splits `<to>` and what the link attribute returns; `<osep>` goes between the nodes of the way. Both are a space by default.

```sharp
> &FUN`WAYS me=iter(lattr(%!/WAY`%0`TO`*),last(%i0,`))
> think graph_path(me/FUN`WAYS,HARBOR,FORT)
DOCKS FORT
> think graph_path(me/FUN`WAYS,HARBOR,FORT,starts)
HARBOR DOCKS FORT
> think graph_path(me/FUN`WAYS,HARBOR,MARKET FORT)
MARKET
```

The ways are those of [graph_path() costs]; for rooms, see [graph_path() examples]. Errors: `#-1 NO PATH`, `#-1 NO SUCH OPTION`, and any `#-1` the link or cost attribute returns.

::: seealso
- [GRAPH FUNCTIONS]
- [graph_path() costs]
- [graph_path() examples]
- [GRAPH_REACH()]
:::

## Costs

`[<obj>/]<cost-attr>` gives the cost of the link from %0 to %1, a number of 0 or more. The way with the lowest total wins.

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

By steps the Docks lead straight to the Fort, but that link costs 5; through the Market the total is 3.

Errors: `#-1 ARGUMENT MUST BE NUMBER` and `#-1 COST MUST NOT BE NEGATIVE`.

## Examples

The way from here to the Keep (#47), and to the Island (#50), which no exit reaches:

```sharp
> &FUN`EXITS me=iter(lexits(%0),loc(%i0))
> &FUN`NAME me=name(%0)
> think map(me/FUN`NAME,graph_path(me/FUN`EXITS,%l,#47,starts))
Square Keep
> think graph_path(me/FUN`EXITS,%l,#50)
#-1 NO PATH
```

Leave out exits the traveller cannot pass. The Keep Gate, from the Wharf (#44), is locked:

```sharp
> &FUN`OPEN me=iter(lexits(%0),if(elock(%i0,%q<mover>),loc(%i0)))
> think map(me/FUN`NAME,graph_path(me/FUN`EXITS,#44,#47,starts))
Wharf Keep
> think [setq(mover,%#)][map(me/FUN`NAME,graph_path(me/FUN`OPEN,#44,#47,starts))]
Wharf Square Keep
```

The first step on the way, for a guide that points: `first(graph_path(...))`.
