<!-- help-article
{
  "corpus": "help",
  "id": "channel-admin-command",
  "lookup": "@channel admin",
  "aliases": ["@channel/add", "@channel/delete", "@channel/mogrifier", "@channel/chown", "@channel/name", "@channel/rename", "@channel/describe", "@channel/privs", "@channel/wipe", "@channel/decompile"],
  "sections": [
    {"id": "creating", "heading": "Creating, deleting and wiping", "lookup": "@channel admin creating"},
    {"id": "changes", "heading": "Changing a channel", "lookup": "@channel admin changes"},
    {"id": "decompile", "heading": "Decompiling and locks", "lookup": "@channel admin decompile"}
  ]
}
-->
# @channel admin

- `@channel/add <channel>=<privlist>`
- `@channel/delete <channel>`
- `@channel/mogrifier <channel>=<object>`
- `@channel/chown <channel>=<player>`
- `@channel/name <channel>=<newname>`
- `@channel/rename <channel>=<newname>`
- `@channel/describe <channel>=<description>`
- `@channel/privs <channel>=<privlist>`
- `@channel/wipe <channel>`
- `@channel/decompile[/brief] <channel>`
- `@clock/join|/speak|/see|/hide|/mod <channel>[=<lock>]`

- [@channel admin creating]: creating, deleting and wiping channels
- [@channel admin changes]: mogrifier, owner, name, description, privileges
- [@channel admin decompile]: decompiling a channel, and its locks

## Creating, deleting and wiping

`@channel/add` creates a new channel with the privileges in *<privlist>*, the same list `@channel/privs` takes, and it is required. You may not own more channels than the `max_channels` limit allows unless you are staff.

`@channel/delete` removes a channel. Only channel admins can do this.

`@channel/wipe` removes all players from a channel. Only channel admins can do this.

## Changing a channel

`@channel/mogrifier` sets an object to be the channel's mogrifier. Only channel admins can do this. See [mogrifier] for details.

`@channel/chown` changes the owner of a channel. Only channel admins can do this.

`@channel/name` and `@channel/rename` both rename a channel. Only channel admins can do this.

`@channel/describe` changes a channel's description. Only channel admins can do this.

`@channel/privs` changes a channel's privileges. Only channel admins can do this. See [@CHANNEL PRIVS] for details.

## Decompiling and locks

`@channel/decompile` prints the commands that would recreate the channel: its privileges, owner, mogrifier, locks, description, buffer size and membership. You must be able to decompile the channel. The /brief switch stops before the membership; members hiding on the channel are listed only to someone who could see them on [@channel joining].

Channel locks are managed by the separate `@clock` command, not by a `@channel` switch. See [@CHANNEL CLOCK] for details.

::: seealso
- [@channel joining]
- [@CHANNEL ADMIN]
- [@CHANNEL CLOCK]
:::
