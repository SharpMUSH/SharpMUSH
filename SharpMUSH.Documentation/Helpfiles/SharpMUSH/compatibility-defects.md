<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-defects",
  "lookup": "compatibility defects",
  "aliases": [],
  "sections": []
}
-->
# COMPATIBILITY DEFECTS

Differences that are **bugs**, tracked and expected to change. Listed so they are not mistaken for
choices.

  **`pcreate()` takes no third argument.** PennMUSH accepts an optional dbref to reuse. (#974)<br>
  **`attrib_set#()` cannot be called.** The parser's function-name token does not admit `#`, so the
  text is returned unchanged. Use `attrib_set()`. (#974)<br>
  **`objmem()` always answers 0.** (#974)<br>
  **`buy` has no economy.** It is a stub. (#1006 item 10)<br>
  **`TERSE` is an alias of `CLOUDY`.** PennMUSH has them as two flags sharing the letter `x`, so
  `@list flags` lists `TERSE (x)` there and not here. (#1404)<br>
  **The vacation flag is named `ON_VACATION`.** PennMUSH names it `ON-VACATION`; both spellings are
  accepted here, but lists show the other one. (#1405)

The other movement and queue gaps left by the movement work are enumerated in #1006 rather than
repeated here; its items 1, 2, 4 and 15 are in [compatibility unresolved].
