<!-- help-article
{
  "corpus": "help",
  "id": "aindex-function",
  "lookup": "aindex()",
  "aliases": [
    "aindex",
    "aiindex()",
    "aiindex",
    "iindex()",
    "iindex",
    "rindex()",
    "rindex"
  ],
  "sections": [
    {
      "id": "aindex-examples",
      "heading": "Examples",
      "lookup": "aindex examples"
    }
  ]
}
-->
# aindex()

`aindex(<list>, <elements>, <position>, <delimiter>)`<br>
`aiindex(<list>, <elements>, <position>, <delimiter>)`<br>
`iindex(<list>, <elements>, <position>, <delimiter>)`<br>
`rindex(<list>, <elements>, <position>, <delimiter>)`

Put `<elements>` into `<list>` at `<position>`, counting from 1. `<elements>` can hold several elements, separated by `<delimiter>`.

- `iindex()` puts them before the element at `<position>`.
- `aiindex()` does the same, and also puts them into an empty list when `<position>` is 1.
- `aindex()` puts them after the element at `<position>`.
- `rindex()` puts them in place of as many elements as they hold, from `<position>`. When they run past the end of the list, the list ends with them.

`<delimiter>` is required; write `%b` for a space. Only its first character is used. An empty element between two delimiters counts as a position, but an empty one at either end of the list does not count toward its length.

The list comes back unchanged when `<position>` is less than 1 or more than the number of elements, when `<elements>` is empty, or when `<delimiter>` is empty.

These functions come from RhostMUSH. See [aindex examples].

## Examples

```sharp
> think iindex(red green blue,cyan magenta,2,%b)
red cyan magenta green blue
> think aindex(red green blue,cyan,3,%b)
red green blue cyan
> think rindex(a|b|c|d,x|y,2,|)
a|x|y|d
> think aiindex(,first,1,|)
first
```

Output: the elements go before, after or in place of the element at the position.

::: seealso
- [insert()]
- [replace()]
- [ldelete()]
:::
