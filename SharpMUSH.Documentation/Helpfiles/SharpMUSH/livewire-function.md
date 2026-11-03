<!-- help-article
{
  "corpus": "help",
  "id": "livewire-function",
  "lookup": "livewire()",
  "aliases": [],
  "sections": []
}
-->
# livewire()

`livewire(<kind>[, <object>[, <owner too>]])`

With `<kind>` `funceval`, returns the game's function invocation limit, then the limit and override [@livewire] gave `<object>`. With `<owner too>` true, the owner's limit and override follow. You must control `<object>`. An empty `<kind>` lists the kinds it knows.

```sharp
> think livewire(funceval, #21)
25000 100000 1
> think livewire(funceval, #21, 1)
25000 100000 1 0 0
```

Output: the game allows 25000 calls, `#21` 100000 with the override on, and its owner has no limit of its own.

::: seealso
- [@livewire]
:::
