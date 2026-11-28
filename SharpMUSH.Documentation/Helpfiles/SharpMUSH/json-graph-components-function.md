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

[GRAPH_COMPONENTS()] over a JSON graph: the groups its links join, whichever way they point. `<graph>` holds a key for each node with its links (see [graph functions json graphs]). The groups hold `<nodes>` and every node they lead to, in the order the walk met them.

- `<delimiter>`: splits `<nodes>`. A space by default.
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

Links count both ways: once the Wharf links to Cove, the Island's group and the Square's are one, listed in the order the walk met them. A loop is one group too:

```sharp
> &GRAPH`JOINED me={"Square":["Wharf"],"Wharf":["Keep","Cove"],"Island":["Cove"]}
> think json_graph_components(v(GRAPH`JOINED),Square Island)
Square Island Wharf Cove Keep
> &GRAPH`LOOP me={"A":["B"],"B":["C"],"C":["A"]}
> think words(json_graph_components(v(GRAPH`LOOP),A),|)
1
```
