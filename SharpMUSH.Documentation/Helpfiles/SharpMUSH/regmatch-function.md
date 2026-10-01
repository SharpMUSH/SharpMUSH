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

`regmatch()` searches `<string>` for any substring matching `<regexp>`, returning 1 if it finds a match and 0 otherwise. `regmatchi()` performs the same search case-insensitively. Use `^` and `$` anchors when the expression must match the entire string; an unanchored match alone does not validate the whole input. For wildcard full-string matching, see [STRMATCH()]; for regular-expression matching over a list, see [reglmatch()].

```sharp
> think regmatch(abc123,123)
1
> think regmatch(abc123,^123$)
0
```

If `<register list>` is specified, there is a side-effect: any parenthesized substrings within the regular expression will be set into the specified local registers. The syntax for this is X:Y, where X is the number (0 is the entire matched text) or name of the substring, and Y is the q-register to save it in. If X: isn't given, the nth substring based on the register's position in the list minus one is used. The first element will have the complete matched text, the second the first substring, and so on. This is to maintain compatibility with old code; it's recommended for new uses that the X:Y syntax be used.

If `<regexp>` is not a valid regular expression, an error in the form "#-1 REGEXP ERROR: `<description>`" will be returned.


**See Also:**
- [GRAB()]
- [REGEDIT()]
- [valid()]
- [RESWITCH()]
- [STRMATCH()]
- [regexp syntax]

## Capture examples

For example, in<br>
```sharp
> think regmatch(cookies=30, (.+)=(\[0-9\]*) )
(note use of escaping for MUSH parser), then the 0th substring matched is 'cookies=30', the 1st substring is 'cookies', and the 2nd substring is '30'. If `<register list>` is '0:0 1:3 2:5', then %q0 will become "cookies=30", %q3 will become "cookies", and %q5 will become "30".
```

If `<register list>` was '0:0 2:5', then the "cookies" substring would simply be discarded. '1:food 2:amount' would store "cookies" in %q`<food>` and "30" in %q`<amount>`.

See [regexp syntax] for an explanation of regular expressions.
