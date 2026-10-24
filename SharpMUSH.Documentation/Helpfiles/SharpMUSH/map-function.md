<!-- help-article
{
  "corpus": "help",
  "id": "map-function",
  "lookup": "map()",
  "aliases": [],
  "sections": [
    {"id": "arguments", "heading": "Extra arguments", "lookup": "map arguments"},
    {"id": "examples", "heading": "Examples", "lookup": "map examples"}
  ]
}
-->
# map()
`map([<object>/]<attribute>, <list>[, <delim>[, <osep>[, <arg1>, ... , <argN>]]])`

This function works much like ITER(). The given `<attribute>` is evaluated once for each element of `<list>`, and the results of the evaluations are returned. For each evaluation, the current list element is passed to the attribute as %0, and its position in the list as %1. Elements of `<list>` are separated by `<delim>`, or a space if none is given, and the results are returned separated by `<osep>`, if given, or the delimiter otherwise.

It is like this, but slightly faster:

`iter(<list>, ulambda(<object>/<attribute>, %i0, inum(0)), <delim>, <osep>)`

- [map arguments]: passing extra arguments to the attribute
- [map examples]: doubling a list with and without a delimiter

## Extra arguments

Any arguments after `<osep>` are passed to the attribute as %1, %2 and so on, in place of the position. Passing them comes from RhostMUSH. With the `rhost_map_args` option on, %1 is empty when there are none, as on RhostMUSH, rather than the position (see [compatibility config]).

```sharp
> &show_args me=<%0:%1:%2>
> think map(show_args, a b,,, x, y)
<a:x:y> <b:x:y>
```

## Examples

```sharp
> &times_two me=mul(%0,2)
> say map(times_two, 5 4 3 2 1)
You say, "10 8 6 4 2"
> say map(times_two,1;2;3;4;5,;)
You say, "2;4;6;8;10"
```

::: seealso
- [anonymous attributes]
- [iter()]
- [@dolist]
:::
