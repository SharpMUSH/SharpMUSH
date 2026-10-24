<!-- help-article
{"corpus":"help","id":"hasarg-function","lookup":"hasarg()","aliases":["hasarg"],"sections":[{"id":"examples","heading":"Examples","lookup":"hasarg examples"}]}
-->
# hasarg()

`hasarg(<name>[, <scope>])`

hasarg() returns 1 if the attribute running now has an argument called `<name>`, even when its value is empty or 0, and 0 if not. A number names an argument by position: `hasarg(0)` is %0. Case does not matter. Outside an attribute, hasarg() returns 0.

`<scope>` is one of:

- `visible`, the default: any argument the attribute can read. That includes what @include carried over from its caller, and the counts a function's `items...` argument makes, such as `itemscount`.
- `supplied`: only what this call itself was passed. After @include/args, that is the arguments the include named, even one with the same value as the caller's.

Any other `<scope>` returns `#-1 INVALID ARGUMENT`.

hasarg() never evaluates the argument. Each u() call has arguments of its own; a built-in function such as iter() keeps the ones around it, and a queued action keeps the ones it was queued with.

- [hasarg examples]: testing an argument, and what an included attribute sees

::: seealso
- [named arguments]
- [uargs()]
- [@include]
:::

## Examples

```sharp
&SHOW me=[hasarg(who)]|[hasarg(who,supplied)]|[hasarg(0,supplied)]|[hasarg(missing)]
think uargs(me/SHOW,who,)
```

Output: `1|1|1|0`

An included attribute sees its caller's `who`, but only its own `x` is supplied:

```sharp
&INNER me=think [hasarg(who)]/[hasarg(who,supplied)]/[hasarg(x,supplied)]
&OUTER me=@include/args me/INNER=x,1
@trigger/inline/args me/OUTER=who,Bob
```

Output: `1/0/1`, after the trigger's own line.
