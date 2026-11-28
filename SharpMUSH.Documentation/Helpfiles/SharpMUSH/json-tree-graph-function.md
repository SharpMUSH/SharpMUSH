<!-- help-article
{
  "corpus": "help",
  "id": "json-tree-graph-function",
  "lookup": "json_tree_graph()",
  "aliases": [
    "json_tree_graph"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "json_tree_graph() examples"
    }
  ]
}
-->
# json_tree_graph()

`json_tree_graph(<json>[, <children>])`

A JSON tree as a JSON graph (see [graph functions json graphs]), so the `json_graph_` functions can walk it: a key for every node, named by its JSONPath, holding its children's paths. `<children>` names where a node's children are, as in [JSON_WALK()]: a key, `children` when left out, or a JSONPath starting with `$`. A top-level array is a list of roots, `$[0]` onward. A JSONPath that matches several containers takes the children of each, every child named from its own place.

```sharp
> &DATA`API me={"title":"Swords","subskills":[{"title":"Parry","subskills":[{"title":"Riposte"}]},{"title":"Dual"}]}
> think json_tree_graph(v(DATA`API),subskills)
{"$":["$.subskills[0]","$.subskills[1]"],"$.subskills[0]":["$.subskills[0].subskills[0]"],"$.subskills[0].subskills[0]":[],"$.subskills[1]":[]}
```

A tree cannot loop, and two nodes with the same contents are still two nodes, since their paths differ. Errors: `#-1 BAD ARGUMENT FORMAT TO json_tree_graph`, and `#-1 KEYS DIFFER ONLY BY CASE` when two paths do, since a JSON graph's keys compare ignoring case.

::: seealso
- [GRAPH FUNCTIONS]
- [JSON_WALK()]
- [json_tree_graph() examples]
:::

## Examples

A node's path finds its contents with [JSON_QUERY()]:

```sharp
> &FUN`TITLE me=json_query(json_query(json_query(v(DATA`API),extract,%0),get,title),unescape)
> think json_graph_reach(json_tree_graph(v(DATA`API),subskills),$,dfs starts,,,%b,,me/FUN`TITLE)
Swords Parry Riposte Dual
> think json_graph_reach(json_tree_graph(v(DATA`API),subskills),$,layers,,,%b,,me/FUN`TITLE)
Dual Riposte Parry
```
