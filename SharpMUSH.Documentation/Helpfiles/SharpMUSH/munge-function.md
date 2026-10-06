<!-- help-article
{
  "corpus": "help",
  "id": "munge-function",
  "lookup": "munge()",
  "aliases": [
    "munge"
  ],
  "sections": [
    {
      "id": "reordering-related-lists",
      "heading": "Reordering related lists",
      "lookup": "munge reordering related lists"
    },
    {
      "id": "connection-order-example",
      "heading": "Connection order example",
      "lookup": "munge connection order example"
    }
  ],
  "redirects": {
    "MUNGE2": "munge reordering related lists",
    "MUNGE3": "munge connection order example"
  }
}
-->
# munge()

`munge([<object>/]<attribute>, <list1>, <list2>[, <delimiter>[, <osep>]])`

This function takes two lists of equal length. It passes the entirety of `<list1>` to the user-defined function as %0, and the delimiter as %1. Then, this resulting list is matched with elements in `<list 2>`, and the rearranged `<list2>` is returned.

This is useful for doing things like sorting a list, and then returning the corresponding elements in the other list. If a resulting element from the user-defined function doesn't match an element in the original `<list1>`, a corresponding element from `<list2>` does not appear in the final result. The elements are matched using an exact, case-sensitive comparision.

`<delimiter>` defaults to a space, and `<osep>` defaults to `<delimiter>`.

## Reordering related lists

For example: Consider attribute PLACES, which contains "Fort Benden Ista", and another attribute DBREFS contains the dbrefs of the main JUMP_OK location of these areas, "#20 #9000 #5000". We want to return a list of dbrefs, corresponding to the names of the places sorted alphabetically. The places sorted this way would be "Benden Fort Ista", so we want the final list to be "#9000 #20 #5000". The functions, using munge(), are simple:

```sharp
> &sort me=sort(%0)
> say munge(sort, v(places), v(dbrefs))
You say, "#9000 #20 #5000"
```

## Connection order example

Another common task that munge() is well suited for is sorting a list of dbrefs of players by order of connection. This example uses #apply to avoid the need for the sort attribute, and also unlike the other example, it builds the list to sort on out of the list to return.

```sharp
> &faction_members me=#3 #12 #234
> say munge(#apply/sort, map(#apply/conn, v(faction_members)), v(faction_members))
You say, "#12 #234 #3"
```


::: seealso
- [anonymous attributes]
:::
