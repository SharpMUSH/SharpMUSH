<!-- help-article
{
  "corpus": "help",
  "id": "discordlink",
  "lookup": "discordlink()",
  "aliases": [],
  "listedIn": ["function list objects"],
  "sections": []
}
-->
# discordlink()

`discordlink(<channel id>)`

Whether the game has linked a Discord channel, and who may post in it: `restricted` when only linked members may, `unrestricted` when anyone on the Discord server may, and `#-1 NO SUCH LINK` when the game has not linked it. A channel id that is not a number is `#-1 INVALID CHANNEL ID`.

Like `@discord`, it needs the `discord.admin` permission; without it the answer is `#-1 PERMISSION DENIED`. A package object that reads it holds the Discord Admin role, as the discord-channels package's Discord Channels object does.

```sharp
> think discordlink(1234567890123456789)
```

Output: unrestricted

See [@discord restrict].
