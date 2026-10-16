<!-- help-article
{
  "corpus": "help",
  "id": "channel-privs-command",
  "lookup": "@channel privs",
  "aliases": ["Channel-privs"],
  "sections": [
    {"id": "list", "heading": "Available privileges", "lookup": "@channel privs list"},
    {"id": "examples", "heading": "Examples", "lookup": "@channel privs examples"}
  ]
}
-->
# @channel privs

`@channel/privs <channel>=<privlist>`

Channel privileges say who a channel admits and how its messages are rendered. They are set with `@channel/privs`, and on a brand new channel with `@channel/add <channel>=<privlist>`.

*<privlist>* is a space-separated list of privilege names, or of the single-letter abbreviations below. The list **replaces** the channel's current privileges rather than adding to them, so name every privilege the channel is to keep. Names match without regard to case; the letters do not, because `O` is Object and `o` is Open.

- [@channel privs list]: every privilege, and how they differ from locks
- [@channel privs examples]: setting privileges by name and by letter

## Available privileges

- **player** (`P`): Players may use the channel
- **object** (`O`): Non-players may use the channel
- **admin** (`A`): Only royalty, wizards, and holders of the `chat_privs` power may use the channel
- **wizard** (`W`): Only wizards may use the channel
- **quiet** (`Q`): The channel does not show connection messages
- **open** (`o`): You may speak on the channel even when you are not listening to it
- **hide_ok** (`H`): You may hide yourself from the channel's who list
- **notitles** (`T`): Channel titles are not shown in channel messages
- **nonames** (`N`): Speakers' names are not shown in channel messages
- **nocemit** (`C`): [@CEMIT] is prohibited on the channel
- **interact** (`I`): Channel output is filtered through the game's interaction rules
- **disabled** (`D`): No one can join or speak on the channel

These are privileges, not locks. A privilege says which *kind* of thing the channel is open to; a lock says which particular objects get through. Joining, speaking, seeing, hiding and modifying are each governed by a lock of their own; see [@CHANNEL CLOCK].

`loud` is not a channel privilege and cannot be given to a channel. It is a flag set on an object; see [FLAG LIST].

## Examples

```sharp
@channel/privs Public=player quiet open nocemit
@channel/privs Admin=player admin
@channel/privs Public=P Q o C
```

::: seealso
- [@channel joining]
- [@CHANNEL CLOCK]
- [@CHANNEL CLOCK]
- [cflags()]
:::
