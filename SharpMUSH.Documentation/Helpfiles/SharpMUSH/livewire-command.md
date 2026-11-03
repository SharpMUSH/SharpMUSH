<!-- help-article
{
  "corpus": "help",
  "id": "livewire-command",
  "lookup": "@livewire",
  "aliases": [],
  "sections": []
}
-->
# @livewire

`@livewire/list <object>`<br>
`@livewire/funceval <object>[=<limit>]`<br>
`@livewire/funcover <object>[=<0 or 1>]`

Gives an object its own function invocation limit in place of the game's `function_invocation_limit`, for softcode that is heavy by design. It needs `config.admin`.

`/funceval` sets the limit, up to 1000000000; 0 or no value removes it. `/funcover` turns the override on (1) or off. A lower limit always applies, a higher one only with the override on. An object with no limit uses its owner's.

The limit follows whose code is running, but the count is shared by the whole command, so every object in a heavy call chain needs the limit, or one raised owner.

```sharp
> @livewire/funceval #21=100000
@livewire: Target PF2e Core(#21) FuncEval changed [0 -> 100000]
> @livewire/funcover #21=1
@livewire: Target PF2e Core(#21) FuncOverride changed [0 -> 1]
```

Output: code on `#21` may now make 100000 function calls in one command. `@livewire/list #21` shows both values.

RhostMUSH's `/queuemax` switch is not supported.

::: seealso
- [livewire()]
- [@config limits]
:::
