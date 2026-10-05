<!-- help-article
{
  "corpus": "help",
  "id": "fn-function",
  "lookup": "fn()",
  "aliases": [
    "fn"
  ],
  "sections": [
    {
      "id": "executor-selection",
      "heading": "Executor selection",
      "lookup": "fn executor selection"
    }
  ],
  "redirects": {
    "FN2": "fn executor selection"
  }
}
-->
# fn()

`fn([<obj>/]<function name>[, <arg0>[, ... , <argN>]])`

fn() executes the built-in/hardcoded function `<function name>`, even if the function has been deleted or overridden with @function. It is primarily useful within @functions that override built-ins in order to be able to call the built-in.

Example:
```sharp
&BRIGHT_PEMIT #10=fn(pemit,%0,-->[ansi(h,%1)])
@function/delete PEMIT
@function PEMIT=#10,BRIGHT_PEMIT
think pemit(me,test)
-->test   (in highlighted letters)
```

To restrict the use of fn() to @functions only (to prevent players from skirting softcoded replacements), use @function/restrict fn=userfn.

To prevent deleted functions from being used with fn(), @function/disable them prior to deleting.

## Executor selection

If `<obj>` is specified, the built-in function will be executed as `<obj>`, rather than as the object which called fn(). This is useful when using fn() to replace a side-effect function, to ensure priviledge checks, etc, are done correctly. You must control `<obj>`, or (if function side effects are disabled) must be see_all.

When an `<obj>` is given, debug information is automatically suppressed when evaluating the built-in function.

Example:
```sharp
&BRIGHT_PEMIT #10=fn(%@/pemit, %0, -->[ansi(h,%1)])
@function/delete PEMIT
@function PEMIT=#10, BRIGHT_PEMIT
@lock/page *Mike=!=*Padraic
```

    (As Padraic)<br>
```sharp
> think pemit(me,test)
-->test  (in highlighted letters)
> think pemit(*Mike,test)
(nothing happens)
```


::: seealso
- [@function]
- [restrict]
- [attribute flags]
:::
