<!-- help-article
{
  "corpus": "help",
  "id": "reglmatch-function",
  "lookup": "reglmatch()",
  "aliases": [
    "REGLMATCHI()",
    "REGLMATCHALL()",
    "REGLMATCHALLI()",
    "REGMATCHALLI()",
    "reglmatch"
  ],
  "sections": [
    {
      "id": "regular-expression-examples",
      "heading": "Regular expression examples",
      "lookup": "reglmatch regular expression examples"
    }
  ],
  "redirects": {
    "REGLMATCH2": "reglmatch regular expression examples"
  }
}
-->
# reglmatch()

`reglmatch(<list>, <regexp>[, <delimiter>])`<br>
`reglmatchi(<list>, <regexp>[, <delimiter>])`<br>
`reglmatchall(<list>, <regexp>[, <delimiter>[, <output separator>]])`<br>
`reglmatchalli(<list>, <regexp>[, <delimiter>[, <output separator>]])`

These functions are the regexp versions of match() and matchall(). reglmatch() returns the position of the first element in `<list>` which matches the regular expression `<regexp>`. reglmatchi() does the same thing, but case-insensitively.

reglmatchall() returns the positions of all elements in `<list>` which match `<regexp>`. reglmatchalli() is case-insensitive.

In all cases, the elements of `<list>` are separated by `<delimiter>`, which defaults to a space. The elements outputted by reglmatchall() are separated by `<output separator>`, if one is given, or by `<delimiter>` if not.

SharpMUSH also registers regmatchalli() for reglmatchalli(). Despite the name it searches a list and returns positions, as the rest of this family does — it is not a case-insensitive [regmatch()].

::: seealso
- [regmatch()]
- [GRAB()]
- [element()]
- [regexp syntax]
:::

## Regular expression examples

Examples:
```sharp

  > say reglmatch(I am testing a test, test)
  You say, "3"

  > say reglmatch(I am testing a test, test$)
  You say, "5"

  > say reglmatchall(I am testing a test, test, , |)
  You say, "3|5"
```
