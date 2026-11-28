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

json_tree_graph() turns a JSON tree into a JSON graph (see [graph functions json graphs]) that the `json_graph_` functions can walk. The graph has a key for every node, named by the node's JSONPath, holding the paths of its children.

`<children>` says where a node's children are, as in [JSON_WALK()]: a key (`children` if left out), or a JSONPath starting with `$`. A top-level array is a list of roots, `$[0]` onward. When a JSONPath matches several containers, the children of each are taken, and each child is named by its own place.

```sharp
> &DATA`API me={"title":"Swords","subskills":[{"title":"Parry","subskills":[{"title":"Riposte"}]},{"title":"Dual"}]}
> think json_tree_graph(v(DATA`API),subskills)
{"$":["$.subskills[0]","$.subskills[1]"],"$.subskills[0]":["$.subskills[0].subskills[0]"],"$.subskills[0].subskills[0]":[],"$.subskills[1]":[]}
```

A tree cannot loop. Two nodes with the same contents are still two nodes, because their paths differ. Errors: `#-1 BAD ARGUMENT FORMAT TO json_tree_graph`, and `#-1 KEYS DIFFER ONLY BY CASE` when two paths differ only by case, since a JSON graph's keys are compared without regard to case.

::: seealso
- [GRAPH FUNCTIONS]
- [JSON_WALK()]
- [json_tree_graph() examples]
:::

## Examples

[JSON_QUERY()] takes a node's path and returns its contents:

```sharp
> &FUN`TITLE me=json_query(json_query(json_query(v(DATA`API),extract,%0),get,title),unescape)
> think json_graph_reach(json_tree_graph(v(DATA`API),subskills),$,dfs starts,,,%b,,me/FUN`TITLE)
Swords Parry Riposte Dual
> think json_graph_reach(json_tree_graph(v(DATA`API),subskills),$,layers,,,%b,,me/FUN`TITLE)
Dual Riposte Parry
```
