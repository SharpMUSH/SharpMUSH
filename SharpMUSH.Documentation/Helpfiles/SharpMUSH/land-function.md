<!-- help-article
{
  "corpus": "help",
  "id": "land-function",
  "lookup": "land()",
  "aliases": [
    "LOR()",
    "LXOR()",
    "LNOR()",
    "LXNOR()",
    "land",
    "lor",
    "lxor",
    "lnor",
    "lxnor"
  ],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "land examples"}
  ]
}
-->
# land()

`land(<list>[, <delimiter>])`<br>
`lor(<list>[, <delimiter>])`<br>
`lxor(<list>[, <delimiter>])`<br>
`lnor(<list>[, <delimiter>])`<br>
`lxnor(<list>[, <delimiter>])`

These combine a whole list of true and false values at once. Each item counts as the whole number it starts with, so `1.9` is 1, `0.9` is 0 and an item that does not start with a number is 0; a number other than 0 is true.

- land() returns 1 if every number is true, and 0 otherwise.
- lor() returns 1 if any number is true, and 0 otherwise.
- lxor(), lnor() and lxnor() work along the list from the left. The first number is the result so far; each number after it is combined with that result to give 1 or 0. lxor() gives 1 when exactly one of the two is true, lnor() gives 1 when neither is, and lxnor() gives 1 when both or neither are.

Because they work along the list, lxor(), lnor() and lxnor() return a list of one number as it is: `lxor(5)` is 5. The items are separated by `<delimiter>`, or by spaces if it is not given or is empty. An empty list gives 0.

land(), lor(), lxor(), lnor() and lxnor() come from RhostMUSH and take the same arguments.

- [land examples]: each function on a short list

::: seealso
- [AND()]
- [OR()]
- [XOR()]
- [NOR()]
- [LMATH()]
- [ladd()]
:::

## Examples

```sharp
> think land(1 1 0)
0
> think lor(0 0 2)
1
> think lxor(0 1 0 1 0 1)
1
> think lnor(0 0)
1
> think lxnor(1 0 1)
0
```
