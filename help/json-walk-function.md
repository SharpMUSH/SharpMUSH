<!-- help-article
{
  "corpus": "help",
  "id": "json-walk-function",
  "lookup": "json_walk()",
  "aliases": [
    "json_walk"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "json_walk() examples"
    },
    {
      "id": "finding-the-children",
      "heading": "Finding the children",
      "lookup": "json_walk() finding the children"
    }
  ]
}
-->
# json_walk()

`json_walk([<obj>/]<attr>, <json>[, <children>[, <osep>[, <arg>[, ..., <argN>]]]])`

Calls `<attr>` (or a `#lambda`) once for every node of the JSON tree `<json>`, parents first. `<children>` is the key that holds a node's children, `children` when left empty; see [json_walk() finding the children]. The results are joined by `<osep>`, a space by default, and empty results are left out.

The attribute gets:
- %0: the node's JSON, its children included.
- %1: its level. The root is 0.
- %2: its place among its siblings, from 1.
- %3: its JSONPath, which [JSON_QUERY()] takes: `json_query(<json>,extract,%3)` is the node.
- %4 and on: the `<arg>`s. Pass the document as one to read a node's parent or siblings.

```sharp
> &DATA me={"title":"Swords","cost":1,"subskills":[{"title":"Parry","cost":2,"subskills":[{"title":"Riposte","cost":3}]},{"title":"Dual","cost":2}]}
> &LINE me=[space(mul(2,%1))][json_query(json_query(%0,get,title),unescape)]
> think json_walk(me/LINE,v(DATA),subskills,%r)
Swords
  Parry
    Riposte
  Dual
```

::: seealso
- [json_walk() examples]
- [JSON_MAP()]
- [JSON_QUERY()]
- [GRAPH FUNCTIONS]
:::

## Examples

With ``DATA`` as in [JSON_WALK()]:

```sharp
> &TITLE me=json_query(json_query(%0,get,title),unescape)
> think json_walk(me/TITLE,v(DATA),subskills,%,%b)
Swords, Parry, Riposte, Dual
> &COST me=json_query(%0,get,cost)
> think ladd(json_walk(me/COST,v(DATA),subskills))
8
> &DEPTH me=%1
> think lmax(json_walk(me/DEPTH,v(DATA),subskills))
2
> &LEAF me=if(not(json_query(%0,exists,subskills)),u(TITLE,%0))
> think json_walk(me/LEAF,v(DATA),subskills)
Riposte Dual
```

The total cost of the tree, how deep it goes, and the nodes with no children: an empty result is left out, so ``LEAF`` lists only those.

## Finding the children

`<children>` is a key of the node, or a JSONPath starting with `$`, where `$` is the node: `$.links.items`. The children are the elements of the array there, or the values of the object there, in order; a node without it, or with something else there, has none. A top-level array is a list of roots, each at level 0.

```sharp
> &WHERE me=%1/%2/%3
> think json_walk(me/WHERE,v(DATA),subskills)
0/1/$ 1/1/$.subskills[0] 2/1/$.subskills[0].subskills[0] 1/2/$.subskills[1]
> &DEEP me={"links":{"items":[{"a":1},{"links":{"items":[{"c":3}]}}]}}
> think json_walk(me/WHERE,v(DEEP),$.links.items)
0/1/$ 1/1/$.links.items[0] 1/2/$.links.items[1] 2/1/$.links.items[1].links.items[0]
```
