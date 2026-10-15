<!-- help-article
{
  "corpus": "help",
  "id": "discord-command",
  "lookup": "@discord",
  "aliases": ["discord"],
  "sections": [
    {
      "id": "discord-setup",
      "heading": "Setting up the bot",
      "lookup": "@discord setup",
      "aliases": ["@discord/connect"]
    },
    {
      "id": "discord-channels",
      "heading": "Linked channels",
      "lookup": "@discord channels",
      "aliases": ["@discord/link", "@discord/unlink", "@discord/resume", "@discord/gate"]
    },
    {
      "id": "discord-members",
      "heading": "Linking members",
      "lookup": "@discord members"
    },
    {
      "id": "discord-speak",
      "heading": "Speaking from Discord",
      "lookup": "@discord speak",
      "aliases": ["@discord/speak"]
    },
    {
      "id": "discord-roles",
      "heading": "Roles",
      "lookup": "@discord roles",
      "aliases": ["@discord/map", "@discord/unmap", "@discord/drift"]
    },
    {
      "id": "discord-bans",
      "heading": "Game bans",
      "lookup": "@discord bans",
      "aliases": ["@discord/bans"]
    },
    {
      "id": "discord-events",
      "heading": "Scheduled events",
      "lookup": "@discord events",
      "aliases": ["@discord/event"]
    }
  ]
}
-->
# @discord

`@discord[/<switch>] [<argument>[=<value>]]`

The Discord plugin links channels on a Discord server to the game, through a bot the game runs itself. Each linked channel is a feed, `discord/<channel id>`: every line sent on that feed is posted in the channel under the speaker's name, and a linked member's message in the channel comes back as a line on the feed, spoken by the character they chose.

`@discord` alone shows the bot's connection and the linked channels. Everything but `@discord/speak` needs the `discord.admin` permission, which the wizard role allows.

Discord ids are numbers. To copy one, turn on Developer Mode in Discord's User Settings, under Advanced, then right-click a channel, role or server and choose Copy ID.

- [@discord setup]: the bot's token and connection
- [@discord channels]: linking, unlinking and resuming channels
- [@discord members]: linking Discord accounts to game accounts
- [@discord speak]: choosing who speaks for you in a channel
- [@discord roles]: Discord roles that follow game roles
- [@discord bans]: banning on Discord when banned in the game
- [@discord events]: the game's events in Discord's Events tab

The portal's Discord page, under Build & manage, does the same as these commands.

## Setting up the bot

- `@discord`
- `@discord/connect`

Each game makes its own Discord application in Discord's developer portal. On its Bot page, copy the token, untick Public Bot, and turn on Server Members Intent and Message Content Intent. Then store the token and connect:

```
> @secret discord.token=<the token>
> @discord/connect
```

`@discord` says where the connection stands and what to do next when something is wrong. `@discord/connect` logs the bot in again with the token stored now; run it after changing the token or the intents.

Once Discord accepts the token, `@discord` ends with a line starting `Add the bot to a server:` and a link. Open it, pick the server and keep every permission it asks for.

## Linked channels

- `@discord/link <channel id>`
- `@discord/unlink <channel id>`
- `@discord/resume <channel id>`
- `@discord/gate <channel id>`

Install the discord package from the portal's Packages page first; it adds the `discord` feed kind. `@discord/link` then makes a webhook in the channel, which the bot posts through, and lets only members holding the Linked role send messages there. These commands return the channel id at once and say how it went when Discord answers.

```
> @discord/link 1234567890123456789
```

Output: Linked #ooc in My Game. Lines sent on the feed discord/1234567890123456789 are posted there. Only linked members may post there.

- `/unlink` stops posting and deletes the webhook. The feed and its lines stay in the game.
- `/resume` posts again after a link stopped. `@discord` shows why a link stopped in its State column: a webhook deleted twice within ten minutes, a permission the bot lost, or five posts refused within a minute.
- `/gate` makes the Linked-role change again, after the bot could not make it while linking.

Anything that sends a line on the feed posts it in Discord, such as `@feed/send discord/1234567890123456789=Server restart in ten minutes.` A line sent with `@feed/send/to` is not posted, and neither is one still waiting after a minute. See [@feed].

## Linking members

A member links their Discord account to their game account once. In the game's Discord server they type `/link`, and the bot answers, to them alone, with a link to open while signed in to the game's website. It works once, for ten minutes, and needs `mud_url` to be set. `/unlink` in Discord, or Unlink on the portal's account page, drops the link.

- A game account links to one Discord account, and a Discord account to one game account.
- Only an active account can link.
- A Discord account unlinked from one game account waits a week before it can link a different one.
- While a game account's ban stands, the Discord account that was linked to it cannot link another.

The bot gives every member linked to an active account a role named Linked, and takes it from everyone else. Only that role may post in linked channels.

## Speaking from Discord

- `@discord/speak <channel id>`
- `@discord/speak/off <channel id>`

Anyone may run these, as the character they want to speak as. The character's account must be linked to Discord, and the character must pass the feed's send lock. From then on, each message the member posts in that channel comes into the game as a line on its feed, spoken by that character. One character speaks per channel; choosing another replaces it. `/off` stops it.

This is not private: anyone who reads both the game and the channel can tell which Discord member plays the character.

A member with no character chosen in a channel gets a direct message saying how to choose one, and their messages do not reach the game. Nothing that arrives from Discord is ever evaluated.

## Roles

- `@discord/map`
- `@discord/map[/confirm] <game role>=<Discord role>`
- `@discord/unmap <Discord role>`
- `@discord/map/enforce <Discord role>` and `@discord/map/report <Discord role>`
- `@discord/drift`
- `@discord/drift/apply [<Discord role>]`

`@discord/map builder=@Builders` gives the Discord role Builders to every linked member whose account, or one of its characters, holds the game role `builder`, and takes it from those who don't. A Discord role is named with or without its `@`, or by its id. `@discord/map` alone lists the mappings and whether the bot can manage each one. `@discord/unmap` stops mapping a role; members keep it as it is.

A role you may map is below your own highest role and does not allow `administrator`. `everyone`, `player` and `god` cannot be mapped. `wizard` and `royalty` need `/confirm`, because the Discord role shows everyone who holds them. See [@role].

The bot needs Manage Roles, and its own role must be above each mapped role in the server's role list.

A mapped role given or taken by hand on Discord is listed by `@discord/drift`. `@discord/drift/apply` puts back the game's answer for every role, or for one. `/enforce` makes the bot put such a change back at once, and `/report` goes back to only listing it.

## Game bans

- `@discord/bans`
- `@discord/bans on` and `@discord/bans off`

A game ban always takes the Linked role and every mapped role. With `@discord/bans on`, it also bans the account's Discord member in every server the bot is in, and turning it on bans the members of accounts banned already. When the game ban ends, the bot lifts the Discord bans it made; a Discord ban staff made themselves is left alone. `/bans off` stops new bans. `@discord/bans` says whether bans are mirrored and lists the members the bot banned. The bot needs Ban Members.

## Scheduled events

- `@discord/event`
- `@discord/event <key>=<json>`
- `@discord/event/start <key>`
- `@discord/event/end <key>`

`@discord/event` publishes a game event to the server's Events tab, under a key the game chooses, such as a scene's id. The value is a JSON object; build it with [json()], since typed braces would be taken as grouping:

```
> @discord/event 12=[json(object,name,json(string,Harbour Night),start,add(secs(),86400),location,json(string,https://game.example/scenes/12))]
```

- `name`: up to 100 characters (required)
- `start`: seconds since 1970 UTC, as [secs()] gives it, still to come (required)
- `end`: three hours after the start if left out
- `location`: up to 100 characters (required)
- `description`: up to 1000 characters
- `server`: the server's id, needed only when the bot is in more than one

Publishing the same key again updates the event; once it has ended, it makes a new one. `/start` shows the event as happening now. `/end` ends an event that started and cancels one that has not. `@discord/event` alone lists what is published. Each command returns the key at once and says how it went when Discord answers.

::: seealso
- [@feed]
- [@role]
- [@secret]
:::
