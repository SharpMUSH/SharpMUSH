<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-defects",
  "lookup": "compatibility defects",
  "aliases": [],
  "sections": []
}
-->
# Compatibility Defects

Differences that are **bugs**, expected to change. Listed so they are not mistaken for choices.

  **`attrib_set#()` cannot be called.** The parser's function-name token does not admit `#`, so the text is returned unchanged. Use `attrib_set()`.<br>
  **`objmem()` always answers 0.**<br>
  **`%c` and `%u` inside locks.** A lock evaluated for a `$`-command sees neither. `%u` also shows the value of `&attr obj=value` and `@attr obj=value` as written, where PennMUSH shows it evaluated in a queued list, and a computed `&` attribute name as written (`ATTRIB_SET/[CAT(F,OO)]`), where PennMUSH shows the name it evaluated to (`ATTRIB_SET/FOO`).<br>
  **`@force` counts toward the in-place depth.** It runs its list in place rather than queueing it, so it is one more of the 50 in-place levels a queue entry may nest (see `help @include`).<br>
  **Characters above U+FFFF** (emoji and other supplementary-plane characters) are stored as UTF-16 surrogate pairs. A substitution or slice that lands between the two halves of a pair could split it.
