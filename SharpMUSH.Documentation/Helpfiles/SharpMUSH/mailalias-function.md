<!-- help-article
{
  "corpus": "help",
  "id": "mailalias-function",
  "lookup": "mailalias()",
  "aliases": [
    "mailalias"
  ],
  "sections": []
}
-->
# mailalias()

`mailalias(<alias>[, <type>])`

mailalias() returns the members of the mail alias `<alias>` as dbrefs separated by spaces. The alias may be written with or without its `+`. An alias that does not exist gives nothing.

Type 1 asks RhostMUSH for the alias's lock. SharpMUSH aliases say who may use them with @malias/use instead of a lock, so type 1 gives nothing.

Only wizards may use mailalias().

```sharp
> @malias +staff=Alice Bob
> think mailalias(staff)
#12 #15
```

::: seealso
- [malias()]
- [@malias]
:::
