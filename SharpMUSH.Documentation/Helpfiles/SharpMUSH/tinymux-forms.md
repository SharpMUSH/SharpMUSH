<!-- help-article
{
  "corpus": "help",
  "id": "tinymux-forms",
  "lookup": "tinymux forms",
  "aliases": [],
  "sections": []
}
-->
# TinyMUX Forms

SharpMUSH answers these calls as TinyMUX does, where PennMUSH refuses them or reads them otherwise (see [compatibility arguments]).

`switch(<string>,<default>)` is the default, a one-argument `add()` or `and()` is that argument tested or returned, `itext()` is the innermost loop's element, and `lnum()` is empty.

`wrap(<text>,<width>,<justify>,<left>,<right>,<hanging>,<osep>,<first width>)` is TinyMUX's `wrap()` whenever it has more than four arguments or its third starts with a letter: it pads every line to its width, justified `left`, `right` or `center`. See [wrap tinymux].

`ansi(<codes>,<string>,<codes2>,<string2>,...)` marks up each string with the codes before it. `secs(utc|local[,<digits>])` reads the clock in UTC or the server's local time, to 0-7 decimal places. `extract()` and `insert()` take an output separator as a fifth argument, and `remove()` as a fourth; `@@` there means no separator. With the fifth argument, `insert()` also takes the position one past the last word, and adds the word at the end.

`setunion()`, `setinter()`, `setdiff()` and `setsymdiff()` take TinyMUX's order, the output separator and then the sort type, when the fourth argument is text that names no sort type and the fifth names one. A four-argument call stays PennMUSH's, where any fourth argument is the sort type, so TinyMUX's `setunion(<list1>,<list2>,<delimiter>,<osep>)` is written `setunion(<list1>,<list2>,<delimiter>,,<osep>)`.
