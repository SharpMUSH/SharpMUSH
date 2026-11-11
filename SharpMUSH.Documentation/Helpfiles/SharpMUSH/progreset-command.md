<!-- help-article
{
  "corpus": "help",
  "id": "progreset-command",
  "lookup": "@progreset",
  "aliases": [
    "@rhost_progreset"
  ],
  "sections": []
}
-->
# @progreset

`@progreset <player>[=<prompt>]`

@progreset gives the player's running [@program] a new prompt, of at most 80 characters. For a player in no program, it takes no prompt and instead clears a prompt left on their screen. You must control the player.

@progreset is RhostMUSH's command, reached under that dialect (see [compatibility config]); its own name everywhere is @rhost_progreset.

```sharp
> @program me=Box/ASK
> |@progreset me=Colour?
Program prompt customized.
```

::: seealso
- [@program]
:::
