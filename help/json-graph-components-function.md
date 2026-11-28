<!-- help-article
{
  "corpus": "help",
  "id": "json-graph-components-function",
  "lookup": "json_graph_components()",
  "aliases": [
    "json_graph_components"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "json_graph_components() examples"
    }
  ]
}
-->
# json_graph_components()

`json_graph_components(<graph>, <nodes>[, <delimiter>[, <osep>[, <gsep>]]])`

json_graph_components() is [GRAPH_COMPONENTS()] for a JSON graph: it splits the graph into groups of connected nodes, following links in either direction. `<graph>` has a key for each node, holding its links (see [graph functions json graphs]). The groups hold `<nodes>` and every node they lead to, in the order the walk found them.

- `<delimiter>`: separates `<nodes>`. A space by default.
- `<osep>`: goes between the nodes of a group. A space by default.
- `<gsep>`: goes between groups. `|` by default, and when empty.

```sharp
> &GRAPH`ISLES me={"Square":["Wharf"],"Wharf":["Keep"],"Island":["Cove"]}
> think json_graph_components(v(GRAPH`ISLES),Square Island)
Square Wharf Keep|Island Cove
```

Errors: `#-1 BAD ARGUMENT FORMAT TO json_graph_components`.

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_COMPONENTS()]
- [json_graph_components() examples]
:::

## Examples

Links count in both directions. Once the Wharf links to the Cove, the Island's group and the Square's group become one, listed in the order the walk found them. A loop is one group too:

```sharp
> &GRAPH`JOINED me={"Square":["Wharf"],"Wharf":["Keep","Cove"],"Island":["Cove"]}
> think json_graph_components(v(GRAPH`JOINED),Square Island)
Square Island Wharf Cove Keep
> &GRAPH`LOOP me={"A":["B"],"B":["C"],"C":["A"]}
> think words(json_graph_components(v(GRAPH`LOOP),A),|)
1
```
