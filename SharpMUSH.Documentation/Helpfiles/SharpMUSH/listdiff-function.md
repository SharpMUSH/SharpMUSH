<!-- help-article
{
  "corpus": "help",
  "id": "listdiff-function",
  "lookup": "listdiff()",
  "aliases": [
    "LISTINTER()",
    "LISTUNION()",
    "listdiff",
    "listinter",
    "listunion"
  ],
  "sections": []
}
-->
# listdiff()

`listdiff(<list1>, <list2>[, <delimiter>[, <output separator>[, <type>]]])`<br>
`listinter(<list1>, <list2>[, <delimiter>[, <output separator>[, <type>]]])`<br>
`listunion(<list1>, <list2>[, <delimiter>[, <output separator>[, <type>]]])`

These are setdiff(), setinter() and setunion() without the sorting: the result keeps the order the items had in the lists.

- listdiff() returns the items of `<list1>` that are not in `<list2>`.
- listinter() returns the items of `<list1>` that are also in `<list2>`.
- listunion() returns `<list1>`, followed by the items of `<list2>` that are not in it.

Items are separated by `<delimiter>`, or by a space if it is not given or is empty, and an empty item is left out. Items match only when they are exactly the same, case included. The result is separated by `<output separator>`, or by `<delimiter>` if it is not given or is empty.

A `<type>` of 1 keeps the items that appear more than once in `<list1>`; anything else, or none, lists each item once. The items listunion() adds from `<list2>` are listed once either way.

Examples:
```sharp
> think listdiff(foo baz gleep bar foo foo, bar moof gleep)
foo baz
> think listdiff(foo baz gleep bar foo foo, bar moof gleep, , -, 1)
foo-baz-foo-foo
> think listinter(foo baz gleep bar foo bar, bar moof gleep)
gleep bar
> think listunion(foo baz gleep bar foo bar, bar moof gleep)
foo baz gleep bar moof
```

listdiff(), listinter() and listunion() come from RhostMUSH and take the same arguments.

::: seealso
- [SETDIFF()]
- [SETINTER()]
- [SETUNION()]
:::
