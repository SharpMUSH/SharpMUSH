<!-- help-article
{
  "corpus": "help",
  "id": "rhostmush-arguments",
  "lookup": "rhostmush arguments",
  "aliases": [],
  "sections": [
    {"id": "lattr", "heading": "lattr() with rhost_lattr", "lookup": "rhostmush arguments lattr"},
    {"id": "list", "heading": "list() with rhost_list", "lookup": "rhostmush arguments list"}
  ]
}
-->
# RhostMUSH Arguments

What the arguments of RhostMUSH's forms of lattr() and list() do. Each form is used only while its
option is on; see [compatibility config].

- [rhostmush arguments lattr]: the target, type, flags, regexp and tree arguments
- [rhostmush arguments list]: the delimiter, header and target arguments, and what is told

## lattr() with rhost_lattr

`lattr(<object>[/<pattern>][, <target>[, <type>[, <flags>[, <regexp>[, <tree>]]]]])`

- `<target>` is a player whose view to list: only the attributes they may see are named. It is used
  only when you control that player; otherwise the list is your own view. The names are always
  separated by a space.
- `<type>` narrows or summarises the list by its first character: `$` keeps attributes holding a
  $-command and `^` those holding a ^-listen pattern; `&` keeps attributes with every attribute flag
  whose letter is in `<flags>`, and `|` those with any of them; `l` answers the number of names and
  of pages; a number answers that page (243 names a page, as on a default RhostMUSH); `~<start>-<count>`
  answers `<count>` names from the `<start>`th; `>` and `<` answer the alphabetically last or first
  name. `+` lists RhostMUSH's lock attributes, which SharpMUSH does not have, so it answers nothing; see
  llocks().
- `<regexp>`, when nonzero, makes `<pattern>` a regular expression.
- `<tree>`, when nonzero, makes `*` stop at a backtick, as SharpMUSH and PennMUSH match attribute
  trees. RhostMUSH has no attribute trees, so without it `*` also matches a backtick and
  `lattr(<object>)` names every attribute, branches' leaves included.

## list() with rhost_list

`list(<list>, <eval>[, <delimiter>][, <header>][, <target>])`

- `<delimiter>` separates the elements; it is a space when left out or empty.
- `<header>`, when it is not empty, is evaluated once and told before the first result.
- `<target>` sends the header and the results to another object instead of the enactor. It is used
  only when the enactor controls it. A target the enactor does not control, or a name that matches
  nothing, is ignored without a word, and the lines go to the enactor.
- An empty list tells nothing, not even the header. A result that is empty is not told.
- `ibreak()` stops the loop after the line it is in is told.
