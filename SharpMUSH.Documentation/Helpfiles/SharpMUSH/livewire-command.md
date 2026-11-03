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

Gives one object its own function invocation limit in place of the game's `function_invocation_limit` (25000 by default). Use it for softcode that is heavy by design, such as a game system's sheet and combat objects. It needs the `config.admin` permission.

- `/list` shows the object's limit and override.
- `/funceval` sets the limit, from 0 to 1000000000. 0, or no value, removes it.
- `/funcover` turns the override on (1) or off (0, or no value).

A limit lower than the game's always applies. A higher one applies only while the override is on. An object with no limit of its own uses its owner's, so setting one on a player covers everything that player owns.

The limit is chosen by whose code is running. The count of calls is shared by the whole command, as it is for the game's limit, so code on a raised object that calls into another object needs that object raised too, or both owned by a raised player.

```sharp
> @livewire/funceval #21=100000
@livewire: Target PF2e Core(#21) FuncEval changed [0 -> 100000]
> @livewire/funcover #21=1
@livewire: Target PF2e Core(#21) FuncOverride changed [0 -> 1]
> @livewire/list #21
@livewire: Target PF2e Core(#21) [Owner: One(#1)] -- FuncEval 100000, FuncEvalOverride 1
```

Output: code `#21` runs may now make 100000 function calls in one command.

This is RhostMUSH's command. Its `/queuemax` switch is not supported.

::: seealso
- [livewire()]
- [@config limits]
:::
