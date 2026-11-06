<!-- help-article
{
  "corpus": "help",
  "id": "andbool-function",
  "lookup": "andbool()",
  "aliases": [
    "ORBOOL()",
    "CANDBOOL()",
    "CORBOOL()",
    "andbool",
    "orbool",
    "candbool",
    "corbool"
  ],
  "sections": []
}
-->
# andbool()

`andbool([<boolean1>[, ... <booleanN>]])`<br>
`orbool([<boolean1>[, ... <booleanN>]])`<br>
`candbool([<boolean1>[, ... <booleanN>]])`<br>
`corbool([<boolean1>[, ... <booleanN>]])`

andbool() returns 1 when every argument is true, and orbool() returns 1 when at least one is. candbool() and corbool() do the same, but evaluate their arguments one at a time and stop as soon as the answer is known, so later arguments may never run.

These read each argument as TinyMUX reads a boolean, whatever the `tiny_booleans` and `mux_logic` @config options say. False is an empty or blank string, a number equal to zero, `NaN`, `Inf` or `Ind`, and anything starting with `#-`. Everything else is true, including negative numbers, dbrefs and words. With no arguments, andbool() is 1 and orbool() is 0.

These functions come from TinyMUX.

```sharp
> think andbool(1, abc, #5, -3)
1
```

Output: every argument is true, the word and the negative number included.

```sharp
> think orbool(0, #-1 NO MATCH, 0.000)
0
```

::: seealso
- [AND()]
- [OR()]
- [CAND()]
- [COR()]
- [boolean values]
:::
