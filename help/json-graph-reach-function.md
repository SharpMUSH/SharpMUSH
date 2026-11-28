<!-- help-article
{
  "corpus": "help",
  "id": "json-graph-reach-function",
  "lookup": "json_graph_reach()",
  "aliases": [
    "json_graph_reach"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "json_graph_reach() examples"
    }
  ]
}
-->
# json_graph_reach()

`json_graph_reach(<graph>, <starts>[, <options>[, <depth>[, <delimiter>[, <osep>[, <lsep>[, [<obj>/]<format-attr>[, <arg>...]]]]]]])`

json_graph_reach() is [GRAPH_REACH()] for a JSON graph: it returns every node that `<starts>` lead to, with the same options, orders and output. `<graph>` has a key for each node, holding its links (see [graph functions json graphs]). `<delimiter>` separates `<starts>`, and each `<arg>` is passed to `<format-attr>` as %3 onward.

```sharp
> &GRAPH`DIAMOND me={"A":["B","C"],"B":["D"],"C":["D"]}
> think json_graph_reach(v(GRAPH`DIAMOND),A,bfs starts,,,%b,/)
A/B C/D
> think json_graph_reach(v(GRAPH`DIAMOND),A,post)
D B C
```

Errors: `#-1 BAD ARGUMENT FORMAT TO json_graph_reach` when `<graph>` is not a JSON graph, and `#-1 CYCLE` as described in [graph_reach() loops].

::: seealso
- [GRAPH FUNCTIONS]
- [GRAPH_REACH()]
- [GRAPH_JSON()]
- [json_graph_reach() examples]
:::

## Examples

A loop is a link back to a key already in the graph. `bfs` and `dfs` skip nodes they have already seen, and list a start node when a link leads back to it. `post` and `layers` return the loop:

```sharp
> &GRAPH`LOOP me={"A":["B"],"B":["C"],"C":["A"]}
> think json_graph_reach(v(GRAPH`LOOP),A)
B C A
> think json_graph_reach(v(GRAPH`LOOP),A,post starts)
#-1 CYCLE A B C A
```

A format attribute gets the node, its level and the node it was reached from, as in [graph_reach() shaping the output]:

```sharp
> &FUN`INDENT me=[space(mul(2,%1))]%0
> think json_graph_reach(v(GRAPH`DIAMOND),A,dfs starts,,,|,,me/FUN`INDENT)
A|  B|    D|  C
```
