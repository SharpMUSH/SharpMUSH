<!-- help-article
{
  "corpus": "help",
  "id": "sandbox-function",
  "lookup": "sandbox()",
  "aliases": [
    "sandbox"
  ],
  "sections": []
}
-->
# sandbox()

`sandbox(<expression>, <functions>[, <unknown>[, <user functions>[, <allow>]]])`

Evaluates `<expression>` and returns the result, with the functions named in `<functions>`, a list separated by spaces, refused. A refused function returns `#-1 PERMISSION DENIED` and does nothing. The refusal covers everything `<expression>` evaluates, including attributes it calls with u() and functions it calls through `#apply`.

- `<unknown>`: if 1, a refused function returns `#-1 FUNCTION (<name>) NOT FOUND` instead, as if it did not exist.
- `<user functions>`: if 1 or 3, functions added with @function are refused too. Otherwise the list covers only built-in functions. Local functions are called through localfun(), so list localfun to refuse them.
- `<allow>`: if 1, `<functions>` are the only ones allowed, and every other function is refused.

If `<functions>` is empty, `<expression>` is evaluated as usual. A sandbox() inside another keeps the outer one's refusals, so it can only refuse more functions. Commands the expression queues, with trigger() or @wait for example, run later without the sandbox.

This function comes from RhostMUSH.

```sharp
> think sandbox([add(1,1)] [sub(1,1)],add)
#-1 PERMISSION DENIED: CANNOT USE ADD 0
> think sandbox([add(1,1)] [sub(1,1)],add,0,0,1)
2 #-1 PERMISSION DENIED: CANNOT USE SUB
```

::: seealso
- [LOCALIZE()]
- [@function]
:::
