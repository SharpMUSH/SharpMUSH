<!-- help-article
{
  "corpus": "help",
  "id": "indexed-function",
  "lookup": "indexed()",
  "aliases": [
    "indexed"
  ],
  "sections": [
    {
      "id": "indexed-operations",
      "heading": "Operations",
      "lookup": "indexed operations"
    },
    {
      "id": "indexed-visibility",
      "heading": "What you see",
      "lookup": "indexed visibility"
    }
  ]
}
-->
# indexed()

`indexed(<attribute>, <operation>, <value>[, <start>, <count>])`<br>
`indexed(<attribute>, <first|last>)`

Returns the dbrefs of the objects whose own copy of `<attribute>` matches `<value>`, read from the attribute's index instead of searching every object. The attribute must have an index; a wizard puts one on with `@attribute/index` (see [@attribute index]). Only an object's own copy is listed, never one it inherits from a parent, and an empty value is never listed.

The answer comes in the index's order: by value, lowest first, and objects with the same value in dbref order. `<start>` (counting from 1) and `<count>` take a page of it, counted among the objects you could see.

While the index is still being built, indexed() returns `#-1 INDEX BUILDING`; it never falls back to a slow search. An attribute with no index returns `#-1 ATTRIBUTE NOT INDEXED`.

Examples:
```sharp
> &STATUS #20=approved
> &STATUS #21=Approved
> &STATUS #22=pending
> think indexed(STATUS, =, approved)
#20 #21
> think indexed(STATUS, count, approved)
2
> think indexed(STATUS, prefix, pen)
#22
```
Output: the index ignores case unless it was made `exact`, so both #20 and #21 match "approved", and #22 is the only value starting with "pen".

## Operations

| Operation | Finds |
|---|---|
| `=` | values equal to `<value>` |
| `prefix` | values starting with `<value>` |
| `<`, `<=`, `>`, `>=` | values below, at or below, above, at or above `<value>` |
| `between` | values from the first to the second of the two words in `<value>`, both included |
| `first` | every value, lowest first; `<value>` is not used |
| `last` | every value, highest first; `<value>` is not used |
| `count` | how many values are equal to `<value>`, as a number |

In a `numeric` index, numbers come before text and compare as numbers, so 9 comes before 10 and `05` equals `5`. `prefix` there matches text values only. In a `split` index each word of a value (or each element, with `split:<delimiter>`) is listed on its own, so `=` finds an object by any one of them; an object that matches by several is still listed once, where it first comes.

Examples:
```sharp
> @attribute/index LEVEL=numeric
> think indexed(LEVEL, between, 10 20)
#31 #35 #30
> think indexed(LEVEL, last, , 1, 3)
#44 #31 #35
```
Output: the first line lists the objects whose LEVEL is from 10 to 20, lowest level first. The second lists the three objects with the highest levels, highest first.

## What you see

Every match is checked the way get() checks an attribute: an object is listed only when you could read its copy of the attribute, so a mortal_dark copy is left out for mortals and an attribute on an object you cannot examine is left out unless it is visual. `<start>` and `<count>` count only what you could see.

For a wizard, `count` is the number of objects listed under the value. For anyone else it counts only the objects whose copy they could read, so a count tells you nothing get() would not.

::: seealso
- [@attribute]
- [lsearch()]
- [get()]
:::
