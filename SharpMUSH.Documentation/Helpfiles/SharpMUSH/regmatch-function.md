<!-- help-article
{
  "corpus": "help",
  "id": "regmatch-function",
  "lookup": "regmatch()",
  "aliases": [
    "REGMATCHI()",
    "regmatch"
  ],
  "sections": [
    {
      "id": "capture-examples",
      "heading": "Capture examples",
      "lookup": "regmatch capture examples"
    }
  ],
  "redirects": {
    "REGMATCH2": "regmatch capture examples"
  }
}
-->
# regmatch()

(Help text from TinyMUSH 2.2.4, with permission)<br>
`regmatch(<string>, <regexp>[, <register list>])`<br>
`regmatchi(<string>, <regexp>[, <register list>])`

`regmatch()` searches `<string>` for any substring matching `<regexp>`, returning 1 if it finds a match and 0 otherwise. `regmatchi()` performs the same search case-insensitively. Use `\A` and `\z` anchors for strict whole-string validation (`^` and `$` are commonly used, but `$` permits a final newline); an unanchored match alone does not validate the whole input. For wildcard full-string matching, see [STRMATCH()]; for regular-expression matching over a list, see [reglmatch()].

```sharp
> think regmatch(abc123,123)
1
> think regmatch(abc123,^123$)
0
```

If `<register list>` is specified, there is a side-effect: any parenthesized substrings within the regular expression will be set into the specified local registers. The syntax for this is X:Y, where X is the number (0 is the entire matched text) or name of the substring, and Y is the q-register to save it in. If X: isn't given, the nth substring based on the register's position in the list minus one is used. The first element will have the complete matched text, the second the first substring, and so on. This is to maintain compatibility with old code; it's recommended for new uses that the X:Y syntax be used.

An invalid `<regexp>` returns `#-1 REGEXP ERROR: INVALID REGULAR EXPRESSION`. A match that exceeds its time budget returns `#-1 REGEXP TIMEOUT`. On a failed match, requested q-registers are cleared; a `-` destination discards a capture.


::: seealso
- [GRAB()]
- [REGEDIT()]
- [valid()]
- [RESWITCH()]
- [STRMATCH()]
- [regexp syntax]
:::

## Capture examples

Use explicit capture-to-register pairs and read the registers in the same evaluation. Here `\\` delivers a regex backslash, `%(`/`%)` deliver grouping parentheses, and `\[`/`\]` protect the character class from MUSH evaluation:

```sharp
> think [regmatch(cookies=30,\\A%(.+%)=%(\[0-9\]+%)\\z,1:food 2:amount)]|%q<food>|%q<amount>
1|cookies|30
```

Capture 0 is `cookies=30`, capture 1 is `cookies`, and capture 2 is `30`. `0:0 1:3 2:5` stores those values in q-registers 0, 3, and 5. `0:0 2:5` discards the first capture. Without colons, `- food amount` discards the whole match and saves captures 1 and 2.

A named capture uses .NET syntax `(?<name>pattern)`; a register-list entry such as `name:food` copies it to q-register FOOD. See [regexp syntax] for engine syntax, MUSH escaping, and capture numbering.
