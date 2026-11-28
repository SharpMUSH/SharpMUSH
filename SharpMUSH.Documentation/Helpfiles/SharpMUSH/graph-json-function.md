<!-- help-article
{
  "corpus": "help",
  "id": "graph-json-function",
  "lookup": "graph_json()",
  "aliases": [
    "graph_json"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "graph_json() examples"
    }
  ]
}
-->
# graph_json()

`graph_json([<obj>/]<link-attr>, <starts>[, <delimiter>[, <arg>...]])`

graph_json() writes the graph that `<link-attr>` describes as a JSON graph (see [graph functions json graphs]). It has a key for every node that `<starts>` lead to, holding the nodes it links to. Each node's links are read once, so a graph with loops is still written out only once.

`<link-attr>` is called with a node as %0 and each `<arg>` as %1 onward (see [graph functions link function]). `<delimiter>` separates `<starts>` and what `<link-attr>` returns. A space by default. Errors: any `#-1` the link attribute returns.

```sharp
> &FUN`NEEDS me=get(%!/SKILL`%0`NEEDS)
> think graph_json(me/FUN`NEEDS,WHIRL)
{"WHIRL":["DUAL","RIPOSTE"],"DUAL":["SWORDS"],"RIPOSTE":["PARRY"],"SWORDS":[],"PARRY":["SWORDS"]}
```

::: seealso
- [GRAPH FUNCTIONS]
- [JSON_GRAPH_REACH()]
- [graph_json() examples]
:::

## Examples

Build the graph once, keep it in a q-register or an attribute, and walk it again without calling the link attribute:

```sharp
> think [setq(g,graph_json(me/FUN`NEEDS,WHIRL))][json_graph_reach(%qg,WHIRL,post starts)]
SWORDS DUAL PARRY RIPOSTE WHIRL
> think [setq(g,graph_json(me/FUN`NEEDS,WHIRL))][json_graph_path(%qg,WHIRL,SWORDS)]
DUAL SWORDS
```

The rooms a player can walk to from the Wharf (#44). The player is passed as %1, so the link attribute leaves out locked exits:

```sharp
> &FUN`OPEN me=iter(lexits(%0),if(elock(%i0,%1),loc(%i0)))
> think graph_json(me/FUN`OPEN,#44,,%#)
{"#44":["#42"],"#42":["#47","#44"],"#47":["#42"]}
```
