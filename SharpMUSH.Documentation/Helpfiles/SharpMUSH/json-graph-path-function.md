<!-- help-article
{
  "corpus": "help",
  "id": "json-graph-path-function",
  "lookup": "json_graph_path()",
  "aliases": [
    "json_graph_path"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "json_graph_path() examples"
    }
  ]
}
-->
# json_graph_path()

`json_graph_path(<graph>, <from>, <to>[, <options>[, <cost-field>[, <delimiter>[, <osep>]]]])`

[GRAPH_PATH()] over a JSON graph: the nodes on the shortest way from `<from>` to `<to>`, with `<from>` left out unless `<options>` is `starts`. `<graph>` holds a key for each node with its links (see [graph functions json graphs]).

Without `<cost-field>`, the shortest way has the fewest links. With it, each link is an object holding that field as a number, and the way with the lowest total wins:

```sharp
> &GRAPH`ROADS me={"HARBOR":{"DOCKS":{"cost":1},"MARKET":{"cost":4}},"DOCKS":{"MARKET":{"cost":1},"FORT":{"cost":5}},"MARKET":{"FORT":{"cost":1}}}
> think json_graph_path(v(GRAPH`ROADS),HARBOR,FORT)
DOCKS FORT
> think json_graph_path(v(GRAPH`ROADS),HARBOR,FORT,starts,cost)
HARBOR DOCKS MARKET FORT
```

Errors: `#-1 NO PATH`, `#-1 NO COST` when a link the search examines lacks the field, even one off the way it returns, and `#-1 BAD ARGUMENT FORMAT TO json_graph_path`.

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_PATH()]
- [json_graph_path() examples]
:::

## Examples

`<to>` may be a list: the way leads to the nearest of them. A loop is skipped, as in [GRAPH_PATH()]:

```sharp
> think json_graph_path(v(GRAPH`ROADS),HARBOR,MARKET FORT,,cost)
DOCKS MARKET
> &GRAPH`LOOP me={"A":["B"],"B":["C"],"C":["A"]}
> think json_graph_path(v(GRAPH`LOOP),C,B)
A B
```
