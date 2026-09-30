<!-- help-article
{
  "corpus": "help",
  "id": "regexp-classes",
  "lookup": "regexp classes",
  "aliases": [],
  "sections": [
    {
      "id": "portable-character-classes",
      "heading": "Portable character classes",
      "lookup": "regexp classes portable character classes"
    }
  ],
  "redirects": {
    "REGEXP CLASSES2": "regexp classes portable character classes"
  }
}
-->
# regexp classes

In a character class, you can use a number of additional keywords that match certain types of characters. The keywords are enclosed in `[: and :]`, within the character class, so the whole thing looks like `[[:NAME:]]`.

These keywords can be mixed with other things in the character class, like `[ab[:digit:]]`, which will match 'a, 'b', or a digit. `[:^NAME:]` reverses the meaning of NAME - it expands to everything but characters that would match `[:NAME:]`.

Some recognized NAMEs:<br>
digit, for numbers. `[[:digit:]]` is the same as \d. `[[:^digit:]]` is the same as \D.<br>
alpha, for letters.<br>
alnum, for numbers and letters.<br>
lower, for lower-case letters.<br>
upper, for upper-case letters.<br>
word, for word characters. `[[:word:]]` is the same as \w. `[[:^word:]]` is the same as \W.<br>
space, for whitespace characters. `[[:space:]]` is the same as \s. `[[:^space:]]` is the same as \S.

## Portable character classes

These keywords (Or the corresponding \codes) should be used instead of explicit ranges where possible to improve portability. For example, `[A-Za-z]` and `[[:alpha:]]` are not the same thing in languages with accented characters.


Examples:
```sharp
    > say regmatch(foo_bar, lit(^`[[:word:]]`+$))
    You say "1"
    > say regmatch(foo bar, lit(^`[[:word:]]`+$))
    You say "0"
```
Other, less useful, character class keywords include ascii, cntrl, graph, print, punct, and xdigit.
