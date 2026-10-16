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
  In functions where you can specify a sorting method, you can provide one of these sort types:

- [sorting types]: every sort type, reversing a sort, and case
- [sorting by attribute]: the attr: and attri: sort keys

## Sort types

  Type    Sorts:<br>
   a       Sorts lexicographically (Maybe case-sensitive).<br>
   i       Sorts lexicographically (Always case-insensitive).<br>
   d       Sorts dbrefs.<br>
   n       Sorts integer numbers.<br>
   f       Sorts decimal numbers.<br>
   m       Sorts strings with embedded numbers and dbrefs (as names).<br>
   name    Sorts dbrefs by their names. (Maybe case-sensitive)<br>
   namei   Sorts dbrefs by their names. (Always case-insensitive)<br>
   conn    Sorts dbrefs by their connection time.<br>
   idle    Sorts dbrefs by their idle time.<br>
   owner   Sorts dbrefs by their owner dbrefs.<br>
   loc     Sorts dbrefs by their location dbref.<br>
   ctime   Sorts dbrefs by their creation time.<br>
   mtime   Sorts dbrefs by their modification time.<br>
   lattr   Sorts attribute names.

  Prefixing the sort type with a minus sign, -, reverses the order of the sort.

  Whether or not the 'a' sort type is case-sensitive or not depends on the particular mush and its environment.

## Sorting by attribute

  The special sort key attr:`<aname>` or attri:`<aname>` will sort dbrefs according to their `<aname>` attributes. For example: Separating by &factions or &species attrs. attr is probably case-sensitive, and attri is case-insensitive.


::: seealso
- [SORT()]
- [SORTBY()]
- [SORTKEY()]
- [SETUNION()]
- [SETINTER()]
- [SETDIFF()]
:::
