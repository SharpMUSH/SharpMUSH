<!-- help-article
{
  "corpus": "help",
  "id": "tinymux-forms",
  "lookup": "tinymux forms",
  "aliases": [],
  "sections": [
    {"id": "flags", "heading": "Flags and powers", "lookup": "tinymux flags"}
  ]
}
-->
# TinyMUX Forms

SharpMUSH answers these calls as TinyMUX does, where PennMUSH refuses them or reads them otherwise (see [compatibility arguments]).

`switch(<string>,<default>)` is the default, a one-argument `add()` or `and()` is that argument tested or returned, `itext()` is the innermost loop's element, and `lnum()` is empty.

`wrap(<text>,<width>,<justify>,<left>,<right>,<hanging>,<osep>,<first width>)` is TinyMUX's `wrap()` whenever it has more than four arguments or its third starts with a letter: it pads every line to its width, justified `left`, `right` or `center`. See [wrap tinymux].

`ansi(<codes>,<string>,<codes2>,<string2>,...)` marks up each string with the codes before it. `secs(utc|local[,<digits>])` reads the clock in UTC or the server's local time, to 0-7 decimal places. `extract()` and `insert()` take an output separator as a fifth argument, and `remove()` as a fourth; `@@` there means no separator. With the fifth argument, `insert()` also takes the position one past the last word, and adds the word at the end.

`setunion()`, `setinter()`, `setdiff()` and `setsymdiff()` take TinyMUX's order, the output separator and then the sort type, when the fourth argument is text that names no sort type and the fifth names one. A four-argument call stays PennMUSH's, where any fourth argument is the sort type, so TinyMUX's `setunion(<list1>,<list2>,<delimiter>,<osep>)` is written `setunion(<list1>,<list2>,<delimiter>,,<osep>)`.

- [tinymux flags]: flag and power names and flag letters in TinyMUX's dialect

## Flags and powers

Code in TinyMUX's dialect (an attribute flagged `mux`, or any code on a game whose `dialect` option is `mux`) tests flags and powers by TinyMUX's names and letters.

`hasflag()`, `andlflags()` and `orlflags()` read `HALTED` as `HALT`, `ASCII` as `NOACCENTS` and `IMMORTAL` as the Immortal power; `COMMANDS`, `SPOOF`, `BLEED` and `ACCENTS` hold when `NO_COMMAND`, `NOSPOOF`, `NOBLEED` and `NOACCENTS` are not set; and `HAS_STARTUP`, `HAS_LISTEN`, `HAS_FORWARDLIST` and `HAS_DAILY` hold when the object has that attribute, which only God is told.

`flags()`, `andflags()` and `orflags()` use TinyMUX's letters (`n` is NO_COMMAND, which a new thing has, `s` is SAFE, `q` TERSE, `T` TRACE, `Z` ROYALTY, `X` ANSI, and a thing has no type letter), and `flags()` leaves out a flag TinyMUX has no letter for.

`haspower()`, `andlpowers()` and `orlpowers()` read TinyMUX's power names: `comm_all`, `dark`, `free_money`, `quota`, `control_all`, `siteadmin` and the rest. A power that is a permission here (`control_all` is `control.all`, `siteadmin` is `server.operate`) answers 1 for anyone a role or override gives that permission. Lock keys and `@set` take SharpMUSH's names.

```sharp unchecked
> @create Box
> @set Box=SAFE
> @set Box=TERSE
> &TEST Box=[flags(me)] [hasflag(me,COMMANDS)]
> @set Box/TEST=mux
> think u(Box/TEST)
nsq 0
```
