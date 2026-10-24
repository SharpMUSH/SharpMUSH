<!-- help-article
{
  "corpus": "help",
  "id": "sorting",
  "lookup": "Sorting",
  "aliases": [],
  "sections": [
    {"id": "types", "heading": "Sort types", "lookup": "sorting types"},
    {"id": "by-attribute", "heading": "Sorting by attribute", "lookup": "sorting by attribute"}
  ]
}
-->
# Sorting

Functions that sort, such as sort() and setunion(), take one of these sort types.

- [sorting types]: every sort type, reversing a sort, and case
- [sorting by attribute]: the attr: and attri: sort keys

## Sort types

| Type | Sorts |
| --- | --- |
| `a` | text, case-sensitive: capitals come before lower case |
| `i` | text, ignoring case |
| `d` | dbrefs, by number |
| `n` | whole numbers |
| `f` | decimal numbers |
| `m` | text with numbers and dbrefs in it, numbers in number order |
| `name` | dbrefs by their names, case-sensitive |
| `namei` | dbrefs by their names, ignoring case |
| `conn` | dbrefs by their connection time |
| `idle` | dbrefs by their idle time |
| `owner` | dbrefs by their owners' dbrefs |
| `loc` | dbrefs by their locations' dbrefs |
| `ctime` | dbrefs by their creation time |
| `mtime` | dbrefs by their modification time |
| `lattr` | attribute names |

A minus sign before the type, such as `-a`, reverses the order.

```sharp
> think sort(b A a B c C,a)
A B C a b c
> think sort(b A a B,-a)
b a B A
```

## Sorting by attribute

`attr:<attribute>` sorts dbrefs by the value of that attribute on each, case-sensitive; `attri:<attribute>` does the same, ignoring case. For instance, `attri:SPECIES` groups characters by their SPECIES attribute.

::: seealso
- [SORT()]
- [SORTBY()]
- [SORTKEY()]
- [SETUNION()]
- [SETINTER()]
- [SETDIFF()]
:::
