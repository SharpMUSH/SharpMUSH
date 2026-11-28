<!-- help-article
{
  "corpus": "help",
  "id": "json-graph-fold-function",
  "lookup": "json_graph_fold()",
  "aliases": [
    "json_graph_fold"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "json_graph_fold() examples"
    }
  ]
}
-->
# json_graph_fold()

`json_graph_fold(<graph>, <root>, [<obj>/]<format-attr>[, [<obj>/]<combine-attr>[, <options>[, <delimiter>[, <osep>[, <arg>...]]]]])`

[GRAPH_FOLD()] over a JSON graph: a result for `<root>` built from its children's results. `<graph>` holds a key for each node with its children (see [graph functions json graphs]). `<format-attr>`, `<combine-attr>`, `up` and `down` work as in [GRAPH_FOLD()], and each `<arg>` reaches `<format-attr>` as %3 onward and `<combine-attr>` as %2 onward.

```sharp
> &GRAPH`DIAMOND me={"A":["B","C"],"B":["D"],"C":["D"]}
> &FUN`WRAP me=%0<%1>
> think json_graph_fold(v(GRAPH`DIAMOND),A,me/FUN`WRAP)
A<B<D<>> C<D<>>>
```

Errors: `#-1 CYCLE` with the loop, and `#-1 BAD ARGUMENT FORMAT TO json_graph_fold`.

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_FOLD()]
- [json_graph_fold() examples]
:::

## Examples

The total cost of a skill and everything it needs, the graph made once with [GRAPH_JSON()]:

```sharp
> &FUN`TOTAL me=add(get(%!/SKILL`%0`COST),ladd(%1))
> think json_graph_fold(graph_json(me/FUN`NEEDS,WHIRL),WHIRL,me/FUN`TOTAL)
14
```

A tree from an API, folded with [JSON_TREE_GRAPH()]: each node counts itself and what is under it.

```sharp
> &DATA`API me={"title":"Swords","subskills":[{"title":"Parry","subskills":[{"title":"Riposte"}]},{"title":"Dual"}]}
> think json_graph_fold(json_tree_graph(v(DATA`API),subskills),$,#lambda/add\(1\,ladd\(\%1\)\))
4
```
