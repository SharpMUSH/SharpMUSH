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
    }
  ]
}
-->
# graph_path()

`graph_path(<links>, <from>, <to>[, <options>[, <cost>[, <delimiter>[, <osep>]]]])`

The nodes on the shortest way from `<from>` to `<to>` through `<links>`, a link function (see [graph functions link function]), without `<from>` unless `<options>` is `starts`. `<to>` may be a list: the way leads to the nearest of them.

Without `<cost>`, the shortest way has the fewest links; of two as short, the one the link function listed first wins. See [graph_path() costs] for the lowest total instead.

`<delimiter>` splits `<to>` and what the link function returns; `<osep>` goes between the nodes of the way. Both are a space by default.

```sharp
> &FUN`WAYS me=iter(lattr(%!/WAY`%0`TO`*),last(%i0,`))
> think graph_path(me/FUN`WAYS,HARBOR,FORT)
DOCKS FORT
> think graph_path(me/FUN`WAYS,HARBOR,MARKET FORT)
MARKET
```

Errors: `#-1 NO PATH`, `#-1 NO SUCH OPTION`, and any `#-1` the link or cost function returns.

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_REACH()]
- [graph_path() costs]
:::

## Costs

`<cost>`, an attribute or `#lambda`, gives the cost of the link from %0 to %1, a number of 0 or more. The way with the lowest total wins.

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

Errors: `#-1 ARGUMENT MUST BE NUMBER` and `#-1 COST MUST NOT BE NEGATIVE`.
