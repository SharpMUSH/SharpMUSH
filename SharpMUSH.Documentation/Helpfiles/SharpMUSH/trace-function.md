<!-- help-article
{
  "corpus": "help",
  "id": "trace-function",
  "lookup": "trace()",
  "aliases": [
    "trace"
  ],
  "sections": []
}
-->
# trace()

`trace(<expression>)`

Evaluates `<expression>` with debug output on, as if you had the DEBUG flag, and returns the result. Turn debug output on for one part of an attribute this way, without setting the flag.

This function comes from TinyMUX. RhostMUSH's trace() and chktrace() set and check the TRACE flag instead; a RhostMUSH import rewrites them to set and check the DEBUG flag.

```sharp
> think trace(add(1,2))
#3! add(1,2) => 3
3
```

::: seealso
- [DEBUG]
:::
